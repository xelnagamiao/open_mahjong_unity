"""Singapore policy on the shared turn/action, reconnect and replay contracts."""
from dataclasses import replace

from ...game_calculation.singapore import rules as book
from ..game_taiwan.TaiwanGameState import TaiwanGameState
from ..game_taiwan import action_check as actions
from ..game_taiwan.boardcast import broadcast_do_action
from ..public.game_record_manager import append_action_tick
from ..public.hand_slot_utils import has_draw_slot, retain_opening_first_player_draw_slot
from ..public.ai.bot_executor import run_room_bot_cpu
from .lifecycle import SingaporeLifecycle
from .hints import compute_hints


class SingaporeGameState(SingaporeLifecycle, TaiwanGameState):
    flower_tiles = book.BONUS_TILES
    structure_tiles = book.TILES

    def __init__(self, server, room, calculation_service, db, state_id):
        if room.get("sub_rule", book.SUB_RULE) != book.SUB_RULE or len(room["player_list"]) != 4:
            raise ValueError("新加坡 SPGG 规则固定四人")
        super().__init__(server, {**room, "detailed_config": None}, calculation_service, db, state_id)
        self.room_rule, self.sub_rule = book.RULE, book.SUB_RULE
        self.rules = replace(self.rules, dead_wall_count=15, minimum_tai=1, tai_cap=5,
            ready_qualification_mode="disabled", public_ready_enabled=False,
            multi_win_mode="head_bump", chow_discard_restriction_mode="none",
            pung_same_tile_discard_forbidden=False, allow_kong_from_upper_discard=True,
            direct_kong_replacement_win_allowed=True, missed_win_blocks_self_draw=False,
            missed_win_blocks_claims=False, missed_win_released_by_kong=False,
            seven_flowers_steal_eighth_enabled=False)
        self.rules_dict, self.hepai_limit, self.open_cuohe = dict(book.CONFIG), 1, False
        self.use_flowers = True
        for player in self.player_list: player.score = 400
        self._reset_hand_runtime()
        self._stored_match = False
        from .bot import singapore_bot_action
        self.smart_bot_action = singapore_bot_action

    def _reset_taiwan_players(self):
        super()._reset_taiwan_players()
        self.sg_payments, self.sg_paid = [], {i: set() for i in range(4)}
        self.sg_initial_bonus = {i: set() for i in range(4)}
        self.sg_liability = {}
        self.sg_discarded = set()
        self.sg_fresh_discard = False
        self.sg_last_draw_count = 0
        self.sg_kong_chain = 0
        self.sg_replacement = False
        self.sg_last_drawer = None
        self._terminal_result = None
        self._settled_round = None
        self._hint_cache, self._recorded_hints = {}, {}

    def _register_initial_heavenly_ready(self): pass
    def _revoke_qualification(self, *args, **kwargs): pass
    def _mark_eight_flowers_if_ready(self, *args, **kwargs): pass
    def _finalize_ready_after_discard(self, *args, **kwargs): pass
    def enter_water(self, index): return False
    def ready_candidate_cuts(self, index): return {}
    def _remember_claim_liability(self, *args): pass

    def accept_action_ingress(self, index, action):
        return getattr(getattr(self, "vote_manager", None), "phase", None) not in ("paused", "resume_voting", "resume_countdown")

    def refresh_waits(self, index):
        p = self.player_list[index]
        p.waiting_tiles = set(book.structural_waits(tuple(sorted(p.hand_tiles)), tuple(p.combination_tiles)))
        return p.waiting_tiles

    def score_candidate(self, index, source, tile=None, **kwargs):
        p = self.player_list[index]
        self_draw = source == "self_draw"
        if "peida" in p.tag_list or (self_draw and not has_draw_slot(p)):
            return None
        tile = p.last_drawn_tile if tile is None and self_draw else tile
        return book.score(list(p.hand_tiles) + ([] if self_draw else [tile]), p.combination_tiles,
            p.huapai_list, winning_tile=tile, self_draw=self_draw, seat=index, round_wind=self._round_wind(),
            replacement=self_draw and self.sg_replacement, kong_chain=self.sg_kong_chain,
            last_tile=self_draw and self.last_draw_was_last,
            heavenly=self_draw and index == 0 and self.opening_dealer_action and not self.table_claim_or_kong,
            earthly=source == "discard" and self.current_player_index == 0 and
                self.player_list[0].discard_count == 1 and not self.table_claim_or_kong and
                sum(p.normal_draw_count for p in self.player_list) == 0)

    def _kong_candidates(self, index, added=False):
        p = self.player_list[index]
        if not has_draw_slot(p) or not self.can_take_supplement_tile() or any(t in self.flower_tiles for t in p.hand_tiles):
            return []
        return sorted(t for t in set(p.hand_tiles) if t in book.TILES and
            (f"k{t}" in p.combination_tiles if added else p.hand_tiles.count(t) == 4))

    def check_hand_actions(self, index):
        p = self.player_list[index]
        result = {i: [] for i in range(4)}
        if any(t in self.flower_tiles for t in p.hand_tiles):
            result[index] = ["buhua"]
            return result
        value = self.score_candidate(index, "self_draw")
        if value:
            result[index].append("hu_self"); self.result_dict["hu_self"] = value
        if self._kong_candidates(index): result[index].append("angang")
        if self._kong_candidates(index, True): result[index].append("jiagang")
        result[index].append("cut")
        return result

    def check_discard_actions(self, tile):
        return actions.check_action_after_cut(self, tile) if tile in book.TILES else {i: [] for i in range(4)}

    def check_added_kong_actions(self, tile):
        return actions.check_action_jiagang(self, tile)

    def _selected_winners(self, selected):
        return sorted(selected, key=lambda item: (not self.result_dict[item[1]].get("orphans"),
                      (item[0] - self.current_player_index) % 4))[:1]

    def _liability_payer_for_win(self, index, source, payer, tile, value):
        if payer is not None and source == "discard" and self.sg_fresh_discard and self.sg_last_draw_count <= 5:
            return payer
        previous = self.sg_liability.get(index)
        return previous if previous is not None and (payer is None or payer == previous) else None

    def _pay_instant(self, winner, amount, reason, liable=None):
        changes = {i: 0 for i in range(4)}
        for index in range(4):
            if index != winner:
                changes[liable if liable is not None else index] -= amount
                changes[winner] += amount
        for index, delta in changes.items(): self.player_list[index].score += delta
        payment = {"winner": winner, "amount": amount, "reason": reason, "changes": changes}
        self.sg_payments.append(payment)
        append_action_tick(self, ["singapore", "payment", [changes[i] for i in range(4)], reason])
        if any(p.score <= 0 for p in self.player_list):
            self.draw_reason, self.game_status = "capital_exhausted", "END"

    def _publish_flower(self, index, tile):
        super()._publish_flower(index, tile)
        for name, (required, points) in book.marriage_keys(self.player_list[index].huapai_list).items():
            if name not in self.sg_paid[index]:
                self.sg_paid[index].add(name)
                initial = required <= self.sg_initial_bonus[index]
                self._pay_instant(index, points * (2 if initial else 1), name)

    def _flower_special(self):
        for winner, player in enumerate(self.player_list):
            owned = set(player.huapai_list).intersection(book.FLOWERS)
            if len(owned) == 8:
                return {"winner": winner, "payer": None, "tile": player.huapai_list[-1]}
            if len(owned) == 7:
                missing = next(t for t in book.FLOWERS if t not in owned)
                for payer, other in enumerate(self.player_list):
                    if payer != winner and missing in other.huapai_list:
                        return {"winner": winner, "payer": payer, "tile": missing, "stolen": missing,
                                "mode": "seven_then_last"}
        return None

    async def _replace_one_flower(self, index, flower_index, *, is_drawn, opening):
        player = self.player_list[index]
        flower = player.hand_tiles.pop(flower_index)
        self._publish_flower(index, flower)
        await self._broadcast_flower(index, flower, is_drawn=is_drawn, record=False)
        special = self._flower_special() if self.game_status != "END" else None
        transfer = special if special and special["payer"] is not None else None
        if transfer: self._transfer_flower_win(transfer)
        self._record_published_flower(index, flower, is_drawn=is_drawn, special=transfer)
        if special:
            if transfer: await self._broadcast_flower_win(transfer)
            value = book.detail([("八花和", 5)], limit=True, automatic=True)
            source = "flower_steal" if transfer else "self_draw"
            hu = actions.hu_action_for_player(special["payer"], special["winner"]) if transfer else "hu_self"
            self._queue_winner_resolution([self._build_pending_winner(special["winner"], source, hu,
                value, special["tile"], special["payer"])])
            return False
        if self.game_status == "END": return False
        tile = await self._draw_tail_for_player(index, opening=opening)
        self.sg_replacement = True
        if not opening:
            self.sg_last_drawer = index
            self.sg_last_draw_count = self.playable_wall_count() + 1
        return tile is not None

    async def _opening_flower_replacement(self):
        self.sg_initial_bonus = {i: set(p.hand_tiles).intersection(self.flower_tiles) for i, p in enumerate(self.player_list)}
        # Publish/replenish each seat completely, including chains, with no arbitrary client-dependent order.
        for index, player in enumerate(self.player_list):
            while any(t in self.flower_tiles for t in player.hand_tiles):
                pos = next(i for i, t in enumerate(player.hand_tiles) if t in self.flower_tiles)
                if not await self._replace_one_flower(index, pos, is_drawn=has_draw_slot(player) and pos == len(player.hand_tiles)-1, opening=True):
                    return
        retain_opening_first_player_draw_slot(self.player_list, 0)
        # Opening supplements carry their owner's seat; normal cut/draw ticks are
        # relative to the active seat, so return the replay cursor to the dealer.
        append_action_tick(self, ["reset", 0])
        self.sg_replacement = False

    async def _process_drawn_flowers(self, index, origin):
        if origin == "normal":
            self.sg_kong_chain = 0
        self.sg_last_draw_count = self.playable_wall_count() + 1
        self.sg_last_drawer = index
        self.sg_replacement = origin != "normal"
        # Even a terminal bonus tile must be published (and an eight-flower win resolved).
        p = self.player_list[index]
        if self.last_draw_was_last and p.hand_tiles[-1] in self.flower_tiles:
            await self._replace_one_flower(index, len(p.hand_tiles)-1, is_drawn=True, opening=False)
            return False
        return await super()._process_drawn_flowers(index, origin)

    async def execute_buhua(self, index):
        p = self.player_list[index]
        positions = [i for i, t in enumerate(p.hand_tiles) if t in self.flower_tiles]
        if not positions: return
        pos = positions[-1]
        if await self._replace_one_flower(index, pos, is_drawn=has_draw_slot(p) and pos == len(p.hand_tiles)-1, opening=False):
            if self.last_draw_was_last and p.hand_tiles[-1] in self.flower_tiles:
                await self._replace_one_flower(index, len(p.hand_tiles)-1, is_drawn=True, opening=False)
                return
            await self._prepare_hand_action_after_draw()

    async def _prepare_hand_action_after_draw(self):
        # Last playable tile can win; otherwise the hand ends without a discard (SPGG 11/25c).
        if self.last_draw_was_last and not self.score_candidate(self.current_player_index, "self_draw"):
            self.draw_reason, self.game_status = "last_tile_no_win", "END"
            return
        await super()._prepare_hand_action_after_draw()

    async def execute_cut(self, index, data, *, declare_ready=False, is_timeout_action=False):
        if declare_ready or index != self.current_player_index: return
        if self.last_draw_was_last:
            self.draw_reason, self.game_status = "last_tile_passed", "END"
            return
        tile = data.get("TileId")
        if type(tile) is not int or tile not in book.TILES or any(t in self.flower_tiles for t in self.player_list[index].hand_tiles): return
        old_count = self.player_list[index].discard_count
        fresh = tile not in self.sg_discarded
        self.sg_fresh_discard = fresh and self.sg_last_drawer == index and self.sg_last_draw_count > 0
        await super().execute_cut(index, data, is_timeout_action=is_timeout_action)
        if self.player_list[index].discard_count > old_count:
            self.sg_discarded.add(tile)
            self.sg_replacement = False
            self.sg_kong_chain = 0

    async def _kong_payment(self, index, *, concealed=False, liable=None):
        self.sg_kong_chain += 1
        self._pay_instant(index, 2 if concealed else 1, "暗杠" if concealed else "明杠", liable)
        await broadcast_do_action(self, ["singapore_payment"], index, silent=True)

    async def execute_angang(self, index, tile):
        if tile not in self._kong_candidates(index): return
        before = len(self.player_list[index].combination_tiles)
        await super().execute_angang(index, tile)
        if len(self.player_list[index].combination_tiles) > before:
            await self._kong_payment(index, concealed=True)
            self._automatic_meld_win(index, None, tile)

    async def execute_jiagang(self, index, tile):
        if tile in self._kong_candidates(index, True): await super().execute_jiagang(index, tile)

    async def finalize_jiagang(self):
        if self._pending_jiagang is None: return
        index = self.current_player_index
        tile = self.jiagang_tile
        await super().finalize_jiagang()
        await self._kong_payment(index)
        self._automatic_meld_win(index, None, tile)

    def _automatic_meld_win(self, index, payer, tile):
        if self.draw_reason == "capital_exhausted": return
        value = book.automatic_meld_win(self.player_list[index].combination_tiles)
        if value:
            source = "meld_auto" if payer is not None else "self_draw"
            hu = actions.hu_action_for_player(payer, index) if payer is not None else "hu_self"
            self._queue_winner_resolution([self._build_pending_winner(index, source, hu, value, tile, payer)])

    async def execute_claim(self, index, action):
        payer = self.current_player_index
        river = self.player_list[payer].discard_tiles
        if not river or action not in self.check_discard_actions(river[-1]).get(index, []): return
        tile = river[-1]
        p = self.player_list[index]
        liable = book.liability_trigger(p.combination_tiles, p.huapai_list, tile, seat=index, round_wind=self._round_wind())
        before = len(p.combination_tiles)
        await super().execute_claim(index, action)
        if len(p.combination_tiles) == before: return
        self.sg_kong_chain = 0
        if liable: self.sg_liability[index] = payer
        if action == "gang":
            fresh_liability = payer if self.sg_fresh_discard and self.sg_last_draw_count <= 7 else None
            await self._kong_payment(index, liable=fresh_liability)
        self._automatic_meld_win(index, payer, tile)

    def build_record_title_fields(self):
        return {"detailed_config": dict(book.CONFIG), "starting_score": 400, "player_count": 4}

    def build_game_info_fields(self):
        return {"detailed_config": dict(book.CONFIG), "singapore_info": self.public_info()}

    def public_info(self):
        return {"scores": {p.player_index: p.score for p in self.player_list}, "payments": list(self.sg_payments),
                "liability": dict(self.sg_liability), "dead_wall": 15, "rule_version": book.VERSION,
                "round_index": self.round_index, "settled": self._settled_round == self.round_index}

    async def prepare_private_fields(self, indices):
        for index in indices:
            p = self.player_list[index]
            key = (tuple(p.hand_tiles), tuple(p.combination_tiles), tuple(p.huapai_list), index, self._round_wind())
            if self._hint_cache.get(index, (None,))[0] != key:
                hints = await run_room_bot_cpu(self, compute_hints, *key)
                if self.player_list[index] is not p or tuple(p.hand_tiles) != key[0]: continue
                self._hint_cache[index] = (key, hints)
            if f"round_index_{self.round_index}" in self.game_record.get("game_round", {}) and self._recorded_hints.get(index) != key:
                append_action_tick(self, ["singapore", "hints", index, self._hint_cache[index][1]])
                self._recorded_hints[index] = key

    def build_private_game_info_fields(self, index):
        info = {**self.public_info(), "self_has_draw_slot": has_draw_slot(self.player_list[index])}
        fields = {"singapore_info": info}
        if (self.tips or self.count_tips) and index in self._hint_cache:
            fields["singapore_hints"] = self._hint_cache[index][1]
        return fields

    def build_private_hand_action_info(self, index):
        return {**self.build_private_game_info_fields(index),
                "kong_candidates": {"angang": self._kong_candidates(index), "jiagang": self._kong_candidates(index, True)}}

    def build_private_do_action_info(self, action_player, viewer_index):
        return self.build_private_game_info_fields(viewer_index)
