"""Public gold/flower state and private declaration choices share one wire model."""
import math
from ..game_zhongyong import boardcast as wire
from .actions import legal_cuts, kong_tiles, declaration_cuts
from .hints import build_hints
from .state_machine import Phase as P


class Protocol:
    def fuzhou_info(self):
        ended = self.machine.phase in (P.END,P.READY,P.FINISHED)
        return dict(rule_version=self.rule_version,phase=self.game_status,base_hand_tiles=16,
            hand_number=self.round_index,dealer=self.dealer_index,dealer_streak=self.dealer_streak,
            dealer_changes=self.dealer_changes,dice=list(self.dice),gold_tile=self.jin or 0,
            indicator_claimed=self.indicator_claimed,reserved_tiles=self.dead_wall_count,
            last_four=self.in_last_four,sea_remaining=len(self.sea_queue),
            youjin_owner=self.youjin_owner,double_youjin_owner=self.double_youjin_owner,
            match_finishing=self.match_finishing,round_changes=list(self.round_changes),
            start_scores=list(self.round_start_scores),
            declarations=[dict(youjin=p.youjin,golden_middle=p.golden_middle,discarded_gold=p.discarded_gold,
                               youjin_tile=p.youjin_tile or 0) for p in self.player_list],
            final_hands={i:list(p.hand_tiles)+p.won_tiles for i,p in enumerate(self.player_list)} if ended else None,
            ledger=self.round_settlement if ended else None,
            seat_to_original=[p.original_player_index for p in self.player_list])

    def _player_view(self,index,viewer):
        p = self.player_list[index]
        ended = self.machine.phase in (P.END,P.READY,P.FINISHED)
        data = wire.player_info_payload(self,index,viewer,reveal_final=ended)
        hand = list(p.hand_tiles)+(p.won_tiles if ended else [])
        data.update(hand_tiles=hand if index==viewer or ended else None,hand_tiles_count=len(hand),
                    score=p.score+(0 if self.deferred_scores_applied else self.round_changes[index]),
                    remaining_time=math.ceil(max(0,p.remaining_time)),huapai_list=list(p.huapai_list))
        return data

    def _adapt(self,payload,viewer):
        payload["player_index"] = viewer
        info = payload.get("game_info")
        if info is not None:
            info["detailed_config"] = dict(self.detailed_config)
            info["fuzhou_info"] = self.fuzhou_info()
            if viewer in range(4):
                info["fuzhou_info"]["self_has_draw_slot"] = self.player_list[viewer].has_draw_slot
                info["fuzhou_hints"] = build_hints(self,viewer)
            info["players_info"] = [self._player_view(i,viewer) for i in range(4)]
            info["self_hand_tiles"] = list(self.player_list[viewer].hand_tiles) if viewer in range(4) else None
        return payload

    def build_game_start_payload(self,index):
        return self._adapt(wire.game_start_payload(self,index),index)

    def _ask_payload(self,index,window):
        phase = P(window["status"])
        actor = window["player"] if window.get("player") is not None else self.current_player_index
        payload = wire.ask_action_payload(self,index,self.action_dict.get(index,[]),action_player_index=actor,
            # Opening declarations can include the dealer's heavenly self-draw.
            # Send their hand-action prompt; a discard-claim prompt filters out
            # self-draw in clients. Other seats still claim the gold indicator.
            cut_tile=window.get("tile") if phase==P.RESPONSE or phase==P.OPENING and index!=self.dealer_index else None,
            rob_kong_tile=window.get("tile") if phase==P.KONG else None)
        info = payload.get("ask_hand_action_info") or payload.get("ask_other_action_info")
        if info is not None:
            p = self.player_list[index]
            info.update(fuzhou_info=self.fuzhou_info(),fuzhou_hints=build_hints(self,index),
                        remaining_time=math.ceil(max(0,p.remaining_time)))
            clock = self.action_clocks.get(index)
            if clock:
                info["remaining_time"],info["step_remaining"] = clock.parts(self._timing_now())
            if phase in (P.TURN,P.DISCARD_ONLY,P.SEA):
                active = self.player_list[actor]
                info["deal_tile_type"] = {"normal":"deal_tile","kong":"deal_gang_tile","flower":"deal_buhua_tile","sea":"deal_tile"}.get(active.draw_kind) if active.has_draw_slot else None
                if index==actor:
                    legal = legal_cuts(self,index) if phase!=P.SEA else set()
                    info["forbidden_cut_tiles"] = sorted(set(p.hand_tiles)-legal)
                    info["kong_candidates"] = {a:sorted(kong_tiles(self,index,a)) for a in ("angang","jiagang")}
                    info["fuzhou_declaration_candidates"] = {a:sorted(declaration_cuts(self,index,a)) for a in ("youjin","tingjin")}
            else:
                info["deal_tile_type"] = None
            if phase==P.OPENING:
                info["fuzhou_declaration_candidates"] = {"qiangjin":sorted(self.qiangjin_cuts(index))} if index==self.dealer_index else {}
        return self._adapt(payload,index)

    def emit_window_payloads(self,window):
        if window["status"]==P.END.value:
            self._final_result_pending = True
            return []
        payloads = [self._ask_payload(i,window) for i in range(4)
                    if window["status"]!=P.RESPONSE.value or self.action_dict.get(i)]
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
            if action=="jiagang":
                info["combination_target"] = f"k{event['tile']}"
            if action=="buhua":
                info.update(buhua_tile=event["tile"],is_mo_buhua=event.get("is_mo_buhua",False))
            info["fuzhou_info"] = self.fuzhou_info()
            # Gold flips and dynamic reserves consume drawable tiles too;
            # qiangjin transfers the exposed indicator without a second draw.
            info["tile_count"] = max(0,len(self.tiles_list)-self.dead_wall_count)
            payloads.append(self._adapt(payload,viewer))
        self.outbound_payloads.extend(payloads)
        if action=="buhua":
            from ..game_guobiao.buhua_broadcast import HAND_SETTLE_GAP_SEC
            self.outbound_payloads.append({"_pause":HAND_SETTLE_GAP_SEC})
        return payloads

    def build_settlement_payloads(self,index):
        self.apply_deferred_score_changes()
        info = wire._final_show_result_info(self)
        info.update(next_status="match_end" if self.match_finishing else "round_end_by_ready",fuzhou_info=self.fuzhou_info())
        if self.deferred_hu_settlements:
            entry = self.deferred_hu_settlements[0]
            info.update(hu_fan=list(entry["fan_names"]),fuzhou_fan_details=entry["fan_details"],
                        hepai_player_huapai=list(self.player_list[entry["winner"]].huapai_list),
                        recycle_discard=entry["recycle_discard"],skip_hand_reveal=entry["forced"],
                        fuzhou_special=entry["special"],fuzhou_qiangjin=entry["qiangjin"])
            if entry["payment_source"]=="self_draw":
                info["hu_class"] = "hu_self"
        return [self._adapt(dict(type="gamestate/fuzhou/show_result",success=True,
            game_info=wire.game_info_payload(self,index,reveal_final=True),show_result_info=info),index)]

    def build_final_settlement_payload(self,index):
        return self.build_settlement_payloads(index)[0]

    async def present_final_settlements(self):
        for i in range(4):
            await self._send_claim_protection_payload(i,self.build_final_settlement_payload(i))

    def build_ready_status_payload(self,index):
        payload = wire.ready_status_payload(self,index)
        payload["ready_status_info"].update(action_tick=self.server_action_tick,fuzhou_info=self.fuzhou_info())
        return payload

    def emit_ready_status_payloads(self):
        payloads = [self.build_ready_status_payload(i) for i in range(4)]
        self.outbound_payloads.extend(payloads)
        return payloads

    def restore_payloads(self,index):
        payloads = [self.build_game_start_payload(index)]
        if self.machine.phase in (P.END,P.READY,P.FINISHED):
            payloads.extend(self.build_settlement_payloads(index))
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
