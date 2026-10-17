"""Use existing turn messages; only recipient-private hints contain the hand."""
from ..game_zhongyong import boardcast as W
from ...game_calculation.malaysia import rules as R
from .phases import P
from . import actions as A


class Protocol:
    def public_info(self, viewer=None):
        ended = self.machine.phase in (P.END, P.READY, P.FINISHED)
        melds = []
        for i, p in enumerate(self.player_list):
            melds.append([m.as_dict() if not m.concealed or ended or i == viewer else
                dict(kind="kong", physical=[0] * 4, logical=[0] * 4, concealed=True, source=-1) for m in p.melds])
        return dict(rule_version=self.rule_version, phase=self.game_status, player_count=3,
            dice=getattr(self, "dice", []), hand_number=self.current_round, max_hands=self.max_round * 3,
            seat_to_original=[p.original_player_index for p in self.player_list], melds=melds,
            self_has_draw_slot=bool(viewer in range(3) and self.player_list[viewer].has_draw_slot),
            start_scores={p.original_player_index:self.round_start_scores[i] for i, p in enumerate(self.player_list)},
            round_changes={p.original_player_index:self.round_changes[i] for i, p in enumerate(self.player_list)} if ended else None,
            settlement=self.round_settlement if ended else None)

    def client_context(self, i):
        from ..public.local_hint_context import hint_context
        p = self.player_list[i]
        return hint_context("malaysia", p.hand_tiles, p.combination_tiles,
                            cuts=A.cuts(p), meld_records=[m.as_dict() for m in p.melds])

    def adapt(self, payload, viewer):
        payload["player_index"] = viewer
        info = payload.get("game_info")
        if info is not None:
            info.update(detailed_config=dict(self.detailed_config), malaysia_info=self.public_info(viewer),
                        malaysia_hints=self.client_context(viewer) if viewer in range(3) and self.tips else None, hepai_limit=5,
                        player_entry_order=list(self.player_entry_order))
            for i, p in enumerate(self.player_list):
                info["players_info"][i]["huapai_list"] = list(p.huapai_list)
        return payload

    def build_game_start_payload(self, i):
        ended = self.machine.phase in (P.END, P.READY, P.FINISHED)
        return self.adapt(W.game_start_payload(self, i, reveal_final=ended), i)

    def ask_payload(self, i, window):
        phase, actor = P(window["status"]), window["player"]
        payload = W.ask_action_payload(self, i, self.action_dict.get(i, []), action_player_index=actor,
            cut_tile=window.get("tile") if phase == P.RESPONSE else None,
            rob_kong_tile=window.get("tile") if phase == P.KONG else None)
        info = payload.get("ask_hand_action_info")
        if info is not None:
            p = self.player_list[actor]
            info["deal_tile_type"] = {"normal":"deal_tile", "flower":"deal_buhua_tile", "kong":"deal_gang_tile"}.get(p.draw_kind) if p.has_draw_slot and phase == P.TURN else None
            info["malaysia_info"] = self.public_info(i)
            info["malaysia_hints"] = self.client_context(i) if self.tips else None
            if i == actor:
                info["forbidden_cut_tiles"] = sorted(set(p.hand_tiles) - A.cuts(p))
                info["kong_candidates"] = {a:A.kongs(self, i, a) for a in ("angang", "jiagang")}
        return self.adapt(payload, i)

    def emit_window_payloads(self, window):
        if window["status"] == P.END.value:
            self._final_result_pending = True
            return []
        payloads = [self.ask_payload(i, window) for i in range(3) if window["status"] not in (P.RESPONSE.value, P.KONG.value) or self.action_dict.get(i)]
        self.outbound_payloads.extend(payloads)
        return payloads

    def build_pending_action_payload(self, i):
        if self.machine.phase in (P.END, P.READY, P.FINISHED) or not self.live_pending_window:
            return None
        return self.ask_payload(i, self.live_pending_window)

    def emit_visible_action_payloads(self, event, **kwargs):
        self.record_visible_action(event)
        if event["action"].startswith("hu"):
            return []
        payloads = []
        for i in range(3):
            payload = W.visible_action_payload(self, i, event)
            info = payload["do_action_info"]
            info["malaysia_info"] = self.public_info(i)
            info["malaysia_hints"] = self.client_context(i) if self.tips else None
            if event["action"] == "buhua":
                info.update(buhua_tile=event["tile"], is_mo_buhua=event.get("is_mo_buhua", False))
            if event["action"] == "malaysia_payment":
                info["gang_score_changes"] = dict(enumerate(event["score_changes"]))
            if event["action"] == "malaysia_redeem":
                info.update(malaysia_meld_index=event["meld_index"], malaysia_redeemed_tile=event["tile"])
            if event["action"] == "rob_kan":
                info.update(cut_tile=event["tile"], is_mo_gang=event["is_mo_gang"])
            payloads.append(self.adapt(payload, i))
        self.outbound_payloads.extend(payloads)
        return payloads

    def build_final_settlement_payload(self, i):
        self.apply_deferred_score_changes()
        payload = W.final_settlement_payload(self, i)
        info = payload["show_result_info"]
        info.update(malaysia_info=self.public_info(i), next_status="match_end" if self.match_finishing else "round_end_by_ready")
        if self.deferred_hu_settlements:
            entry = self.deferred_hu_settlements[0]
            info.update(hepai_player_huapai=list(self.player_list[entry["winner"]].huapai_list),
                recycle_discard=entry["recycle_discard"], hepai_tile=entry["tile"])
            if entry["automatic"]:
                info["hu_class"] = "initial_hu"
        else:
            info["score_changes"] = {p.original_player_index:self.round_changes[j] for j, p in enumerate(self.player_list)}
        return self.adapt(payload, i)

    async def present_final_settlements(self):
        for i in range(3):
            await self._send_claim_protection_payload(i, self.build_final_settlement_payload(i))

    def build_ready_status_payload(self, i):
        payload = W.ready_status_payload(self, i)
        payload["ready_status_info"].update(action_tick=self.server_action_tick, malaysia_info=self.public_info(i))
        return payload

    def emit_ready_status_payloads(self):
        payloads = [self.build_ready_status_payload(i) for i in range(3)]
        self.outbound_payloads.extend(payloads)
        return payloads

    def restore_payloads(self, i):
        result = [self.build_game_start_payload(i)]
        if self.machine.phase in (P.END, P.READY, P.FINISHED):
            result.append(self.build_final_settlement_payload(i))
            if self.machine.phase == P.READY:
                result.append(self.build_ready_status_payload(i))
        else:
            pending = self.build_pending_action_payload(i)
            if pending:
                result.append(pending)
        return result

    async def player_reconnect(self, user_id):
        for i, p in enumerate(self.player_list):
            if p.user_id == user_id:
                from ..public.outbound_pipe import drain_viewer
                await drain_viewer(self, i)
                if "offline" in p.tag_list:
                    p.tag_list.remove("offline")
                for payload in self.restore_payloads(i):
                    await self.send_payload_to_player(i, payload)
                return

    async def send_realtime_spectator_snapshot(self, user_id, i):
        if i in range(3) and self.game_server is not None:
            connection = self.game_server.user_id_to_connection.get(user_id)
            if connection and connection.websocket:
                for payload in self.restore_payloads(i):
                    await connection.websocket.send_json(payload)
