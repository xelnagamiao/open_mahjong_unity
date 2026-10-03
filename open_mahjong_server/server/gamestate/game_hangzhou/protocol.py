"""One viewer-safe snapshot for live play, reconnect, and real-time spectating."""

import math
import time

from ..game_zhongyong import boardcast as wire
from ...game_calculation.hangzhou.rules import JOKER
from .actions import kong_tiles, legal_cuts
from .hints import private_hints
from .state_machine import Phase as P


class Protocol:
    def hangzhou_info(self, viewer=None):
        ended = self.machine.phase in (P.END, P.READY, P.FINISHED)
        data = dict(rule_version=self.rule_version, phase=self.game_status, base_hand_tiles=13,
                    hand_number=self.round_index, dealer_streak=self.dealer_streak,
                    dealer_multiplier=2 ** self.dealer_streak, joker_tiles=[JOKER], dice=list(self.dice),
                    cai_discard_locks=sorted(self.cai_discard_locks),
                    forced_draw_discard=[self.is_cai_locked(i) for i in range(4)],
                    cai_piao_counts=[p.cai_piao_count for p in self.player_list],
                    chi_counts=[p.chi_count for p in self.player_list],
                    ten_winds_counts=[len(p.discard_origin_tiles) if not p.had_meld_action and
                        all(t >= 41 for t in p.discard_origin_tiles) and len(p.discard_origin_tiles) <= 10
                        else 0 for p in self.player_list],
                    seat_to_original=[p.original_player_index for p in self.player_list],
                    wall_count=len(self.tiles_list), burned_count=len(self.burned_tiles),
                    match_finishing=self.match_finishing, ledger=self.round_settlement if ended else None)
        if viewer in (0, 1, 2, 3) and not ended:
            data["self_has_draw_slot"] = self.player_list[viewer].has_draw_slot
            hints = private_hints(self, viewer)
            data.update(hints)
            self.record_hints(viewer, hints)
        return data

    def _player_view(self, index, viewer):
        p = self.player_list[index]
        ended = self.machine.phase in (P.END, P.READY, P.FINISHED)
        data = wire.player_info_payload(self, index, viewer, reveal_final=ended)
        data["hand_tiles"] = list(p.hand_tiles) if index == viewer or ended else None
        data["hand_tiles_count"] = len(p.hand_tiles)
        data["combination_tiles"] = wire.public_melds_for_viewer(p, viewer, reveal_final=ended)
        data["combination_mask"] = wire.public_combination_masks_for_viewer(p, viewer, reveal_final=ended)
        return data

    def _adapt(self, payload, viewer):
        payload["player_index"] = viewer
        payload.setdefault("message", "")
        info = payload.get("game_info")
        if info is not None:
            info["detailed_config"] = dict(self.detailed_config)
            info["hangzhou_info"] = self.hangzhou_info(viewer)
            info["players_info"] = [self._player_view(i, viewer) for i in range(4)]
            info["self_hand_tiles"] = list(self.player_list[viewer].hand_tiles) if viewer in range(4) else None
            info["self_has_draw_slot"] = self.player_list[viewer].has_draw_slot if viewer in range(4) else False
        return payload

    def build_game_start_payload(self, index):
        return self._adapt(wire.game_start_payload(self, index), index)

    def _ask_payload(self, index, window):
        actor = window["player"] if window.get("player") is not None else self.current_player_index
        phase = P(window["status"])
        payload = wire.ask_action_payload(self, index, self.action_dict.get(index, []), action_player_index=actor,
                                         cut_tile=window.get("tile") if phase == P.RESPONSE else None)
        info = payload.get("ask_hand_action_info")
        if info is not None:
            info["hangzhou_info"] = self.hangzhou_info(index)
            active = self.player_list[actor]
            info["deal_tile_type"] = ({"normal": "deal_tile", "kong": "deal_gang_tile"}.get(active.draw_kind)
                                      if phase == P.TURN and active.has_draw_slot else None)
            if index == actor and phase in (P.TURN, P.DISCARD_ONLY):
                legal = legal_cuts(self, index)
                info["forbidden_cut_tiles"] = sorted(set(active.hand_tiles) - legal)
                if self.is_cai_locked(index):
                    info["forced_cut_tiles"] = sorted(legal)
                info["kong_candidates"] = {a: sorted(kong_tiles(self, index, a)) for a in ("angang", "jiagang")}
        if phase == P.RESPONSE and payload.get("ask_other_action_info"):
            payload["ask_other_action_info"]["remaining_time"] = 3
            payload["ask_other_action_info"]["step_remaining"] = 0
        deadline = getattr(self, "_action_deadlines", {}).get(index)
        if deadline is not None and index in self.waiting_players_list:
            active_info = info or payload.get("ask_other_action_info")
            remaining = max(0, math.ceil(deadline - time.monotonic()))
            step_remaining = 0 if phase == P.RESPONSE else min(self.step_time, remaining)
            active_info.update(remaining_time=max(0, remaining - step_remaining), step_remaining=step_remaining)
        return self._adapt(payload, index)

    def emit_window_payloads(self, window):
        self.record_state()
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
        action, actor = event["action"], event["player"]
        if action.startswith("hu"):
            return []
        payloads = []
        for viewer in range(4):
            payload = wire.visible_action_payload(self, viewer, event)
            info = payload["do_action_info"]
            if action == "jiagang":
                info["combination_target"] = f"k{event['tile']}"
            if action == "angang" and viewer != actor:
                payload["tile"], payload["meld_code"] = 0, "G0"
                info["combination_target"], info["combination_mask"] = "G0", [2, 0] * 4
            if action == "hangzhou_tail_burn":
                payload["tile"] = 0
                info["hangzhou_burn_count"] = event["burn_count"]
            info["hangzhou_info"] = self.hangzhou_info(viewer)
            payloads.append(self._adapt(payload, viewer))
        self.outbound_payloads.extend(payloads)
        return payloads

    def build_final_settlement_payload(self, index):
        self.apply_deferred_score_changes()
        info = wire._final_show_result_info(self)
        info.update(next_status="match_end" if self.match_finishing else "round_end_by_ready",
                    hangzhou_info=self.hangzhou_info(),
                    hangzhou_round_changes={p.original_player_index: self.round_changes[p.player_index] for p in self.player_list})
        if self.deferred_hu_settlements:
            entry = self.deferred_hu_settlements[0]
            info.update(hangzhou_fan_details=entry["fan_details"], hangzhou_win_source=entry["source"],
                        recycle_discard=False, hu_class="hu_self")
            if entry["source"] == "ten_winds":
                info.update(hepai_tile=0, hepai_player_hand=list(self.player_list[entry["winner"]].hand_tiles))
        else:
            info["score_changes"] = info["hangzhou_round_changes"]
        return self._adapt(dict(type="gamestate/hangzhou/show_result", success=True,
            game_info=wire.game_info_payload(self, index, reveal_final=True), show_result_info=info), index)

    async def present_final_settlements(self):
        for index in range(4):
            await self._send_claim_protection_payload(index, self.build_final_settlement_payload(index))

    def build_ready_status_payload(self, index):
        payload = wire.ready_status_payload(self, index)
        payload["ready_status_info"].update(action_tick=self.server_action_tick, hangzhou_info=self.hangzhou_info())
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
                await self.prepare_private_hints(index)
                for payload in self.restore_payloads(index):
                    await self.send_payload_to_player(index, payload)
                return

    async def send_realtime_spectator_snapshot(self, user_id, index):
        if index not in range(4) or self.game_server is None:
            return
        connection = self.game_server.user_id_to_connection.get(user_id)
        if connection is not None and connection.websocket is not None:
            await self.prepare_private_hints(index)
            for payload in self.restore_payloads(index):
                await connection.websocket.send_json(payload)
