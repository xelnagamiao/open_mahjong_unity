from copy import deepcopy
import math

from ..game_zhongyong import boardcast as wire
from ..public import game_record_manager as record
from ..public.settlement_sync import send_game_packet
from .actions import CHOWS, claim_tiles, kong_tiles, legal_cuts
from .state_machine import Phase as P


class Protocol:
    def current_payer(self):
        if self.machine.phase in (P.RESPONSE, P.ROB_KONG) and self.live_pending_window:
            return self.live_pending_window.get("player")
        return None

    def view(self, viewer):
        ended = self.machine.phase in (P.END, P.READY, P.FINISHED)
        p = self.player_list[viewer] if viewer in range(4) else None
        info = dict(phase=self.game_status, hand_number=self.round_index, repeat=self.repeat,
                    bao=self.bao if ended or p and p.declared else 0, revision=self.bao_revision,
                    declared=[other.declared for other in self.player_list],
                    chains=[other.chain for other in self.player_list], best_chains=[p.longest_chain for p in self.player_list],
                    supplements=len(self.supplements),
                    rob_from_discard=bool(self.pending_kong and self.pending_kong[2] == "gang"),
                    acting=(self.live_pending_window or {}).get("player"), payer=self.current_payer(), cut_tile=(self.live_pending_window or {}).get("tile") or 0,
                    win_blocked=bool(p and "peida" in p.tag_list), pass_level=p.level if p else 0, passed_ron=sorted(p.discard_win_lockout_tiles) if p else [],
                    self_has_draw_slot=bool(p and p.has_draw_slot), after_kong=bool(p and p.after_kong),
                    visible=dict(self.visible_counts()), start_scores=list(self.round_start_scores),
                    settlement=self.deferred_hu_settlements[-1]["fan_details"] if ended and self.deferred_hu_settlements else None)
        if self.machine.phase == P.EXCHANGE and p:
            submitted = self.live_pending_window.get("submitted", {})
            if viewer in submitted or viewer not in self.waiting_players_list:
                info["exchange_outgoing"] = list(submitted.get(viewer, []))
        elif self.machine.phase == P.EXCHANGE_CHOICES:
            window = self.live_pending_window
            info.update(exchange_choices=[i in window["submissions"] for i in range(4)],
                        exchange_duration_ms=window["duration_ms"],
                        exchange_remaining_ms=self.presentation_remaining_ms())
            if viewer in window["submissions"]:
                info["exchange_outgoing"] = list(window["submissions"][viewer])
        elif self.machine.phase == P.EXCHANGE_RECEIPT:
            info["exchange_remaining_ms"] = self.presentation_remaining_ms()
            if viewer in self.live_pending_window["incoming"]:
                info["exchange_incoming"] = list(self.live_pending_window["incoming"][viewer])
        elif self.machine.phase == P.BAO_REVEAL:
            info.update(bao_initial=self.live_pending_window["initial"], bao_remaining_ms=self.presentation_remaining_ms())
        return info

    def presentation_remaining_ms(self):
        window = self.live_pending_window
        if window.get("broadcast_complete") is False:
            return window["duration_ms"]
        return max(0, round((window["deadline"] - self._timing_now()) * 1000))

    def _refresh_clock_payload(self, index, payload):
        super()._refresh_clock_payload(index, payload)
        game_info = payload.get("game_info") or {}
        info = game_info.get("new_chinese")
        phase = info.get("phase") if info else None
        if phase in (P.EXCHANGE_CHOICES, P.EXCHANGE_RECEIPT, P.BAO_REVEAL):
            current = self.machine.phase == phase and game_info.get("action_tick") == self.server_action_tick
            field = "bao_remaining_ms" if phase == P.BAO_REVEAL else "exchange_remaining_ms"
            info[field] = self.presentation_remaining_ms() if current else 0

    def adapt(self, payload, viewer):
        info = payload.get("game_info")
        payload.setdefault("message", "")
        payload["player_index"] = viewer
        for key in ("ask_hand_action_info", "ask_other_action_info"):
            ask = payload.get(key)
            if ask and "remaining_time" in ask:
                ask["remaining_time"] = math.ceil(max(0, ask["remaining_time"]))
        if info is not None:
            ended = self.machine.phase in (P.END, P.READY, P.FINISHED)
            info.update(new_chinese=self.view(viewer), detailed_config=self.detailed_config, hepai_limit=self.hepai_limit)
            for i, p in enumerate(self.player_list):
                fields = wire.player_info_payload(self, i, viewer, reveal_final=ended)
                fields["remaining_time"] = math.ceil(max(0, p.remaining_time))
                fields["discard_riichi_flags"] = list(p.riichi_flags)
                info["players_info"][i] = fields
        return payload

    def build_game_start_payload(self, index):
        return self.adapt(wire.game_start_payload(self, index), index)

    def _ask(self, viewer, window):
        phase, actor, tile = window["status"], window["player"], window["tile"]
        options = self.action_dict.get(viewer, [])
        payload = wire.ask_action_payload(self, viewer, options, action_player_index=actor,
            cut_tile=tile if phase == P.RESPONSE else None, rob_kong_tile=tile if phase == P.ROB_KONG else None)
        hand = payload.get("ask_hand_action_info")
        if hand is not None:
            if phase in (P.BAO, P.EXCHANGE, P.AFTER_MELD):
                hand["deal_tile_type"] = None
            if phase == P.EXCHANGE:
                hand["player_index"] = viewer
            if viewer == actor and phase in (P.TURN, P.AFTER_MELD):
                p = self.player_list[viewer]
                hand.update(riichi_candidate_cuts=self.candidate_cuts,
                            forbidden_cut_tiles=sorted(set(p.hand_tiles)-legal_cuts(self, viewer)),
                            kong_candidates={k: sorted(kong_tiles(self, viewer, k)) for k in ("angang", "jiagang")})
                if p.declared:
                    hand["forced_cut_tiles"] = sorted(legal_cuts(self, viewer))
        claim = payload.get("ask_other_action_info")
        if claim is not None:
            claim["chi_candidates"] = {a: [claim_tiles(a.removesuffix("_ting"), tile)] for a in options if a.removesuffix("_ting") in CHOWS}
        return self.adapt(payload, viewer)

    def emit_window_payloads(self, window):
        if window["status"] == P.BAO_REVEAL:
            action = "new_chinese_bao_reveal" if window["initial"] else "new_chinese_bao_change"
            return self.emit_visible_action_payloads(dict(action=action, player=window["player"]))
        if window["status"] == P.EXCHANGE_CHOICES:
            return self.emit_visible_action_payloads(dict(action="new_chinese_exchange_choices", player=self.dealer_index))
        if window["status"] == P.EXCHANGE_RECEIPT:
            return self.emit_visible_action_payloads(dict(action="new_chinese_exchange", player=self.dealer_index))
        if window["status"] == P.END:
            self._final_result_pending = True
            return []
        payloads = [self._ask(i, window) for i in range(4)
                    if window["status"] not in (P.ROB_KONG, P.RESPONSE, P.BAO) or self.action_dict[i]]
        self.outbound_payloads.extend(payloads)
        return payloads

    def build_pending_action_payload(self, index):
        if self.machine.phase == P.EXCHANGE and (index not in self.waiting_players_list or not self.action_queues[index].empty()):
            return None
        if self.live_pending_window and self.machine.phase not in (P.EXCHANGE_CHOICES, P.EXCHANGE_RECEIPT, P.BAO_REVEAL, P.END, P.READY, P.FINISHED):
            return self._ask(index, self.live_pending_window)
        return None

    def emit_visible_action_payloads(self, event, **kwargs):
        self.record_visible_action(event)
        if event["action"].startswith("hu"):
            return []
        payloads = []
        for viewer in range(4):
            payload = wire.visible_action_payload(self, viewer, event)
            action = payload["do_action_info"]
            action["tile_count"] = len(self.tiles_list)
            if event["action"] == "cut":
                action["is_riichi_horizontal"] = event.get("declared", False)
            if event["action"] == "jiagang":
                action["combination_target"] = f"k{event['tile']}"
            if event["action"] in ("declare_kan", "rob_kan"):
                action["cut_tile"] = event["tile"]
            payloads.append(self.adapt(payload, viewer))
        self.outbound_payloads.extend(payloads)
        return payloads

    def build_final_settlement_payload(self, index):
        self.apply_deferred_score_changes()
        info = wire._final_show_result_info(self)
        entry = next(iter(self.deferred_hu_settlements), None)
        info.update(next_status="match_end" if self.match_finishing else "round_end_by_ready",
                    revealed_hands={i: list(p.hand_tiles) for i, p in enumerate(self.player_list)},
                    recycle_discard=bool(entry and entry["recycle_discard"]))
        return self.adapt(dict(type="gamestate/new_chinese/show_result", success=True,
                               game_info=wire.game_info_payload(self,index,reveal_final=True), show_result_info=info), index)

    async def present_final_settlements(self):
        for i in range(4):
            await self._send_claim_protection_payload(i, self.build_final_settlement_payload(i))

    def build_ready_status_payload(self, index):
        payload = wire.ready_status_payload(self, index)
        payload["ready_status_info"]["action_tick"] = self.server_action_tick
        return self.adapt(payload, index)

    def emit_ready_status_payloads(self):
        payloads = [self.build_ready_status_payload(i) for i in range(4)]
        self.outbound_payloads.extend(payloads)
        return payloads

    def restore_payloads(self, index):
        packets = [self.build_game_start_payload(index)]
        if self.machine.phase in (P.END, P.READY, P.FINISHED):
            packets.append(self.build_final_settlement_payload(index))
            if self.machine.phase == P.READY:
                packets.append(self.build_ready_status_payload(index))
        else:
            pending = self.build_pending_action_payload(index)
            if pending is not None:
                packets.append(pending)
        return packets

    async def player_reconnect(self, user_id):
        from ..public.outbound_pipe import drain_viewer
        for i, p in enumerate(self.player_list):
            if p.user_id == user_id:
                await drain_viewer(self, i)
                if "offline" in p.tag_list:
                    p.tag_list.remove("offline")
                for payload in self.restore_payloads(i):
                    await self.send_payload_to_player(i, payload)
                return

    async def send_realtime_spectator_snapshot(self, user_id, index):
        connection = self.game_server.user_id_to_connection.get(user_id) if self.game_server else None
        if connection and connection.websocket and index in range(4):
            for payload in self.restore_payloads(index):
                await send_game_packet(self, connection.websocket, payload)

    def spectator_round_header(self, header):
        return {**{k: v for k, v in header.items() if k not in ("round_random_seed", "new_chinese_supplements")},
                "tiles_list": [0] * len(header["tiles_list"])}

    def spectator_record_tick(self, tick):
        if tick and tick[0] == "new_chinese" and tick[1] == "state":
            state = {**tick[2], "bao": 0, "levels": [], "passed": []}
            state.pop("exchange_packets", None)
            return ["new_chinese", "state", state]
        return tick

    def complete_spectator_record(self, record_view):
        return {**deepcopy(self.game_record), "players_settings": record_view["players_settings"]}

    def start_game_recording(self):
        record.init_game_record(self)
        self.game_record["game_title"].update(rule_version=self.rule_version, detailed_config=self.detailed_config)
        self.spectator_manager.record_game_title()

    def build_record_round_fields(self):
        return dict(new_chinese=self.view(None), new_chinese_supplements=list(self.supplements))

    def record_visible_action(self, event):
        if not self._has_active_round_record():
            return
        action, i = event["action"], event["player"]
        if action == "cut":
            record.append_action_tick(self, ["reset", i])
            record.player_action_record_cut(self, event["tile"], bool(event["cutClass"]), event["declared"])
        elif action.startswith("hu"):
            entry = self.deferred_hu_settlements[-1]
            if entry["fan_details"]["source"] == "bao_seen":
                record.append_action_tick(self, ["new_chinese", "seen_win", i, entry["tile"]])
            record.player_action_record_hu(self, action, entry["points"], entry["fan_ids"], i, entry["score_changes"],
                hepai_tile=entry["tile"], multi_ron=False, ron_discarder_index=entry["discarder"], recycle_discard=entry["recycle_discard"],
                statistics_fan=entry["fan_details"]["fan"])
            self._update_record_counter_for_settlement(entry, entry["fan_names"])
        elif action == "new_chinese_exchange":
            record.append_action_tick(self, ["new_chinese", "exchange", {j: list(p.hand_tiles) for j, p in enumerate(self.player_list)}])
        elif action in ("declare_kan", "rob_kan"):
            record.append_action_tick(self, ["new_chinese", action, i, event["tile"], event["kong_kind"],
                                              event["original_source"], int(event.get("for_chow", False))])
        elif not action.startswith("new_chinese_"):
            super().record_visible_action(event)
        self.record_state()

    def record_state(self):
        if not self._has_active_round_record():
            return
        state = self.view(None)
        if self.machine.phase in (P.EXCHANGE_CHOICES, P.EXCHANGE_RECEIPT):
            state["exchange_remaining_ms"] = self.live_pending_window["duration_ms"]
        if self.machine.phase == P.EXCHANGE_CHOICES:
            submissions = self.live_pending_window["submissions"]
            state["exchange_packets"] = [list(submissions.get(i, [])) for i in range(4)]
        state.update(bao=self.bao, levels=[p.level for p in self.player_list],
                     after_kongs=[p.after_kong for p in self.player_list],
                     blocked=[bool("peida" in p.tag_list) for p in self.player_list],
                     passed=[sorted(p.discard_win_lockout_tiles) for p in self.player_list])
        record.append_action_tick(self, ["new_chinese", "state", state])

    def _update_record_counter_for_settlement(self, entry, names):
        p = self.player_list[entry["winner"]]
        counter = p.record_counter
        counter.recorded_fans.append(names)
        counter.win_score += entry["points"]
        counter.win_turn += len(p.discard_origin_tiles) + 1
        if entry["source"] == "self_draw":
            counter.zimo_times += 1
        else:
            counter.dianhe_times += 1
            payer = self.player_list[entry["discarder"]].record_counter
            payer.fangchong_times += 1
            payer.fangchong_score -= entry["score_changes"][entry["discarder"]]

    def persist_game_record(self):
        if self.db_manager and self.game_record:
            return self.db_manager.store_new_chinese_game_record(self.game_record, self.player_list, self.room_type, f"{self.max_round}/4")
