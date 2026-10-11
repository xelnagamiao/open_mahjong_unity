import asyncio
from dataclasses import replace

from ...game_calculation.shenyang import rules as book
from ..game_taiwan.TaiwanGameState import TaiwanGameState
from ..game_taiwan.action_check import hu_action_for_player
from ..game_taiwan.boardcast import broadcast_do_action, broadcast_ready_status
from ..game_taiwan.wait_action import _collect_responses
from ..game_shanxi.clock import RoomActionClock, TimedActionQueue
from ..game_shanxi.result import restore_terminal_result
from ..public.game_record_manager import append_action_tick
from ..public.hand_slot_utils import has_draw_slot
from .lifecycle import ShenyangLifecycle
from .protocol import ShenyangProtocol


class ShenyangGameState(ShenyangLifecycle, ShenyangProtocol, TaiwanGameState):
    flower_tiles = ()
    structure_tiles = book.TILES

    def __init__(self, game_server, room_data, calculation_service, db_manager, gamestate_id):
        from ...room.shenyang_room import ShenyangRoomValidator
        if room_data.get("sub_rule", book.SUB_RULE) != book.SUB_RULE:
            raise ValueError("不支持的沈阳子规则")
        ShenyangRoomValidator.valid_config(room_data.get("detailed_config"))
        super().__init__(game_server, {**room_data, "detailed_config": None}, calculation_service, db_manager, gamestate_id)
        self.room_rule, self.sub_rule = "shenyang", book.SUB_RULE
        self.rules = replace(self.rules, dead_wall_count=0, multi_win_mode="head_bump",
            ready_qualification_mode="disabled", public_ready_enabled=True,
            declared_ready_win_policy="allow_pass", declared_ready_auto_added_kong=False,
            allow_kong_from_upper_discard=True, direct_kong_replacement_win_allowed=True,
            allow_rob_added_kong=True, pung_same_tile_discard_forbidden=False,
            chow_discard_restriction_mode="none", missed_win_blocks_self_draw=False,
            missed_win_blocks_claims=False, missed_win_released_by_kong=False,
            seven_flowers_steal_eighth_enabled=False, four_winds_abort=False, four_kongs_abort=False)
        self.rules_dict = {"rule_version": book.VERSION}
        self.dead_wall_count = self.hepai_limit = 0
        self.open_cuohe = False
        self.room_action_clock = RoomActionClock(self)
        self.room_action_clock.statuses = frozenset(("waiting_hand_action", "waiting_action_after_cut", "waiting_action_qianggang"))
        self.action_queues = {i: TimedActionQueue(self.room_action_clock, i) for i in range(4)}
        from .bot import shenyang_bot_action
        self.smart_bot_action = shenyang_bot_action

    def _reset_taiwan_players(self):
        super()._reset_taiwan_players()
        self.long_hair = getattr(self, "previous_draw", False)
        self.final_four = False
        self.tail_tiles = {}
        self.egg_offer = None
        self.egg_ledger = []
        self.kong_discard = False
        self._sy_event = None
        self._terminal_result = None
        self._prepared_hints = {}
        self._recorded_hints = {}
        for p in self.player_list:
            p.passed_fan = -1
            p.opened = False

    def _reset_hand_runtime(self):
        super()._reset_hand_runtime()
        self.room_action_clock.reset_hand()

    def action_clock(self, player, reconnecting=False):
        return self.room_action_clock.snapshot(player)

    claim_clock = action_clock

    def on_action_window_broadcast(self):
        self.room_action_clock.begin()

    def on_action_window_delivered(self, index):
        self.room_action_clock.delivered(index)
        p = self.player_list[index]
        if p.user_id == 0 or "offline" in p.tag_list:
            actions = self.action_dict.get(index, ())
            choice = "sy_hide" if "sy_hide" in actions else "sy_pass" if "sy_pass" in actions else None
            if choice:
                self.action_queues[index].put_nowait({"action_type": choice, "_action_tick": self.server_action_tick})
                self.action_events[index].set()

    def is_action_pending(self, index):
        return not self.room_action_clock.active() or self.room_action_clock.is_pending(index)

    async def collect_action_responses(self):
        return await self.room_action_clock.collect() if self.room_action_clock.active() else await _collect_responses(self)

    async def player_reconnect(self, user_id):
        await super().player_reconnect(user_id)
        await restore_terminal_result(self, user_id)

    def closed(self, index):
        return not self.player_list[index].opened

    def borrowed(self, index):
        codes = {c for p in self.player_list if p.player_index != index for c in p.combination_tiles}
        return tuple(t for code, tiles in book.SPECIAL.items() if code in codes for t in tiles)

    def refresh_waits(self, index):
        p = self.player_list[index]
        p.waiting_tiles = book.waits(p.hand_tiles, p.combination_tiles)
        return p.waiting_tiles

    def score_candidate(self, index, source, tile=None, *, ignore_pass=False, **_):
        p = self.player_list[index]
        self_draw = source == "self_draw"
        if "peida" in p.tag_list or source not in ("self_draw", "discard", "robbing_kong"):
            return None
        if self_draw:
            tile = p.last_drawn_tile if tile is None else tile
            if tile is None or not has_draw_slot(p):
                return None
        elif tile not in book.TILES or self.final_four:
            return None
        hand = list(p.hand_tiles) + ([] if self_draw else [tile])
        detail = book.score(hand, p.combination_tiles, winning_tile=tile, self_draw=self_draw,
            after_kong=self.last_draw_after_kong, last_four=self.final_four,
            robbing_kong=source == "robbing_kong", kong_discard=self.kong_discard,
            long_hair=self.long_hair, declared_ready=p.declared_ready, borrowed=self.borrowed(index))
        if detail is None:
            return None
        payer = None if self_draw else self.current_player_index
        if payer == index:
            return detail
        changes, rows = book.payments(index, detail, discarder=payer, closed=tuple(self.closed(i) for i in range(4)))
        detail = {**detail, "fan": max(r["fan"] for r in rows), "payments": rows, "changes": changes}
        if not self_draw and not ignore_pass and detail["fan"] <= p.passed_fan:
            return None
        return detail

    score_action_candidate = score_candidate

    def ready_candidate_cuts(self, index):
        p = self.player_list[index]
        if p.ready_locked or p.opened or self.final_four or "peida" in p.tag_list or not has_draw_slot(p):
            return {}
        result = {}
        for tile in sorted(set(p.hand_tiles)):
            hand = list(p.hand_tiles)
            hand.remove(tile)
            waiting = book.waits(hand, p.combination_tiles)
            if waiting:
                result[tile] = sorted(waiting)
        return result

    def can_establish_kong(self):
        return not self.final_four and len(self.tiles_list) > 4

    can_keep_wall_after_kong = can_establish_kong
    can_take_supplement_tile = can_establish_kong

    def kong_allowed(self, index, tile, kind):
        p = self.player_list[index]
        if tile not in book.TILES or not self.can_establish_kong() or "peida" in p.tag_list:
            return False
        if kind != "direct" and not has_draw_slot(p):
            return False
        if p.ready_locked and kind == "added":
            return False
        hand, codes = list(p.hand_tiles), list(p.combination_tiles)
        required = {"concealed": 4, "direct": 3, "added": 1}[kind]
        if hand.count(tile) < required:
            return False
        for _ in range(required):
            hand.remove(tile)
        if kind == "added":
            if f"k{tile}" not in codes:
                return False
            codes[codes.index(f"k{tile}")] = f"g{tile}"
        else:
            codes.append(f"{'G' if kind == 'concealed' else 'g'}{tile}")
        return not p.ready_locked or bool(book.waits(hand, codes))

    def special_allowed(self, index, code):
        p = self.player_list[index]
        return (code in book.SPECIAL and not p.ready_locked and not self.final_four
                and has_draw_slot(p) and "peida" not in p.tag_list
                and (code == "Ydragon" or self.can_establish_kong())
                and all(t in p.hand_tiles for t in book.SPECIAL[code]))

    def check_hand_actions(self, index):
        result = {i: [] for i in range(4)}
        p, actions = self.player_list[index], result[index]
        if self.egg_offer is not None:
            actions.extend(("sy_reveal", "sy_hide"))
            return result
        detail = self.score_candidate(index, "self_draw")
        if detail:
            actions.append("hu_self")
            self.result_dict["hu_self"] = detail
        if self.final_four:
            actions.append("sy_pass")
            return result
        for action, kind in (("angang", "concealed"), ("jiagang", "added")):
            if any(self.kong_allowed(index, t, kind) for t in set(p.hand_tiles)):
                actions.append(action)
        actions.extend(action for code, action in (("Ydragon", "sy_xi"), ("Ywind", "sy_wind")) if self.special_allowed(index, code))
        p.riichi_candidate_cuts = self.ready_candidate_cuts(index)
        if p.riichi_candidate_cuts:
            actions.append("riichi_cut")
        actions.append("cut")
        return result

    def check_discard_actions(self, tile):
        result = {i: [] for i in range(4)}
        for distance in (1, 2, 3):
            index = (self.current_player_index + distance) % 4
            p, actions = self.player_list[index], result[index]
            if "peida" in p.tag_list or self.final_four:
                continue
            detail = self.score_candidate(index, "discard", tile)
            if detail:
                action = hu_action_for_player(self.current_player_index, index)
                actions.append(action)
                self.result_dict[action] = detail
            if len(self.tiles_list) >= 4:
                if not p.ready_locked and p.hand_tiles.count(tile) >= 2:
                    actions.append("peng")
                if self.kong_allowed(index, tile, "direct"):
                    actions.append("gang")
                if distance == 1 and p.opened and not p.ready_locked and tile in book.NUMBERS:
                    for a, members in (("chi_left", (tile-2, tile-1)), ("chi_mid", (tile-1, tile+1)), ("chi_right", (tile+1, tile+2))):
                        if all(t in book.NUMBERS and t // 10 == tile // 10 and t in p.hand_tiles for t in members):
                            actions.append(a)
            if actions:
                actions.append("pass")
        return result

    def check_added_kong_actions(self, tile):
        result = {i: [] for i in range(4)}
        for i in range(4):
            if i == self.current_player_index:
                continue
            detail = self.score_candidate(i, "robbing_kong", tile)
            if detail:
                action = hu_action_for_player(self.current_player_index, i)
                result[i] = [action, "pass"]
                self.result_dict[action] = detail
        return result

    def enter_water(self, index):
        action = hu_action_for_player(self.current_player_index, index)
        detail = self.result_dict.get(action)
        if detail:
            self.player_list[index].passed_fan = max(self.player_list[index].passed_fan, detail["fan"])
        return False

    def _finalize_ready_after_discard(self, index, declared):
        if declared:
            self._declare_ready(index)
            self.table_claim_or_kong = True
        p = self.player_list[index]
        detail = self.score_candidate(index, "discard", p.last_discarded_tile, ignore_pass=True)
        if detail:
            closed = tuple(self.closed(i) for i in range(4))
            fans = [book.payments(index, detail, discarder=j, closed=closed)[1][0]["fan"] for j in range(4) if j != index]
            p.passed_fan = max(p.passed_fan, min(fans))
        if not self.table_claim_or_kong and all(p.discard_count == 1 for p in self.player_list):
            self.long_hair |= len({p.last_discarded_tile for p in self.player_list}) == 1

    def _register_initial_heavenly_ready(self):
        pass

    def _revoke_qualification(self, *args, **kwargs):
        return False

    def _remember_claim_liability(self, *args):
        pass

    def _liability_payer_for_win(self, *args):
        return None

    def _is_four_winds_abort(self):
        return False

    async def _opening_flower_replacement(self):
        p = self.player_list[0]
        p.has_draw_slot = True
        p.last_drawn_tile = p.hand_tiles[-1]

    async def _process_drawn_flowers(self, index, origin):
        p = self.player_list[index]
        if not p.declared_ready:
            p.passed_fan = -1
        self.last_draw_after_kong = origin != "normal"
        self.kong_discard = False
        if origin != "normal" and book.egg_points(p.last_drawn_tile):
            self.egg_offer = p.last_drawn_tile
        return True

    async def _deal_normal(self):
        if 0 < len(self.tiles_list) <= 4:
            self.final_four = True
        await super()._deal_normal()

    async def execute_cut(self, index, data, **kwargs):
        if index != self.current_player_index or self.final_four or self.egg_offer is not None:
            return
        self.kong_discard = self.last_draw_after_kong
        await super().execute_cut(index, data, **kwargs)

    async def execute_angang(self, index, tile):
        if index == self.current_player_index and self.egg_offer is None and self.kong_allowed(index, tile, "concealed"):
            await super().execute_angang(index, tile)

    async def execute_jiagang(self, index, tile):
        if index == self.current_player_index and self.egg_offer is None and self.kong_allowed(index, tile, "added"):
            await super().execute_jiagang(index, tile)

    async def execute_claim(self, index, action):
        river = self.player_list[self.current_player_index].discard_tiles
        if not river or action not in self.check_discard_actions(river[-1]).get(index, ()):
            return
        p = self.player_list[index]
        if action != "gang" or not p.declared_ready:
            p.opened = True
        await super().execute_claim(index, action)
        if not p.declared_ready and not book.shapes(p.hand_tiles, p.combination_tiles):
            p.passed_fan = -1

    async def resolve_rob_kong_responses(self, responses, allowed):
        declarer, tile = self.current_player_index, self.jiagang_tile
        await super().resolve_rob_kong_responses(responses, allowed)
        if self.pending_winners:
            p = self.player_list[declarer]
            await self.emit(dict(kind="robbed", player=declarer, tile=tile,
                                 melds=p.combination_tiles, masks=p.combination_mask))

    async def emit(self, event):
        append_action_tick(self, ["sy", event])
        self._sy_event = event
        try:
            await broadcast_do_action(self, action_list=["sy_state"], action_player=self.current_player_index)
        finally:
            self._sy_event = None

    async def execute_special(self, index, code):
        if index != self.current_player_index or self.egg_offer is not None or not self.special_allowed(index, code):
            return False
        p = self.player_list[index]
        physical = book.SPECIAL[code]
        for tile in physical:
            p.hand_tiles.remove(tile)
        p.combination_tiles.append(code)
        mask = [v for t in physical for v in (0, t)]
        p.combination_mask.append(mask)
        self.table_claim_or_kong = True
        if code == "Ywind":
            p.has_draw_slot = False
            p.last_drawn_tile = None
            self.next_supplement_kind = "wind"
            self.game_status = "deal_card_after_gang"
        else:
            p.has_draw_slot = True
            p.last_drawn_tile = p.hand_tiles[-1]
            self.game_status = "waiting_hand_action"
        await self.emit(dict(kind="special", player=index, code=code, physical=list(physical), mask=mask))
        self.action_dict = self.check_hand_actions(index)
        return True

    async def resolve_egg(self, reveal):
        if self.egg_offer is None:
            return
        tile, self.egg_offer = self.egg_offer, None
        if reveal:
            index = self.current_player_index
            points = book.egg_points(tile)
            delta = {i: 3 * points if i == index else -points for i in range(4)}
            for p in self.player_list:
                p.score += delta[p.player_index]
            event = dict(kind="egg", player=index, tile=tile, changes=delta)
            self.egg_ledger.append(event)
            await self.emit(event)
        self.action_dict = self.check_hand_actions(self.current_player_index)

    async def pass_tail(self, index):
        if not self.final_four or index != self.current_player_index:
            return
        p = self.player_list[index]
        tile = p.last_drawn_tile
        if tile is None:
            return
        p.hand_tiles.pop()
        p.has_draw_slot = False
        p.last_drawn_tile = None
        self.tail_tiles[index] = tile
        await self.emit(dict(kind="tail_pass", player=index, tile=tile))
        self.game_status = "deal_card"

    async def wait_action(self):
        responses, allowed = await self.collect_action_responses()
        if self.game_status == "waiting_ready":
            for i in responses:
                self.action_dict[i] = []
            await broadcast_ready_status(self)
            return bool(responses)
        if self.game_status == "waiting_action_after_cut":
            await self.resolve_discard_responses(responses, allowed)
        elif self.game_status == "waiting_action_qianggang":
            await self.resolve_rob_kong_responses(responses, allowed)
        elif self.game_status == "waiting_hand_action":
            i = self.current_player_index
            data = responses.get(i, {})
            action = data.get("action_type")
            if self.egg_offer is not None:
                await self.resolve_egg(action == "sy_reveal")
            elif action == "hu_self":
                self.accept_self_draw(i)
            elif self.final_four:
                await self.pass_tail(i)
            elif action in ("sy_xi", "sy_wind"):
                await self.execute_special(i, "Ydragon" if action == "sy_xi" else "Ywind")
            elif action == "angang":
                await self.execute_angang(i, data.get("target_tile"))
            elif action == "jiagang":
                await self.execute_jiagang(i, data.get("target_tile"))
            elif action in ("cut", "riichi_cut"):
                await self.execute_cut(i, data, declare_ready=action == "riichi_cut")
            else:
                await self.execute_timeout_cut(i)
        return True
