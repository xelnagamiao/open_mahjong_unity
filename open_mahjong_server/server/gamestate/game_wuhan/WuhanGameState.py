import asyncio
import random
import logging
from copy import deepcopy
from dataclasses import replace

from ...game_calculation.wuhan import rules as book
from ..game_taiwan.TaiwanGameState import TaiwanGameState
from ..game_taiwan.action_check import hu_action_for_player
from ..game_taiwan.boardcast import broadcast_game_start, broadcast_do_action, broadcast_game_end
from ..game_guobiao.combination_mask_view import build_revealed_angang_masks
from ..public.game_record_manager import (
    append_action_tick, capture_player_entry_order, init_game_record, init_game_round,
    player_action_record_buhua, player_action_record_hu, player_action_record_round_end,
    end_game_record, remember_local_record_detail,
)
from ..public.hand_slot_utils import clear_draw_slot, remove_cut_tile, resolve_is_mo_buhua
from ..public.logic_common import assign_competition_final_ranks
from ..public.random_seed_manager import setup_random_seed_system, derive_round_seed
from ..public.round_end_timing import liuju_ready_wait_seconds
from ...database.fulu_utils import record_fulu_rounds_for_players
from ..public.ai.bot_executor import run_room_bot_cpu
from .hints import compute_hand
from .timing import ActionClock, TimedActionQueue, PHASES, collect_responses


class WuhanGameState(TaiwanGameState):
    flower_tiles = ()
    structure_tiles = book.TILES
    claim_response_seconds = 3

    def __init__(self, game_server, room_data, calculation_service, db_manager, gamestate_id):
        if room_data.get("sub_rule", book.SUB_RULE) != book.SUB_RULE:
            raise ValueError("不支持的武汉麻将子规则")
        config = book.normalize_config(room_data.get("detailed_config"))
        super().__init__(game_server, {**room_data, "detailed_config": None}, calculation_service, db_manager, gamestate_id)
        self.room_rule, self.sub_rule = "wuhan", book.SUB_RULE
        self.rules = replace(self.rules, dead_wall_count=9, multi_win_mode="head_bump",
            ready_qualification_mode="disabled", public_ready_enabled=False,
            chow_discard_restriction_mode="none", pung_same_tile_discard_forbidden=False,
            allow_kong_from_upper_discard=True, direct_kong_replacement_win_allowed=True,
            allow_rob_added_kong=True, missed_win_blocks_self_draw=False,
            missed_win_blocks_claims=False, missed_win_released_by_kong=False,
            seven_flowers_steal_eighth_enabled=False)
        self.rules_dict, self.dead_wall_count = config, 9
        self.open_cuohe, self.hepai_limit = False, 6
        self.previous_gold = None
        self.action_clock = ActionClock(self)
        self.action_queues = {i: TimedActionQueue(self.action_clock) for i in range(4)}
        from .bot import wuhan_bot_action
        self.smart_bot_action = wuhan_bot_action

    def _reset_taiwan_players(self):
        super()._reset_taiwan_players()
        if hasattr(self, "action_clock"):
            self.action_clock.reset()
        self.indicator = None
        self.joker, self.skins = None, (45,)
        self.tail_single = False
        self.sea_phase = False
        self.sea_start_count = None
        self._terminal_result = None
        self._hand_settled = False
        self._score_event = None
        self._hint_cache = {}
        self._hint_keys = {}
        self._recorded_hints = {}
        self._recorded_public = None
        self._generation = getattr(self, "_generation", 0) + 1
        self.rain = {}
        self.push_liability = set()
        self.bonus_paid = False
        self.bonus_events = []
        for p in self.player_list:
            p.wuhan_added = set()
        self._round_start_scores = {p.original_player_index: p.score for p in self.player_list}

    def init_tiles(self):
        self.round_random_seed = derive_round_seed(self.master_seed, self.round_index)
        self.tiles_list = [tile for tile in book.TILES for _ in range(4)]
        random.Random(self.round_random_seed).shuffle(self.tiles_list)
        for _ in range(3):
            for p in self.player_list:
                p.hand_tiles.extend(self.tiles_list[:4])
                del self.tiles_list[:4]
        last, self.tiles_list = self.tiles_list[:5], self.tiles_list[5:]
        for index, p in enumerate(self.player_list):
            p.hand_tiles.append(last[index])
        self.player_list[0].hand_tiles.append(last[4])
        self.player_list[0].has_draw_slot = True
        self.player_list[0].last_drawn_tile = last[4]

    async def _opening_flower_replacement(self):
        pass

    def _register_initial_heavenly_ready(self):
        pass

    def _mark_eight_flowers_if_ready(self, *args, **kwargs):
        pass

    def ready_candidate_cuts(self, index):
        return {}

    def enter_water(self, index):
        return False

    def _liability_payer_for_win(self, *args):
        return None

    def claim_clock(self, player, reconnecting=False):
        return self.action_clock.project(player) or (player.remaining_time, self.step_time)

    def on_action_window_broadcast(self):
        fresh = self.action_clock.begin()
        window = self.action_clock.ensure()
        if window:
            self.waiting_players_list = sorted(i for i, a in self.action_dict.items() if a and i not in window.resolved)
        return fresh

    def on_action_window_delivered(self, index):
        self.action_clock.delivered(index)

    def is_action_pending(self, index):
        window = self.action_clock.ensure()
        return bool(self.action_dict.get(index)) and (window is None or index not in window.resolved)

    def prepare_action_window(self):
        window = self.action_clock.ensure()
        if window and window.prepared:
            return
        super().prepare_action_window()
        if window:
            window.prepared = True

    async def collect_action_responses(self):
        if self.game_status in PHASES:
            return await collect_responses(self)
        from ..game_taiwan.wait_action import _collect_responses
        return await _collect_responses(self)

    def table_snapshot(self):
        rows = [{"melds": list(p.combination_tiles), "specials": list(p.huapai_list),
                 "added": sorted(p.wuhan_added)} for p in self.player_list]
        pending = self._pending_jiagang
        if pending:
            rows[pending["player_index"]]["melds"] = list(pending["combination_tiles"])
        return rows

    def refresh_waits(self, index):
        p = self.player_list[index]
        p.waiting_tiles = book.waits(p.hand_tiles, p.combination_tiles, self.indicator) if self.indicator else set()
        return p.waiting_tiles

    def liabilities(self, index, source, payer, detail):
        liable = set(self.push_liability) if not self.all_public_minimum() else set()
        if source == "robbing_kong":
            liable.add(payer)
        if payer is not None and len(self.player_list[index].hand_tiles) == 1:
            hand = self.player_list[payer].hand_tiles
            if not all(t in book.JIANG and t != self.joker for t in hand):
                liable.add(payer)
        if "清一色" in detail["fan_names"] and index in self.rain:
            liable.update(self.rain[index]["payers"])
        return liable - {index}

    def score_candidate(self, index, source, tile=None, **kwargs):
        p = self.player_list[index]
        self_draw = source == "self_draw"
        if self.indicator is None or "peida" in p.tag_list or not book.open_count(p.combination_tiles):
            return None
        if self_draw:
            tile = p.last_drawn_tile if tile is None else tile
            if not p.has_draw_slot or tile not in p.hand_tiles:
                return None
        elif source not in ("discard", "robbing_kong") or tile == self.joker or tile in self.skins or self.sea_phase:
            return None
        hand = list(p.hand_tiles) + ([] if self_draw else [tile])
        detail = book.hand_detail(hand, p.combination_tiles, self.indicator,
            replacement=self_draw and self.last_draw_after_kong, sea=self_draw and self.sea_phase,
            robbing=source == "robbing_kong")
        if detail is None:
            return None
        payer = None if self_draw else self.current_player_index
        previous = next((q.player_index for q in self.player_list if q.original_player_index == self.previous_gold), None)
        payment = book.settlement(detail, index, payer, self.table_snapshot(), self.indicator,
            previous_gold=previous, liabilities=self.liabilities(index, source, payer, detail))
        return {**detail, **payment, "is_win": True} if payment else None

    def can_take_normal_tile(self):
        return len(self.tiles_list) > (self.sea_start_count - 4 if self.sea_start_count is not None else 9)

    def playable_wall_count(self):
        reserve = self.sea_start_count - 4 if self.sea_start_count is not None else 9 + int(not self.tail_single)
        return max(0, len(self.tiles_list) - reserve)

    def can_establish_kong(self):
        return not self.sea_phase and len(self.tiles_list) > 13

    def can_take_supplement_tile(self):
        return len(self.tiles_list) > 9 and not self.sea_phase

    def can_keep_wall_after_kong(self):
        return self.can_establish_kong()

    def _on_kong_established(self):
        return False

    def _take_supplement_tile(self):
        tile = self.tiles_list.pop(-1 if self.tail_single else -2)
        self.tail_single = not self.tail_single
        return tile

    def kong_allowed(self, index, tile, kind):
        if type(index) is not int or index not in range(4) or type(tile) is not int or tile not in book.TILES or not self.can_establish_kong():
            return False
        p = self.player_list[index]
        if "peida" in p.tag_list:
            return False
        if tile == self.joker or tile in self.skins:
            return kind == "concealed" and tile in p.hand_tiles
        return p.hand_tiles.count(tile) >= {"concealed": 4, "direct": 3, "added": 1}.get(kind, 99) and (
            kind != "added" or f"k{tile}" in p.combination_tiles)

    def is_single_tile_kong(self, index, tile):
        return (tile == self.joker or tile in self.skins) and self.kong_allowed(index, tile, "concealed")

    def check_hand_actions(self, index):
        result = {i: [] for i in range(4)}
        detail = self.score_candidate(index, "self_draw")
        self.result_dict.pop("hu_self", None)
        if detail:
            self.result_dict["hu_self"] = detail
            result[index].append("hu_self")
        if self.sea_phase:
            result[index].append("pass")
            return result
        for action, kind in (("angang", "concealed"), ("jiagang", "added")):
            if any(self.kong_allowed(index, t, kind) for t in set(self.player_list[index].hand_tiles)):
                result[index].append(action)
        result[index].append("cut")
        return result

    def after_claim_actions(self):
        return self.check_hand_actions(self.current_player_index)

    def check_discard_actions(self, tile):
        result = {i: [] for i in range(4)}
        if self.indicator is None or tile == self.joker or tile in self.skins or self.sea_phase:
            return result
        for distance in (1, 2, 3):
            index = (self.current_player_index + distance) % 4
            p = self.player_list[index]
            if "peida" in p.tag_list:
                continue
            detail = self.score_candidate(index, "discard", tile)
            if detail:
                word = hu_action_for_player(self.current_player_index, index)
                result[index].append(word)
                self.result_dict[word] = detail
            if self.can_take_normal_tile():
                if p.hand_tiles.count(tile) >= 2:
                    result[index].append("peng")
                if self.kong_allowed(index, tile, "direct"):
                    result[index].append("gang")
                if distance == 1 and tile < 40:
                    for action in ("chi_left", "chi_mid", "chi_right"):
                        code, required, _ = self._claim_mask(action, tile, "left")
                        tiles = book.meld_tiles(code)
                        if tiles and not any(t == self.joker or t in self.skins for t in tiles) and all(t in p.hand_tiles for t in required):
                            result[index].append(action)
            if result[index]:
                result[index].append("pass")
        return result

    def check_added_kong_actions(self, tile):
        result = {i: [] for i in range(4)}
        for distance in (1, 2, 3):
            index = (self.current_player_index + distance) % 4
            detail = self.score_candidate(index, "robbing_kong", tile)
            if detail:
                word = hu_action_for_player(self.current_player_index, index)
                result[index] = [word, "pass"]
                self.result_dict[word] = detail
        return result

    def _remember_claim_liability(self, index, payer, tile):
        melds = [c for c in self.player_list[index].combination_tiles if c[0] != "G"]
        tiles = [t for c in melds for t in book.meld_tiles(c)]
        if len(melds) == 3 and tiles and all(t < 40 for t in tiles) and len({t // 10 for t in tiles}) == 1:
            self.rain[index] = {"suit": tiles[0] // 10, "payers": {payer}}

    async def _reveal_indicator(self):
        if self.indicator is not None:
            return
        self.indicator = self.tiles_list.pop(0)
        self.joker, self.skins = book.special_tiles(self.indicator)
        append_action_tick(self, ["wuhan", "indicator", self.indicator])
        await broadcast_do_action(self, action_list=[], action_player=self.current_player_index)

    def _track_liability_cut(self, index, tile):
        for owner, obligation in self.rain.items():
            if index != owner and index not in obligation["payers"] and tile // 10 != obligation["suit"]:
                obligation["payers"].add(index)

    def public_fans(self):
        return [book.table_fan(p["melds"], p["specials"], self.joker, p["added"]) for p in self.table_snapshot()]

    def all_public_minimum(self):
        fans = sorted(self.public_fans())
        return sum(fans[:2]) >= 6

    def push_gold_active(self):
        fans = self.public_fans()
        return any(book.open_count(self.player_list[i].combination_tiles) and
                   all(value + other >= 10 for j, other in enumerate(fans) if i != j)
                   for i, value in enumerate(fans)) and not self.all_public_minimum()

    async def execute_cut(self, index, data, *, declare_ready=False, is_timeout_action=False):
        if index != self.current_player_index or declare_ready or self.sea_phase:
            return
        tile = data.get("TileId")
        p = self.player_list[index]
        if type(tile) is not int or tile not in p.hand_tiles:
            return
        if tile == self.joker or tile in self.skins:
            if self.can_establish_kong():
                await self._special_kong(index, tile, draw=False)
            return
        before = p.discard_count
        owes_push = self.push_gold_active() and any(t in self.skins for t in p.hand_tiles)
        await super().execute_cut(index, data, is_timeout_action=is_timeout_action)
        if p.discard_count != before:
            self._track_liability_cut(index, tile)
            if owes_push:
                self.push_liability.add(index)
        if p.discard_count != before and self.indicator is None:
            await self._reveal_indicator()
            self.action_dict = self.check_discard_actions(tile)
            self.game_status = "waiting_action_after_cut" if any(self.action_dict.values()) else "deal_card"

    async def _special_kong(self, index, tile, *, draw):
        p = self.player_list[index]
        is_mo = resolve_is_mo_buhua(p.hand_tiles, tile, draw_slot=p.has_draw_slot)
        if remove_cut_tile(p.hand_tiles, tile, is_mo, draw_slot=p.has_draw_slot) is None:
            return
        clear_draw_slot(p)
        p.last_drawn_tile = None
        p.huapai_list.append(tile)
        self.last_draw_after_kong = False
        self.opening_dealer_action = False
        player_action_record_buhua(self, tile, index, is_mo)
        append_action_tick(self, ["wuhan", "special", index, tile, draw])
        await broadcast_do_action(self, action_list=["buhua"], action_player=index, buhua_tile=tile, is_mo_buhua=is_mo)
        if self.indicator is None:
            await self._reveal_indicator()
        await self._check_bonus(index)
        if draw:
            self.next_supplement_kind = "angang"
            self.game_status = "deal_card_after_gang"
        else:
            self.game_status = "deal_card"

    async def execute_angang(self, index, tile):
        if index != self.current_player_index or not self.kong_allowed(index, tile, "concealed"):
            return
        if tile == self.joker or tile in self.skins:
            await self._special_kong(index, tile, draw=True)
        else:
            await super().execute_angang(index, tile)
            await self._check_bonus(index)

    async def execute_jiagang(self, index, tile):
        if index == self.current_player_index and self.kong_allowed(index, tile, "added"):
            await super().execute_jiagang(index, tile)

    async def finalize_jiagang(self):
        if self._pending_jiagang is None:
            return
        index, tile = self._pending_jiagang["player_index"], self._pending_jiagang["normal"]
        self.player_list[index].wuhan_added.add(tile)
        await super().finalize_jiagang()
        await self._check_bonus(index)

    async def execute_claim(self, index, action):
        p = self.player_list[self.current_player_index]
        if not p.discard_tiles or action not in self.check_discard_actions(p.discard_tiles[-1]).get(index, ()):
            return
        await super().execute_claim(index, action)
        if action == "gang":
            await self._check_bonus(index)

    async def _check_bonus(self, index):
        if self.bonus_paid:
            return
        p = self.player_list[index]
        skins = sum(t in self.skins for t in p.huapai_list)
        units = book.kong_units(p.combination_tiles, p.huapai_list, self.joker)
        if skins < 7 and units < 10:
            return
        self.bonus_paid = True
        points = 200 if self.joker in book.FENG or self.joker in book.JIANG else 100
        changes = {i: 3 * points if i == index else -points for i in range(4)}
        for i, delta in changes.items():
            self.player_list[i].score += delta
        event = {"kind": "七皮" if skins >= 7 else "十杠", "changes": changes}
        self.bonus_events.append(event)
        append_action_tick(self, ["wuhan", "bonus", [changes[i] for i in range(4)], event["kind"], "gs", *[changes[i] for i in range(4)]])
        self._score_event = {"gang_score_changes": changes, "gang_score_type": event["kind"]}
        try:
            await broadcast_do_action(self, action_list=[], action_player=index)
        finally:
            self._score_event = None

    async def _deal_normal(self):
        if not self.sea_phase and len(self.tiles_list) <= 14:
            self.sea_phase = True
            self.sea_start_count = len(self.tiles_list)
            append_action_tick(self, ["wuhan", "sea", self.sea_start_count])
        await super()._deal_normal()

    async def wait_action(self):
        if self.game_status == "waiting_hand_action" and self.sea_phase:
            responses, _ = await self.collect_action_responses()
            index = self.current_player_index
            if responses.get(index, {}).get("action_type") == "hu_self" and self.score_candidate(index, "self_draw"):
                self.accept_self_draw(index)
            else:
                self.game_status = "deal_card"
            return True
        return await super().wait_action()

    async def execute_timeout_cut(self, index):
        if self.sea_phase:
            self.game_status = "deal_card"
            return
        await super().execute_timeout_cut(index)

    def public_info(self):
        return {"indicator": self.indicator, "joker": self.joker, "skins": list(self.skins), "sea": self.sea_phase,
            "rain": {i: {"suit": r["suit"], "payers": sorted(r["payers"])} for i, r in self.rain.items()},
            "fan_by_player": self.public_fans(),
            "previous_gold": self.previous_gold, "push_gold": self.push_gold_active(), "push_liability": sorted(self.push_liability)}

    def build_game_info_fields(self):
        return {"detailed_config": self.rules_dict, "wuhan_info": self.public_info()}

    def build_record_round_fields(self):
        return {"detailed_config": self.rules_dict, "wuhan_info": self.public_info()}

    async def prepare_private_fields(self, indices):
        public = self.public_info()
        if public != self._recorded_public and f"round_index_{self.round_index}" in self.game_record.get("game_round", {}):
            append_action_tick(self, ["wuhan", "state", public])
            self._recorded_public = deepcopy(public)
        if self.indicator is None:
            return
        generation = self._generation
        table = self.table_snapshot()
        for index in indices:
            p = self.player_list[index]
            args = (list(p.hand_tiles), list(p.combination_tiles), self.indicator, table, index, "peida" in p.tag_list, self.sea_phase, next((q.player_index for q in self.player_list if q.original_player_index == self.previous_gold), None))
            key = repr(args)
            if self._hint_keys.get(index) == key:
                continue
            payload = await run_room_bot_cpu(self, compute_hand, *args)
            if generation != self._generation or self.player_list[index] is not p or args[0] != p.hand_tiles or args[1] != p.combination_tiles or table != self.table_snapshot():
                continue
            self._hint_keys[index], self._hint_cache[index] = key, payload
            round_key = f"round_index_{self.round_index}"
            if round_key in self.game_record.get("game_round", {}) and self._recorded_hints.get(index) != key:
                append_action_tick(self, ["wuhan", "hints", index, payload])
                self._recorded_hints[index] = key

    def build_private_game_info_fields(self, index):
        p = self.player_list[index]
        info = {**self.public_info(), "self_has_draw_slot": p.has_draw_slot, "last_drawn_tile": p.last_drawn_tile}
        fields = {"wuhan_info": info}
        payload = self._hint_cache.get(index)
        if payload and (self.tips or self.count_tips) and sorted(payload["source_hand_tiles"]) == sorted(p.hand_tiles) and payload["source_melds"] == p.combination_tiles:
            fields["wuhan_hints"] = payload
        return fields

    def build_private_hand_action_info(self, index):
        return {**self.build_private_game_info_fields(index), "kong_candidates": {
            action: [t for t in sorted(set(self.player_list[index].hand_tiles)) if self.kong_allowed(index, t, kind)]
            for action, kind in (("angang", "concealed"), ("jiagang", "added"))}}

    def build_private_do_action_info(self, actor, viewer):
        return {**self.build_private_game_info_fields(viewer), **(self._score_event or {})}

    async def player_reconnect(self, user_id):
        await super().player_reconnect(user_id)
        connection = self.game_server.user_id_to_connection.get(user_id)
        if connection and self._terminal_result:
            await connection.websocket.send_json(self._terminal_result)

    async def _broadcast_result(self, **fields):
        from ...response import Response, Show_result_info
        self.server_action_tick += 1
        response = Response(type="gamestate/wuhan/show_result", success=True, message="显示结算结果",
            show_result_info=Show_result_info(**fields, action_tick=self.server_action_tick,
                player_to_score={p.player_index: p.score for p in self.player_list},
                revealed_hands={p.player_index: list(p.hand_tiles) for p in self.player_list}))
        self._terminal_result = response.model_dump(exclude_none=True)
        for p in self.player_list:
            try:
                await self.send_to_realtime_spectators(p.player_index, response)
            except Exception:
                logging.getLogger(__name__).exception("武汉结算观战发送失败 seat=%s", p.player_index)
            connection = self.game_server.user_id_to_connection.get(p.user_id)
            if connection and "offline" not in p.tag_list:
                try:
                    await connection.websocket.send_json(self._terminal_result)
                except Exception:
                    logging.getLogger(__name__).exception("武汉结算发送失败 seat=%s", p.player_index)

    async def _settle_hand(self, before):
        match_end = self.current_round >= self.max_round * 4
        if self._hand_settled:
            return match_end
        self._hand_settled = True
        info = {**self.public_info(), "bonus_events": self.bonus_events, "start_scores": before}
        fields = {"hu_class": "liuju", "score_changes": {i: 0 for i in range(4)}}
        if self.pending_winners:
            item = self.pending_winners[0]
            index, detail = item["index"], item["detail"]
            p = self.player_list[index]
            changes = detail["changes"]
            for i, delta in changes.items():
                self.player_list[i].score += delta
            p.record_counter.win_score += changes[index]
            p.record_counter.win_turn += self.xunmu
            if item["source"] == "self_draw":
                p.record_counter.zimo_times += 1
            else:
                p.record_counter.dianhe_times += 1
                payer = self.player_list[item["payer"]]
                payer.record_counter.fangchong_times += 1
                payer.record_counter.fangchong_score += max(0, -changes[item["payer"]])
            self.previous_gold = p.original_player_index if detail["gold"] else None
            player_action_record_hu(self, hu_class=item["hu_class"], hu_score=detail["fan"],
                hu_fan=detail["fan_names"], hepai_player_index=index,
                score_changes=[changes[i] for i in range(4)], hepai_tile=item["tile"])
            fields = dict(hu_class=item["hu_class"], hepai_player_index=index, hu_score=detail["fan"],
                hu_fan=detail["fan_names"], hepai_player_hand=list(p.hand_tiles) + ([] if item["source"] == "self_draw" else [item["tile"]]),
                hepai_player_huapai=list(p.huapai_list), hepai_player_combination_mask=p.combination_mask,
                is_qianggang=item["source"] == "robbing_kong", ron_discarder_index=item["payer"],
                recycle_discard=item["source"] == "discard",
                hepai_tile=item["tile"], score_changes={q.original_player_index: changes[q.player_index] for q in self.player_list})
            info["detail"] = detail
        else:
            self.previous_gold = None
            self.hu_class = "liuju"
            self._record_liuju(self.draw_reason)
        info["round_score_changes"] = {p.original_player_index: p.score - before[p.original_player_index] for p in self.player_list}
        await self._broadcast_result(**fields, wuhan_info=info, revealed_angang_masks=build_revealed_angang_masks(self.player_list),
            next_status="match_end" if match_end else "round_end_by_ready")
        for p in self.player_list:
            delta = p.score - before[p.original_player_index]
            p.score_history.append(f"{delta:+d}" if delta else "0")
            p.round_number_history.append(self.current_round)
            p.record_counter.round_score_total += delta
        record_fulu_rounds_for_players(self.player_list)
        player_action_record_round_end(self)
        if self.pending_winners and not match_end:
            await self.run_hu_result_ready_phase(len(fields["hu_fan"]))
        elif not self.pending_winners:
            await asyncio.sleep(liuju_ready_wait_seconds())
        return match_end

    async def game_loop_chinese(self):
        self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = setup_random_seed_system(self.room_random_seed or None)
        capture_player_entry_order(self)
        random.Random(self.master_seed).shuffle(self.player_list)
        for i, p in enumerate(self.player_list):
            p.player_index = p.original_player_index = i
        init_game_record(self)
        self.game_record["game_title"].update(self.build_record_title_fields())
        while self.current_round <= self.max_round * 4:
            before = {p.original_player_index: p.score for p in self.player_list}
            self._reset_hand_runtime()
            self.init_tiles()
            self.current_player_index = 0
            await broadcast_game_start(self)
            init_game_round(self)
            await self._run_hand()
            if await self._settle_hand(before):
                break
            for p in self.player_list:
                p.player_index = (p.player_index - 1) % 4
                for values in (p.hand_tiles, p.combination_tiles, p.combination_mask, p.discard_tiles, p.discard_origin_tiles, p.huapai_list):
                    values.clear()
                p.remaining_time = self.round_time
            self.player_list.sort(key=lambda p: p.player_index)
            self.current_round += 1
            self.round_index += 1
            self.xunmu = 1
        end_game_record(self)
        assign_competition_final_ranks(self.player_list)
        match_type = f"{self.max_round}/4"
        game_id = self.db_manager.store_wuhan_game_record(self.game_record, self.player_list, self.room_type, match_type)
        remember_local_record_detail(self, game_id, match_type)
        await broadcast_game_end(self)
        await self.spectator_manager.send_final_record_and_close()
        await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)
