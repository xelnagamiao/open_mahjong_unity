"""Private view projection and explicit end-of-hand accounting payloads."""

from dataclasses import asdict

from ..game_zhongyong import boardcast as wire
from ...game_calculation.guizhou.rules import RULE_VERSION, waiting_tiles
from .actions import legal_cuts, ready_cuts, kong_tiles
from .state_machine import Phase as P


class Protocol:
    def guizhou_info(self, viewer=None):
        ended = self.machine.phase in (P.END, P.READY, P.FINISHED)
        hidden = {(actor, int(self.player_list[actor].combination_tiles[pos][1:]))
                  for actor, pos in self.hidden_opening_kongs}
        kongs = []
        for entry in self.kongs:
            data = asdict(entry)
            if not ended and viewer != entry.owner and (entry.owner, entry.tile) in hidden:
                data["tile"] = 0
            kongs.append(data)
        return dict(rule_version=RULE_VERSION, phase=self.game_status, hand_number=self.current_round,
                    total_hands=self.max_round*4, match_finishing=self.match_finishing,
                    opening_revealed=self.opening_revealed,
                    self_has_draw_slot=not ended and viewer in range(4) and self.player_list[viewer].has_draw_slot,
                    chickens=[asdict(c) for c in self.chickens.values()], kongs=kongs,
                    seat_to_original=[p.original_player_index for p in self.player_list],
                    ledger=self.round_settlement.as_dict() if ended and self.round_settlement else None)

    def _player_view(self, index, viewer):
        p = self.player_list[index]
        ended = self.machine.phase in (P.END, P.READY, P.FINISHED)
        data = wire.player_info_payload(self, index, viewer, reveal_final=ended)
        data["hand_tiles_count"] = len(p.hand_tiles)
        data["hand_tiles"] = list(p.hand_tiles) if index == viewer or ended else None
        data["combination_tiles"] = list(p.combination_tiles)
        data["combination_mask"] = [list(m) for m in p.combination_mask]
        if not ended and index != viewer:
            for actor, position in self.hidden_opening_kongs:
                if actor == index:
                    data["combination_tiles"][position] = "G0"
                    data["combination_mask"][position] = [2, 0]*4
        data["discard_riichi_flags"] = list(p.discard_riichi_flags)
        data["ready_qualification"] = p.ready_kind or "none"
        return data

    def _adapt(self, payload, viewer):
        payload["player_index"] = viewer
        info = payload.get("game_info")
        if info is not None:
            info["detailed_config"] = dict(self.detailed_config)
            info["guizhou_info"] = self.guizhou_info(viewer)
            info["players_info"] = [self._player_view(i, viewer) for i in range(4)]
            info["self_hand_tiles"] = list(self.player_list[viewer].hand_tiles) if viewer in range(4) else None
        return payload

    def build_game_start_payload(self, index):
        return self._adapt(wire.game_start_payload(self, index), index)

    def _ask_payload(self, index, window):
        actor = window.get("player")
        if actor is None:
            actor = self.current_player_index
        phase = P(window["status"])
        if phase == P.INITIAL_READY and self.action_dict.get(index):
            actor = index
        payload = wire.ask_action_payload(self, index, self.action_dict.get(index, []),
            action_player_index=actor, cut_tile=window.get("tile") if phase == P.RESPONSE else None,
            rob_kong_tile=window.get("tile") if phase == P.KONG else None)
        info = payload.get("ask_hand_action_info")
        if info is not None:
            p = self.player_list[index]
            info["ready_qualification"] = p.ready_kind or "none"
            info["guizhou_info"] = self.guizhou_info(index)
            if index == actor and phase in (P.TURN, P.DISCARD_ONLY):
                legal = legal_cuts(self, index)
                info["forbidden_cut_tiles"] = sorted(set(p.hand_tiles)-legal)
                if p.declared_ready:
                    info["forced_cut_tiles"] = sorted(legal)
                if p.ready_pending:
                    candidates = {}
                    for tile in ready_cuts(self, index):
                        rest = list(p.hand_tiles)
                        rest.remove(tile)
                        candidates[tile] = sorted(waiting_tiles(rest, p.combination_tiles))
                    info["riichi_candidate_cuts"] = candidates
                info["kong_candidates"] = {a: sorted(kong_tiles(self, index, a)) for a in ("angang", "jiagang")}
            else:
                info["deal_tile_type"] = None
        clock = window.get("_clocks", {}).get(index)
        if clock is not None:
            bank, step = clock.display()
            for key in ("ask_hand_action_info", "ask_other_action_info"):
                if payload.get(key) is not None:
                    payload[key].update(remaining_time=bank, step_remaining=step)
        return self._adapt(payload, index)

    def emit_window_payloads(self, window):
        if window["status"] == P.END.value:
            self._final_result_pending = True
            return []
        payloads = [self._ask_payload(i, window) for i in range(4)
                    if window["status"] != P.RESPONSE.value or self.action_dict.get(i)]
        self.outbound_payloads.extend(payloads)
        return payloads

    def build_pending_action_payload(self, index):
        if self.machine.phase in (P.END, P.READY, P.FINISHED) or not self.live_pending_window:
            return None
        return self._ask_payload(index, self.live_pending_window)

    def emit_visible_action_payloads(self, event, *, reveal_final=False):
        self.record_visible_action(event)
        if event["action"].startswith("hu"):
            return []
        actor = event["player"]
        payloads = []
        for viewer in range(4):
            payload = self._adapt(wire.visible_action_payload(self, viewer, event), viewer)
            info = payload["do_action_info"]
            if event["action"] == "jiagang":
                # The client upgrades the existing pung by this key. The
                # authoritative snapshot and recorded event already contain g.
                info["combination_target"] = f"k{event['tile']}"
            if event["action"] == "angang" and viewer != actor and any(i == actor and self.player_list[i].combination_tiles[pos] == event["meld_code"] for i, pos in self.hidden_opening_kongs):
                payload["tile"], payload["meld_code"] = 0, "G0"
                info["combination_target"], info["combination_mask"] = "G0", [2, 0]*4
            info["guizhou_info"] = self.guizhou_info(viewer)
            if event["action"] == "riichi":
                info["ready_qualification"] = event["ready_qualification"]
            if event["action"] == "cut":
                info["is_riichi_horizontal"] = bool(event.get("riichi_discard"))
            payloads.append(payload)
        self.outbound_payloads.extend(payloads)
        return payloads

    def final_settlement_payloads(self, viewer):
        self.apply_deferred_score_changes()
        records = self.deferred_hu_settlements
        payloads = []
        for pos in range(max(1, len(records))):
            info = wire._final_show_result_info(self, pos if records else None)
            last = pos == len(records)-1 or not records
            info["next_status"] = ("match_end" if self.match_finishing else "round_end_by_ready") if last else "round_continue"
            info["guizhou_info"] = self.guizhou_info(viewer)
            # End-of-hand checking is public. Each winning hand gets a virtual
            # winning tile for display; physical ownership remains single.
            end_hands = {i: list(p.hand_tiles) for i, p in enumerate(self.player_list)}
            for entry in records:
                if entry["source"] != "self_draw":
                    end_hands[entry["winner"]].append(entry["tile"])
            info["guizhou_end_hands"] = end_hands
            info["guizhou_round_changes"] = {p.original_player_index: self.round_settlement.changes[p.player_index] for p in self.player_list}
            if records:
                info["guizhou_fan_details"] = records[pos]["fan_details"]
                info["multi_ron"] = records[pos]["multi_ron"]
                info["recycle_discard"] = records[pos]["recycle_discard"]
            else:
                info["score_changes"] = info["guizhou_round_changes"]
            payloads.append(self._adapt(dict(type="gamestate/guizhou/show_result", success=True,
                           game_info=wire.game_info_payload(self, viewer, reveal_final=True), show_result_info=info), viewer))
        return payloads

    def build_final_settlement_payload(self, index):
        return self.final_settlement_payloads(index)[-1]

    async def present_final_settlements(self):
        import asyncio
        from ..public.round_end_timing import sichuan_settle_hu_panel_wait_seconds
        views = [self.final_settlement_payloads(i) for i in range(4)]
        for step in range(len(views[0])):
            for i in range(4):
                await self._send_claim_protection_payload(i, views[i][step])
            if step < len(views[0])-1:
                await asyncio.sleep(sichuan_settle_hu_panel_wait_seconds(len(views[0][step]["show_result_info"].get("hu_fan", []))))

    def build_ready_status_payload(self, index):
        payload = wire.ready_status_payload(self, index)
        payload["ready_status_info"].update(action_tick=self.server_action_tick, guizhou_info=self.guizhou_info(index))
        return payload

    def emit_ready_status_payloads(self):
        payloads = [self.build_ready_status_payload(i) for i in range(4)]
        self.outbound_payloads.extend(payloads)
        return payloads

    def restore_payloads(self, index):
        payloads = [self.build_game_start_payload(index)]
        if self.machine.phase in (P.END, P.READY, P.FINISHED):
            payloads.append(self.build_final_settlement_payload(index))
            if self.machine.phase == P.READY:
                payloads.append(self.build_ready_status_payload(index))
        else:
            pending = self.build_pending_action_payload(index)
            if pending:
                payloads.append(pending)
        return payloads

    async def player_reconnect(self, user_id):
        for index, p in enumerate(self.player_list):
            if p.user_id == user_id:
                if "offline" in p.tag_list:
                    p.tag_list.remove("offline")
                for payload in self.restore_payloads(index):
                    await self.send_payload_to_player(index, payload)
                return

    async def send_realtime_spectator_snapshot(self, user_id, index):
        if index not in range(4) or self.game_server is None:
            return
        connection = self.game_server.user_id_to_connection.get(user_id)
        if connection is not None and connection.websocket is not None:
            for payload in self.restore_payloads(index):
                await connection.websocket.send_json(payload)
