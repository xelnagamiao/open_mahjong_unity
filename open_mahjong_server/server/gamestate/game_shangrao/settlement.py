from ...game_calculation.shangrao import rules as book
from .state_machine import Phase as P


class EndOfHand:
    def settle_win(self, winner, source, tile, *, payer=None):
        if self.round_settlement is not None or type(winner) is not int or winner not in range(4):
            raise ValueError("和牌结算阶段错误")
        p = self.player_list[winner]
        if (source != "self_draw" or self.machine.phase != P.TURN or winner != self.current_player_index
                or payer is not None or not p.hand_tiles or p.hand_tiles[-1] != tile):
            raise ValueError("上饶麻将只能自摸")
        if not self.can_win(winner, source, tile):
            raise ValueError("未满足合法和牌条件")
        quote = self.score_win(winner, source, tile)
        for i in range(4):
            self.check_four_bao(i, final=True)
        fans = [f"SR|{value}|番|{name}" for name, value in quote["fan_values"].items()]
        fans += [f"SR|{value}|倍|{name}" for name, value in quote["multipliers"].items()]
        entry = dict(winner=winner, source=source, tile=tile, discarder=None, points=quote["points"],
                     fan_ids=fans, fan_names=quote["fan_names"], fan_details=quote,
                     score_changes=book.payments(winner, quote["points"]), rule_version=self.rule_version,
                     multi_ron=False, recycle_discard=False, hu_order=1)
        self.deferred_hu_settlements.append(entry)
        p.is_hu, p.hu_order, p.has_draw_slot = True, 1, False
        self.hu_order_counter = 1
        for player, change in zip(self.player_list, entry["score_changes"]):
            player.score += change
        self._event(self._hu_action_for_settlement(entry), winner, tile=tile)
        self.finish_round(winner, quote)
        return self._window(P.END)

    def finish_round(self, winner=None, quote=None):
        if self.round_settlement is not None:
            raise ValueError("本副已结算")
        for i, p in enumerate(self.player_list):
            self.check_four_bao(i, final=True)
            if p.hand_tiles.count(self.indicator) == 3:
                self.record_payment(i, 2, "indicator_triplet", self.indicator, announce=False)
                if self._has_active_round_record():
                    from ..public.game_record_manager import append_action_tick
                    append_action_tick(self, ["shangrao", "payment", self.payment_ledger[-1]])
        self.round_changes = [p.score - start for p, start in zip(self.player_list, self.round_start_scores)]
        self.round_settlement = dict(winner=winner, score=quote, payments=list(self.payment_ledger),
                                     changes=list(self.round_changes))
        self.next_dealer = winner if winner is not None else 0
        self.match_finishing = self.current_round >= self.max_round * 4
        self.ended_by = "wall" if winner is None else "win"
        self.machine.transition(P.END)

    def end_draw(self):
        self.finish_round()
        return self._window(P.END)

    def apply_deferred_score_changes(self):
        if self.deferred_scores_applied or self.round_settlement is None:
            return
        for p, change in zip(self.player_list, self.round_changes):
            p.score_history.append(f"{change:+d}" if change else "0")
            p.round_number_history.append(self.current_round)
            p.record_counter.round_score_total = getattr(p.record_counter, "round_score_total", 0) + change
        self.deferred_scores_applied = True
