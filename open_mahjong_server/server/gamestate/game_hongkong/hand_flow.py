"""Transactional action windows: validate every choice before changing the table."""

from collections import Counter

from .state_machine import HongKongPhase as P
from .action_check import turn_actions, claim_actions, rob_kong_actions, required_claim_tiles, forbidden_after_claim
from ...game_calculation.hongkong import structural_waits


class HongKongHandFlow:
    def begin_turn(self,index,*,discard_only=False):
        self.current_player_index = index
        p = self.player_list[index]
        self.hand_action_is_gang_draw[index] = p.replacement_kind=="kong"
        self.action_policy.refresh_waiting_tiles(self,index,exclude_last_tile=p.has_draw_slot)
        return self._window(P.DISCARD_ONLY if discard_only else P.TURN,index,
                            actions=turn_actions(self,index,discard_only=discard_only))

    def apply_action_results(self,window,action_results,*,settlements=None):
        if settlements:
            raise ValueError("Hong Kong settlement is calculated by the authoritative room")
        if window is not self.live_pending_window or window.get("action_tick") != self.server_action_tick:
            raise ValueError("Expired action window")
        offered = window.get("actions") or {}
        expected = {i for i,a in offered.items() if a}
        if set(action_results) != expected:
            raise ValueError("Responses do not match the pending seats")
        for index,data in action_results.items():
            self.validate_response(index,data,offered[index])
        phase = P(window["status"])
        if phase in (P.TURN,P.DISCARD_ONLY):
            result = self._resolve_turn(window,action_results[window["player"]])
        elif phase==P.DISCARD_RESPONSE:
            result = self._resolve_discard(window,action_results)
        elif phase==P.KONG_RESPONSE:
            result = self._resolve_kong(action_results)
        elif phase in (P.FLOWER,P.FLOWER_RESPONSE):
            result = self.resolve_flower(action_results)
        elif phase==P.INITIAL_READY:
            result = self.resolve_initial_ready(action_results)
        elif phase==P.REPLACEMENT:
            result = self.resolve_replacement()
        else:
            raise ValueError(f"No action resolution in {phase.value}")
        return self.open_action_window(result)

    def validate_response(self,index,data,offered):
        action = data.get("action_type")
        if action not in offered:
            raise ValueError(f"Illegal action {action!r} for seat {index}")
        if action == "cut":
            self._validated_cut(index,data)
        elif action in ("angang","jiagang"):
            tile = data.get("target_tile")
            p = self.player_list[index]
            if type(tile) is not int or tile not in p.hand_tiles:
                raise ValueError("Kong tile is not in hand")
            if action=="angang" and p.hand_tiles.count(tile)!=4:
                raise ValueError("Concealed kong requires four physical copies")
            if action=="jiagang" and f"k{tile}" not in p.combination_tiles:
                raise ValueError("Added kong requires an exposed pung")

    def _validated_cut(self,index,data):
        p = self.player_list[index]
        tile = data.get("TileId")
        if type(tile) is not int or tile not in self.legal_discard_tiles(index):
            raise ValueError("Illegal discard tile")
        cut_index = data.get("cutIndex",-1)
        if type(cut_index) is not int or cut_index < -1 or cut_index>=len(p.hand_tiles):
            raise ValueError("Invalid discard index")
        cut_class = data.get("cutClass")
        if cut_class is not None:
            if type(cut_class) is not bool:
                raise ValueError("Discard class must be a boolean")
            # The UI index is a presentation coordinate: players may drag their
            # tiles, and a claimed meld need not sort the remaining client hand.
            # Locate the physical server copy by tile and drawn/held identity.
            if cut_class:
                if not p.has_draw_slot or p.hand_tiles[-1]!=tile or cut_index not in (-1,len(p.hand_tiles)-1):
                    raise ValueError("Draw discard does not identify the drawn tile")
                cut_index = len(p.hand_tiles)-1
            else:
                held = p.hand_tiles[:-1] if p.has_draw_slot else p.hand_tiles
                if tile not in held:
                    raise ValueError("Held discard does not identify a held tile")
                cut_index = held.index(tile)
        elif cut_index>=0:
            if p.hand_tiles[cut_index]!=tile:
                raise ValueError("Discard index does not identify the requested tile")
        else:
            cut_index = len(p.hand_tiles)-1 if p.has_draw_slot and p.hand_tiles[-1]==tile else p.hand_tiles.index(tile)
        if p.declared_ready and cut_index != len(p.hand_tiles)-1:
            raise ValueError("Declared ready can only discard the drawn physical tile")
        return tile,cut_index,p.has_draw_slot and cut_index==len(p.hand_tiles)-1

    def _resolve_turn(self,window,data):
        index = window["player"]
        p = self.player_list[index]
        action = data["action_type"]
        if action == "riichi":
            p.ready_pending = True
            return self.begin_turn(index,discard_only=P(window["status"])==P.DISCARD_ONLY)
        if action == "riichi_cancel":
            p.ready_pending = False
            return self.begin_turn(index,discard_only=P(window["status"])==P.DISCARD_ONLY)
        if action == "hu_self":
            return self.settle_winners([index],"self_draw",p.hand_tiles[-1])
        if "hu_self" in window["actions"][index]:
            self.remember_pass(index,p.hand_tiles[-1],source="self_draw")
        if action in ("angang","jiagang"):
            return self._attempt_kong(index,data["target_tile"],concealed=action=="angang")
        tile,cut_index,moqie = self._validated_cut(index,data)
        newly_ready = p.ready_pending
        if newly_ready:
            self.declare_ready(index,self._ready_kind(index,moqie))
        elif p.declared_ready:
            p.immediate_ready = False
        p.hand_tiles.pop(cut_index)
        p.hand_tiles.sort()
        p.has_draw_slot = False
        p.discard_count += 1
        p.discard_tiles.append(tile)
        p.discard_origin_tiles.append(tile)
        marker = p.pending_ready_marker
        p.discard_riichi_flags.append(marker)
        p.pending_ready_marker = False
        p.discarded_since_draw.add(tile)
        p.forbidden_discards.clear()
        self.discard_log.append((index,tile))
        if self.rules.is_remix and index == 3:
            # The source defines a circuit as East through North, not a
            # player's next draw. North's discard starts the final response
            # window of that circuit, so reset after it resolves below.
            self._remix_circuit_ends = True
        self.last_discard_offsets[index] = len(self.discard_log)-1
        self.opening_action_taken = True
        display_index = data.get("cutIndex",-1)
        self._event("cut",index,tile=tile,cutIndex=display_index if display_index>=0 else cut_index,cutClass=moqie,
                    riichi_discard=marker,is_timeout_action=bool(data.get("is_timeout_action")))
        actions = claim_actions(self,index,tile)
        self.begin_claim_protection(actions,index)
        return self._window(P.DISCARD_RESPONSE,index,tile=tile,actions=actions)

    def declare_ready(self,index,kind):
        p = self.player_list[index]
        p.ready_pending = False
        p.declared_ready = True
        p.ready_kind = kind
        p.immediate_ready = True
        p.pending_ready_marker = True
        if "declared_ready" not in p.tag_list:
            p.tag_list.append("declared_ready")
        self._event("riichi",index,ready_qualification=kind)

    def _ready_kind(self,index,moqie):
        p = self.player_list[index]
        first = p.discard_count==0
        if self.rules.is_sixteen:
            if first and not self.opening_flow_interrupted:
                return "heaven" if index==0 and moqie else "earth"
            if first and len(self.discard_log)<=4 and getattr(self,"meld_actors",[])==[index]:
                return "human"
        elif not self.opening_flow_interrupted:
            if index==0 and first:
                return "heaven"
            if len(self.discard_log)<8:
                return "earth"
        return "ordinary"

    def _resolve_discard(self,window,responses):
        discarder,tile = window["player"],window["tile"]
        winners = sorted((i for i,d in responses.items() if d["action_type"]=="hu"),key=lambda i:(i-discarder)%4)
        if winners:
            selected = self.prioritize_winners(winners,"discard",tile,discarder)
            bumped = [i for i in winners if i not in selected] if self.rules.is_remix else ()
            return self.settle_winners(selected,"discard",tile,payer=discarder,head_bumped=bumped)
        for index,data in responses.items():
            if "hu" in window["actions"][index]:
                self.remember_pass(index,tile,payer=discarder)
            if self.rules.is_lianhuise and "peng" in window["actions"][index] and data["action_type"] not in ("peng","gang"):
                self.player_list[index].passed_claim_tiles.add(tile)
            if data["action_type"]=="pass" and any(a in window["actions"][index] for a in ("peng","gang","chi_left","chi_mid","chi_right")):
                self.player_list[index].passed_claim_tiles.add(tile)
        if self.rules.is_remix and getattr(self,"_remix_circuit_ends",False):
            for player in self.player_list:
                player.discard_win_lockout_tiles.clear()
            self._remix_circuit_ends = False
        priority = {"peng":2,"gang":2,"chi_left":1,"chi_mid":1,"chi_right":1}
        choices = [(-priority[d["action_type"]],(i-discarder)%4,i,d["action_type"])
                   for i,d in responses.items() if d["action_type"] in priority]
        if not choices:
            return self.draw_for((discarder+1)%4,normal=True)
        _,_,index,action = min(choices)
        return self._claim(index,discarder,tile,action)

    def prioritize_winners(self,winners,source,tile,payer):
        if self.rules.is_lianhuise:
            winners = sorted(winners,key=lambda i:self.score_win(i,source,tile,payer=payer).shape!="orphans")
        return winners if self.rules.allow_multiple_winners else winners[:1]

    @staticmethod
    def _claim_mask(action,tile,index,discarder):
        required = list(required_claim_tiles(action,tile))
        if action.startswith("chi"):
            middle = {"chi_left":tile-1,"chi_mid":tile,"chi_right":tile+1}[action]
            return f"s{middle}",required,[1,tile,0,required[0],0,required[1]]
        count = 4 if action=="gang" else 3
        offset = (discarder-index)%4
        marked = {1:count-1,2:1,3:0}[offset]
        mask = [part for n in range(count) for part in (1 if n==marked else 0,tile)]
        return ("g" if action=="gang" else "k")+str(tile),required,mask

    def _claim(self,index,discarder,tile,action):
        p = self.player_list[index]
        code,required,mask = self._claim_mask(action,tile,index,discarder)
        liable = self.would_assume_liability(index,action,tile)
        dragon_liability = liable and self.rules.use_dragon_liability and action in ("peng","gang") and tile in (45,46,47) and sum(c[0] in "kgG" and int(c[1:]) in (45,46,47) for c in p.combination_tiles)==2
        for member in required:
            p.hand_tiles.remove(member)
        p.combination_tiles.append(code)
        p.combination_mask.append(mask)
        p.meld_suppliers.append(self.player_list[discarder].original_player_index)
        if self.rules.is_remix:
            self._remix_liability(index,discarder)
        if liable and (self.rules.is_old or p.liability_payer is None or (self.rules.is_lianhuise and dragon_liability)):
            p.liability_payer = self.player_list[discarder].original_player_index
            p.liability_kind = "dragons" if dragon_liability else "twelve"
        if action == "gang":
            p.kong_liability_payer = (self.player_list[discarder].original_player_index
                if self.rules.use_kong_liability and not any(t==tile for _,t in self.discard_log[:-1]) else None)
        source = self.player_list[discarder]
        source.discard_tiles.pop()
        if source.discard_riichi_flags:
            marker = source.discard_riichi_flags.pop()
            if marker:
                source.pending_ready_marker = True
        p.has_draw_slot = False
        self.current_player_index = index
        self.meld_actors.append(index)
        self.interrupt_opening()
        self._event(action,index,tile=tile,meld_code=code,combination_mask=mask,cut_from_player=discarder)
        if action=="gang":
            p.consecutive_kongs = 1
            return self.draw_for(index,kind="kong")
        p.forbidden_discards = {tile} if self.rules.is_lianhuise else forbidden_after_claim(action,tile)
        return self.begin_turn(index,discard_only=True)

    def _remix_liability(self,index,discarder):
        p = self.player_list[index]
        supplier = self.player_list[discarder].original_player_index
        if self.rules.use_twelve_liability and len(p.meld_suppliers)==4 and all(s==supplier for s in p.meld_suppliers):
            p.liability_payer,p.liability_kind = supplier,"twelve"
            return
        if not self.rules.liability_limit or p.liability_payer is not None:
            return
        melds = p.combination_tiles
        pungs = {int(c[1:]) for c in melds if c[0] in "kgG"}
        four = len(melds)==4
        dragons = {45,46,47} <= pungs
        winds = {41,42,43,44} <= pungs
        four_kongs = four and all(c[0] in "gG" for c in melds)
        four_identical = four and all(c[0]=="s" for c in melds) and len(set(melds))==1
        four_steps = four and len(pungs)==4 and max(pungs)<40 and len({t//10 for t in pungs})==1 and max(pungs)-min(pungs)==3
        if dragons or winds or four_kongs or four_identical or four_steps:
            p.liability_payer,p.liability_kind = supplier,"limit"

    def _attempt_kong(self,index,tile,*,concealed):
        self.current_player_index = index
        p = self.player_list[index]
        self.pending_kong = dict(actor=index,tile=tile,concealed=concealed,
                                 is_mo_gang=p.has_draw_slot and p.hand_tiles[-1]==tile)
        self.meld_actors.append(index)
        self.interrupt_opening()
        actions = rob_kong_actions(self,index,tile,concealed=concealed)
        # Even an uncontested kong traverses this phase; it commits only after
        # the response window, so an orphan rob cannot remove four tiles first.
        return self._window(P.KONG_RESPONSE,index,tile=tile,actions=actions)

    def _resolve_kong(self,responses):
        pending = self.pending_kong
        actor,tile = pending["actor"],pending["tile"]
        winners = sorted((i for i,d in responses.items() if d["action_type"]=="hu"),key=lambda i:(i-actor)%4)
        if winners:
            selected = self.prioritize_winners(winners,"rob_kong",tile,actor)
            bumped = [i for i in winners if i not in selected] if self.rules.is_remix else ()
            # Settlement validates the unmutated hand and consumes one robbed
            # tile. The other three concealed tiles / the old pung stay put.
            return self.settle_winners(selected,"rob_kong",tile,payer=actor,head_bumped=bumped)
        for index in responses:
            if "hu" in self.live_pending_window["actions"][index]:
                self.remember_pass(index,tile,source="rob_kong",payer=actor)
        p = self.player_list[actor]
        if pending["concealed"]:
            for _ in range(4):
                p.hand_tiles.remove(tile)
            code = f"G{tile}"
            mask = [0 if self.rules.open_concealed_kong else 2,tile]*4
            if self.rules.is_remix:
                mask = [2,tile,0,tile,0,tile,2,tile]
            p.combination_tiles.append(code)
            p.combination_mask.append(mask)
            p.meld_suppliers.append(None)
            action = "angang"
        else:
            p.hand_tiles.remove(tile)
            position = p.combination_tiles.index(f"k{tile}")
            code = f"g{tile}"
            mask = list(p.combination_mask[position])
            called = next(i for i in range(0,len(mask),2) if mask[i]==1)
            mask[called:called] = [3,tile]
            p.combination_tiles[position] = code
            p.combination_mask[position] = mask
            action = "jiagang"
        p.has_draw_slot = False
        p.kong_liability_payer = None
        p.consecutive_kongs += 1
        self._event(action,actor,tile=tile,meld_code=code,combination_mask=mask,is_mo_gang=pending["is_mo_gang"])
        if pending["concealed"] and self.rules.is_sixteen:
            changes = [-5]*4
            changes[actor] = 15
            self.apply_immediate_points(changes,"暗杠")
        self.pending_kong = None
        return self.draw_for(actor,kind="kong")

    def end_draw(self):
        self.ended_by = "wall"
        self.match_finishing = not self.dealer_keeps_after_hand([]) and self.current_round>=self.max_round*4
        if self.rules.is_sixteen and self.match_finishing and self.ledger.debts:
            self.apply_immediate_points(self._stable_to_seats(self.ledger.collect()),"终局收账")
        return self._window(P.END,ended_by="wall")
