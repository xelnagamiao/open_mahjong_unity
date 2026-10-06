"""M-league game points drive Riichi PT; grade progression stays shared."""
from decimal import Decimal, InvalidOperation, ROUND_HALF_EVEN

RIICHI_PT_ALGORITHM = 'riichi_mleague_pt_v1'
LEGACY_PT_ALGORITHM = 'grade_place_pt_v1'
TIER_MULTIPLIER = {'beginner': Decimal('0.3'), 'intermediate': Decimal('0.6'), 'advanced': Decimal('1')}
GAME_MULTIPLIER = {'dongfeng': Decimal('0.7'), 'banzhuang': Decimal('1')}
# Independent Riichi configuration. Offsets preserve the old neutral-player mean
# without increasing only third/fourth-place penalties at higher grades.
TIER_COST_OFFSET = {'beginner': Decimal('5.25'), 'intermediate': Decimal('11.375'), 'advanced': Decimal('18.375')}
RANK_BASE_COST = dict(zip(
    ('10级', '9级', '8级', '7级', '6级', '5级', '4级', '3级', '2级', '1级',
     '初段', '二段', '三段', '四段', '五段', '六段', '七段', '八段', '九段', '十段'),
    map(Decimal, ('0', '0', '0', '0', '0', '0', '0', '0', '2.625', '6.125',
                  '7.875', '9.625', '11.375', '16.625', '18.375', '21', '23.625', '28.875', '31.5', '0'))))


def riichi_pt_details(tier, mode, rank_name, match_points):
    if tier not in TIER_MULTIPLIER or mode not in GAME_MULTIPLIER or rank_name not in RANK_BASE_COST:
        raise ValueError('无效的立直段位计分配置')
    try:
        if isinstance(match_points, bool) or match_points is None:
            raise ValueError('立直排位结算缺少比赛分')
        points = Decimal(str(match_points))
        if not points.is_finite():
            raise ValueError('立直比赛分必须为有限数值')
    except (InvalidOperation, TypeError) as exc:
        raise ValueError('无效的立直比赛分') from exc
    cost = RANK_BASE_COST[rank_name] - TIER_COST_OFFSET[tier]
    fixed = rank_name == '十段'
    if fixed:
        cost = Decimal('0')
    raw_pt = Decimal('0') if fixed else GAME_MULTIPLIER[mode] * (TIER_MULTIPLIER[tier] * points - cost)
    return dict(rating_algorithm=RIICHI_PT_ALGORITHM, rating_match_points=float(points),
                rating_game_multiplier=float(GAME_MULTIPLIER[mode]),
                rating_tier_multiplier=float(TIER_MULTIPLIER[tier]), rating_rank_cost=float(cost),
                rating_pt=float(raw_pt.quantize(Decimal('0.01'), rounding=ROUND_HALF_EVEN)))
