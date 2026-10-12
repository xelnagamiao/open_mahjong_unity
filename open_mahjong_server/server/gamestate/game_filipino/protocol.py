"""Private hands stay private; reconnect includes the exact pending decision."""
from ..game_zhongyong import boardcast as wire
from .state_machine import Phase as P


class Protocol:
    def filipino_info(self, viewer=None):
        return dict(rule_version=self.rule_version, phase=self.game_status, base_hand_tiles=16, opening_complete=self.opening_queue is None,
                    hand_number=self.round_index, match_finishing=self.match_finishing,
                    dice=list(self.dice), hand_multiplier=self.hand_multiplier,
                    round_changes=list(self.round_changes), ledger=list(self.ledger),
                    self_has_draw_slot=self.player_list[viewer].has_draw_slot if viewer in range(4) else False)

    def _adapt(self, payload, viewer):
        payload["player_index"] = viewer
        info = payload.get("game_info")
        if info is not None:
            ended = self.machine.phase in (P.END, P.READY, P.FINISHED)
            info["detailed_config"] = dict(self.detailed_config)
            info["filipino_info"] = self.filipino_info(viewer)
            info["player_entry_order"] = list(self.player_entry_order)
            for index, p in enumerate(self.player_list):
                data = wire.player_info_payload(self, index, viewer, reveal_final=ended)
                data["huapai_list"] = list(p.huapai_list)
                data["score"] = p.score + (0 if self.deferred_scores_applied else self.round_changes[index])
                if ended:
                    data["hand_tiles"] = list(p.hand_tiles) + list(p.won_tiles)
                    data["hand_tiles_count"] = len(data["hand_tiles"])
                info["players_info"][index] = data
            info["self_hand_tiles"] = list(self.player_list[viewer].hand_tiles) if viewer in range(4) else None
        return payload

    def build_game_start_payload(self, index):
        return self._adapt(wire.game_start_payload(self, index), index)

    def _ask_payload(self, index, window):
        actor = window["player"] if window.get("player") is not None else self.current_player_index
        payload = wire.ask_action_payload(self, index, self.action_dict.get(index, []),
                    action_player_index=actor, cut_tile=window.get("tile") if window["status"] == P.RESPONSE.value else None)
        info = payload.get("ask_hand_action_info") or payload.get("ask_other_action_info")
        if info is not None:
            info["filipino_info"] = self.filipino_info(index)
            if window["status"] in (P.TURN.value, P.DISCARD_ONLY.value):
                active = self.player_list[actor]
                info["deal_tile_type"] = {"normal": "deal_tile", "kong": "deal_gang_tile", "flower": "deal_buhua_tile"}.get(active.draw_kind) if active.has_draw_slot else None
                if index == actor:
                    info["forbidden_cut_tiles"] = sorted(set(active.hand_tiles) - self.legal_discard_tiles(index))
                    info["kong_candidates"] = {a: sorted(self.kong_tiles(index, a)) for a in ("angang", "jiagang")}
            else:
                info["deal_tile_type"] = None
        return self._adapt(payload, index)

    def emit_window_payloads(self, window):
        if window["status"] == P.END.value:
            self._final_result_pending = True
            return []
        payloads = [self._ask_payload(i, window) for i in range(4)]
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
        payloads = []
        for viewer in range(4):
            payload = wire.visible_action_payload(self, viewer, event)
            info = payload["do_action_info"]
            if event["action"] == "buhua":
                info.update(buhua_tile=event["tile"], is_mo_buhua=event.get("is_mo_buhua", False))
            if event["action"] == "jiagang":
                info["combination_target"] = f"k{event['tile']}"
            if event["action"] == "filipino_score":
                info["gang_score_changes"] = {i: v for i, v in enumerate(event["score_changes"])}
            info["filipino_info"] = self.filipino_info(viewer)
            payloads.append(self._adapt(payload, viewer))
        self.outbound_payloads.extend(payloads)
        if event["action"] == "buhua":
            from ..game_guobiao.buhua_broadcast import HAND_SETTLE_GAP_SEC
            self.outbound_payloads.append({"_pause": HAND_SETTLE_GAP_SEC})
        return payloads

    def build_final_settlement_payload(self, index):
        self.apply_deferred_score_changes()
        info = wire._final_show_result_info(self)
        info.update(next_status="match_end" if self.match_finishing else "round_end_by_ready",
                    filipino_info=self.filipino_info(index),
                    score_changes={p.original_player_index: self.round_changes[p.player_index] for p in self.player_list},
                    player_to_score={p.player_index: p.score for p in self.player_list})
        if self.deferred_hu_settlements:
            winner = self.player_list[self.deferred_hu_settlements[0]["winner"]]
            info["hepai_player_huapai"] = list(winner.huapai_list)
        return self._adapt(dict(type="gamestate/filipino/show_result", success=True,
                    game_info=wire.game_info_payload(self, index, reveal_final=True), show_result_info=info), index)

    async def present_final_settlements(self):
        for index in range(4):
            await self._send_claim_protection_payload(index, self.build_final_settlement_payload(index))

    def build_ready_status_payload(self, index):
        payload = wire.ready_status_payload(self, index)
        payload["ready_status_info"].update(action_tick=self.server_action_tick, filipino_info=self.filipino_info(index))
        return payload

    def emit_ready_status_payloads(self):
        payloads = [self.build_ready_status_payload(i) for i in range(4)]
        self.outbound_payloads.extend(payloads)
        return payloads

    def restore_payloads(self, index):
        ended = self.machine.phase in (P.END, P.READY, P.FINISHED)
        if ended:
            self.apply_deferred_score_changes()
        payloads = [self.build_game_start_payload(index)]
        if ended:
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
