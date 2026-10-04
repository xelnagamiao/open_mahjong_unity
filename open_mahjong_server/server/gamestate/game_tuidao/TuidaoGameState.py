"""MIL 推倒和；复用已有报听回合执行器，规则检查和结算由本模块拥有。"""

import asyncio
import logging
import math
import random
import time
from dataclasses import replace

from ...game_calculation.tuidao import rules as book
from ..game_taiwan.TaiwanGameState import TaiwanGameState
from ..game_taiwan import action_check as actions
from ..game_taiwan.boardcast import broadcast_game_start, broadcast_do_action, broadcast_game_end
from .result import broadcast_result
from ..game_guobiao.combination_mask_view import build_revealed_angang_masks
from ..public.game_record_manager import (capture_player_entry_order, init_game_record, init_game_round,
    append_action_tick, player_action_record_hu, player_action_record_round_end,
    end_game_record, remember_local_record_detail)
from ..public.random_seed_manager import setup_random_seed_system, derive_round_seed
from ..public.logic_common import assign_competition_final_ranks
from ..public.round_end_timing import liuju_ready_wait_seconds
from ...database.fulu_utils import record_fulu_rounds_for_players

logger = logging.getLogger(__name__)


class TuidaoGameState(TaiwanGameState):
    flower_tiles = ()
    structure_tiles = book.TILES
    claim_response_seconds = 3.0

    def __init__(self, game_server, room_data, calculation_service, db_manager, gamestate_id):
        from ...room.tuidao_room import normalize_tuidao_config
        sub_rule = room_data.get("sub_rule", book.SUB_RULE)
        if sub_rule != book.SUB_RULE:
            raise ValueError("不支持的推倒和子规则")
        config = normalize_tuidao_config(room_data.get("detailed_config"))
        super().__init__(game_server, {**room_data, "detailed_config": None},
                         calculation_service, db_manager, gamestate_id)
        self.room_rule, self.sub_rule = "guangdong", book.SUB_RULE
        self.rules = replace(self.rules, dead_wall_count=12, ready_qualification_mode="disabled",
            public_ready_enabled=True, declared_ready_win_policy="allow_pass",
            declared_ready_auto_added_kong=False, multi_win_mode="head_bump",
            chow_discard_restriction_mode="none", pung_same_tile_discard_forbidden=False,
            allow_kong_from_upper_discard=True, direct_kong_replacement_win_allowed=True,
            missed_win_blocks_self_draw=False, missed_win_blocks_claims=False,
            missed_win_released_by_kong=False, seven_flowers_steal_eighth_enabled=False)
        self.rules_dict = config
        self.dead_wall_count = 12
        self.hepai_limit = 0
        self.open_cuohe = False
        from .bot import tuidao_bot_action
        self.smart_bot_action = tuidao_bot_action

    def _reset_taiwan_players(self):
        super()._reset_taiwan_players()
        self.kong_ledger = []
        self._score_event = None
        self._terminal_result = None
        for player in self.player_list:
            player.locked_waits = set()
            player.passed_fan = -1
            player.tuidao_heavenly_ready = False

    async def player_reconnect(self, user_id):
        await super().player_reconnect(user_id)
        connection = self.game_server.user_id_to_connection.get(user_id)
        if connection is not None and self._terminal_result is not None:
            await connection.websocket.send_json(self._terminal_result)

    def can_take_normal_tile(self):
        # 末端单张不计墩数：12、13张均已到六墩；不能固定保留12张。
        return len(self.tiles_list)//2 > 6

    can_take_supplement_tile = can_take_normal_tile

    def playable_wall_count(self):
        return max(0, len(self.tiles_list) - 13)

    def claim_clock(self, player, reconnecting=False):
        elapsed = 0.0
        if reconnecting:
            start = (getattr(self, "_ask_delivered_at", None) or {}).get(player.player_index,
                getattr(self, "_ask_broadcast_time", None))
            if start is not None:
                elapsed = max(0, time.time()-start)
        return 0, math.ceil(max(0, min(3.0, player.remaining_time+self.step_time)-elapsed))

    def refresh_waits(self, index):
        player = self.player_list[index]
        player.waiting_tiles = book.waits(player.hand_tiles, player.combination_tiles)
        return player.waiting_tiles

    def score_candidate(self, index, source, tile=None, *, ignore_pass=False, **_):
        player = self.player_list[index]
        if "peida" in player.tag_list:
            return None
        hand = list(player.hand_tiles)
        self_draw = source == "self_draw"
        if not self_draw:
            if tile is None:
                return None
            hand.append(tile)
        elif tile is None:
            tile = player.last_drawn_tile or (hand[-1] if hand else None)
        first_draw = (not self.table_claim_or_kong and player.discard_count == 0
                      and (self.opening_dealer_action and index == 0 or player.normal_draw_count == 1))
        detail = book.score(hand, player.combination_tiles, winning_tile=tile, self_draw=self_draw,
            declared_ready=player.declared_ready, heavenly_ready=player.tuidao_heavenly_ready,
            first_draw=first_draw, rob_kong=source == "robbing_kong",
            last_discard=source == "discard" and not self.can_take_normal_tile(),
            replacement=self_draw and self.last_draw_after_kong)
        if detail and not self_draw and not ignore_pass and detail["fan"] <= player.passed_fan:
            return None
        return detail

    def ready_candidate_cuts(self, index):
        player = self.player_list[index]
        # 七-1明确为摸牌后的报听，不接受吃碰后立即报听。
        if player.ready_locked or player.last_drawn_tile is None:
            return {}
        result = {}
        for tile in sorted(set(player.hand_tiles)):
            hand = list(player.hand_tiles)
            hand.remove(tile)
            waiting = book.waits(hand, player.combination_tiles)
            if waiting:
                result[tile] = sorted(waiting)
        return result

    def _declare_ready(self, index):
        player = self.player_list[index]
        player.tuidao_heavenly_ready = (not self.table_claim_or_kong and player.discard_count == 1
            and (index == 0 and player.normal_draw_count == 0 or player.normal_draw_count == 1))
        player.locked_waits = set(self.refresh_waits(index))
        super()._declare_ready(index)

    def _register_initial_heavenly_ready(self):
        pass

    def _revoke_qualification(self, index, **_):
        return False  # 合法杠不会撤销公开报听。

    def _mark_eight_flowers_if_ready(self, index, **_):
        pass

    def _liability_payer_for_win(self, *args):
        return None

    def _remember_claim_liability(self, *args):
        pass

    async def _opening_flower_replacement(self):
        player = self.player_list[0]
        player.has_draw_slot = True
        player.last_drawn_tile = player.hand_tiles[-1]

    async def _process_drawn_flowers(self, index, origin):
        self.player_list[index].passed_fan = -1
        return await super()._process_drawn_flowers(index, origin)

    def enter_water(self, index):
        # 原书过水针对放过点和或自己打出和牌张；仅取消自摸不能提高过水门槛。
        # 自己的和牌张实际打出后由 _finalize_ready_after_discard 重新按点和计番。
        if index == self.current_player_index:
            return False
        player = self.player_list[index]
        action = actions.hu_action_for_player(self.current_player_index, index)
        detail = self.result_dict.get(action)
        if detail:
            player.passed_fan = max(player.passed_fan, detail["fan"])
        # 不设置台湾的 water：本规则允许更高番点和、吃碰及摸牌后自摸。
        return False

    def _finalize_ready_after_discard(self, index, declared):
        super()._finalize_ready_after_discard(index, declared)
        player = self.player_list[index]
        detail = self.score_candidate(index, "discard", player.last_discarded_tile, ignore_pass=True)
        if detail:
            player.passed_fan = max(player.passed_fan, detail["fan"])

    def kong_allowed(self, index, tile, kind):
        player = self.player_list[index]
        if type(tile) is not int or tile not in book.TILES or not self.can_establish_kong():
            return False
        if kind in ("concealed", "added") and player.last_drawn_tile is None:
            return False
        hand, melds = list(player.hand_tiles), list(player.combination_tiles)
        count = 4 if kind == "concealed" else 3 if kind == "direct" else 1
        if hand.count(tile) < count or kind == "added" and f"k{tile}" not in melds:
            return False
        for _ in range(count):
            hand.remove(tile)
        if kind == "added":
            melds[melds.index(f"k{tile}")] = f"g{tile}"
        else:
            melds.append(f"{'G' if kind == 'concealed' else 'g'}{tile}")
        if not player.ready_locked:
            return True
        # 保持声明时完整听口；暗杠必须从声明时已有暗刻补成，避免拆顺改型。
        if kind == "concealed" and tile != player.last_drawn_tile:
            return False
        if not player.locked_waits or book.waits(hand, melds) != player.locked_waits:
            return False
        if kind != "added":
            before = list(player.hand_tiles)
            if kind == "concealed":
                before.remove(player.last_drawn_tile)
            # 听口相同还不够：不得把原本可拆成顺子/七对的牌改成杠。
            for waiting in player.locked_waits:
                complete = before+[waiting]
                if book.special_shape(complete, player.combination_tiles):
                    return False
                shapes = book.decompositions(complete, player.combination_tiles, book.STRUCTURE, waiting)
                if any(not any(m.kind == "triplet" and m.tile == tile and not m.external
                               for m in shape.melds) for shape in shapes):
                    return False
        return True

    def check_hand_actions(self, index):
        result = actions.check_action_hand_action(self, index)
        result[index] = [a for a in result[index] if a not in ("angang", "jiagang")]
        if "peida" not in self.player_list[index].tag_list:
            for kind, action in (("concealed", "angang"), ("added", "jiagang")):
                if any(self.kong_allowed(index, t, kind) for t in set(self.player_list[index].hand_tiles)):
                    result[index].insert(0, action)
        return result

    def check_discard_actions(self, tile):
        result = actions.check_action_after_cut(self, tile)
        for i in range(4):
            if i != self.current_player_index and self.player_list[i].ready_locked and self.kong_allowed(i, tile, "direct"):
                result[i] = [a for a in result[i] if a != "pass"] + ["gang", "pass"]
        return result

    def after_claim_actions(self):
        return {i: ["cut"] if i == self.current_player_index else [] for i in range(4)}

    def build_private_hand_action_info(self, index):
        player = self.player_list[index]
        return {"riichi_candidate_cuts": player.riichi_candidate_cuts,
                "kong_candidates": {action: [tile for tile in sorted(set(player.hand_tiles))
                    if self.kong_allowed(index, tile, kind)]
                    for action, kind in (("angang", "concealed"), ("jiagang", "added"))},
                "forbidden_cut_tiles": sorted(set(player.hand_tiles)-{player.last_drawn_tile}) if player.ready_locked else []}

    def build_private_do_action_info(self, action_player, viewer_index):
        if self._score_event:
            return self._score_event
        return {}

    async def _pay_kong(self, index, kind, tile, *, payer=None, drawn=True):
        changes = book.kong_payments(index, kind, payer=payer, drawn=drawn)
        if not any(changes.values()):
            return
        for i, delta in changes.items():
            self.player_list[i].score += delta
        event = dict(player=index, kind=kind, tile=tile, payer=payer, changes=changes)
        self.kong_ledger.append(event)
        append_action_tick(self, ["tuidao", "kong_score", [changes[i] for i in range(4)], kind])
        self._score_event = {"gang_score_changes": changes, "gang_score_type": kind}
        try:
            await broadcast_do_action(self, action_list=[], action_player=index)
        finally:
            self._score_event = None

    async def execute_angang(self, index, tile):
        if not self.kong_allowed(index, tile, "concealed"):
            return
        await super().execute_angang(index, tile)
        await self._pay_kong(index, "concealed", tile)

    async def execute_jiagang(self, index, tile):
        if not self.kong_allowed(index, tile, "added"):
            return
        await super().execute_jiagang(index, tile)

    async def finalize_jiagang(self):
        pending = self._pending_jiagang
        if pending:
            await self._pay_kong(pending["player_index"], "added", pending["normal"], drawn=pending["is_mo_gang"])
        await super().finalize_jiagang()

    async def execute_claim(self, index, action):
        payer = self.current_player_index
        if not self.player_list[payer].discard_tiles:
            return
        tile = self.player_list[payer].discard_tiles[-1]
        if action not in self.check_discard_actions(tile).get(index, []):
            return
        await super().execute_claim(index, action)
        if action == "gang":
            await self._pay_kong(index, "direct", tile, payer=payer)
        elif action == "peng" and not book.is_complete(self.player_list[index].hand_tiles, self.player_list[index].combination_tiles):
            self.player_list[index].passed_fan = -1

    def init_tiles(self):
        self.round_random_seed = derive_round_seed(self.master_seed, self.round_index)
        wall = [t for t in book.TILES for _ in range(4)]
        random.Random(self.round_random_seed).shuffle(wall)
        self.tiles_list = wall
        for player in self.player_list:
            player.hand_tiles = [wall.pop(0) for _ in range(13)]
        self.player_list[0].hand_tiles.append(wall.pop(0))

    def next_dealer(self):
        # 原书只规定庄和连庄/闲和轮庄；未定荒庄，线上补则取荒庄连庄。
        return 0 if not self.pending_winners or self.pending_winners[0]["index"] == 0 else 1

    def build_record_round_fields(self):
        return {"detailed_config": self.rules_dict}

    async def _settle_hand(self, scores_before):
        match_end = self.current_round >= self.max_round*4
        next_status = "match_end" if match_end else "round_end_by_ready"
        if self.pending_winners:
            item = self.pending_winners[0]
            detail, index = item["detail"], item["index"]
            changes = book.payments(index, detail["fan"], item["payer"])
            for i, delta in changes.items():
                self.player_list[i].score += delta
            winner = self.player_list[index]
            counter = winner.record_counter
            counter.recorded_fans.append(detail["fan_names"])
            counter.win_score += changes[index]
            counter.win_turn += self.xunmu
            if item["source"] == "self_draw":
                counter.zimo_times += 1
            else:
                counter.dianhe_times += 1
                self.player_list[item["payer"]].record_counter.fangchong_times += 1
                self.player_list[item["payer"]].record_counter.fangchong_score -= changes[item["payer"]]
            ron = item["source"] == "discard"
            player_action_record_hu(self, hu_class=item["hu_class"], hu_score=detail["fan"],
                hu_fan=detail["fan_names"], hepai_player_index=index,
                score_changes=[changes[i] for i in range(4)], hepai_tile=item["tile"],
                ron_discarder_index=item["payer"] if ron else None, recycle_discard=True if ron else None)
            await broadcast_result(self, hepai_player_index=index,
                player_to_score={p.player_index:p.score for p in self.player_list},
                hu_score=detail["fan"], hu_fan=detail["fan_names"], hu_class=item["hu_class"],
                hepai_player_hand=list(winner.hand_tiles)+([item["tile"]] if item["source"] != "self_draw" else []),
                hepai_player_huapai=[], hepai_player_combination_mask=winner.combination_mask,
                score_changes={p.original_player_index:changes[p.player_index] for p in self.player_list},
                revealed_angang_masks=build_revealed_angang_masks(self.player_list), hepai_tile=item["tile"],
                is_qianggang=True if item["source"] == "robbing_kong" else None,
                ron_discarder_index=item["payer"], recycle_discard=True if ron else None, next_status=next_status)
            if not match_end:
                await self.run_hu_result_ready_phase(len(detail["fan_names"]))
        else:
            self.hu_class = "liuju"
            self._record_liuju(self.draw_reason)
            await broadcast_result(self, hu_class="liuju", score_changes={i:0 for i in range(4)},
                player_to_score={p.player_index:p.score for p in self.player_list},
                revealed_angang_masks=build_revealed_angang_masks(self.player_list), next_status=next_status)
            await asyncio.sleep(liuju_ready_wait_seconds())
        for player in self.player_list:
            delta = player.score-scores_before[player.original_player_index]
            player.score_history.append(f"{delta:+d}" if delta else "0")
            player.round_number_history.append(self.current_round)
            player.record_counter.round_score_total += delta
        record_fulu_rounds_for_players(self.player_list)
        player_action_record_round_end(self)
        return match_end

    async def game_loop_chinese(self):
        self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = setup_random_seed_system(self.room_random_seed or None)
        capture_player_entry_order(self)
        random.Random(self.master_seed).shuffle(self.player_list)
        for i, player in enumerate(self.player_list):
            player.player_index = player.original_player_index = i
        init_game_record(self)
        self.game_record["game_title"].update(self.build_record_title_fields())
        while self.current_round <= self.max_round*4:
            before = {p.original_player_index:p.score for p in self.player_list}
            self._reset_hand_runtime()
            self.init_tiles()
            self.current_player_index = 0
            await broadcast_game_start(self)
            init_game_round(self)
            await self._run_hand()
            if await self._settle_hand(before):
                break
            dealer = self.next_dealer()
            for player in self.player_list:
                player.player_index = (player.player_index-dealer) % 4
                for values in (player.hand_tiles, player.combination_tiles, player.combination_mask,
                               player.discard_tiles, player.discard_origin_tiles, player.huapai_list):
                    values.clear()
                player.remaining_time = self.round_time
            self.player_list.sort(key=lambda p:p.player_index)
            self.current_round += 1
            self.round_index += 1
            self.xunmu = 1
        end_game_record(self)
        assign_competition_final_ranks(self.player_list)
        match_type = f"{self.max_round}/4"
        store = getattr(self.db_manager, "store_tuidao_game_record", None)
        game_id = store(self.game_record, self.player_list, self.room_type, match_type) if store else None
        remember_local_record_detail(self, game_id, match_type)
        await broadcast_game_end(self)
        await self.spectator_manager.send_final_record_and_close()
        await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)
