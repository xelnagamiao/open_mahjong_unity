"""MIL 红中2024。沿用 TaiwanGameState 的动作窗口、摸打及重连执行器。

本类只定义红中规则的政策：实体红中不能鸣牌，只有自摸，无吃、报听
或过水；杠分即时记账，流局退还，和牌再从牌墙前端扎鸟。
"""

import asyncio
import random
from copy import deepcopy
from dataclasses import replace

from ...game_calculation.hongzhong import rules as book
from ..game_taiwan.TaiwanGameState import TaiwanGameState
from ..game_taiwan.boardcast import broadcast_game_start, broadcast_do_action, broadcast_game_end
from ..game_guobiao.combination_mask_view import build_revealed_angang_masks
from ..public.game_record_manager import (
    capture_player_entry_order, init_game_record, init_game_round, append_action_tick,
    player_action_record_hu, player_action_record_round_end, end_game_record,
    remember_local_record_detail,
)
from ..public.random_seed_manager import setup_random_seed_system, derive_round_seed
from ..public.logic_common import assign_competition_final_ranks
from ..public.round_end_timing import liuju_ready_wait_seconds
from ...database.fulu_utils import record_fulu_rounds_for_players
from .result import broadcast_result
from .hints import compute_hand, match_hint
from ..public.ai.bot_executor import run_room_bot_cpu
from .timing import ActionClock, TimedActionQueue, PHASES, collect_responses


class HongzhongGameState(TaiwanGameState):
    flower_tiles = ()
    structure_tiles = book.NUMBERS
    # Online timing follows room step + remaining hand bank, without a book cap.
    claim_response_seconds = None

    def __init__(self, game_server, room_data, calculation_service, db_manager, gamestate_id):
        if room_data.get("sub_rule", book.SUB_RULE) != book.SUB_RULE:
            raise ValueError("不支持的红中麻将子规则")
        config = book.normalize_config(room_data.get("detailed_config"))
        super().__init__(game_server, {**room_data, "detailed_config": None},
                         calculation_service, db_manager, gamestate_id)
        self.room_rule, self.sub_rule = "hongzhong", book.SUB_RULE
        self.rules = replace(self.rules, dead_wall_count=0, ready_qualification_mode="disabled",
            public_ready_enabled=False, multi_win_mode="head_bump",
            chow_discard_restriction_mode="none", pung_same_tile_discard_forbidden=False,
            allow_kong_from_upper_discard=True, direct_kong_replacement_win_allowed=True,
            allow_rob_added_kong=False, missed_win_blocks_self_draw=False,
            missed_win_blocks_claims=False, missed_win_released_by_kong=False,
            seven_flowers_steal_eighth_enabled=False)
        self.rules_dict, self.dead_wall_count = config, 0
        self.hepai_limit, self.open_cuohe = 0, False
        self.action_clock = ActionClock(self)
        self.action_queues = {index: TimedActionQueue(self.action_clock) for index in range(4)}
        from .bot import hongzhong_bot_action
        self.smart_bot_action = hongzhong_bot_action

    def _reset_taiwan_players(self):
        super()._reset_taiwan_players()
        if hasattr(self, "action_clock"):
            self.action_clock.reset()
        self._hint_generation = getattr(self, "_hint_generation", 0) + 1
        self.kong_ledger = []
        self.birds = []
        self._score_event = None
        self._terminal_result = None
        self._hand_settled = False
        self._tail_upper_next = True
        self._round_start_scores = {p.original_player_index: p.score for p in self.player_list}
        self._hint_cache = {}
        self._score_cache = {}
        self._recorded_hints = {}

    async def player_reconnect(self, user_id):
        player = next((p for p in self.player_list if p.user_id == user_id), None)
        window = self.action_clock.ensure()
        if window is not None and player is not None and self.is_action_pending(player.player_index):
            # A reconnect can send the first ask while the original broadcast is
            # still warming private hints. Its response must already be accepted.
            self.prepare_action_window()
            self._open_pending_action_players(window)
        await super().player_reconnect(user_id)
        connection = self.game_server.user_id_to_connection.get(user_id)
        if (connection is not None and player is not None and window is not None
                and self.action_clock.ensure() is window
                and self.is_action_pending(player.player_index)):
            # setdefault records only a first ask; ordinary reconnect never renews.
            self.action_clock.delivered(player.player_index)
        if connection is not None and self._terminal_result is not None:
            await connection.websocket.send_json(self._terminal_result)

    def _take_supplement_tile(self):
        # 线性牌墙为每墩上、下排列；普通摸牌从前端，补牌从末墩上再下。
        offset = -2 if self._tail_upper_next and len(self.tiles_list) >= 2 else -1
        tile = self.tiles_list.pop(offset)
        self._tail_upper_next = not self._tail_upper_next
        return tile

    def claim_clock(self, player, reconnecting=False):
        return self.action_clock.project(player) or (player.remaining_time, self.step_time)

    def on_action_window_broadcast(self):
        fresh = self.action_clock.begin()
        window = self.action_clock.window
        if self.game_status in PHASES:
            # The real submission entry checks this list before queueing. Open it
            # before sending: later players/spectators may block the broadcast.
            self._open_pending_action_players(window)
        return fresh

    def _open_pending_action_players(self, window):
        self.waiting_players_list = sorted(index for index, actions in self.action_dict.items()
                                           if actions and index not in window.resolved)

    def on_action_window_delivered(self, index):
        self.action_clock.delivered(index)

    def is_action_pending(self, index):
        window = self.action_clock.ensure()
        return bool(self.action_dict.get(index)) and (window is None or index not in window.resolved)

    async def collect_action_responses(self):
        if self.game_status in PHASES:
            return await collect_responses(self)
        from ..game_taiwan.wait_action import _collect_responses
        return await _collect_responses(self)

    def prepare_action_window(self):
        window = self.action_clock.ensure()
        if window is not None and window.prepared:
            # A resend is still the same decision. In-flight timely responses
            # belong to this window and must survive a later original broadcast.
            return
        super().prepare_action_window()
        if window is not None:
            window.prepared = True

    async def wait_action(self):
        window = self.action_clock.ensure()
        index = self.current_player_index
        actions = list(self.action_dict.get(index, []))
        result = await super().wait_action()
        # A valid action word with a forged tile can be rejected by the executor.
        # Reask this decision without renewing its step or charging the bank twice.
        if (window is not None and self.game_status == "waiting_hand_action"
                and self.action_clock.key() == window.key):
            window.resolved.discard(index)
            self.action_dict[index] = actions
        return result

    def refresh_waits(self, index):
        player = self.player_list[index]
        payload = match_hint(self._hint_cache.get(index), player.hand_tiles, player.combination_tiles)
        player.waiting_tiles = set(payload["waiting_tiles"]) if payload else book.waits(player.hand_tiles, player.combination_tiles)
        return player.waiting_tiles

    def score_candidate(self, index, source, tile=None, **_):
        player = self.player_list[index]
        if source != "self_draw" or "peida" in player.tag_list or not player.has_draw_slot:
            return None
        tile = tile if tile is not None else player.last_drawn_tile
        cached = self._score_cache.get(index)
        if cached and cached[0] == (tuple(player.hand_tiles), tuple(player.combination_tiles), tile, self.last_draw_after_kong):
            return deepcopy(cached[1])
        return book.score(player.hand_tiles, player.combination_tiles,
            winning_tile=tile,
            replacement=self.last_draw_after_kong)

    async def _warm_hints(self, index):
        player = self.player_list[index]
        generation, round_index = self._hint_generation, self.round_index
        key = (tuple(player.hand_tiles), tuple(player.combination_tiles), player.last_drawn_tile, self.last_draw_after_kong)
        payload, detail = await run_room_bot_cpu(self, compute_hand, *key)
        # 计算不持有牌局锁；换局、轮庄或后续动作可能先于工作进程结束。
        # 丢弃过期结果，尤其不能将上一局手牌提示记录到新一局牌谱。
        if (generation != self._hint_generation or round_index != self.round_index
                or self.player_list[index] is not player
                or key != (tuple(player.hand_tiles), tuple(player.combination_tiles),
                           player.last_drawn_tile, self.last_draw_after_kong)):
            return
        self._hint_cache[index] = payload
        self._score_cache[index] = (key, detail)
        self._record_hints(index)

    def _record_hints(self, index):
        payload = self._hint_cache.get(index)
        blocked = "peida" in self.player_list[index].tag_list
        key = (tuple(payload["source_hand_tiles"]), tuple(payload["source_melds"]), blocked) if payload else None
        round_key = f"round_index_{self.round_index}"
        if (payload and round_key in self.game_record.get("game_round", {})
                and self._recorded_hints.get(index) != key):
            append_action_tick(self, ["hongzhong", "hints", index, {**payload, "win_blocked": blocked}])
            self._recorded_hints[index] = key

    async def _prepare_hand_action_after_draw(self):
        await self._warm_hints(self.current_player_index)
        await super()._prepare_hand_action_after_draw()

    def ready_candidate_cuts(self, index):
        return {}

    def _register_initial_heavenly_ready(self):
        pass

    def _mark_eight_flowers_if_ready(self, index, **_):
        pass

    def _liability_payer_for_win(self, *args):
        return None

    def _remember_claim_liability(self, *args):
        pass

    def enter_water(self, index):
        return False

    async def _opening_flower_replacement(self):
        player = self.player_list[0]
        player.has_draw_slot = True
        player.last_drawn_tile = player.hand_tiles[-1]
        # 开局提示在 game_start 前预热；这里补上庄家独立摸牌槽的计分缓存。
        await self._warm_hints(0)

    def build_concealed_kong_mask(self, tiles):
        # 暗杠先亮明、最终只扣两端；中间两张保持可见。
        return [2, tiles[0], 0, tiles[1], 0, tiles[2], 2, tiles[3]]

    def kong_allowed(self, index, tile, kind):
        if type(index) is not int or index not in range(4) or kind not in ("concealed", "added", "direct"):
            return False
        player = self.player_list[index]
        if type(tile) is not int or tile not in book.NUMBERS or "peida" in player.tag_list:
            return False
        if not self.can_establish_kong():
            return False
        if kind in ("concealed", "added") and (not player.has_draw_slot or player.last_drawn_tile is None):
            return False
        count = {"concealed": 4, "direct": 3, "added": 1}[kind]
        return (player.hand_tiles.count(tile) >= count
                and (kind != "added" or f"k{tile}" in player.combination_tiles))

    def check_hand_actions(self, index):
        result = {i: [] for i in range(4)}
        self.result_dict.pop("hu_self", None)
        detail = self.score_candidate(index, "self_draw")
        if detail:
            self.result_dict["hu_self"] = detail
            result[index].append("hu_self")
        for kind, action in (("concealed", "angang"), ("added", "jiagang")):
            if any(self.kong_allowed(index, tile, kind) for tile in set(self.player_list[index].hand_tiles)):
                result[index].append(action)
        result[index].append("cut")
        return result

    def check_discard_actions(self, tile):
        result = {i: [] for i in range(4)}
        # 打出海底即流局；红中从不被碰、杠或和。
        if tile not in book.NUMBERS or not self.can_take_normal_tile():
            return result
        for index, player in enumerate(self.player_list):
            if index == self.current_player_index or "peida" in player.tag_list:
                continue
            if player.hand_tiles.count(tile) >= 2:
                result[index].append("peng")
            if self.kong_allowed(index, tile, "direct"):
                result[index].append("gang")
            if result[index]:
                result[index].append("pass")
        return result

    def check_added_kong_actions(self, tile):
        return {i: [] for i in range(4)}

    def build_private_hand_action_info(self, index):
        return {**self.build_private_game_info_fields(index), "kong_candidates": {
            action: [tile for tile in sorted(set(self.player_list[index].hand_tiles))
                     if self.kong_allowed(index, tile, kind)]
            for action, kind in (("angang", "concealed"), ("jiagang", "added"))}}

    def build_private_do_action_info(self, action_player, viewer_index):
        private = self.build_private_game_info_fields(viewer_index) if action_player == viewer_index else {}
        return {**private, **(self._score_event or {})}

    def build_private_game_info_fields(self, index):
        player = self.player_list[index]
        payload = match_hint(self._hint_cache.get(index), player.hand_tiles, player.combination_tiles)
        # Physical draw identity is needed even with strategic hints disabled.
        # This hook is called for the receiving player or authorized view seat.
        fields = {"hongzhong_info": {
            "self_has_draw_slot": bool(player.has_draw_slot),
            "last_drawn_tile": player.last_drawn_tile if player.has_draw_slot else None,
        }}
        if payload and (self.tips or self.count_tips):
            fields["hongzhong_hints"] = {**payload, "win_blocked": "peida" in player.tag_list}
        return fields

    async def execute_cut(self, index, data, *, declare_ready=False, is_timeout_action=False):
        # 红中没有报听，不能通过伪造 riichi_cut 绕过动作窗口取得锁手状态。
        if declare_ready:
            return
        before = self.player_list[index].discard_count
        await super().execute_cut(index, data, is_timeout_action=is_timeout_action)
        if self.player_list[index].discard_count != before:
            await self._warm_hints(index)

    async def _pay_kong(self, index, kind, tile, *, payer=None, drawn=True):
        changes = book.kong_payments(index, kind, payer=payer, drawn=drawn)
        event = dict(player=index, kind=kind, tile=tile, payer=payer, drawn=drawn, changes=changes)
        self.kong_ledger.append(event)
        await self._apply_score_event(changes, "kong_score", kind)

    async def _apply_score_event(self, changes, event_type, kind="refund"):
        for i, delta in changes.items():
            self.player_list[i].score += delta
        append_action_tick(self, ["hongzhong", event_type, [changes[i] for i in range(4)], kind])
        self._score_event = {"gang_score_changes": changes, "gang_score_type": kind}
        try:
            await broadcast_do_action(self, action_list=[], action_player=self.current_player_index)
        finally:
            self._score_event = None

    async def execute_angang(self, index, tile):
        if not self.kong_allowed(index, tile, "concealed"):
            return
        await super().execute_angang(index, tile)
        await self._pay_kong(index, "concealed", tile)

    async def execute_jiagang(self, index, tile):
        if self.kong_allowed(index, tile, "added"):
            await super().execute_jiagang(index, tile)

    async def finalize_jiagang(self):
        pending = self._pending_jiagang
        if pending is None:
            return
        await self._pay_kong(pending["player_index"], "added", pending["normal"],
                             drawn=pending["is_mo_gang"])
        await super().finalize_jiagang()

    async def execute_claim(self, index, action):
        payer = self.current_player_index
        if not self.player_list[payer].discard_tiles:
            return
        tile = self.player_list[payer].discard_tiles[-1]
        if action not in ("peng", "gang") or action not in self.check_discard_actions(tile).get(index, []):
            return
        await super().execute_claim(index, action)
        await self._warm_hints(index)
        if action == "gang":
            await self._pay_kong(index, "direct", tile, payer=payer)

    def init_tiles(self):
        self.round_random_seed = derive_round_seed(self.master_seed, self.round_index)
        self.tiles_list = [tile for tile in book.TILES for _ in range(4)]
        random.Random(self.round_random_seed).shuffle(self.tiles_list)
        for _ in range(3):
            for player in self.player_list:
                player.hand_tiles.extend(self.tiles_list[:4])
                del self.tiles_list[:4]
        last = self.tiles_list[:5]
        del self.tiles_list[:5]
        # 庄家拿上层一张、隔一墩再拿上层一张（线性上/下序列第1、5张）。
        self.player_list[0].hand_tiles.extend((last[0], last[4]))
        # game_start precedes _opening_flower_replacement; its first snapshot
        # must already identify the dealer's physical fourteenth tile.
        self.player_list[0].has_draw_slot = True
        self.player_list[0].last_drawn_tile = last[4]
        for index, position in ((1, 1), (2, 2), (3, 3)):
            self.player_list[index].hand_tiles.append(last[position])

    def next_dealer(self):
        return self.pending_winners[0]["index"] if self.pending_winners else 0

    def build_record_round_fields(self):
        return {"detailed_config": self.rules_dict}

    async def _settle_hand(self, scores_before):
        match_end = self.current_round >= self.max_round * 4
        if self._hand_settled:
            return match_end
        self._hand_settled = True
        next_status = "match_end" if match_end else "round_end_by_ready"
        if self.pending_winners:
            item = self.pending_winners[0]
            detail, index = item["detail"], item["index"]
            self.birds = self.tiles_list[:2]
            del self.tiles_list[:len(self.birds)]
            changes = book.win_payments(index, detail["fan"], self.birds)
            for i, delta in changes.items():
                self.player_list[i].score += delta
            bird_event = {"tiles": self.birds, "hits": book.bird_hits(self.birds), "detail": detail}
            append_action_tick(self, ["hongzhong", "birds", bird_event])
            winner = self.player_list[index]
            winner.record_counter.recorded_fans.append(detail["fan_names"])
            winner.record_counter.win_score += changes[index]
            winner.record_counter.win_turn += self.xunmu
            winner.record_counter.zimo_times += 1
            player_action_record_hu(self, hu_class="hu_self", hu_score=detail["fan"],
                hu_fan=detail["fan_names"], hepai_player_index=index,
                score_changes=[changes[i] for i in range(4)], hepai_tile=item["tile"])
            await broadcast_result(self, hepai_player_index=index,
                player_to_score={p.player_index: p.score for p in self.player_list},
                hu_score=detail["fan"], hu_fan=detail["fan_names"], hu_class="hu_self",
                hepai_player_hand=list(winner.hand_tiles), hepai_player_huapai=[],
                hepai_player_combination_mask=winner.combination_mask,
                score_changes={p.original_player_index: changes[p.player_index] for p in self.player_list},
                revealed_angang_masks=build_revealed_angang_masks(self.player_list),
                hepai_tile=item["tile"], next_status=next_status,
                hongzhong_info={"bird_tiles": self.birds, "bird_hits": bird_event["hits"],
                    "detail": detail, "kong_ledger": self.kong_ledger,
                    "start_scores": scores_before,
                    "round_score_changes": {p.original_player_index: p.score - scores_before[p.original_player_index]
                                            for p in self.player_list}})
            if not match_end:
                await self.run_hu_result_ready_phase(len(detail["fan_names"]))
        else:
            if self.kong_ledger:
                await self._apply_score_event(book.reverse_kong_payments(self.kong_ledger), "kong_refund")
            self.hu_class = "liuju"
            self._record_liuju(self.draw_reason)
            await broadcast_result(self, hu_class="liuju", score_changes={i: 0 for i in range(4)},
                player_to_score={p.player_index: p.score for p in self.player_list},
                revealed_angang_masks=build_revealed_angang_masks(self.player_list), next_status=next_status,
                hongzhong_info={"bird_tiles": [], "bird_hits": 0, "kong_ledger": self.kong_ledger,
                    "start_scores": scores_before, "round_score_changes": {i: 0 for i in range(4)}})
            await asyncio.sleep(liuju_ready_wait_seconds())
        for player in self.player_list:
            delta = player.score - scores_before[player.original_player_index]
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
        while self.current_round <= self.max_round * 4:
            before = {p.original_player_index: p.score for p in self.player_list}
            self._reset_hand_runtime()
            self.init_tiles()
            self.current_player_index = 0
            for index in range(4):
                await self._warm_hints(index)
            await broadcast_game_start(self)
            init_game_round(self)
            for index in range(4):
                self._record_hints(index)
            await self._run_hand()
            if await self._settle_hand(before):
                break
            dealer = self.next_dealer()
            for player in self.player_list:
                player.player_index = (player.player_index - dealer) % 4
                for values in (player.hand_tiles, player.combination_tiles, player.combination_mask,
                               player.discard_tiles, player.discard_origin_tiles, player.huapai_list):
                    values.clear()
                player.remaining_time = self.round_time
            self.player_list.sort(key=lambda p: p.player_index)
            self.current_round += 1
            self.round_index += 1
            self.xunmu = 1
        end_game_record(self)
        assign_competition_final_ranks(self.player_list)
        match_type = f"{self.max_round}/4"
        store = getattr(self.db_manager, "store_hongzhong_game_record", None)
        game_id = store(self.game_record, self.player_list, self.room_type, match_type) if store else None
        remember_local_record_detail(self, game_id, match_type)
        await broadcast_game_end(self)
        await self.spectator_manager.send_final_record_and_close()
        await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)
