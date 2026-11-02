"""Qingque net-point rating, calibrated to the existing 1500 Elo scale.

The MMCR reference estimates performance from Y - N * (mu - table_mu).
We use its equal-variance Gaussian update with a fixed evidence prior, rather
than introducing per-account volatility/rank points. R = 1500 + 32 * mu;
sigma^2 / tau^2 = 1584. At N=16, equal ratings give delta_R = 0.02 * Y.
There is deliberately no score/delta clipping. All four updates use one frozen
input, so player iteration order cannot affect the result.
"""
import math
from decimal import Decimal, ROUND_HALF_UP, ROUND_FLOOR

ALGORITHM = 'qingque_mean_points_v2'
POINTS_TO_RATING = 32.0
PRIOR_HANDS = 1584.0


def validate_scores(players, completed_hands):
    if type(completed_hands) is not int or completed_hands <= 0:
        raise ValueError('青雀素点评级需要正整数的实际完成局数')
    if (len(players) != 4 or len({p['user_id'] for p in players}) != 4
            or any(type(p.get('net_points')) is not int for p in players)
            or sum(p['net_points'] for p in players) != 0):
        raise ValueError('青雀素点评级需要四名玩家的零和净素点')


def _zero_sum_cents(values, user_ids):
    # Largest remainder, with stable user identity resolving equal remainders.
    exact = [Decimal(str(v)) * 100 for v in values]
    cents = [int(v.to_integral_value(rounding=ROUND_FLOOR)) for v in exact]
    remainder = -sum(cents)
    if not 0 <= remainder <= len(values):
        raise ValueError('青雀评分舍入误差超出范围')
    order = sorted(range(4), key=lambda i: (-(exact[i] - cents[i]), user_ids[i]))
    for i in order[:remainder]:
        cents[i] += 1
    return [v / 100 for v in cents]


def score_elo_details(ratings, players, completed_hands):
    from .rating_rules import ELO_EXPECTATION_SCALE, ELO_TABLE_AVERAGE_FLOOR
    validate_scores(players, completed_hands)
    if len(ratings) != 4 or any(not math.isfinite(float(r)) for r in ratings):
        raise ValueError('青雀赛前评分必须为四个有限数值')
    ratings = list(map(float, ratings))
    mean = math.fsum(r / 4 for r in ratings)
    n = completed_hands
    gain = POINTS_TO_RATING / (n + PRIOR_HANDS)
    k = 3200.0 * (n / (n + PRIOR_HANDS))
    expected = [(r - mean) / POINTS_TO_RATING for r in ratings]
    base = [gain * (p['net_points'] - n * e) for p, e in zip(players, expected)]
    if any(not math.isfinite(x) for x in base):
        raise ValueError('青雀评分输入超出可计算范围')
    # Remove only floating-point summation drift, before deterministic rounding.
    drift = math.fsum(base) / 4
    base = _zero_sum_cents([d - drift for d in base], [p['user_id'] for p in players])
    shift = max(0.0, ELO_TABLE_AVERAGE_FLOOR - mean)

    def probability(own, opponent):
        return 1 / (1 + 10 ** max(-16, min(16, (opponent - own) / ELO_EXPECTATION_SCALE)))

    result = []
    for i, (player, ordinary) in enumerate(zip(players, base)):
        bonus = k / 3 * math.fsum(
            probability(ratings[i], r) - probability(ratings[i], r + shift)
            for j, r in enumerate(ratings) if i != j)
        bonus = max(Decimal(0), Decimal(str(bonus))).quantize(Decimal('.01'), rounding=ROUND_HALF_UP)
        delta = float(Decimal(str(ordinary)) + bonus)
        result.append(dict(rating_algorithm=ALGORITHM, rating_k=k,
            rating_completed_hands=n, rating_net_points=player['net_points'],
            rating_average_points=player['net_points'] / n,
            rating_expected_average=expected[i], rating_base_delta=ordinary,
            rating_protection_bonus=float(bonus), elo_delta=delta))
    return result
