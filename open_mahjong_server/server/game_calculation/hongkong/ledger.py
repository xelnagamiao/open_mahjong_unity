"""HKMA sixteen-tile 拉/踢半 ledger, in stable seat identities.

This is deliberately separate from live scores: pending debts are visible but
are not paid twice at a win, a reconnect, or a replay. Immediate kong/flower
awards bypass this ledger. End-of-match collection settles remaining debts.
"""

from dataclasses import dataclass


@dataclass(frozen=True)
class Debt:
    creditor: int
    debtor: int
    points: int
    mouths: int = 1


class PullLedger:
    def __init__(self):
        self.debts: dict[tuple[int, int], Debt] = {}

    @staticmethod
    def _pay(delta, creditor, debtor, amount):
        delta[creditor] += amount
        delta[debtor] -= amount

    def apply_win(self, payments: dict[tuple[int, int], int], *, winners, self_draw=False):
        """Resolve old obligations simultaneously, then add this hand's debts.

        Keys are (winner, payer). Multi-ron uses the same pre-hand ledger; a
        pair directly involved in the win takes priority over an outside win.
        This explicit simultaneous extension avoids response-order dependence.
        """
        winners = frozenset(winners)
        if not winners or not winners <= set(range(4)) or (self_draw and len(winners) != 1):
            raise ValueError("Invalid winners")
        for (creditor, debtor), value in payments.items():
            if creditor not in winners or debtor not in range(4) or creditor == debtor or type(value) is not int or value < 0:
                raise ValueError("Invalid payment")
        delta = [0] * 4
        next_debts = {}
        consumed = set()
        for key, old in self.debts.items():
            a, b = key
            if key in payments:
                # ceil(1.5 * prior), then add this win. Never use float money.
                next_debts[key] = Debt(a, b, (old.points * 3 + 1) // 2 + payments[key], old.mouths + 1)
                consumed.add(key)
            elif (b, a) in payments or (self_draw and b in winners):
                self._pay(delta, a, b, old.points // 2)
            elif b in winners or not (winners <= {a}):
                self._pay(delta, a, b, old.points)
            else:
                next_debts[key] = old
        for key, amount in payments.items():
            if key not in consumed and amount:
                next_debts[key] = Debt(*key, amount)
        self.debts = next_debts
        return delta

    def cut(self, debtor, creditor):
        key = (creditor, debtor)
        debt = self.debts.get(key)
        if debt is None or debt.mouths < 3:
            raise ValueError("Only a debtor pulled at least three mouths may cut")
        delta = [0] * 4
        self._pay(delta, creditor, debtor, debt.points)
        del self.debts[key]
        return delta

    def collect(self):
        delta = [0] * 4
        for debt in self.debts.values():
            self._pay(delta, debt.creditor, debt.debtor, debt.points)
        self.debts.clear()
        return delta

    def snapshot(self):
        return [dict(creditor=d.creditor, debtor=d.debtor, points=d.points, mouths=d.mouths)
                for _, d in sorted(self.debts.items())]
