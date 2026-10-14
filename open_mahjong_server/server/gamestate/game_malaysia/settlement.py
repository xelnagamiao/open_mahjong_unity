from ...game_calculation.malaysia import rules as R
from .phases import P


class Settlement:
    def pay_instant(self, receiver, payers, amount, reason):
        changes = [0, 0, 0]
        for payer in payers:
            R.transfer(changes, payer, receiver, amount)
            self.ledger.append(dict(payer=payer, receiver=receiver, points=amount, reason=reason))
        for p, delta in zip(self.player_list, changes):
            p.score += delta
        self.event("malaysia_payment", receiver, score_changes=changes)

    def settle_win(self, winner, source, tile, *, payer=None, automatic=False):
        if self.round_settlement is not None or winner not in range(3):
            raise ValueError("本局已结算或和家无效")
        if automatic:
            if source != "self_draw" or self.discard_log or self.opening_interrupted:
                raise ValueError("仅起手可以四飞自动和")
        elif source == "self_draw":
            if self.machine.phase != P.TURN or winner != self.current_player_index or payer is not None:
                raise ValueError("非自摸阶段")
        elif source == "discard":
            if self.machine.phase != P.RESPONSE or payer != self.current_player_index or not self.player_list[payer].discard_tiles or self.player_list[payer].discard_tiles[-1] != tile:
                raise ValueError("和牌张不属于当前舍牌")
        elif source == "rob_kong":
            if self.machine.phase != P.KONG or not self.pending_kong or self.pending_kong["concealed"] or self.pending_kong["actor"] != payer or self.pending_kong["tile"] != tile:
                raise ValueError("不是可抢的加杠")
        else:
            raise ValueError("未知和牌来源")
        quote = self.score_win(winner, source, tile, automatic=automatic)
        if quote is None:
            raise ValueError("未达到和牌条件")
        sides = [R.side_fan(p.hand_tiles, p.melds, p.huapai_list, i) for i, p in enumerate(self.player_list)]
        flies = [R.fly_count(p.hand_tiles, p.melds, p.huapai_list) for p in self.player_list]
        changes, ledger = R.payments(winner, quote, sides, flies, payer=payer, liable=self.player_list[winner].liability)
        self.ledger.extend(ledger)
        p = self.player_list[winner]
        if source != "self_draw":
            if source == "discard":
                self.player_list[payer].discard_tiles.pop()
            else:
                source_player = self.player_list[payer]
                from_draw = source_player.has_draw_slot and source_player.hand_tiles[-1] == tile
                source_player.hand_tiles.remove(tile)
                source_player.has_draw_slot = False
                # Added kongs remain uncommitted during the response window.
                # Remove the fourth held tile on every live client before revealing the winner.
                self.event("rob_kan", payer, tile=tile, is_mo_gang=from_draw)
            p.won_tiles.append(tile)
        p.is_hu, p.hu_order = True, 1
        self.hu_order_counter = 1
        entry = dict(winner=winner, source=source, tile=tile, discarder=payer if source == "discard" else None,
            kong_player=payer if source == "rob_kong" else None, points=quote.points, fan_ids=quote.labels,
            fan_names=[x.name for x in quote.items] + list(quote.maximums), score_changes=changes,
            multi_ron=False, recycle_discard=source == "discard", automatic=automatic, hu_order=1)
        self.deferred_hu_settlements.append(entry)
        self.round_settlement = dict(winner=winner, source=source, payer=payer, tile=tile, quote=quote.as_dict(),
            changes=changes, sides=sides, flies=flies, ledger=list(self.ledger), automatic=automatic)
        self.next_dealer = winner
        self.pending_kong = None
        self.machine.transition(P.END)
        self.ended_by = "win"
        self.event("hu_self" if source == "self_draw" else "hu", winner, tile=tile)
        return self.window(P.END)

    def end_draw(self):
        if self.round_settlement is not None:
            raise ValueError("本局已经结算")
        # Instant bonuses remain. Fly comparison is a winning-hand side payment,
        # so a drawn hand has no additional transfer.
        self.round_settlement = dict(winner=None, source="draw", payer=None, tile=None, changes=[0, 0, 0], quote=None, ledger=list(self.ledger))
        self.ended_by, self.next_dealer = "wall", 0
        return self.window(P.END)

    def apply_deferred_score_changes(self):
        if self.deferred_scores_applied or self.round_settlement is None:
            return
        for p, delta, start in zip(self.player_list, self.round_settlement["changes"], self.round_start_scores):
            p.score += delta
            total = p.score - start
            p.score_history.append(f"{total:+d}" if total else "0")
            p.round_number_history.append(self.current_round)
            p.record_counter.round_score_total = getattr(p.record_counter, "round_score_total", 0) + total
        self.round_changes = [p.score - start for p, start in zip(self.player_list, self.round_start_scores)]
        self.deferred_scores_applied = True
