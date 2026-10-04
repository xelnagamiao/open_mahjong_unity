"""Viewer-safe physical snapshots; authoritative waits never expose other hands."""

from ...game_calculation.wenzhou.rules import hand_multiplier, waiting_tiles
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
        data["combination_tiles"] = wire.public_melds_for_viewer(p,viewer,reveal_final=ended)
        data["combination_mask"] = wire.public_combination_masks_for_viewer(p,viewer,reveal_final=ended)
        return data

    def _waits_for_hand(self, hand, melds):
        key = (self.caishen,tuple(sorted(hand)),tuple((m["code"],tuple(m["physical"])) for m in melds))
        if key in self._wait_cache:
            self._wait_cache.move_to_end(key)
            return self._wait_cache[key]
        result = []
        for tile in sorted(waiting_tiles(hand,melds,caishen=self.caishen)):
            ron = hand_multiplier(list(hand)+[tile],melds,caishen=self.caishen,winning_tile=tile,self_draw=False)
            zimo = hand_multiplier(list(hand)+[tile],melds,caishen=self.caishen,winning_tile=tile,self_draw=True)
            result.append(dict(tile=tile, ron=ron is not None, self_draw=zimo is not None,
                               ron_multiplier=ron or 0,
                               self_draw_multiplier=zimo or 0))
        self._wait_cache[key] = result
        if len(self._wait_cache) > 128:
            self._wait_cache.popitem(last=False)
        return result

    def authoritative_waits(self, viewer):
        if viewer not in range(4) or not self.tips or self.machine.phase in (P.END,P.READY,P.FINISHED):
            return {}
        p = self.player_list[viewer]
        hand, hands = sorted(p.hand_tiles), []
        base_size = 16-3*len(p.meld_records)
        if len(hand) == base_size:
            hands.append((hand, p.passed_win_multiplier))
        elif len(hand) == base_size+1:
            for cut in sorted(set(hand)):
                rest = list(hand)
                rest.remove(cut)
                discarded_win = hand_multiplier(hand,p.meld_records,caishen=self.caishen,winning_tile=cut,self_draw=False)
                threshold = max(p.passed_win_multiplier,discarded_win or 0)
                hands.append((rest,threshold))
        result = {}
        for candidate, threshold in hands:
            result[",".join(map(str,candidate))] = [dict(item,ron=item["ron"] and item["ron_multiplier"] > threshold)
                for item in self._waits_for_hand(candidate,p.meld_records)]
        return result

    def _adapt(self, payload, viewer):
        payload["player_index"] = viewer
        info = payload.get("game_info")
        if info is not None:
            info["detailed_config"] = dict(self.detailed_config)
            info["wenzhou_info"] = self.wenzhou_info()
            info["wenzhou_info"]["self_has_draw_slot"] = viewer in range(4) and self.player_list[viewer].has_draw_slot
            info["wenzhou_waits"] = self.authoritative_waits(viewer)
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
            info["wenzhou_info"] = self.wenzhou_info()
            active = self.player_list[actor]
            info["deal_tile_type"] = ("deal_gang_tile" if active.draw_kind == "kong" else "deal_tile") if active.has_draw_slot else None
            if index == actor:
                info["kong_candidates"] = {kind:sorted(kong_tiles(self,index,kind)) for kind in ("angang","jiagang")}
        claim = payload.get("ask_other_action_info")
        if claim is not None:
            logical = natural(window["tile"],self.caishen)
            claim["chi_candidates"] = {kind:[physical_for(self.player_list[index].hand_tiles,
                needed_tiles(kind,logical),self.caishen)] for kind in self.action_dict.get(index,[]) if kind in CHOWS}
        bank,step = self.remaining_clock(index,window)
        for ask in (info,claim):
            if ask is not None:
                ask.update(remaining_time=bank,step_remaining=step)
        return self._adapt(payload,index)

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
