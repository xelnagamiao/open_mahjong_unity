from ...game_calculation.beijing import rules as book
from .state_machine import Phase as P


class EndOfHand:
    def settle_win(self, winner, source, tile, *, payer=None, four_hun=False):
        if (self.machine.phase != P.TURN or source != "self_draw" or payer is not None
                or winner != self.current_player_index or self.round_settlement is not None):
            raise ValueError("自摸窗口错误")
        if not (self.can_four_hun(winner) if four_hun else self.can_win(winner, source, tile)):
            raise ValueError("未满足合法和牌条件")
        self._settle_one(winner, source, tile, four_hun=four_hun)
        return self.finish_round([winner])

    def settle_winners(self, winners, source, tile, payer):
        if (not winners or self.round_settlement is not None or payer != self.current_player_index
                or self.machine.phase != (P.ROB_KONG if source == "rob_kong" else P.RESPONSE)
                or any(not self.can_win(i, source, tile, payer=payer) for i in winners)):
            raise ValueError("点和窗口错误")
        if source == "rob_kong":
            declarer = self.player_list[payer]
            drawn = declarer.has_draw_slot and declarer.hand_tiles[-1] == tile
            declarer.hand_tiles.remove(tile)
            declarer.has_draw_slot = False
            self._event("rob_kan", payer, tile=tile, is_mo_gang=drawn)
            self.pending_kong = None
        for n, winner in enumerate(winners):
            self._settle_one(winner, source, tile, payer=payer,
                             multi_ron=len(winners) > 1, recycle_discard=source == "discard" and n == 0)
        if source == "discard":
            river = self.player_list[payer].discard_tiles
            if river and river[-1] == tile:
                river.pop()
        return self.finish_round(winners)

    def _settle_one(self, winner, source, tile, *, payer=None, four_hun=False, multi_ron=False, recycle_discard=False):
        p = self.player_list[winner]
        quote = self.score_win(winner, source, tile, payer=payer, four_hun=four_hun)
        liability = None
        if self.detailed_config["pao_four_calls"] and p.open_calls == 4 and len(p.hand_tiles) == (2 if source == "self_draw" else 1):
            liability = p.fourth_call_payer
        if self.detailed_config["pao_kong"] and source == "self_draw" and p.after_kong and p.kong_payer is not None:
            liability = p.kong_payer
        changes = book.payments(winner, quote["points"], payer=payer, liability=liability,
                                all_pay=self.detailed_config["ron_all_pay"])
        fans = [f"BJ|{value}|翻|{name}" for name, value in quote["fan_values"].items()]
        if quote["fan"] != quote["raw_fan"]:
            fans.append(f"BJ|{quote['fan'] - quote['raw_fan']}|翻|混儿减半")
        entry = dict(winner=winner, source=source, tile=tile, discarder=payer, liability=liability,
            points=quote["points"], fan_ids=fans, fan_names=quote["fan_names"], fan_details=quote,
            score_changes=changes, rule_version=self.rule_version,
            multi_ron=multi_ron, recycle_discard=recycle_discard, hu_order=len(self.deferred_hu_settlements) + 1)
        self.deferred_hu_settlements.append(entry)
        p.is_hu, p.hu_order, p.has_draw_slot = True, entry["hu_order"], False
        self.hu_order_counter += 1
        for player, change in zip(self.player_list, changes):
            player.score += change
        self._event(self._hu_action_for_settlement(entry), winner, tile=tile)

    def finish_round(self, winners=()):
        if self.round_settlement is not None:
            raise ValueError("本副已结算")
        self.next_dealer = 0 if 0 in winners else 1
        self.match_finishing = self.next_dealer != 0 and self.current_round >= self.max_round * 4
        self.round_changes = [p.score - start for p, start in zip(self.player_list, self.round_start_scores)]
        self.ended_by = "win" if winners else "wall"
        self.round_settlement = dict(winners=list(winners), reason=self.ended_by,
            changes=list(self.round_changes), payments=list(self.payment_ledger))
        return self._window(P.END)

    def end_draw(self):
        return self.finish_round()

    def apply_deferred_score_changes(self):
        if self.deferred_scores_applied or self.round_settlement is None:
            return
        for p, change in zip(self.player_list, self.round_changes):
            p.score_history.append(f"{change:+d}" if change else "0")
            p.round_number_history.append(self.current_round)
            p.record_counter.round_score_total = getattr(p.record_counter, "round_score_total", 0) + change
        self.deferred_scores_applied = True
