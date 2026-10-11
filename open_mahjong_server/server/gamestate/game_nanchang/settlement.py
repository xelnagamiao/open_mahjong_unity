from ...game_calculation.nanchang import rules as book
from .state_machine import Phase as P


class EndOfHand:
    def settle_win(self,winner,source,tile,*,payer=None):
        if self.round_settlement is not None or type(winner) is not int or winner not in range(4):
            raise ValueError("和牌结算阶段错误")
        p = self.player_list[winner]
        if source == "self_draw":
            if self.machine.phase != P.TURN or winner != self.current_player_index or payer is not None or not p.hand_tiles or p.hand_tiles[-1] != tile:
                raise ValueError("自摸实体错误")
        elif source == "discard":
            if self.machine.phase != P.RESPONSE or payer != self.current_player_index or payer == winner:
                raise ValueError("放铳座位错误")
            if not self.player_list[payer].discard_tiles or self.player_list[payer].discard_tiles[-1] != tile:
                raise ValueError("和牌张不是当前弃牌")
        else:
            raise ValueError("南昌规则没有此和牌来源")
        if not self.can_win(winner,source,tile,payer=payer):
            raise ValueError("未满足合法和牌条件")
        quote = self.score_win(winner,source,tile,payer=payer)
        if source == "discard":
            self.player_list[payer].discard_tiles.pop()
            p.won_tiles.append(tile)
            self.transfer_jing(payer,winner,tile)
        fans = [f"NC|{value}|{'分' if quote['fixed'] else '番'}|{name}" for name,value in quote["fan_values"].items()]
        entry = dict(winner=winner,source=source,tile=tile,discarder=payer,points=quote["points"],
                     fan_ids=fans,fan_names=quote["fan_names"],fan_details=quote,score_changes=quote["changes"],
                     rule_version=book.RULE_VERSION,multi_ron=False,recycle_discard=source == "discard",hu_order=1)
        self.deferred_hu_settlements.append(entry)
        p.is_hu,p.hu_order,p.has_draw_slot = True,1,False
        self.hu_order_counter = 1
        self.finish_round(winner,quote)
        self._event(self._hu_action_for_settlement(entry),winner,tile=tile)
        return self._window(P.END)

    def finish_round(self,winner=None,quote=None):
        owned = [list(p.hand_tiles)+list(p.discard_tiles)+list(p.won_tiles)+
                 [t for m in p.combination_tiles for t in book.meld_tiles(m)] for p in self.player_list]
        disabled = [i for i,p in enumerate(self.player_list) if "peida" in p.tag_list]
        jing = book.jing_payments(owned,indicator=self.indicator,transfers=self.jing_transfers,disabled=disabled)
        kong = [sum(x["changes"][i] for x in self.kong_ledger if x["actor"] not in disabled) for i in range(4)]
        win = quote["changes"] if quote else [0]*4
        self.round_changes = [a+b+c for a,b,c in zip(jing["changes"],kong,win)]
        self.round_settlement = dict(winner=winner,score=quote,jing=jing,kong=list(self.kong_ledger),
                                     jing_changes=jing["changes"],kong_changes=kong,win_changes=win,
                                     changes=list(self.round_changes))
        self.next_dealer = winner if winner is not None else 0
        self.match_finishing = self.current_round >= self.max_round*4
        self.ended_by = "wall" if winner is None else "win"
        self.machine.transition(P.END)

    def end_draw(self):
        if self.round_settlement is not None:
            raise ValueError("本副已结算")
        self.finish_round()
        return self._window(P.END)

    def apply_deferred_score_changes(self):
        if self.deferred_scores_applied or self.round_settlement is None:
            return
        for p,change in zip(self.player_list,self.round_changes):
            p.score += change
            p.score_history.append(f"{change:+d}" if change else "0")
            p.round_number_history.append(self.current_round)
            p.record_counter.round_score_total = getattr(p.record_counter,"round_score_total",0)+change
        self.deferred_scores_applied = True
