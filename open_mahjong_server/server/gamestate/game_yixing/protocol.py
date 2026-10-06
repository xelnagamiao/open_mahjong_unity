"""Viewer-safe snapshots, action prompts, and flower accounting details."""

from ..game_zhongyong import boardcast as wire
from .actions import legal_cuts, kong_tiles
from .state_machine import Phase as P


class Protocol:
    def yixing_info(self):
        ended = self.machine.phase in (P.END,P.READY,P.FINISHED)
        return dict(rule_version=self.rule_version, phase=self.game_status, base_hand_tiles=13,
                    hand_number=self.round_index, dealer_streak=self.dealer_streak, dice=list(self.dice),
                    last_draw_passes=list(self.last_draw_passes), match_finishing=self.match_finishing,
                    seat_to_original=[p.original_player_index for p in self.player_list],
                    ledger=self.round_settlement if ended else None)

    def _player_view(self, index, viewer):
        p = self.player_list[index]
        ended = self.machine.phase in (P.END,P.READY,P.FINISHED)
        data = wire.player_info_payload(self,index,viewer,reveal_final=ended)
        data["hand_tiles"] = list(p.hand_tiles) if index == viewer or ended else None
        data["hand_tiles_count"] = len(p.hand_tiles)
        data["huapai_list"] = list(p.huapai_list)
        # One face-up tile of a Yixing concealed kong reveals its kind.
        data["combination_tiles"] = list(p.combination_tiles)
        data["combination_mask"] = wire.public_combination_masks_for_viewer(p,viewer,reveal_final=ended)
        return data

    def _adapt(self, payload, viewer):
        payload["player_index"] = viewer
        info = payload.get("game_info")
        if info is not None:
            info["detailed_config"] = dict(self.detailed_config)
            info["yixing_info"] = self.yixing_info()
            info["players_info"] = [self._player_view(i,viewer) for i in range(4)]
            info["self_hand_tiles"] = list(self.player_list[viewer].hand_tiles) if viewer in range(4) else None
        return payload

    def build_game_start_payload(self,index):
        return self._adapt(wire.game_start_payload(self,index),index)

    def _ask_payload(self,index,window):
        actor = window["player"] if window.get("player") is not None else self.current_player_index
        phase = P(window["status"])
        payload = wire.ask_action_payload(self,index,self.action_dict.get(index,[]),action_player_index=actor,
            cut_tile=window.get("tile") if phase == P.RESPONSE else None,
            rob_kong_tile=window.get("tile") if phase == P.KONG else None)
        info = payload.get("ask_hand_action_info")
        if info is not None:
            p = self.player_list[index]
            info["yixing_info"] = self.yixing_info()
            if phase in (P.TURN,P.DISCARD_ONLY):
                active = self.player_list[actor]
                info["deal_tile_type"] = {"normal":"deal_tile","kong":"deal_gang_tile","flower":"deal_buhua_tile"}.get(active.draw_kind) if active.has_draw_slot else None
                if index == actor:
                    legal = legal_cuts(self,index)
                    info["forbidden_cut_tiles"] = sorted(set(p.hand_tiles)-legal)
                    if p.last_draw_last_wall:
                        info["forced_cut_tiles"] = sorted(legal)
                    info["kong_candidates"] = {a:sorted(kong_tiles(self,index,a)) for a in ("angang","jiagang")}
            else:
                info["deal_tile_type"] = None
        return self._adapt(payload,index)

    def emit_window_payloads(self,window):
        if window["status"] == P.END.value:
            self._final_result_pending = True
            return []
        payloads = [self._ask_payload(i,window) for i in range(4)
                    if window["status"] != P.RESPONSE.value or self.action_dict.get(i)]
        self.outbound_payloads.extend(payloads)
        return payloads

    def build_pending_action_payload(self,index):
        if self.machine.phase in (P.END,P.READY,P.FINISHED) or not self.live_pending_window:
            return None
        return self._ask_payload(index,self.live_pending_window)

    def emit_visible_action_payloads(self,event,*,reveal_final=False):
        self.record_visible_action(event)
        action = event["action"]
        if action.startswith("hu"):
            return []
        payloads = []
        for viewer in range(4):
            payload = wire.visible_action_payload(self,viewer,event)
            info = payload["do_action_info"]
            if action == "jiagang":
                # The client upgrades this existing pung; the authoritative
                # snapshot already contains the resulting g meld.
                info["combination_target"] = f"k{event['tile']}"
            if action == "buhua":
                info.update(buhua_tile=event["tile"],is_mo_buhua=event.get("is_mo_buhua",False))
            if action == "yixing_last_choice":
                info["yixing_take_last"] = event["take"]
            info["yixing_info"] = self.yixing_info()
            payloads.append(self._adapt(payload,viewer))
        self.outbound_payloads.extend(payloads)
        if action == "buhua":
            from ..game_guobiao.buhua_broadcast import HAND_SETTLE_GAP_SEC
            self.outbound_payloads.append({"_pause":HAND_SETTLE_GAP_SEC})
        return payloads

    def build_final_settlement_payload(self,index):
        self.apply_deferred_score_changes()
        info = wire._final_show_result_info(self)
        info["next_status"] = "match_end" if self.match_finishing else "round_end_by_ready"
        info["yixing_info"] = self.yixing_info()
        info["yixing_round_changes"] = {p.original_player_index:self.round_changes[p.player_index] for p in self.player_list}
        if self.deferred_hu_settlements:
            entry = self.deferred_hu_settlements[0]
            info["hepai_player_huapai"] = list(self.player_list[entry["winner"]].huapai_list)
            info["yixing_fan_details"] = entry["fan_details"]
            info["recycle_discard"] = entry["recycle_discard"]
        else:
            info["score_changes"] = info["yixing_round_changes"]
        return self._adapt(dict(type="gamestate/yixing/show_result",success=True,
            game_info=wire.game_info_payload(self,index,reveal_final=True),show_result_info=info),index)

    async def present_final_settlements(self):
        for i in range(4):
            await self._send_claim_protection_payload(i,self.build_final_settlement_payload(i))

    def build_ready_status_payload(self,index):
        payload = wire.ready_status_payload(self,index)
        payload["ready_status_info"].update(action_tick=self.server_action_tick,yixing_info=self.yixing_info())
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
            for payload in self.restore_payloads(index):
                await connection.websocket.send_json(payload)
