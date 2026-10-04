"""Rule-local view projection around the shared turn-based wire contract."""

from copy import deepcopy

from ..game_zhongyong import boardcast as wire
from .state_machine import HongKongPhase as P
from .action_check import ready_discards
from ...game_calculation.hongkong import structural_waits
from .hints import private_hints


class HongKongProtocol:
    def hongkong_info(self):
        return dict(rule_version=self.rules.version,phase=self.game_status,
                    base_hand_tiles=self.rules.structure.base_hand_tile_count,
                    opening_complete=not self.opening_flower_stage,
                    dealer_streak=self.dealer_streak,pulls=self.ledger.snapshot(),
                    starting_score=self.rules.starting_score,
                    negative_score_grace=sorted(self.negative_score_grace),
                    match_finishing=self.match_finishing)

    def _adapt(self,payload,viewer):
        payload["player_index"] = viewer
        info = payload.get("game_info")
        if info is not None:
            info["detailed_config"] = dict(self.detailed_config)
            info["hepai_limit"] = self.rules.minimum_fan
            info["hongkong_info"] = self.hongkong_info()
            info["hongkong_waits"] = private_hints(self,viewer) if viewer in range(4) else {}
            info["players_info"] = [self._player_view(i,viewer) for i in range(4)]
            info["self_hand_tiles"] = list(self.player_list[viewer].hand_tiles) if viewer in range(4) else None
        return payload

    def _player_view(self,index,viewer):
        p = self.player_list[index]
        data = wire.player_info_payload(self,index,viewer,reveal_final=False)
        reveal = index==viewer or p.is_hu
        data["hand_tiles"] = list(p.hand_tiles) if reveal else None
        data["hand_tiles_count"] = len(p.hand_tiles)
        data["huapai_list"] = list(p.huapai_list)
        data["discard_riichi_flags"] = list(p.discard_riichi_flags)
        data["combination_tiles"] = [c if reveal or self.rules.open_concealed_kong or not c.startswith("G") else "G0" for c in p.combination_tiles]
        data["combination_mask"] = []
        for code,mask in zip(p.combination_tiles,p.combination_mask):
            visible = list(mask)
            if code.startswith("G") and not self.rules.open_concealed_kong and not reveal:
                visible = [2,0]*4
            data["combination_mask"].append(visible)
        return data

    def build_game_start_payload(self,index):
        return self._adapt(wire.game_start_payload(self,index,reveal_final=False),index)

    def _ask_payload(self,index,window):
        phase = window.get("status")
        actor = window.get("player")
        if actor is None:
            actor = self.current_player_index
        if phase==P.INITIAL_READY.value and self.action_dict.get(index):
            actor = index
        cut = window.get("tile") if phase==P.DISCARD_RESPONSE.value else None
        if phase==P.FLOWER_RESPONSE.value:
            cut = 0
        rob = window.get("tile") if phase==P.KONG_RESPONSE.value else None
        payload = wire.ask_action_payload(self,index,self.action_dict.get(index,[]),
                                          action_player_index=actor,cut_tile=cut,rob_kong_tile=rob)
        info = payload.get("ask_hand_action_info")
        if info is not None:
            p = self.player_list[index]
            info["ready_qualification"] = p.ready_kind or "none"
            if index==actor and phase in (P.TURN.value,P.DISCARD_ONLY.value):
                legal = self.legal_discard_tiles(index)
                info["forbidden_cut_tiles"] = sorted(set(p.hand_tiles)-legal)
                if p.declared_ready:
                    info["forced_cut_tiles"] = sorted(legal)
                if p.ready_pending:
                    info["riichi_candidate_cuts"] = {}
                    for tile in sorted(ready_discards(self,index)):
                        hand = list(p.hand_tiles)
                        hand.remove(tile)
                        info["riichi_candidate_cuts"][tile] = sorted(structural_waits(hand,p.combination_tiles,self.rules))
            else:
                info["deal_tile_type"] = None
            info["hongkong_info"] = self.hongkong_info()
        return self._adapt(payload,index)

    def emit_window_payloads(self,window):
        if window.get("status")==P.END.value:
            self._final_result_pending = True
            return []
        payloads = [self._ask_payload(i,window) for i in range(4)]
        self.outbound_payloads.extend(payloads)
        return payloads

    def build_pending_action_payload(self,index):
        if self.machine.phase in (P.END,P.FINISHED) or self.live_pending_window is None:
            return None
        return self._ask_payload(index,self.live_pending_window)

    def emit_visible_action_payloads(self,event,*,reveal_final=False):
        self.record_visible_action(event)
        action,actor = event["action"],event["player"]
        if action.startswith("hu"):
            return []
        payloads = []
        for viewer in range(4):
            payload = wire.visible_action_payload(self,viewer,event,reveal_final=False)
            info = payload["do_action_info"]
            if action=="angang" and not self.rules.open_concealed_kong and viewer!=actor:
                payload["tile"] = 0
                payload["meld_code"] = "G0"
                info["combination_target"] = "G0"
                info["combination_mask"] = [2,0]*4
            if action=="buhua":
                info["buhua_tile"] = event["tile"]
                info["is_mo_buhua"] = event.get("is_mo_buhua",False)
            if action=="riichi":
                info["ready_qualification"] = event.get("ready_qualification")
            if action=="cut":
                info["is_riichi_horizontal"] = bool(event.get("riichi_discard"))
            if action=="hongkong_score":
                info["gang_score_changes"] = dict(enumerate(event["score_changes"]))
                info["gang_score_type"] = event["reason"]
            info["hongkong_info"] = self.hongkong_info()
            payloads.append(self._adapt(payload,viewer))
        self.outbound_payloads.extend(payloads)
        if action=="buhua":
            # Match Guobiao's reveal -> replacement pacing. This marker is
            # consumed by the transport shell, never sent or stored in replay.
            from ..game_guobiao.buhua_broadcast import HAND_SETTLE_GAP_SEC
            self.outbound_payloads.append({"_pause":HAND_SETTLE_GAP_SEC})
        return payloads

    def final_settlement_payloads(self,viewer):
        self.apply_deferred_score_changes()
        records = self.deferred_hu_settlements
        result = []
        for index in range(max(1,len(records))):
            info = wire._final_show_result_info(self,index if records else None)
            last = index==len(records)-1 or not records
            info["next_status"] = ("match_end" if self.match_finishing else "round_end_by_ready") if last else "round_continue"
            info["hongkong_info"] = self.hongkong_info()
            info["hongkong_round_changes"] = {p.original_player_index:p.score-self.round_start_scores[p.player_index] for p in self.player_list}
            if records:
                record = records[index]
                p = self.player_list[record["winner"]]
                info["hepai_player_huapai"] = list(p.huapai_list)
                info["multi_ron"] = record.get("multi_ron",False)
                info["recycle_discard"] = record.get("recycle_discard",True)
                info["hongkong_flower_win"] = record.get("flower_win","")
                if record.get("flower_win"):
                    info["hepai_player_hand"] = list(p.hand_tiles)
                    info["hepai_tile"] = None
                info["hongkong_fan_details"] = record["fan_details"]
            else:
                info["score_changes"] = info["hongkong_round_changes"]
            result.append(dict(type="gamestate/hongkong/show_result",success=True,player_index=viewer,show_result_info=info))
        return result

    def build_final_settlement_payload(self,index):
        return self.final_settlement_payloads(index)[-1]

    async def present_final_settlements(self):
        import asyncio
        from ..public.round_end_timing import sichuan_settle_hu_panel_wait_seconds
        views = [self.final_settlement_payloads(i) for i in range(4)]
        for step in range(len(views[0])):
            for i in range(4):
                await self._send_claim_protection_payload(i,views[i][step])
            if step<len(views[0])-1:
                await asyncio.sleep(sichuan_settle_hu_panel_wait_seconds(len(views[0][step]["show_result_info"].get("hu_fan") or [])))

    def emit_ready_status_payloads(self):
        payloads = [self.build_ready_status_payload(index) for index in range(4)]
        self.outbound_payloads.extend(payloads)
        return payloads

    def build_ready_status_payload(self,index):
        payload = wire.ready_status_payload(self,index)
        payload["ready_status_info"]["action_tick"] = self.server_action_tick
        payload["ready_status_info"]["hongkong_info"] = self.hongkong_info()
        payload["ready_status_info"]["can_cut_pull"] = bool(self.cut_eligible_pulls(index)) and not self.match_finishing and index in self.waiting_players_list
        return payload

    def restore_payloads(self,index):
        payloads = [self.build_game_start_payload(index)]
        if self.machine.phase in (P.END,P.READY):
            # All winners are present in the snapshot; the final panel carries the
            # aggregate ledger delta and is safe to restore without repeating it.
            payloads.append(self.build_final_settlement_payload(index))
            if self.machine.phase==P.READY:
                payloads.append(self.build_ready_status_payload(index))
        else:
            pending = self.build_pending_action_payload(index)
            if pending:
                payloads.append(pending)
        return payloads

    async def player_reconnect(self,user_id):
        for index,p in enumerate(self.player_list):
            if p.user_id==user_id:
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
