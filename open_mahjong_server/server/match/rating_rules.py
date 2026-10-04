"""Authoritative ranked-rule catalogue and four-player Elo calculation.

Guobiao queue identifiers remain compatible with existing clients and records.
Ratings are independent per rule; every rated game updates R, including grade games.
"""
from dataclasses import dataclass
from itertools import combinations
import math

RULES = {"guobiao": "国标麻将", "riichi": "立直麻将", "qingque": "青雀", "sichuan": "川麻血战"}
GRADE_RULES = {"guobiao", "riichi"}
DEFAULT_R = 1500.0
ELO_EXPECTATION_SCALE = 2000.0


@dataclass(frozen=True)
class RankedQueue:
    rule: str
    tier: str
    mode: str
    rounds: int

    @property
    def graded(self):
        return self.rule in GRADE_RULES


QUEUES = {}
for rule in ("guobiao", "riichi"):
    for tier in (("beginner", "intermediate", "advanced", "mcrpl") if rule == "guobiao" else ("beginner", "intermediate", "advanced")):
        for mode, rounds in (("dongfeng", 1), ("banzhuang", 2), ("quanzhuang", 4)):
            if rule == "riichi" and rounds == 4:
                continue  # Riichi supports East and East-South games.
            key = f"{tier}_{mode}" if rule == "guobiao" else f"{rule}_{tier}_{mode}"
            QUEUES[key] = RankedQueue(rule, tier, mode, rounds)
QUEUES["qingque_elo_quanzhuang"] = RankedQueue("qingque", "elo", "quanzhuang", 4)
# Keep the existing Sichuan queue ID for clients and historical records.
QUEUES["sichuan_elo_xuezhan"] = RankedQueue("sichuan", "elo", "quanzhuang", 4)


def default_rating(rule):
    if rule not in RULES:
        raise ValueError("不支持的匹配规则")
    return {"rule": rule, "system": "grade" if rule in GRADE_RULES else "elo",
            "rank_name": "10级" if rule in GRADE_RULES else "", "rank_score": 0.0,
            "elo": DEFAULT_R, "games": 0}


def elo_deltas(ratings, places, k=32.0):
    """Average three rank comparisons on a 2000-point expectation scale.

    Ties score 1/2; deltas sum to zero. Game points do not affect R.
    """
    if len(ratings) != 4 or len(places) != 4 or any(not math.isfinite(float(r)) for r in ratings):
        raise ValueError("Elo 结算需要四名有效玩家")
    deltas = [0.0] * 4
    for i, j in combinations(range(4), 2):
        expected = 1 / (1 + 10 ** max(-16, min(16, (ratings[j] - ratings[i]) / ELO_EXPECTATION_SCALE)))
        actual = .5 if places[i] == places[j] else float(places[i] < places[j])
        change = k / 3 * (actual - expected)
        deltas[i] += change
        deltas[j] -= change
    rounded = [round(d, 2) for d in deltas]
    rounded[-1] = round(-sum(rounded[:-1]), 2)
    return rounded
