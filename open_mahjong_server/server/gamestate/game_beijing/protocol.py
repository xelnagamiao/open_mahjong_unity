from ..public.settlement_sync import send_game_packet
import math
from ..game_zhongyong import boardcast as wire
from ...game_calculation.beijing import rules as book
from .actions import CHOWS,claim_tiles,legal_cuts,kong_tiles
from .client_context import private_context
from .state_machine import Phase as P


class Protocol:
    def beijing_info(self,viewer=None):
        ended = self.machine.phase in (P.END,P.READY,P.FINISHED)
        result = dict(rule_version=self.rule_version, phase=self.game_status, base_hand_tiles=13,
            hand_number=self.round_index, indicator=self.indicator, hun=self.hun,
            dice=list(self.dice), repeat=self.repeat, start_scores=list(self.round_start_scores),
            tian_ting=[p.tian_ting for p in self.player_list], discarded_hun=[p.discarded_hun for p in self.player_list],
            seat_to_original=[p.original_player_index for p in self.player_list],
            match_finishing=self.match_finishing, ledger=self.round_settlement if ended else None)
        if viewer in range(4):
            result["self_has_draw_slot"] = self.player_list[viewer].has_draw_slot
        return result

    def _player_view(self,index,viewer):
        p = self.player_list[index]
        ended = self.machine.phase in (P.END,P.READY,P.FINISHED)
        result = wire.player_info_payload(self,index,viewer,reveal_final=ended)
        result["remaining_time"] = math.ceil(max(0.0,p.remaining_time))
        result["hand_tiles"] = list(p.hand_tiles) if index == viewer or ended else None
        result["hand_tiles_count"] = len(p.hand_tiles)
        result["combination_tiles"] = ["G0" if code.startswith("G") and index != viewer and not ended else code for code in p.combination_tiles]
        result["combination_mask"] = [[0 if ended and n % 2 == 0 and v == 2 or n % 2 and p.combination_tiles[i].startswith("G") and index != viewer and not ended else v
            for n,v in enumerate(mask)] for i,mask in enumerate(p.combination_mask)]
        result["discard_riichi_flags"] = [False] * len(p.discard_tiles)
        return result

    def _adapt(self,payload,viewer):
        payload.setdefault("message", "")
        payload["player_index"] = viewer
        info = payload.get("game_info")
        if info is not None:
            info.update(detailed_config=dict(self.detailed_config),beijing_info=self.beijing_info(viewer),
                        players_info=[self._player_view(i,viewer) for i in range(4)])
            if viewer in range(4):
                info["self_hand_tiles"] = list(self.player_list[viewer].hand_tiles)
                info["beijing_hints"] = private_context(self,viewer)
        return payload

    def build_game_start_payload(self,index):
        return self._adapt(wire.game_start_payload(self,index),index)

    def _ask_payload(self,index,window):
        actor = window.get("player") if window.get("player") is not None else self.current_player_index
        payload = wire.ask_action_payload(self,index,self.action_dict.get(index,[]),action_player_index=actor,
            cut_tile=window.get("tile") if window["status"] == P.RESPONSE.value else None,
            rob_kong_tile=window.get("tile") if window["status"] == P.ROB_KONG.value else None)
        info = payload.get("ask_hand_action_info")
        if info is not None:
            p = self.player_list[index]
            active = self.player_list[actor]
            info["deal_tile_type"] = (("deal_gang_tile" if active.after_kong else "deal_tile")
                if active.has_draw_slot and window["status"] != P.TIANTING.value else None)
            if window["status"] == P.TIANTING.value:
                info["player_index"] = index
            if index == actor:
                info["forbidden_cut_tiles"] = sorted(set(p.hand_tiles)-legal_cuts(self,index))
                info["kong_candidates"] = {a:sorted(kong_tiles(self,index,a)) for a in ("angang","jiagang")}
        info = payload.get("ask_other_action_info")
        if info is not None:
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
                current = private_context(self,i,for_record=True)
                if current != snapshots.get(i):
                    append_action_tick(self,["beijing","waits",i,current])
                    snapshots[i] = current
        payloads = [self._ask_payload(i,window) for i in range(4)
                    if window["status"] not in (P.RESPONSE.value, P.ROB_KONG.value) or self.action_dict.get(i)]
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
            if event["action"] == "jiagang":
                payload["do_action_info"]["combination_target"] = f"k{event['tile']}"
            if event["action"] in ("declare_kan", "rob_kan"):
                payload["do_action_info"]["cut_tile"] = event["tile"]
            if event["action"] == "beijing_payment":
                payload["do_action_info"]["beijing_payment"] = event["payment"]
                payload["do_action_info"]["gang_score_changes"] = dict(enumerate(event["payment"]["changes"]))
            payloads.append(self._adapt(payload,viewer))
        self.outbound_payloads.extend(payloads)
        return payloads

    def build_final_settlement_payload(self,index):
        self.apply_deferred_score_changes()
        info = wire._final_show_result_info(self)
        info.update(next_status="match_end" if self.match_finishing else "round_end_by_ready",
                    beijing_info=self.beijing_info(index),
                    score_changes={p.original_player_index:self.round_changes[p.player_index] for p in self.player_list},
                    player_to_score={p.player_index:p.score for p in self.player_list},
                    beijing_end_hands={p.player_index:list(p.hand_tiles) for p in self.player_list})
        if self.deferred_hu_settlements:
            panels = []
            simultaneous = {e["winner"]:list(self.player_list[e["winner"]].hand_tiles) + ([] if e["source"] == "self_draw" else [e["tile"]])
                            for e in self.deferred_hu_settlements}
            for n, entry in enumerate(self.deferred_hu_settlements):
                panel = wire._final_show_result_info(self, n)
                panel.update(multi_ron=len(simultaneous)>1, recycle_discard=entry["recycle_discard"],
                    hepai_player_combination_mask=self._player_view(entry["winner"],index)["combination_mask"],
                    simultaneous_hu_hands=simultaneous if len(simultaneous)>1 else None,
                    next_status=info["next_status"] if n==len(self.deferred_hu_settlements)-1 else "round_continue")
                panels.append(panel)
            info["beijing_settlements"] = panels
        return self._adapt(dict(type="gamestate/beijing/show_result",success=True,
                           game_info=wire.game_info_payload(self,index,reveal_final=True),show_result_info=info),index)

    async def present_final_settlements(self):
        for i in range(4):
            await self._send_claim_protection_payload(i,self.build_final_settlement_payload(i))

    def build_ready_status_payload(self,index):
        result = wire.ready_status_payload(self,index)
        result["ready_status_info"]["action_tick"] = self.server_action_tick
        result["ready_status_info"]["beijing_info"] = self.beijing_info(index)
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
                await send_game_packet(self, connection.websocket, payload)
