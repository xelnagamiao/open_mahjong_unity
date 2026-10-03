"""广东政策适配器：复用台湾/推倒和已经验证的权威动作状态机。"""

import random
from dataclasses import replace

from ...game_calculation.guangdong import config as book
from ...game_calculation.guangdong import rules
from ...game_calculation.guangdong.scoring import Context
from ...game_calculation.guangdong.settlement import kong_payments
from ..game_taiwan.TaiwanGameState import TaiwanGameState
from ..game_taiwan.action_check import hu_action_for_player
from ..game_taiwan.boardcast import broadcast_do_action
from ..public.game_record_manager import append_action_tick
from ..public.random_seed_manager import derive_round_seed
from ..public.ai.bot_executor import run_room_bot_cpu
from .lifecycle import GuangdongMatchMixin
from .tips import GuangdongTipsMixin


class GuangdongGameState(GuangdongMatchMixin, GuangdongTipsMixin, TaiwanGameState):
    flower_tiles = ()  # 55–58 是留在手内的实体鬼，不能自动补花。
    structure_tiles = book.TILES
    concealed_kongs_public = True  # 原书五-5：先公示，再仅扣两侧两张。

    @staticmethod
    def build_concealed_kong_mask(tiles):
        tile = tiles[0]
        return [2, tile, 0, tile, 0, tile, 2, tile]

    def __init__(self, game_server, room_data, calculation_service, db_manager, gamestate_id):
        if room_data.get("sub_rule", book.SUB_RULE) != book.SUB_RULE:
            raise ValueError("广东花鬼执行器只接受 MIL2023 子规则")
        config = book.normalize_config(room_data.get("detailed_config"))
        super().__init__(game_server, {**room_data, "detailed_config": None},
                         calculation_service, db_manager, gamestate_id)
        self.room_rule, self.sub_rule = "guangdong", book.SUB_RULE
        self.rules = replace(self.rules, dead_wall_count=0, ready_qualification_mode="disabled",
            public_ready_enabled=False, multi_win_mode="head_bump",
            chow_discard_restriction_mode="none", pung_same_tile_discard_forbidden=False,
            allow_kong_from_upper_discard=True, direct_kong_replacement_win_allowed=True,
            missed_win_blocks_self_draw=False, missed_win_blocks_claims=False,
            missed_win_released_by_kong=False, seven_flowers_steal_eighth_enabled=False,
            four_winds_abort=False, four_kongs_abort=False)
        self.rules_dict = config
        self.dead_wall_count = 0
        self.hepai_limit = 2
        self.open_cuohe = False
        from .bot import guangdong_bot_action
        self.smart_bot_action = guangdong_bot_action

    def _reset_taiwan_players(self):
        super()._reset_taiwan_players()
        self.kong_ledger = []
        self._score_event = None
        self._terminal_result = None
        self.direct_kong_payer = None
        self._tips_cache = {}
        self._recorded_tips = {}
        self._candidate_cache = {}
        for player in self.player_list:
            player.passed_base_score = -1
            player.discarded_ghosts = 0

    async def player_reconnect(self, user_id):
        await super().player_reconnect(user_id)
        connection = self.game_server.user_id_to_connection.get(user_id)
        if connection is not None and self._terminal_result is not None:
            await connection.websocket.send_json(self._terminal_result)

    def refresh_waits(self, index):
        player = self.player_list[index]
        player.waiting_tiles = rules.structural_waits(player.hand_tiles, player.combination_tiles)
        return player.waiting_tiles

    def scoring_context(self, index, source, *, prediction=False):
        player = self.player_list[index]
        self_draw = source == "self_draw"
        return Context(self_draw=self_draw, rob_kong=source == "robbing_kong",
            last_tile=not self.can_take_normal_tile(),
            kong_flower=self_draw and not prediction and self.last_draw_after_kong,
            heavenly=self_draw and not prediction and index == 0 and self.opening_dealer_action
                     and not self.table_claim_or_kong,
            earthly=self_draw and index != 0 and player.discard_count == 0
                    and player.normal_draw_count == (0 if prediction else 1)
                    and (prediction or not self.last_draw_after_kong),
            discarded_ghosts=player.discarded_ghosts,
            require_minimum_score=self.rules_dict["require_minimum_score"])

    def score_candidate(self, index, source, tile=None, *, ignore_pass=False, **_):
        player = self.player_list[index]
        if "peida" in player.tag_list:
            return None
        hand = list(player.hand_tiles)
        if source != "self_draw":
            if tile not in book.TILES:  # 鬼的打出张不能点和或被抢杠。
                return None
            hand.append(tile)
        elif tile is None:
            tile = player.last_drawn_tile or (hand[-1] if hand else None)
        context = self.scoring_context(index, source)
        key = (tuple(sorted(hand)), tuple(player.combination_tiles), tile, context)
        detail = (self._candidate_cache[key] if key in self._candidate_cache else
                  rules.score(hand, player.combination_tiles, winning_tile=tile, context=context))
        if detail and source != "self_draw" and not ignore_pass and detail["base_score"] <= player.passed_base_score:
            return None
        return detail

    async def _prepare_score_candidates(self, queries):
        pending = [key for key in queries if key not in self._candidate_cache]
        if not pending:
            return
        results = await run_room_bot_cpu(self, rules.evaluate_score_batch, pending)
        if len(self._candidate_cache) + len(pending) > 32:
            self._candidate_cache.clear()
        self._candidate_cache.update(zip(pending, results))

    async def _prepare_other_scores(self, tile, source):
        if tile not in book.TILES:
            return
        queries = []
        for index, player in enumerate(self.player_list):
            if index != self.current_player_index:
                queries.append((tuple(sorted(player.hand_tiles + [tile])), tuple(player.combination_tiles),
                                tile, self.scoring_context(index, source)))
            elif source == "discard":
                queries.append((tuple(sorted(player.hand_tiles)), tuple(player.combination_tiles),
                                tile, self.scoring_context(index, source)))
        await self._prepare_score_candidates(queries)

    async def _prepare_self_score(self):
        player = self.player_list[self.current_player_index]
        tile = player.last_drawn_tile or (player.hand_tiles[-1] if player.hand_tiles else None)
        await self._prepare_score_candidates([(tuple(sorted(player.hand_tiles)), tuple(player.combination_tiles),
                                              tile, self.scoring_context(self.current_player_index, "self_draw"))])

    async def _prepare_hand_action_after_draw(self):
        await self._prepare_self_score()
        return await super()._prepare_hand_action_after_draw()

    async def execute_cut(self, index, action_data, **kwargs):
        await self._prepare_other_scores(action_data.get("TileId"), "discard")
        return await super().execute_cut(index, action_data, **kwargs)

    def ready_candidate_cuts(self, index):
        return {}

    def _register_initial_heavenly_ready(self):
        pass

    def _revoke_qualification(self, index, **_):
        return False

    def _mark_eight_flowers_if_ready(self, index, **_):
        pass

    def _remember_claim_liability(self, *args):
        pass

    def _liability_payer_for_win(self, index, source, *args):
        return self.direct_kong_payer if source == "self_draw" and self.last_draw_after_kong else None

    async def _opening_flower_replacement(self):
        self.player_list[0].has_draw_slot = True
        self.player_list[0].last_drawn_tile = self.player_list[0].hand_tiles[-1]
        await self._prepare_self_score()

    async def _process_drawn_flowers(self, index, origin):
        self.player_list[index].passed_base_score = -1
        if origin != "direct_kong":
            self.direct_kong_payer = None
        return await super()._process_drawn_flowers(index, origin)

    def enter_water(self, index):
        # 顺和门槛按基本分，直至本人再摸牌；碰牌不能解除。
        if index != self.current_player_index:
            detail = self.result_dict.get(hu_action_for_player(self.current_player_index, index))
            if detail:
                player = self.player_list[index]
                player.passed_base_score = max(player.passed_base_score, detail["base_score"])
        return False  # 不使用会禁止吃碰/自摸的台湾 water 标记。

    def _finalize_ready_after_discard(self, index, declared):
        player = self.player_list[index]
        tile = player.last_discarded_tile
        if tile in book.GHOSTS:
            player.discarded_ghosts += 1
            append_action_tick(self, ["guangdong", "ghost_discard", index, player.discarded_ghosts])
        else:
            detail = self.score_candidate(index, "discard", tile, ignore_pass=True)
            if detail:
                player.passed_base_score = max(player.passed_base_score, detail["base_score"])
        self.direct_kong_payer = None

    def kong_allowed(self, index, tile, kind):
        if type(tile) is not int or tile not in book.TILES or not self.can_establish_kong():
            return False
        player = self.player_list[index]
        if "peida" in player.tag_list:
            return False
        if kind not in ("concealed", "added", "direct"):
            return False
        if kind != "direct" and (index != self.current_player_index or player.last_drawn_tile is None):
            return False
        required = {"concealed": 4, "added": 1, "direct": 3}[kind]
        return (player.hand_tiles.count(tile) >= required
                and (kind != "added" or f"k{tile}" in player.combination_tiles))

    def check_hand_actions(self, index):
        result = {i: [] for i in range(4)}
        self.result_dict.pop("hu_self", None)
        detail = self.score_candidate(index, "self_draw")
        if detail:
            result[index].append("hu_self")
            self.result_dict["hu_self"] = detail
        for kind, action in (("concealed", "angang"), ("added", "jiagang")):
            if any(self.kong_allowed(index, tile, kind) for tile in set(self.player_list[index].hand_tiles)):
                result[index].append(action)
        result[index].append("cut")
        return result

    def _claim_actions(self, tile, source):
        result = {i: [] for i in range(4)}
        for distance in (1, 2, 3):
            index = (self.current_player_index + distance) % 4
            action = hu_action_for_player(self.current_player_index, index)
            self.result_dict.pop(action, None)
            if tile not in book.TILES or "peida" in self.player_list[index].tag_list:
                continue
            detail = self.score_candidate(index, source, tile)
            if detail:
                result[index].append(action)
                self.result_dict[action] = detail
            # 摸完末张后的弃牌无人成和即流局（五-6），不再开启碰杠。
            if source == "discard" and self.can_take_normal_tile():
                if self.player_list[index].hand_tiles.count(tile) >= 2:
                    result[index].append("peng")
                if self.kong_allowed(index, tile, "direct"):
                    result[index].append("gang")
            if result[index]:
                result[index].append("pass")
        return result

    def check_discard_actions(self, tile):
        return self._claim_actions(tile, "discard")

    def check_added_kong_actions(self, tile):
        return self._claim_actions(tile, "robbing_kong")

    def after_claim_actions(self):
        return {i: ["cut"] if i == self.current_player_index else [] for i in range(4)}

    async def _pay_kong(self, index, kind, tile, *, payer=None, drawn=True):
        changes = kong_payments(index, kind, payer=payer, drawn=drawn)
        if not any(changes.values()):
            return
        for i, delta in changes.items():
            self.player_list[i].score += delta
        self.kong_ledger.append(dict(player=index, kind=kind, tile=tile, payer=payer, changes=changes))
        append_action_tick(self, ["guangdong", "kong_score", [changes[i] for i in range(4)], kind, payer, tile])
        self._score_event = {"gang_score_changes": changes, "gang_score_type": kind}
        try:
            await broadcast_do_action(self, action_list=[], action_player=index)
        finally:
            self._score_event = None

    async def execute_angang(self, index, tile):
        if not self.kong_allowed(index, tile, "concealed"):
            return
        before = len(self.player_list[index].combination_tiles)
        await super().execute_angang(index, tile)
        if len(self.player_list[index].combination_tiles) > before:
            self.direct_kong_payer = None
            await self._pay_kong(index, "concealed", tile)

    async def execute_jiagang(self, index, tile):
        if self.kong_allowed(index, tile, "added"):
            await self._prepare_other_scores(tile, "robbing_kong")
            await super().execute_jiagang(index, tile)

    async def finalize_jiagang(self):
        pending = self._pending_jiagang
        await super().finalize_jiagang()
        if pending:
            self.direct_kong_payer = None
            await self._pay_kong(pending["player_index"], "added", pending["normal"], drawn=pending["is_mo_gang"])

    async def execute_claim(self, index, action):
        payer = self.current_player_index
        if action not in ("peng", "gang") or not self.player_list[payer].discard_tiles:
            return
        tile = self.player_list[payer].discard_tiles[-1]
        if action not in self.check_discard_actions(tile).get(index, []):
            return
        before = len(self.player_list[index].combination_tiles)
        await super().execute_claim(index, action)
        if len(self.player_list[index].combination_tiles) > before:
            self.direct_kong_payer = payer if action == "gang" else None
            if action == "gang":
                await self._pay_kong(index, "direct", tile, payer=payer)

    def init_tiles(self):
        self.round_random_seed = derive_round_seed(self.master_seed, self.round_index)
        wall = book.wall_tiles()
        random.Random(self.round_random_seed).shuffle(wall)
        self.tiles_list = wall
        for player in self.player_list:
            player.hand_tiles = [wall.pop(0) for _ in range(13)]
        self.player_list[0].hand_tiles.append(wall.pop(0))

    def next_dealer(self):
        return self.pending_winners[0]["index"] if self.pending_winners else 0
