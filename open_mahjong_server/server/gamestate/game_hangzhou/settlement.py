"""Zero-sum liability and streaks, evaluated by the rule module."""

from ...game_calculation.hangzhou.rules import settle_win as quote_payments
from .state_machine import Phase as P


class EndOfHand:
    def settle_win(self, winner, source, tile):
        if self.round_settlement is not None or type(winner) is not int or winner not in range(4):
            raise ValueError("本局已结算或和家无效")
        p = self.player_list[winner]
        if winner != self.current_player_index:
            raise ValueError("和家不是当前行动者")
        if source == "self_draw":
            if self.machine.phase != P.TURN or not p.has_draw_slot or not p.hand_tiles or p.hand_tiles[-1] != tile:
                raise ValueError("自摸牌无效")
        elif source == "ten_winds":
            if self.machine.phase != P.TEN_WINDS or len(p.discard_origin_tiles) != 10 or p.discard_origin_tiles[-1] != tile:
                raise ValueError("未到十风宣告时机")
        else:
            raise ValueError("杭州只能自摸或十风和牌")
        quote = self.score_win(winner, source, tile)
        if quote is None:
            raise ValueError("和牌资格不成立")
        payment = quote_payments(quote, winner, self.dealer_index, self.dealer_streak,
                                 tuple(other.chi_count for other in self.player_list))
        changes = list(payment.changes)
        entry = dict(winner=winner, source=source, tile=tile if source == "self_draw" else 0,
                     discarder=None, kong_player=None, points=quote.points,
                     fan_ids=[f"HZ|{fan.id}|{fan.fan}|{fan.name}" for fan in quote.fans],
                     fan_names=quote.fan_names, fan_details=quote.as_dict(), score_changes=changes,
                     rule_version=self.rule_version, multi_ron=False, recycle_discard=False, hu_order=1)
        self.deferred_hu_settlements.append(entry)
        self.round_changes = changes
        self.round_settlement = dict(winner=winner, source=source, tile=tile,
                                     changes=changes, score=quote.as_dict(), payment=payment.as_dict())
        p.is_hu, p.hu_order, p.has_draw_slot = True, 1, False
        self.hu_order_counter = 1
        self.next_dealer = winner
        self._finish_round("win")
        self._event("hu_self", winner, tile=entry["tile"], win_source=source)
        return self._window(P.END)

    def _finish_round(self, reason):
        self.match_finishing = self.current_round >= self.max_round * 4
        self.ended_by = reason
        self.machine.transition(P.END)

    def end_draw(self):
        if self.round_settlement is not None:
            raise ValueError("本局已经结算")
        self.round_settlement = dict(winner=None, source="draw", tile=None,
                                     changes=[0] * 4, score=None, payment=None)
        self.next_dealer = 0
        self._finish_round("wall")
        return self._window(P.END)

    def apply_deferred_score_changes(self):
        if self.deferred_scores_applied or self.round_settlement is None:
            return
        for p, change in zip(self.player_list, self.round_changes):
            p.score += change
            p.score_history.append(f"+{change:02d}" if change > 0 else f"-{abs(change):02d}" if change < 0 else "0")
            p.round_number_history.append(self.current_round)
        self.deferred_scores_applied = True
