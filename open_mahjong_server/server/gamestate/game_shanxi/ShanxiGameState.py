"""MIL 山西十三张：复用现有回合动作、权威询问、重连和观战执行器。"""

import asyncio
import random
from dataclasses import replace

from .clock import RoomActionClock, TimedActionQueue

from ...game_calculation.shanxi import rules as sx
from ..game_taiwan.TaiwanGameState import TaiwanGameState
from ..game_taiwan.action_check import hu_action_for_player
from ..game_taiwan.boardcast import broadcast_game_start, broadcast_result, broadcast_game_end
from ..game_guobiao.combination_mask_view import build_revealed_angang_masks
from ..public.game_record_manager import (
    capture_player_entry_order, init_game_record, init_game_round, player_action_record_hu,
    player_action_record_round_end, end_game_record, remember_local_record_detail,
)
from ..public.hand_slot_utils import has_draw_slot
from ..public.logic_common import assign_competition_final_ranks
from ..public.next_game_round import next_game_round_classical_switchseat
from ..public.random_seed_manager import setup_random_seed_system, derive_round_seed
from ..public.round_end_timing import liuju_ready_wait_seconds
from ...database.fulu_utils import record_fulu_rounds_for_players


class ShanxiGameState(TaiwanGameState):
    flower_tiles = ()
    structure_tiles = sx.TILES

    def __init__(self, game_server, room_data, calculation_service, db_manager, gamestate_id):
        if room_data.get("sub_rule", sx.SUB_RULE) != sx.SUB_RULE:
            raise ValueError("不支持的山西麻将子规则")
        detail = room_data.get("detailed_config")
        if detail is not None and (not isinstance(detail, dict) or set(detail) - {"rule_version"}
                or detail.get("rule_version", sx.VERSION) != sx.VERSION):
            raise ValueError("不支持的山西麻将规则配置")
        super().__init__(game_server, {**room_data, "detailed_config": None},
                         calculation_service, db_manager, gamestate_id)
        self.room_rule, self.sub_rule = "shanxi", sx.SUB_RULE
        self.open_cuohe, self.hepai_limit = False, 0
        self.rules = replace(self.rules, dead_wall_count=14, multi_win_mode="head_bump",
            ready_qualification_mode="disabled", public_ready_enabled=True,
            declared_ready_win_policy="force_win", declared_ready_auto_added_kong=False,
            allow_kong_from_upper_discard=True, direct_kong_replacement_win_allowed=True,
            allow_rob_added_kong=False, pung_same_tile_discard_forbidden=False,
            chow_discard_restriction_mode="none", missed_win_blocks_self_draw=False,
            missed_win_blocks_claims=False, seven_flowers_steal_eighth_enabled=False)
        self.rules_dict = {"rule_version": sx.VERSION}
        self.dead_wall_count = 14
        from .bot import shanxi_smart_bot_action
        self.smart_bot_action = shanxi_smart_bot_action
        self.room_action_clock = RoomActionClock(self)
        self.action_queues = {i: TimedActionQueue(self.room_action_clock, i) for i in range(4)}

    def _reset_taiwan_players(self):
        super()._reset_taiwan_players()
        self.kong_ledger = []
        self.tail_single = False
        self.concealed_discard_actor = None
        for player in self.player_list:
            player.passed_pungs = set()
            player.concealed_discards = {}
            player.discard_origin_tiles = []

    def action_clock(self, player, reconnecting=False):
        if not self.room_action_clock.active():
            from ..public.ask_timing import reconnect_clock
            return reconnect_clock(self, player)
        return self.room_action_clock.snapshot(player)

    def claim_clock(self, player, reconnecting=False):
        return self.action_clock(player, reconnecting)

    def on_action_window_broadcast(self):
        self.room_action_clock.begin()

    def on_action_window_delivered(self, index):
        self.room_action_clock.delivered(index)

    def is_action_pending(self, index):
        return not self.room_action_clock.active() or self.room_action_clock.is_pending(index)

    async def collect_action_responses(self):
        if self.room_action_clock.active():
            return await self.room_action_clock.collect()
        # Result confirmation has its own presentation deadline, not a new
        # thinking step. Reuse the existing ready-phase collector unchanged.
        from ..game_taiwan.wait_action import _collect_responses
        return await _collect_responses(self)

    def decorate_record_cut_tick(self, tick):
        # c 的 C 扩展在同一事件标明暗扣，逐步回放不会先亮出再翻回。
        if self.concealed_discard_actor == self.current_player_index:
            tick.append("C")

    def _reset_hand_runtime(self):
        super()._reset_hand_runtime()
        self.dead_wall_count = 14
        self.room_action_clock.reset_hand()

    def init_tiles(self):
        self.round_random_seed = derive_round_seed(self.master_seed, self.round_index)
        self.tiles_list = [tile for tile in sx.TILES for _ in range(4)]
        random.Random(self.round_random_seed).shuffle(self.tiles_list)
        for player in self.player_list:
            player.hand_tiles = []
        # 三轮每家取两墩，然后庄家跳牌、各闲家一张；与原书开牌顺序一致。
        for _ in range(3):
            for player in self.player_list:
                player.hand_tiles.extend(self.tiles_list[:4])
                del self.tiles_list[:4]
        last = self.tiles_list[:5]
        del self.tiles_list[:5]
        for index, player in enumerate(self.player_list):
            player.hand_tiles.append(last[index])
        self.player_list[0].hand_tiles.append(last[4])
        for player in self.player_list:
            player.hand_tiles.sort()

    def public_tiles(self):
        result = []
        for player in self.player_list:
            result.extend(t for t in player.discard_tiles if t in sx.TILES)
            result.extend(t for code in player.combination_tiles for t in sx.meld_tiles(code))
        return result

    def known_public_tiles(self, index):
        return self.public_tiles() + list(self.player_list[index].concealed_discards.values())

    def refresh_waits(self, player_index):
        player = self.player_list[player_index]
        player.waiting_tiles = sx.waits(player.hand_tiles, player.combination_tiles)
        return player.waiting_tiles

    def ready_candidate_cuts(self, player_index):
        player = self.player_list[player_index]
        if player.ready_locked or "peida" in player.tag_list:
            return {}
        result = {}
        for tile in set(player.hand_tiles):
            hand = list(player.hand_tiles)
            hand.remove(tile)
            # 报听弃牌本人已知，需计入其已知牌；其他人仍看不到。
            waiting = sx.ready_waits(hand, player.combination_tiles, self.known_public_tiles(player_index) + [tile])
            if waiting:
                result[tile] = sorted(waiting)
        return result

    def score_candidate(self, player_index, source, tile=None, **_):
        player = self.player_list[player_index]
        if "peida" in player.tag_list or source not in ("self_draw", "discard"):
            return None
        self_draw = source == "self_draw"
        if self_draw:
            tile = player.last_drawn_tile
            if tile is None or not has_draw_slot(player):
                return None
        hand = list(player.hand_tiles) + ([] if self_draw else [tile])
        return sx.score(hand, player.combination_tiles, winning_tile=tile,
                        declared_ready=player.declared_ready, self_draw=self_draw)

    def can_take_normal_tile(self):
        return len(self.tiles_list) > 14 + int(self.tail_single)

    def playable_wall_count(self):
        return max(0, len(self.tiles_list) - 14 - int(self.tail_single))

    def can_take_supplement_tile(self):
        after_reserve = 14 + int(not self.tail_single)
        return self.can_take_normal_tile() and len(self.tiles_list) - 1 >= after_reserve

    def can_establish_kong(self):
        return self.can_take_supplement_tile()

    def _take_supplement_tile(self):
        if not self.can_take_supplement_tile():
            raise RuntimeError("七墩保留牌墙不足，不能补杠")
        tile = self.tiles_list.pop(-1 if self.tail_single else -2)
        self.tail_single = not self.tail_single
        self.dead_wall_count = 14 + int(self.tail_single)
        return tile

    def kong_allowed(self, index, tile, kind):
        player = self.player_list[index]
        if tile not in sx.TILES or "peida" in player.tag_list or not self.can_establish_kong():
            return False
        if kind != "g" and not (has_draw_slot(player) or self.opening_dealer_action):
            return False
        hand, melds = list(player.hand_tiles), list(player.combination_tiles)
        count = {"G": 4, "g": 3, "added": 1}[kind]
        if hand.count(tile) < count:
            return False
        for _ in range(count):
            hand.remove(tile)
        public = self.known_public_tiles(index)
        if kind == "added":
            if f"k{tile}" not in melds:
                return False
            melds[melds.index(f"k{tile}")] = f"g{tile}"
            public.append(tile)
        else:
            melds.append(f"{kind}{tile}")
            public.extend([tile] * count)
        return not player.ready_locked or bool(sx.ready_waits(hand, melds, public))

    def check_hand_actions(self, player_index):
        result = {i: [] for i in range(4)}
        player = self.player_list[player_index]
        detail = self.score_candidate(player_index, "self_draw")
        if detail:
            self.result_dict["hu_self"] = detail
            result[player_index] = ["hu_self"]
            return result
        if any(self.kong_allowed(player_index, t, "G") for t in set(player.hand_tiles)):
            result[player_index].append("angang")
        if any(self.kong_allowed(player_index, t, "added") for t in set(player.hand_tiles)):
            result[player_index].append("jiagang")
        player.riichi_candidate_cuts = self.ready_candidate_cuts(player_index)
        if player.riichi_candidate_cuts:
            result[player_index].append("riichi_cut")
        result[player_index].append("cut")
        return result

    def check_discard_actions(self, tile):
        result = {i: [] for i in range(4)}
        discarder = self.player_list[self.current_player_index]
        if not discarder.discard_tiles or discarder.discard_tiles[-1] == 0:
            return result  # 暗扣报听弃牌不是可报牌的公开牌。
        for distance in (1, 2, 3):
            index = (self.current_player_index + distance) % 4
            player = self.player_list[index]
            if "peida" in player.tag_list:
                continue
            detail = self.score_candidate(index, "discard", tile)
            if detail:
                action = hu_action_for_player(self.current_player_index, index)
                result[index] = [action]
                self.result_dict[action] = detail
                continue
            if self.can_take_normal_tile():
                if not player.ready_locked and player.hand_tiles.count(tile) >= 2 and tile not in player.passed_pungs:
                    result[index].append("peng")
                if self.kong_allowed(index, tile, "g"):
                    result[index].append("gang")
                if result[index]:
                    result[index].append("pass")
        return result

    def check_added_kong_actions(self, tile):
        return {i: [] for i in range(4)}  # 底本不设抢杠。

    def after_claim_actions(self):
        index = self.current_player_index
        player = self.player_list[index]
        player.riichi_candidate_cuts = self.ready_candidate_cuts(index)
        return {i: (["riichi_cut", "cut"] if player.riichi_candidate_cuts else ["cut"])
                if i == index else [] for i in range(4)}

    def _declare_ready(self, index):
        player = self.player_list[index]
        position = len(player.discard_tiles) - 1
        player.concealed_discards[position] = player.discard_tiles[position]
        player.discard_tiles[position] = 0
        super()._declare_ready(index)
        self._record_state("concealed_discard", index, position)

    def _register_initial_heavenly_ready(self):
        pass

    def _revoke_qualification(self, *args, **kwargs):
        pass

    def _liability_payer_for_win(self, *args):
        return None

    def _remember_claim_liability(self, *args):
        pass

    def enter_water(self, index):
        return False  # 有和必和在权威动作层执行，不允许客户端制造过和。

    def _declared_ready_auto_jiagang_tile(self, index):
        return None  # 原文允许杠，并未强制加杠。

    def build_concealed_kong_mask(self, tiles):
        return [v for i, tile in enumerate(tiles) for v in (2 if i in (0, 3) else 0, tile)]

    def build_private_hand_action_info(self, index):
        return {"riichi_candidate_cuts": self.player_list[index].riichi_candidate_cuts}

    def build_private_do_action_info(self, actor, viewer_index):
        payload = {"tile_count": self.playable_wall_count()}
        if self.concealed_discard_actor == actor:
            payload.update(concealed_discard=True)
            if viewer_index != actor:
                payload["cut_tile"] = 0
        return payload

    async def execute_cut(self, index, data, *, declare_ready=False, is_timeout_action=False):
        if index != self.current_player_index:
            return
        if self.score_candidate(index, "self_draw"):
            self.accept_self_draw(index)
            return
        self.concealed_discard_actor = index if declare_ready else None
        try:
            await super().execute_cut(index, data, declare_ready=declare_ready, is_timeout_action=is_timeout_action)
        finally:
            self.concealed_discard_actor = None

    async def execute_angang(self, index, tile):
        if index != self.current_player_index or not self.kong_allowed(index, tile, "G"):
            return
        if "hu_self" in self.check_hand_actions(index)[index]:
            self.accept_self_draw(index)
            return
        before = len(self.player_list[index].combination_tiles)
        await super().execute_angang(index, tile)
        if len(self.player_list[index].combination_tiles) > before:
            self.kong_ledger.append((index, tile, True))

    async def execute_jiagang(self, index, tile):
        if index != self.current_player_index or not self.kong_allowed(index, tile, "added"):
            return
        if "hu_self" in self.check_hand_actions(index)[index]:
            self.accept_self_draw(index)
            return
        await super().execute_jiagang(index, tile)
        if f"g{tile}" in self.player_list[index].combination_tiles:
            self.kong_ledger.append((index, tile, False))

    async def execute_claim(self, index, action_type):
        river = self.player_list[self.current_player_index].discard_tiles
        if not river or action_type not in ("peng", "gang"):
            return
        tile = river[-1]
        if action_type not in self.check_discard_actions(tile).get(index, ()):
            return
        before = len(self.player_list[index].combination_tiles)
        await super().execute_claim(index, action_type)
        if action_type == "gang" and len(self.player_list[index].combination_tiles) > before:
            self.kong_ledger.append((index, tile, False))

    async def resolve_discard_responses(self, responses, allowed):
        tile = self.player_list[self.current_player_index].discard_tiles[-1]
        for index, actions in allowed.items():
            if "peng" in actions and responses.get(index, {}).get("action_type") not in ("peng", "gang"):
                self.player_list[index].passed_pungs.add(tile)
        await super().resolve_discard_responses(responses, allowed)

    async def _deal_normal(self):
        self.player_list[(self.current_player_index + 1) % 4].passed_pungs.clear()
        await super()._deal_normal()

    async def _deal_supplement(self):
        self.player_list[self.current_player_index].passed_pungs.clear()
        await super()._deal_supplement()

    def _dealer_continues(self):
        return self.pending_winners[0]["index"] == 0 if self.pending_winners else not self.kong_ledger

    def settlement(self):
        if not self.pending_winners:
            return sx.payments()
        item = self.pending_winners[0]
        payer = item.get("payer")
        return sx.payments(item["index"], base_score=item["detail"]["base_score"],
            discarder=payer, discarder_ready=payer is not None and self.player_list[payer].declared_ready,
            kongs=self.kong_ledger)

    async def _settle_hand(self, scores_before):
        continues = self._dealer_continues()
        match_end = self.current_round >= self.max_round * 4 and not continues
        next_status = "match_end" if match_end else "round_end_by_ready"
        settlement = self.settlement()
        changes = settlement["total"]
        for index, player in enumerate(self.player_list):
            player.score += changes[index]
            player.score_history.append(f"{changes[index]:+d}" if changes[index] else "0")
            player.round_number_history.append(self.current_round)
        if self.pending_winners:
            item = self.pending_winners[0]
            winner = self.player_list[item["index"]]
            labels = list(item["detail"]["fan_names"])
            if settlement["liable_payer"] is not None:
                labels.append("未报听放铳包全桌")
            if self.kong_ledger:
                labels.append("杠账结算")
            self._record_state("shanxi_settlement", settlement)
            counter = winner.record_counter
            counter.recorded_fans.append(labels)
            counter.win_score += changes[item["index"]]
            counter.win_turn += self.xunmu
            ron = item["source"] == "discard"
            if ron:
                counter.dianhe_times += 1
                loser = self.player_list[item["payer"]].record_counter
                loser.fangchong_times += 1
                loser.fangchong_score += -changes[item["payer"]]
            else:
                counter.zimo_times += 1
            player_action_record_hu(self, hu_class=item["hu_class"], hu_score=item["detail"]["base_score"],
                hu_fan=labels, hepai_player_index=item["index"], score_changes=[changes[i] for i in range(4)],
                hepai_tile=item["tile"], ron_discarder_index=item["payer"] if ron else None,
                multi_ron=False if ron else None, recycle_discard=True if ron else None)
            await broadcast_result(self, hepai_player_index=item["index"],
                player_to_score={p.player_index:p.score for p in self.player_list},
                hu_score=item["detail"]["base_score"], hu_fan=labels, hu_class=item["hu_class"],
                hepai_player_hand=list(winner.hand_tiles) + ([item["tile"]] if ron else []),
                hepai_player_huapai=[], hepai_player_combination_mask=winner.combination_mask,
                score_changes={p.original_player_index:changes[p.player_index] for p in self.player_list},
                revealed_angang_masks=build_revealed_angang_masks(self.player_list), hepai_tile=item["tile"],
                multi_ron=False if ron else None, ron_discarder_index=item["payer"] if ron else None,
                recycle_discard=True if ron else None, next_status=next_status)
            if not match_end:
                await self.run_hu_result_ready_phase(len(labels))
        else:
            self.hu_class = "liuju"
            self._record_liuju(self.draw_reason)
            await broadcast_result(self, hu_class="liuju", score_changes=changes,
                revealed_angang_masks=build_revealed_angang_masks(self.player_list), next_status=next_status)
            await asyncio.sleep(liuju_ready_wait_seconds())
        record_fulu_rounds_for_players(self.player_list)
        for player in self.player_list:
            player.record_counter.round_score_total += player.score - scores_before[player.original_player_index]
        player_action_record_round_end(self)
        return continues, match_end

    async def game_loop_chinese(self):
        self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = setup_random_seed_system(self.room_random_seed or None)
        capture_player_entry_order(self)
        random.Random(self.master_seed).shuffle(self.player_list)
        for index, player in enumerate(self.player_list):
            player.player_index = player.original_player_index = index
        init_game_record(self)
        self.game_record["game_title"].update(self.build_record_title_fields())
        while self.current_round <= self.max_round * 4:
            before = {p.original_player_index:p.score for p in self.player_list}
            self._reset_hand_runtime()
            self.init_tiles()
            self.current_player_index = 0
            await broadcast_game_start(self)
            init_game_round(self)
            await self._run_hand()
            continues, match_end = await self._settle_hand(before)
            if match_end:
                break
            self.dealer_streak = self.dealer_streak + 1 if continues else 0
            next_game_round_classical_switchseat(self, keep_current_round=continues, keep_dealer_seat=continues)
        end_game_record(self)
        assign_competition_final_ranks(self.player_list)
        match_type = f"{self.max_round}/4"
        store = getattr(self.db_manager, "store_shanxi_game_record", None)
        game_id = store(self.game_record, self.player_list, self.room_type, match_type) if store else None
        remember_local_record_detail(self, game_id, match_type)
        await broadcast_game_end(self)
        await self.spectator_manager.send_final_record_and_close()
        await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)
