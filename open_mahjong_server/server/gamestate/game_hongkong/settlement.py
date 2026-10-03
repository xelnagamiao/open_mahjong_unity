"""Win payment, stable-identity pull ledger, and repeat-dealer policy."""

from .state_machine import HongKongPhase as P
from ...game_calculation.hongkong.old_style import old_payments
from ...game_calculation.hongkong.modern13 import new13_payments
from ...game_calculation.hongkong.lianhuise13 import lianhuise_payments
from ...game_calculation.hongkong.qingzhang_remix import remix_payments


class HongKongSettlement:
    def _stable_to_seats(self,changes):
        return [changes[p.original_player_index] for p in self.player_list]

    def settle_winners(self,winners,source,tile,*,payer=None,flower="",head_bumped=()):
        if not winners or len(set(winners))!=len(winners) or any(i not in range(4) for i in winners):
            raise ValueError("Invalid winner seats")
        if not self.rules.allow_multiple_winners and len(winners)!=1:
            raise ValueError("This thirteen-tile profile uses seat-order priority")
        if source not in ("self_draw","discard","rob_kong"):
            raise ValueError("Invalid win source")
        if source=="self_draw" and len(winners)!=1:
            raise ValueError("A self draw can only have one winner")
        if source!="self_draw" and (payer not in range(4) or payer in winners):
            raise ValueError("Invalid win payer")
        if head_bumped and (not self.rules.is_remix or self.rules.allow_multiple_winners or
                source=="self_draw" or flower or len(winners)!=1 or
                any(type(i) is not int or i not in range(4) or i in winners or i==payer for i in head_bumped) or
                len(set(head_bumped))!=len(head_bumped)):
            raise ValueError("Invalid head-bumped claimants")
        if not flower and source=="discard":
            river = self.player_list[payer].discard_tiles
            if not river or river[-1]!=tile:
                raise ValueError("Winning tile is not the current discard")
        if not flower and source=="rob_kong":
            if not self.pending_kong or self.pending_kong["actor"]!=payer or self.pending_kong["tile"]!=tile:
                raise ValueError("Winning tile is not the pending kong")
            if tile not in self.player_list[payer].hand_tiles:
                raise ValueError("Robbed tile is missing")
        quotes = {}
        for i in (*winners,*head_bumped):
            if not self.can_win(i,source,tile,payer=payer,flower=flower):
                raise ValueError(f"Seat {i} cannot win this action")
        for i in winners:
            quotes[i] = self.score_win(i,source,tile,payer=payer,multi=len(winners),flower=flower)
        dealer_continues = self.rules.repeat_dealer and 0 in winners
        self.match_finishing = self.current_round>=self.max_round*4 and not dealer_continues
        records = []
        payments = {}
        for i in winners:
            p,result = self.player_list[i],quotes[i]
            liability = next((q.player_index for q in self.player_list if q.original_player_index==p.liability_payer),None)
            if self.rules.is_remix:
                changes = remix_payments(result.fan,i,discarder=payer if source!="self_draw" else None,
                    liability=liability,liability_kind=p.liability_kind,head_bumped=head_bumped)
            elif self.rules.is_old:
                changes = old_payments(result.fan,i,discarder=payer if source!="self_draw" else None,
                    liability=liability,rob_kong=source=="rob_kong",kong_draw=p.replacement_kind=="kong")
            elif self.rules.is_lianhuise:
                if p.replacement_kind == "kong" and p.kong_liability_payer is not None:
                    liability = next(q.player_index for q in self.player_list if q.original_player_index==p.kong_liability_payer)
                changes = lianhuise_payments(result.fan,i,discarder=payer if source!="self_draw" else None,liability=liability)
            elif not self.rules.is_sixteen:
                changes = new13_payments(result.fan,i,discarder=payer if source!="self_draw" else None,
                    liability=liability,full_shoot=self.rules.new13_full_shoot,
                    nine_exposed=len(p.combination_tiles)>=3 and "full_flush" in result.fan_ids)
            else:
                changes = [0]*4
                debtors = [j for j in range(4) if j!=i] if source=="self_draw" else [payer]
                for j in debtors:
                    points = result.fan
                    if source=="self_draw" and i!=0 and j==0 and not flower:
                        points += 1+2*self.dealer_streak
                    key = (p.original_player_index,self.player_list[j].original_player_index)
                    payments[key] = payments.get(key,0)+points
            # Dynamic values are part of the label rather than looked up from
            # another variant's static table (winds, kongs, tail chains, doubles).
            labels = [f"HK|{f.id}|{f.value}|{f.name}"+(f"|{f.unit}" if f.unit else "") for f in result.fans]
            records.append(dict(winner=i,source=source,tile=tile,discarder=payer if source=="discard" else None,
                kong_player=payer if source=="rob_kong" else None,points=result.fan,raw_points=result.raw_fan,
                fan_ids=labels,fan_names=list(result.fan_names),fan_details=[dict(id=f.id,name=f.name,value=f.value) for f in result.fans],
                score_changes=changes,rule_version=self.rules.version,flower_win=flower,
                multi_ron=len(winners)>1,recycle_discard=False))
        if self.rules.is_sixteen:
            stable_winners = [self.player_list[i].original_player_index for i in winners]
            delta = self.ledger.apply_win(payments,winners=stable_winners,self_draw=source=="self_draw")
            if self.match_finishing:
                collected = self.ledger.collect()
                delta = [a+b for a,b in zip(delta,collected)]
            records[-1]["score_changes"] = self._stable_to_seats(delta)
            records[-1]["pull_payments"] = [dict(winner=a,payer=b,points=v) for (a,b),v in sorted(payments.items())]
        if not flower and source in ("discard","rob_kong"):
            owner = self.player_list[payer]
            if source=="discard":
                owner.discard_tiles.pop()
                if owner.discard_riichi_flags:
                    owner.discard_riichi_flags.pop()
            else:
                owner.hand_tiles.remove(tile)
                owner.has_draw_slot = False
            self.won_tiles.append(tile)
            records[-1]["recycle_discard"] = True
        self.pending_kong = None
        self.deferred_hu_settlements.extend(records)
        for order,record in enumerate(records,1):
            p = self.player_list[record["winner"]]
            p.is_hu = True
            p.hu_order = order
            record["hu_order"] = order
            p.has_draw_slot = False
        self.hu_order_counter = len(records)
        self.ended_by = "win"
        self.machine.transition(P.END)
        for record in records:
            self._event(self._hu_action_for_settlement(record),record["winner"],tile=tile)
        return self._window(P.END,ended_by="win")

    def apply_immediate_points(self,changes,reason):
        if len(changes)!=4 or any(type(v) is not int for v in changes) or sum(changes)!=0:
            raise ValueError("Immediate payment must be a zero-sum four-seat vector")
        for i,value in enumerate(changes):
            self.player_list[i].score += value
            self.immediate_changes[i] += value
        if self.deferred_scores_applied:
            self._refresh_score_history()
        self._event("hongkong_score",self.current_player_index,score_changes=list(changes),reason=reason)

    def apply_deferred_score_changes(self):
        if self.deferred_scores_applied:
            return
        for record in self.deferred_hu_settlements:
            for i,value in enumerate(record["score_changes"]):
                self.player_list[i].score += value
        for p in self.player_list:
            p.score_history.append("0")
            p.round_number_history.append(self.current_round)
        self.deferred_scores_applied = True
        self._refresh_score_history()
        if self.rules.is_remix:
            negative = {p.original_player_index for p in self.player_list if p.score < 0}
            # Judge once, after all winners are paid. A player still below zero
            # after their one recovery hand ends the match, even on a repeat.
            self.match_finishing |= bool(negative & self.negative_score_grace)
            self.negative_score_grace = negative

    def _refresh_score_history(self):
        for p in self.player_list:
            change = p.score-self.round_start_scores[p.player_index]
            p.score_history[-1] = f"{change:+d}" if change else "0"

    def cut_eligible_pulls(self,index):
        stable = self.player_list[index].original_player_index
        return [d for d in self.ledger.debts.values() if d.debtor==stable and d.mouths>=3]

    def cut_pulls(self,index):
        if self.machine.phase != P.READY:
            raise ValueError("Pulls can only be cut between hands")
        debts = self.cut_eligible_pulls(index)
        if not debts:
            raise ValueError("No three-mouth debt to cut")
        total = [0]*4
        for debt in debts:
            change = self.ledger.cut(debt.debtor,debt.creditor)
            total = [a+b for a,b in zip(total,change)]
        self.apply_immediate_points(self._stable_to_seats(total),"斩拉")

    def advance_round_after_ready(self):
        if self.machine.phase != P.READY:
            raise RuntimeError("Advance requires every player to finish the result phase")
        winners = [r["winner"] for r in self.deferred_hu_settlements]
        keep = self.dealer_keeps_after_hand(winners)
        if keep:
            self.round_index += 1
            if self.rules.is_remix:
                self.dealer_streak += 1
            elif winners:
                self.dealer_streak = self.dealer_streak+1 if winners==[0] else 0
        else:
            self.advance_dealer_after_round()
            self.dealer_streak = 0
        self.current_player_index = 0
        self.live_pending_window = None
        self.machine.transition(P.STARTING)
