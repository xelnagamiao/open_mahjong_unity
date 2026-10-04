from dataclasses import asdict, replace

from ...game_calculation.guizhou.rules import RULE_VERSION, maximum_ready_score
from ...game_calculation.guizhou.ledger import settle
from .state_machine import Phase as P


class EndOfHand:
    def _settlement_snapshot(self):
        return [dict(hand_tiles=list(p.hand_tiles), combination_tiles=list(p.combination_tiles),
                     discard_tiles=list(p.discard_tiles), won_tiles=list(p.won_tiles)) for p in self.player_list]

    def _calculate_end_ledger(self, winners=(), source="draw", tile=None, payer=None, quotes=None):
        values = [maximum_ready_score(p.hand_tiles, p.combination_tiles, ready=p.ready_kind)
                  if i not in winners else 0 for i, p in enumerate(self.player_list)]
        self.round_settlement = settle(self._settlement_snapshot(), winners=winners, source=source, payer=payer,
            scores={i: q.points for i, q in (quotes or {}).items()}, ready_values=values,
            indicator=self.tiles_list[0] if self.tiles_list and winners else None,
            chickens=tuple(self.chickens.values()), kongs=self.kongs,
            hot=source == "discard" and self.hot_discard)
        self.next_dealer = payer if len(winners) > 1 else winners[0] if winners else 0
        # MIL games have a fixed hand count, not extra hands for dealer repeats.
        self.match_finishing = self.current_round >= self.max_round * 4
        self.ended_by = "win" if winners else "wall"
        self.machine.transition(P.END)
        self._reveal_opening_kongs(at_settlement=True)

    def settle_winners(self, winners, source, tile, *, payer=None):
        if self.machine.phase not in (P.TURN, P.RESPONSE, P.KONG):
            raise ValueError("当前阶段不能和牌结算")
        if not winners or len(set(winners)) != len(winners) or any(type(i) is not int or i not in range(4) for i in winners):
            raise ValueError("和家列表无效")
        if source not in ("self_draw", "discard", "rob_kong"):
            raise ValueError("和牌来源无效")
        if source == "self_draw":
            if len(winners) != 1 or self.player_list[winners[0]].hand_tiles[-1] != tile:
                raise ValueError("自摸牌或和家无效")
        elif type(payer) is not int or payer not in range(4) or payer in winners:
            raise ValueError("放铳者无效")
        elif source == "discard":
            river = self.player_list[payer].discard_tiles
            if not river or river[-1] != tile:
                raise ValueError("和牌张不属于当前舍牌")
        elif not self.pending_kong or self.pending_kong["actor"] != payer or self.pending_kong["tile"] != tile or self.pending_kong["concealed"]:
            raise ValueError("只有当前加杠可被抢杠")
        if any(not self.can_win(i, source, tile, payer=payer) for i in winners):
            raise ValueError("和牌资格不成立")
        winners = sorted(winners, key=lambda i: (i-payer) % 4) if payer is not None else list(winners)
        quotes = {i: self.score_win(i, source, tile, payer=payer) for i in winners}
        if source != "self_draw":
            owner = self.player_list[payer]
            if source == "discard":
                owner.discard_tiles.pop()
                owner.discard_riichi_flags.pop()
                first = self.chickens.get(tile)
                if first and first.supplier == payer and first.claimant is None and sum(t == tile for _, t in self.discard_log) == 1:
                    self.chickens[tile] = replace(first, won=True)
            else:
                owner.hand_tiles.remove(tile)
                owner.has_draw_slot = False
            self.player_list[winners[-1]].won_tiles.append(tile)
        self._calculate_end_ledger(winners, source, tile, payer, quotes)
        self.pending_kong = None
        assigned = [0]*4
        for order, index in enumerate(winners, 1):
            quote, p = quotes[index], self.player_list[index]
            changes = [0]*4
            for transfer in self.round_settlement.transfers:
                if transfer.category == "hand" and transfer.payee == index:
                    changes[transfer.payer] -= transfer.points
                    changes[index] += transfer.points
            if order == len(winners):
                changes = [total-old for total, old in zip(self.round_settlement.changes, assigned)]
            assigned = [a+b for a, b in zip(assigned, changes)]
            record = dict(winner=index, source=source, tile=tile, discarder=payer if source == "discard" else None,
                          kong_player=payer if source == "rob_kong" else None, points=quote.points,
                          raw_points=quote.raw_points, fan_ids=[f"GZ|{f.id}|{f.points}|{f.name}" for f in quote.fans],
                          fan_names=[f.name for f in quote.fans], fan_details=[asdict(f) for f in quote.fans],
                          score_changes=changes, rule_version=RULE_VERSION, multi_ron=len(winners)>1,
                          recycle_discard=source != "self_draw" and order == len(winners), hu_order=order)
            self.deferred_hu_settlements.append(record)
            p.is_hu, p.hu_order, p.has_draw_slot = True, order, False
        self.hu_order_counter = len(winners)
        for record in self.deferred_hu_settlements:
            self._event(self._hu_action_for_settlement(record), record["winner"], tile=tile)
        return self._window(P.END)

    def end_draw(self):
        if self.round_settlement is not None:
            raise ValueError("本局已经结算")
        self._calculate_end_ledger()
        return self._window(P.END)

    def apply_deferred_score_changes(self):
        if self.deferred_scores_applied or self.round_settlement is None:
            return
        for p, change in zip(self.player_list, self.round_settlement.changes):
            p.score += change
            p.score_history.append(f"+{change:02d}" if change > 0 else f"-{abs(change):02d}" if change < 0 else "0")
            p.round_number_history.append(self.current_round)
        self.deferred_scores_applied = True
