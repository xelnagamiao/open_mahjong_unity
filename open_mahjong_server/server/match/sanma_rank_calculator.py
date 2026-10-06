"""Three-player Riichi placement PT, independent from four-player M-league PT."""
from .rank_calculator import RANK_NAME_TO_INDEX, MAX_RANK_NAME

SANMA_PT_ALGORITHM = 'riichi_sanma_place_pt_v1'
SANMA_RATING_RULE = 'riichi_sanma'
# Tenhou's general/upper/special table first-place awards. Last place pays the
# same amount here; this platform deliberately uses a simpler symmetric rule.
EAST_PT = {'beginner': 30, 'intermediate': 50, 'advanced': 70}
MODE_MULTIPLIER = {'dongfeng': 1, 'banzhuang': 1.5}


def sanma_pt_details(tier, mode, rank_name, place, places):
    if tier not in EAST_PT or mode not in MODE_MULTIPLIER or rank_name not in RANK_NAME_TO_INDEX:
        raise ValueError('无效的三人立直段位计分配置')
    if (len(places) != 3 or any(type(p) is not int or p not in (1, 2, 3) for p in places)
            or place not in places
            or any(p != 1 + sum(other < p for other in places) for p in places)):
        raise ValueError('三人立直结算需要三名有效名次')
    start = 1 + sum(p < place for p in places)
    tied = sum(p == place for p in places)
    award = EAST_PT[tier] * MODE_MULTIPLIER[mode]
    # Shared ranks split the occupied positions; all-three tie is exactly zero.
    pt = sum((1, 0, -1)[p - 1] * award for p in range(start, start + tied)) / tied
    if rank_name == MAX_RANK_NAME:
        pt = 0.0
    return dict(rating_algorithm=SANMA_PT_ALGORITHM, rating_pt=float(pt))
