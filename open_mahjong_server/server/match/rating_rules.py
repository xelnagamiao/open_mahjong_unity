"""Authoritative ranked-rule catalogue and four-player Elo calculation.

Guobiao queue identifiers remain compatible with existing clients and records.
Guobiao and Riichi use grades/PT; the other ranked pools calculate Elo.
"""
from dataclasses import dataclass
from itertools import combinations
import math

XUELIU_EXCHANGE_RATING_RULE = 'sichuan_xueliu_exchange'
XUELIU_EXCHANGE_SUB_RULE = 'sichuan/xueliu_exchange'
XUELIU_EXCHANGE_QUEUE = 'sichuan_xueliu_exchange_elo_quanzhuang'
RULES = {"guobiao": "国标麻将", "riichi": "立直麻将", "qingque": "青雀", "sichuan": "川麻血战", "riichi_sanma": "立直三麻",
         XUELIU_EXCHANGE_RATING_RULE: "川麻血流换三张"}
GRADE_RULES = {"guobiao", "riichi", "riichi_sanma"}
ELO_RULES = set(RULES) - GRADE_RULES
DEFAULT_R = 1500.0
ELO_EXPECTATION_SCALE = 2400.0
ELO_TABLE_AVERAGE_FLOOR = 1500.0
ELO_ALGORITHM_VERSION = "elo_2400_table_protection_v1"
# Shared by settlement and startup rebuild; always acquire before row/game locks.
RATING_WRITE_LOCK = "open_mahjong:rating_history"


@dataclass(frozen=True)
class RankedQueue:
    rule: str
    tier: str
    mode: str
    rounds: int
    player_count: int = 4
    sub_rule: str = ''

    @property
    def rating_rule(self):
        if self.rule == 'sichuan' and self.sub_rule == XUELIU_EXCHANGE_SUB_RULE:
            return XUELIU_EXCHANGE_RATING_RULE
        return 'riichi_sanma' if self.rule == 'riichi' and self.player_count == 3 else self.rule

    @property
    def graded(self):
        return self.rating_rule in GRADE_RULES


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
QUEUES[XUELIU_EXCHANGE_QUEUE] = RankedQueue('sichuan', 'elo', 'quanzhuang', 4,
                                          sub_rule=XUELIU_EXCHANGE_SUB_RULE)
for tier in ('beginner', 'intermediate', 'advanced'):
    for mode, rounds in (('dongfeng', 1), ('banzhuang', 2)):
        QUEUES[f'riichi_sanma_{tier}_{mode}'] = RankedQueue('riichi', tier, mode, rounds, 3)


def ranked_record_scope(rule):
    """Return the stored game rule and the sub-rule required by a rating pool."""
    if rule not in RULES:
        raise ValueError('不支持的匹配规则')
    if rule == XUELIU_EXCHANGE_RATING_RULE:
        return 'sichuan', XUELIU_EXCHANGE_SUB_RULE
    if rule == 'sichuan':
        return 'sichuan', 'sichuan/standard'
    if rule in ('riichi', 'riichi_sanma'):
        return 'riichi', 'riichi/sanma' if rule == 'riichi_sanma' else 'riichi/standard'
    return rule, None


def default_rating(rule):
    if rule not in RULES:
        raise ValueError("不支持的匹配规则")
    rating = {"rule": rule, "system": "grade" if rule in GRADE_RULES else "elo",
            "rank_name": "10级" if rule in GRADE_RULES else "", "rank_score": 0.0,
            "games": 0}
    if rule in ELO_RULES:
        rating["elo"] = DEFAULT_R
    return rating


def elo_deltas(ratings, places, k=32.0):
    """D=2400, K=32, with the agreed 1500 table-average protection.

    Ordinary Elo is rounded to a zero-sum base. When the table mean is below
    1500, raise each virtual opponent by the same shortfall and add the rounded
    expectation difference as a nonnegative bonus. There is no personal floor.
    """
    ratings = [float(r) for r in ratings]
    if (len(ratings) != 4 or len(places) != 4
            or any(not math.isfinite(r) for r in ratings)
            or any(p not in (1, 2, 3, 4) for p in places)
            or not math.isfinite(k) or k <= 0):
        raise ValueError("Elo 结算需要四名有效玩家")
    # Divide first to keep the mean finite even for extreme finite inputs.
    shift = max(0.0, ELO_TABLE_AVERAGE_FLOOR - sum(r / 4 for r in ratings))
    def expected(own, opponent):
        exponent = max(-16, min(16, (opponent - own) / ELO_EXPECTATION_SCALE))
        return 1 / (1 + 10 ** exponent)

    deltas = [0.0] * 4
    bonuses = [0.0] * 4
    for i, j in combinations(range(4), 2):
        e = expected(ratings[i], ratings[j])
        actual = .5 if places[i] == places[j] else float(places[i] < places[j])
        change = k / 3 * (actual - e)
        deltas[i] += change
        deltas[j] -= change
        if shift:
            bonuses[i] += k / 3 * (e - expected(ratings[i], ratings[j] + shift))
            bonuses[j] += k / 3 * ((1 - e) - expected(ratings[j], ratings[i] + shift))
    rounded = [round(d, 2) for d in deltas]
    rounded[-1] = round(-sum(rounded[:-1]), 2)
    return [(round(d * 100) + max(0, round(b * 100))) / 100
            for d, b in zip(rounded, bonuses)]
