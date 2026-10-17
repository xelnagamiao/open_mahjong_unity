"""Viewer-safe physical snapshots; client calculation inputs never expose other hands."""

import math

from ..game_zhongyong import boardcast as wire
from .actions import CHOWS, kong_tiles, natural, needed_tiles, physical_for
from .state_machine import Phase as P


class Protocol:
    def wenzhou_info(self):
        ended = self.machine.phase in (P.END,P.READY,P.FINISHED)
        info = dict(rule_version=self.rule_version, phase=self.game_status, base_hand_tiles=16,
                    caishen=self.caishen, indicator=self.indicator, indicator_index=self.indicator_index,
                    white_natural=natural(46,self.caishen), dice=list(self.dice),
                    hand_number=self.round_index, dealer_streak=self.dealer_streak,
                    match_finishing=self.match_finishing,
                    seat_to_original=[p.original_player_index for p in self.player_list])
        if ended:
            info.update(caishen_counts=list(self.caishen_counts), caishen_changes=list(self.caishen_changes),
                        round_changes=list(self.round_changes), ledger=list(self.ledger),
                        score_details=self.round_settlement, next_dealer_dice=list(self.next_dealer_dice),
                        next_dealer_shift=self.next_dealer_shift, next_dealer_streak=self.next_dealer_streak)
        return info

    def _player_view(self, index, viewer):
        p = self.player_list[index]
        ended = self.machine.phase in (P.END,P.READY,P.FINISHED)
        data = wire.player_info_payload(self,index,viewer,reveal_final=ended)
        data["hand_tiles"] = list(p.hand_tiles) if index == viewer or ended else None
        data["hand_tiles_count"] = len(p.hand_tiles)
        data["remaining_time"] = math.ceil(p.remaining_time)
        data["combination_tiles"] = wire.public_melds_for_viewer(p,viewer,reveal_final=ended)
        data["combination_mask"] = wire.public_combination_masks_for_viewer(p,viewer,reveal_final=ended)
        return data


    def client_context(self, viewer, *, for_record=False):
        # Replays expose their own analysis controls independently of live room
        # tips. Count-only rooms also need eligibility from the same scorer.
        if viewer not in range(4) or (not (self.tips or self.count_tips) and not for_record) or self.machine.phase in (P.END,P.READY,P.FINISHED):
            return {}
        from ..public.local_hint_context import hint_context
        p = self.player_list[viewer]
        records = list(p.meld_records)
        request = hint_context("wenzhou", p.hand_tiles, [m["code"] for m in records],
            caishen=self.caishen, meld_records=records,
            passed_multiplier=p.passed_win_multiplier)["local_request"]
        return {"__local__": [dict(local_request=request)]}

    def _adapt(self, payload, viewer):
        payload["player_index"] = viewer
        info = payload.get("game_info")
        if info is not None:
            info["detailed_config"] = dict(self.detailed_config)
            info["wenzhou_info"] = self.wenzhou_info()
            info["wenzhou_info"]["self_has_draw_slot"] = viewer in range(4) and self.player_list[viewer].has_draw_slot
            info["wenzhou_waits"] = self.client_context(viewer)
            info["players_info"] = [self._player_view(i,viewer) for i in range(4)]
            info["self_hand_tiles"] = list(self.player_list[viewer].hand_tiles) if viewer in range(4) else None
        return payload

    def build_game_start_payload(self,index):
        return self._adapt(wire.game_start_payload(self,index),index)

    def _ask_payload(self,index,window):
        actor = window["player"] if window.get("player") is not None else self.current_player_index
        phase = P(window["status"])
        actions = [] if index in window["_clock_finished"] else self.action_dict.get(index,[])
        payload = wire.ask_action_payload(self,index,actions,action_player_index=actor,
            cut_tile=window.get("tile") if phase == P.RESPONSE else None,
            rob_kong_tile=window.get("tile") if phase == P.KONG else None)
        info = payload.get("ask_hand_action_info")
        if info is not None:
            info["wenzhou_info"] = self.wenzhou_info()
            active = self.player_list[actor]
            info["deal_tile_type"] = ("deal_gang_tile" if active.draw_kind == "kong" else "deal_tile") if active.has_draw_slot else None
            if index == actor:
                info["kong_candidates"] = {kind:sorted(kong_tiles(self,index,kind)) for kind in ("angang","jiagang")}
        claim = payload.get("ask_other_action_info")
        if claim is not None:
            logical = natural(window["tile"],self.caishen)
            claim["chi_candidates"] = {kind:[physical_for(self.player_list[index].hand_tiles,
                needed_tiles(kind,logical),self.caishen)] for kind in actions if kind in CHOWS}
        bank,step = self.remaining_clock(index,window)
        for ask in (info,claim):
            if ask is not None:
                ask.update(remaining_time=bank,step_remaining=step)
        payload = self._adapt(payload,index)
        bank_exact,step_exact = self._clock_values(index,window)
        payload["game_info"]["wenzhou_info"].update(
            clock_remaining_ms=(bank_exact+step_exact)*1000,
            clock_step_remaining_ms=step_exact*1000,
            clock_active=bool(actions))
        return payload

    def _live_ask_window(self, payload):
        ask = payload.get("ask_hand_action_info") or payload.get("ask_other_action_info")
        window = self.live_pending_window
        return window if ask is not None and window and ask.get("action_tick") == window.get("action_tick") else None

    async def _deliver_claim_payload(self, index, payload):
        """Use the existing pipe, but anchor each seat before later sends wait."""
        window = self._live_ask_window(payload)
        if window is not None:
            payload = self._ask_payload(index,window)
        connection = (getattr(self.game_server,"user_id_to_connection",{}) or {}).get(self.player_list[index].user_id)
        if connection is not None and getattr(connection,"websocket",None) is not None:
            await connection.websocket.send_json(payload)
            if window is not None:
                self.start_action_clock(index,window)
            self.websocket_sent_payloads.append(payload)
        await self.send_to_realtime_spectators(index,payload)

    async def send_payload_to_player(self, index, payload, *, record_fallback=True):
        window = self._live_ask_window(payload)
        if window is not None:
            payload = self._ask_payload(index,window)
        sent = await super().send_payload_to_player(index,payload,record_fallback=record_fallback)
        if sent and window is not None:
            self.start_action_clock(index,window)
        return sent

    def emit_window_payloads(self,window):
        if window["status"] == P.END.value:
            self._final_result_pending = True
            return []
        self.record_waits()
        payloads = [self._ask_payload(i,window) for i in range(4)
                    if window["status"] not in (P.RESPONSE.value,P.KONG.value) or self.action_dict.get(i)]
        self.outbound_payloads.extend(payloads)
        return payloads

    def build_pending_action_payload(self,index):
        if self.machine.phase in (P.END,P.READY,P.FINISHED) or not self.live_pending_window:
            return None
        return self._ask_payload(index,self.live_pending_window)

    def emit_visible_action_payloads(self,event,*,reveal_final=False):
        self.record_visible_action(event)
        action,actor = event["action"],event["player"]
        if action.startswith("hu"):
            return []
        payloads = []
        for viewer in range(4):
            payload = wire.visible_action_payload(self,viewer,event)
            info = payload["do_action_info"]
            if action == "jiagang":
                info["combination_target"] = f"k{natural(event['tile'],self.caishen)}"
                info["cut_tile"] = event["tile"]
            if action == "angang" and viewer != actor:
                payload["tile"],payload["meld_code"] = 0,"G0"
                info["combination_target"],info["combination_mask"] = "G0",[2,0]*4
            if action == "wenzhou_kong_claim_source":
                info.update(cut_tile=event["tile"],cut_class=event["is_mo_gang"],is_mo_gang=event["is_mo_gang"])
            info["wenzhou_info"] = self.wenzhou_info()
            payloads.append(self._adapt(payload,viewer))
        self.outbound_payloads.extend(payloads)
        return payloads

    def build_final_settlement_payload(self,index):
        self.apply_deferred_score_changes()
        info = wire._final_show_result_info(self)
        info.update(next_status="match_end" if self.match_finishing else "round_end_by_ready",
                    wenzhou_info=self.wenzhou_info(),
                    wenzhou_end_hands={p.player_index:list(p.hand_tiles) for p in self.player_list},
                    player_to_score={p.player_index:p.score for p in self.player_list},
                    score_changes={p.original_player_index:self.round_changes[p.player_index] for p in self.player_list})
        if self.deferred_hu_settlements:
            entry = self.deferred_hu_settlements[0]
            info["recycle_discard"] = entry["recycle_discard"]
        return self._adapt(dict(type="gamestate/wenzhou/show_result",success=True,
            game_info=wire.game_info_payload(self,index,reveal_final=True),show_result_info=info),index)

    async def present_final_settlements(self):
        for i in range(4):
            await self._send_claim_protection_payload(i,self.build_final_settlement_payload(i))

    def build_ready_status_payload(self,index):
        payload = wire.ready_status_payload(self,index)
        payload["ready_status_info"].update(action_tick=self.server_action_tick,wenzhou_info=self.wenzhou_info())
        return payload

    def emit_ready_status_payloads(self):
        payloads = [self.build_ready_status_payload(i) for i in range(4)]
        self.outbound_payloads.extend(payloads)
        return payloads

    def restore_payloads(self,index):
        if self.machine.phase in (P.END,P.READY,P.FINISHED):
            self.apply_deferred_score_changes()
        payloads = [self.build_game_start_payload(index)]
        if self.machine.phase in (P.END,P.READY,P.FINISHED):
            payloads.append(self.build_final_settlement_payload(index))
            if self.machine.phase == P.READY:
                payloads.append(self.build_ready_status_payload(index))
        else:
            pending = self.build_pending_action_payload(index)
            if pending:
                payloads.append(pending)
        return payloads

    async def player_reconnect(self,user_id):
        for index,p in enumerate(self.player_list):
            if p.user_id == user_id:
                from ..public.outbound_pipe import drain_viewer
                await drain_viewer(self,index)
                if "offline" in p.tag_list:
                    p.tag_list.remove("offline")
                for payload in self.restore_payloads(index):
                    await self.send_payload_to_player(index,payload)
                return

    async def send_realtime_spectator_snapshot(self,user_id,index):
        if index not in range(4) or self.game_server is None:
            return
        connection = self.game_server.user_id_to_connection.get(user_id)
        if connection is not None and connection.websocket is not None:
            from ..public.outbound_pipe import drain_viewer
            await drain_viewer(self,index)
            for payload in self.restore_payloads(index):
                await connection.websocket.send_json(payload)
