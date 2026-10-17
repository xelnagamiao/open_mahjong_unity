from ...game_calculation.fuzhou import rules as book
from .state_machine import Phase as P


class EndOfHand:
    def settle_win(self,winner,source,tile=None,*,payer=None,special=None,heavenly=False,qiangjin=False):
        if self.round_settlement is not None:
            raise ValueError("本局已结算")
        quote = self.score_win(winner,source,tile,special=special,heavenly=heavenly,qiangjin=qiangjin)
        if quote is None:
            raise ValueError("不能按该牌型和牌")
        p = self.player_list[winner]
        if source in ("discard","rob_kong"):
            if type(payer) is not int or payer not in range(4) or payer==winner:
                raise ValueError("缺少有效的和牌来源")
            owner = self.player_list[payer]
            if source=="discard":
                if not owner.discard_tiles or owner.discard_tiles[-1]!=tile:
                    raise ValueError("和牌来源不在牌河末尾")
                owner.discard_tiles.pop()
            else:
                if not self.pending_kong or self.pending_kong["actor"]!=payer or self.pending_kong["tile"]!=tile:
                    raise ValueError("没有待裁定的加杠")
                if self.pending_kong["concealed"]:
                    raise ValueError("福州不能抢暗杠")
                owner.hand_tiles.remove(tile)
                owner.has_draw_slot = False
                self.pending_kong = None
            p.won_tiles.append(tile)
        self.round_changes = book.payments(winner,quote["points"],payer if source=="discard" else None)
        p.is_hu,p.hu_order = True,1
        entry = dict(winner=winner,source="self_draw" if source=="opening" else source,tile=tile,
            discarder=payer if source=="discard" else None,kong_player=payer if source=="rob_kong" else None,
            points=quote["points"],fan_ids=[row["id"] for row in quote["items"]],
            fan_names=[f"FZ|{row['id']}|{row['points']}|{row['name']}" for row in quote["items"]],
            fan_details=quote["items"],score_changes=list(self.round_changes),rule_version=self.rule_version,
            multi_ron=False,recycle_discard=source=="discard",special=special,qiangjin=qiangjin,heavenly=heavenly,
            forced=special is not None,hu_order=1,payment_source="discard" if source=="discard" else "self_draw")
        self.deferred_hu_settlements.append(entry)
        self.round_settlement = dict(winner=winner,source=source,tile=tile,payer=payer,
                                     changes=list(self.round_changes),score=quote)
        self._finish_round(winner)
        self._event("hu" if source in ("discard","rob_kong") else "hu_self",winner,tile=tile)
        return self._window(P.END)

    def _finish_round(self,winner=None):
        repeats = winner is None or winner==self.dealer_index
        self.next_dealer_streak = self.dealer_streak+1 if repeats else 0
        self.next_dealer_changes = self.dealer_changes+(0 if repeats else 1)
        self.next_dealer = self.dealer_index if repeats else (self.dealer_index+1)%4
        self.match_finishing = self.next_dealer_changes >= self.max_round*4
        self.ended_by = "wall" if winner is None else "win"
        self.machine.transition(P.END)

    def end_draw(self):
        if self.round_settlement is not None:
            raise ValueError("本局已结算")
        self.round_settlement = dict(winner=None,source="draw",tile=None,payer=None,changes=[0]*4,score=None)
        self._finish_round()
        return self._window(P.END)

    def apply_deferred_score_changes(self):
        if self.deferred_scores_applied or self.round_settlement is None:
            return
        for p,change in zip(self.player_list,self.round_changes):
            p.score += change
            p.score_history.append(f"+{change}" if change>0 else str(change))
            p.round_number_history.append(self.current_round)
        self.deferred_scores_applied = True

    def advance_round_after_ready(self):
        if self.machine.phase!=P.READY or self.match_finishing:
            raise RuntimeError("当前不能开始下一局")
        rotate = self.next_dealer_changes != self.dealer_changes and self.next_dealer_changes%4==0
        self.dealer_changes,self.dealer_streak = self.next_dealer_changes,self.next_dealer_streak
        self.dealer_index = self.current_player_index = self.next_dealer
        if rotate:
            # Every original player moves counterclockwise to the next seat.
            # Move player objects (scores, histories and identity) together.
            self.player_list = self.player_list[-1:] + self.player_list[:-1]
            for i,p in enumerate(self.player_list):
                p.player_index = i
            self.dealer_index = self.current_player_index = (self.dealer_index+1)%4
        self.current_round = self.dealer_changes+1
        self.round_index += 1
        self.machine.transition(P.START)

    def is_match_end(self):
        return self.match_finishing
