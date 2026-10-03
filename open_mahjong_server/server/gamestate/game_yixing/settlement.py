"""Single-winner, zero-sum payments and dealer progression."""

from ...game_calculation.yixing.rules import RULE_VERSION, payments
from .state_machine import Phase as P


class EndOfHand:
    def settle_win(self, winner, source, tile, *, payer=None):
        if self.machine.phase not in (P.TURN, P.RESPONSE, P.KONG) or self.round_settlement is not None:
            raise ValueError("当前阶段不能和牌结算")
        if type(winner) is not int or winner not in range(4) or source not in ("self_draw", "discard", "rob_kong"):
            raise ValueError("和家或和牌来源无效")
        p = self.player_list[winner]
        if source == "self_draw":
            if self.machine.phase != P.TURN or payer is not None or winner != self.current_player_index or not p.hand_tiles or p.hand_tiles[-1] != tile:
                raise ValueError("自摸牌或和家无效")
        elif type(payer) is not int or payer not in range(4) or payer == winner:
            raise ValueError("放铳者无效")
        elif source == "discard":
            river = self.player_list[payer].discard_tiles
            if self.machine.phase != P.RESPONSE or not river or river[-1] != tile or payer != self.current_player_index:
                raise ValueError("和牌张不属于当前舍牌")
        elif self.machine.phase != P.KONG or not self.pending_kong or self.pending_kong["actor"] != payer or self.pending_kong["tile"] != tile or self.pending_kong["concealed"]:
            raise ValueError("只有当前加杠可被抢杠")
        if not self.can_win(winner, source, tile, payer=payer):
            raise ValueError("和牌资格不成立")
        quote = self.score_win(winner, source, tile, payer=payer)
        changes = payments(winner, quote.points, payer=payer)
        if source != "self_draw":
            owner = self.player_list[payer]
            if source == "discard":
                owner.discard_tiles.pop()
            else:
                owner.hand_tiles.remove(tile)
                owner.has_draw_slot = False
            p.won_tiles.append(tile)
        fans = [f"YX|{item.id}|{item.flowers}|{item.name}" for item in quote.patterns]
        if not quote.patterns:
            fans.append("YX|win_bottom|1|胡牌底花")
        elif len(quote.patterns) > 1:
            fans.append(f"YX|shared_bottom|{1-len(quote.patterns)}|重复底花")
        fans += [f"YX|{item.id}|{item.flowers}|{item.name}" for item in quote.extras]
        fans += [f"YX|multiplier|×{factor}|{name}" for name,factor in quote.multipliers]
        entry = dict(winner=winner, source=source, tile=tile, discarder=payer if source == "discard" else None,
                     kong_player=payer if source == "rob_kong" else None, points=quote.points,
                     fan_ids=fans, fan_names=quote.fan_names, fan_details=quote.as_dict(),
                     score_changes=changes, rule_version=RULE_VERSION, multi_ron=False,
                     recycle_discard=source == "discard", hu_order=1)
        self.deferred_hu_settlements.append(entry)
        p.is_hu, p.hu_order, p.has_draw_slot = True, 1, False
        self.hu_order_counter = 1
        self.round_changes = changes
        self.round_settlement = dict(winner=winner, payer=payer, source=source, tile=tile,
                                     changes=list(changes), score=quote.as_dict())
        self._finish_round(winner)
        self.pending_kong = None
        self._event(self._hu_action_for_settlement(entry), winner, tile=tile)
        return self._window(P.END)

    def _finish_round(self, winner=None):
        self.next_dealer = 0 if winner in (None,0) else 1
        self.match_finishing = self.current_round >= self.max_round*4 and bool(self.next_dealer)
        self.ended_by = "wall" if winner is None else "win"
        self.machine.transition(P.END)

    def end_draw(self):
        if self.round_settlement is not None:
            raise ValueError("本局已经结算")
        self.round_settlement = dict(winner=None, payer=None, source="draw", tile=None,
                                     changes=[0]*4, score=None)
        self._finish_round()
        return self._window(P.END)

    def apply_deferred_score_changes(self):
        if self.deferred_scores_applied or self.round_settlement is None:
            return
        for p,change in zip(self.player_list,self.round_changes):
            p.score += change
            p.score_history.append(f"+{change:02d}" if change > 0 else f"-{abs(change):02d}" if change < 0 else "0")
            p.round_number_history.append(self.current_round)
        self.deferred_scores_applied = True
