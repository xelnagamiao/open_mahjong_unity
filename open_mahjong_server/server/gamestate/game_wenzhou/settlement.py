"""Independent zero-sum ledgers for kong, win and caishen, applied once."""

import random

from ...game_calculation.wenzhou.rules import caishen_payments, dealer_multiplier, kong_payments, payments
from .state_machine import Phase as P


class EndOfHand:
    def settle_kong(self, player, kind):
        changes = kong_payments(player, kind, dealer=0, repeats=self.dealer_streak)
        for p, amount in zip(self.player_list, changes):
            p.score += amount
            self.round_changes[p.player_index] += amount
        self.ledger.append(dict(kind=kind, player=player, changes=list(changes)))
        return {i: value for i,value in enumerate(changes)}

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
        win_changes = payments(winner, quote["multiplier"], dealer=0, repeats=self.dealer_streak)
        counts = [player.hand_tiles.count(self.caishen) for player in self.player_list]
        if source != "self_draw" and tile == self.caishen:
            counts[winner] += 1
            if source == "rob_kong":
                counts[payer] -= 1
        caishen_changes = caishen_payments(counts)
        # Validate scoring before consuming either the river or added tile.
        if source != "self_draw":
            owner = self.player_list[payer]
            if source == "discard":
                owner.discard_tiles.pop()
            else:
                owner.hand_tiles.remove(tile)
                owner.hand_tiles.sort()
                owner.has_draw_slot = False
            p.won_tiles.append(tile)
        self.caishen_counts, self.caishen_changes = counts, caishen_changes
        changes = [a+b for a,b in zip(win_changes, caishen_changes)]
        self.round_changes = [a+b for a,b in zip(self.round_changes, changes)]
        self.ledger.extend([dict(kind="win", player=winner, changes=win_changes),
                            dict(kind="caishen", counts=counts, changes=caishen_changes)])
        label = {1:"软和", 2:"硬和", 4:"双番"}[quote["multiplier"]]
        fans = [f"WZ|win|x{quote['multiplier']}|{label}",
                f"WZ|dealer|x{dealer_multiplier(self.dealer_streak)}|庄家{self.dealer_streak}连庄"]
        entry = dict(winner=winner, source=source, tile=tile, discarder=payer if source == "discard" else None,
                     kong_player=payer if source == "rob_kong" else None, points=quote["multiplier"],
                     fan_ids=fans, fan_names=quote["fan_names"], fan_details=quote,
                     score_changes=changes, rule_version=self.rule_version, multi_ron=False,
                     recycle_discard=source == "discard", hu_order=1)
        self.deferred_hu_settlements.append(entry)
        p.is_hu, p.hu_order, p.has_draw_slot = True, 1, False
        self.hu_order_counter = 1
        self.round_settlement = dict(winner=winner, payer=payer, source=source, tile=tile,
                                     changes=list(self.round_changes), score=quote)
        self._finish_round(winner)
        self._event(self._hu_action_for_settlement(entry), winner, tile=tile)
        self.pending_kong = None
        return self._window(P.END)

    def _finish_round(self, winner=None):
        self.next_dealer_shift, self.next_dealer_streak = 0, self.dealer_streak
        if winner == 0 and self.dealer_streak < 3:
            self.next_dealer_streak += 1
        elif winner is not None:
            self.next_dealer_streak = 0
            self.dealer_changes += 1
            self.next_dealer_shift = 1
            if winner == 0:
                rng = random.Random(self.round_random_seed ^ 0x575A2024)
                self.next_dealer_dice = [rng.randint(1,6), rng.randint(1,6)]
                self.next_dealer_shift = (sum(self.next_dealer_dice)-1) % 4
        # 荒庄 preserves dealer and streak; no invented increment or caishen fee.
        self.match_finishing = self.dealer_changes >= self.max_round*4
        self.ended_by = "wall" if winner is None else "win"
        self.machine.transition(P.END)

    def end_draw(self):
        if self.round_settlement is not None:
            raise ValueError("本局已经结算")
        self.caishen_counts = [p.hand_tiles.count(self.caishen) for p in self.player_list]
        self.round_settlement = dict(winner=None, payer=None, source="draw", tile=None,
                                     changes=list(self.round_changes), score=None)
        self._finish_round()
        return self._window(P.END)

    def apply_deferred_score_changes(self):
        if self.deferred_scores_applied or self.round_settlement is None:
            return
        remaining = self.deferred_hu_settlements[0]["score_changes"] if self.deferred_hu_settlements else [0]*4
        for p, amount, total in zip(self.player_list, remaining, self.round_changes):
            p.score += amount
            p.score_history.append(f"+{total:02d}" if total > 0 else f"-{abs(total):02d}" if total < 0 else "0")
            p.round_number_history.append(self.current_round)
        self.deferred_scores_applied = True
