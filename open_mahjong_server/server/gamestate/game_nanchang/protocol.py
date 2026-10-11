import math
from ..game_zhongyong import boardcast as wire
from ...game_calculation.nanchang import rules as book
from .actions import CHOWS,claim_tiles,legal_cuts,kong_tiles
from .hints import private_hints
from .state_machine import Phase as P


class Protocol:
    def nanchang_info(self,viewer=None):
        ended = self.machine.phase in (P.END,P.READY,P.FINISHED)
        result = dict(rule_version=self.rule_version,phase=self.game_status,base_hand_tiles=13,
            hand_number=self.round_index,indicator=self.indicator,secondary=book.secondary_jing(self.indicator),
            indicator_index=self.indicator_index,dice=list(self.dice),jing_dice=list(self.jing_dice),
            declared_jingdiao=[p.declared_jingdiao for p in self.player_list],
            seat_to_original=[p.original_player_index for p in self.player_list],
            match_finishing=self.match_finishing,ledger=self.round_settlement if ended else None)
        if viewer in range(4):
            result["self_has_draw_slot"] = self.player_list[viewer].has_draw_slot
        return result

    def _player_view(self,index,viewer):
        p = self.player_list[index]
        ended = self.machine.phase in (P.END,P.READY,P.FINISHED)
        result = wire.player_info_payload(self,index,viewer,reveal_final=ended)
        # The time bank keeps fractions internally; the common client field is
        # an integer. Exact action deadlines use nanchang_info's millisecond fields.
        result["remaining_time"] = math.ceil(max(0.0,p.remaining_time))
        result["hand_tiles"] = list(p.hand_tiles) if index == viewer or ended else None
        result["hand_tiles_count"] = len(p.hand_tiles)
        result["combination_tiles"] = wire.public_melds_for_viewer(p,viewer,reveal_final=ended)
        result["combination_mask"] = wire.public_combination_masks_for_viewer(p,viewer,reveal_final=ended)
        result["discard_riichi_flags"] = [t in book.jing_tiles(self.indicator) for t in p.discard_tiles]
        return result

    def _adapt(self,payload,viewer):
        payload.setdefault("message", "")
        payload["player_index"] = viewer
        info = payload.get("game_info")
        if info is not None:
            info.update(detailed_config=dict(self.detailed_config),nanchang_info=self.nanchang_info(viewer),
                        players_info=[self._player_view(i,viewer) for i in range(4)])
            if viewer in range(4):
                info["self_hand_tiles"] = list(self.player_list[viewer].hand_tiles)
                info["nanchang_hints"] = private_hints(self,viewer)
        return payload

    def build_game_start_payload(self,index):
        return self._adapt(wire.game_start_payload(self,index),index)

    def _ask_payload(self,index,window):
        actor = window.get("player") if window.get("player") is not None else self.current_player_index
        payload = wire.ask_action_payload(self,index,self.action_dict.get(index,[]),action_player_index=actor,
                                        cut_tile=window.get("tile") if window["status"] == P.RESPONSE.value else None)
        info = payload.get("ask_hand_action_info")
        if info is not None:
            p = self.player_list[index]
            active = self.player_list[actor]
            info["deal_tile_type"] = ("deal_gang_tile" if active.after_kong else "deal_tile") if active.has_draw_slot else None
            if index == actor:
                info["forbidden_cut_tiles"] = sorted(set(p.hand_tiles)-legal_cuts(self,index))
                info["kong_candidates"] = {a:sorted(kong_tiles(self,index,a)) for a in ("angang","jiagang")}
        info = payload.get("ask_other_action_info")
        if info is not None:
            info["nanchang_chows"] = {a:claim_tiles(a,window["tile"])+[window["tile"]]
                                      for a in self.action_dict.get(index,[]) if a in CHOWS}
            info["chi_candidates"] = {a:[claim_tiles(a,window["tile"])] for a in self.action_dict.get(index,[]) if a in CHOWS}
        return self._adapt(payload,index)

    def emit_window_payloads(self,window):
        if window["status"] == P.END.value:
            self._final_result_pending = True
            return []
        if self._has_active_round_record():
            from ..public.game_record_manager import append_action_tick
            snapshots = self.__dict__.setdefault("_record_hints",{})
            for i in range(4):
                current = private_hints(self,i,for_record=True)
                if current != snapshots.get(i):
                    append_action_tick(self,["nanchang","waits",i,current])
                    snapshots[i] = current
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
        if event["action"].startswith("hu"):
            return []
        payloads = []
        for viewer in range(4):
            payload = wire.visible_action_payload(self,viewer,event)
            if event["action"] == "cut":
                payload["do_action_info"]["is_riichi_horizontal"] = event["tile"] in book.jing_tiles(self.indicator)
            if event["action"] == "jiagang":
                payload["do_action_info"]["combination_target"] = f"k{event['tile']}"
            payloads.append(self._adapt(payload,viewer))
        self.outbound_payloads.extend(payloads)
        return payloads

    def build_final_settlement_payload(self,index):
        self.apply_deferred_score_changes()
        info = wire._final_show_result_info(self)
        info.update(next_status="match_end" if self.match_finishing else "round_end_by_ready",
                    nanchang_info=self.nanchang_info(index),
                    score_changes={p.original_player_index:self.round_changes[p.player_index] for p in self.player_list},
                    player_to_score={p.player_index:p.score for p in self.player_list},
                    nanchang_end_hands={p.player_index:list(p.hand_tiles) for p in self.player_list})
        return self._adapt(dict(type="gamestate/nanchang/show_result",success=True,
                           game_info=wire.game_info_payload(self,index,reveal_final=True),show_result_info=info),index)

    async def present_final_settlements(self):
        for i in range(4):
            await self._send_claim_protection_payload(i,self.build_final_settlement_payload(i))

    def build_ready_status_payload(self,index):
        result = wire.ready_status_payload(self,index)
        result["ready_status_info"]["action_tick"] = self.server_action_tick
        result["ready_status_info"]["nanchang_info"] = self.nanchang_info(index)
        return self._adapt(result,index)

    def emit_ready_status_payloads(self):
        payloads = [self.build_ready_status_payload(i) for i in range(4)]
        self.outbound_payloads.extend(payloads)
        return payloads

    def restore_payloads(self,index):
        ended = self.machine.phase in (P.END,P.READY,P.FINISHED)
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

    def emit_game_end_payloads(self):
        results = self.finalize_match_result()
        payloads = [wire.game_end_payload(self,i) for i in range(4)]
        for payload in payloads:
            final = payload["game_end_info"]["player_final_data"]
            for p,result in zip(self.player_list,results):
                final[str(p.player_index)].update(competition_points=result["competition_points"])
        self.outbound_payloads.extend(payloads)
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
            for payload in self.restore_payloads(index):
                await connection.websocket.send_json(payload)
