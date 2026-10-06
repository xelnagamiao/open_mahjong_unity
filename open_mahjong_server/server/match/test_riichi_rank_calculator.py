"""Examples and compatibility boundaries of the independent Riichi PT contract."""
from decimal import Decimal
import pytest
from .riichi_rank_calculator import riichi_pt_details, RANK_BASE_COST, RIICHI_PT_ALGORITHM
from .rank_calculator import RANK_AVG_LOSS_PT, TIER_BASE_SCORE, calculate_pt, apply_pt


def test_agreed_seventh_dan_example_includes_game_points():
    results = [riichi_pt_details('advanced', 'banzhuang', '七段', points) for points in (60, 10, -20, -50)]
    assert [r['rating_pt'] for r in results] == [54.75, 4.75, -25.25, -55.25]
    assert all(r['rating_rank_cost'] == 5.25 and r['rating_algorithm'] == RIICHI_PT_ALGORITHM for r in results)
    assert riichi_pt_details('advanced', 'banzhuang', '七段', 70)['rating_pt'] == 64.75


@pytest.mark.parametrize('rank', [r for r in RANK_BASE_COST if r != '十段'])
@pytest.mark.parametrize('tier', list(TIER_BASE_SCORE)[:3])
def test_uniform_cost_preserves_neutral_mean_and_top_reward_gaps(rank, tier):
    details = [riichi_pt_details(tier, 'banzhuang', rank, points) for points in (60,10,-20,-50)]
    old_mean = sum(calculate_pt(tier, 'banzhuang', place, rank) for place in range(1,5)) / 4
    # Each PT is rounded to cents; tolerate half a cent plus float conversion noise.
    assert sum(d['rating_pt'] for d in details) / 4 == pytest.approx(old_mean, abs=.005001)
    expected_cost = (Decimal(str(RANK_AVG_LOSS_PT[rank])) - Decimal(str(TIER_BASE_SCORE[tier]))) * Decimal('.175')
    assert details[0]['rating_rank_cost'] == float(expected_cost)
    assert len({d['rating_rank_cost'] for d in details}) == 1


def test_east_scales_the_entire_formula_and_cost_is_not_rounded_early():
    assert riichi_pt_details('advanced', 'dongfeng', '七段', 60)['rating_pt'] == 38.32
    assert riichi_pt_details('advanced', 'banzhuang', '九段', 10)['rating_pt'] == -3.12
    assert riichi_pt_details('advanced', 'banzhuang', '九段', 60)['rating_pt'] == 46.88


@pytest.mark.parametrize('rank', ('10级', '三段', '九段'))
@pytest.mark.parametrize('tier,k', [('beginner', .3), ('intermediate', .6), ('advanced', 1)])
def test_placement_award_gaps_remain_equal_across_grades(rank, tier, k):
    pt = [riichi_pt_details(tier, 'banzhuang', rank, points)['rating_pt'] for points in (50,10,-10,-30)]
    assert [round(pt[i] - pt[i+1], 2) for i in range(3)] == pytest.approx([40*k,20*k,20*k])


def test_fixed_tenth_dan_and_existing_promotion_and_demotion():
    assert riichi_pt_details('advanced','banzhuang','十段',-100)['rating_pt'] == 0
    assert apply_pt('九段', 6999, 46.88) == ('十段',100)
    assert apply_pt('七段', 1, -55.25) == ('六段',1200)
    assert apply_pt('10级', 0, -50) == ('10级',0)


@pytest.mark.parametrize('points', [None,True,float('nan'),float('inf'),'invalid'])
def test_missing_or_invalid_points_are_not_silently_replaced_by_place_pt(points):
    with pytest.raises(ValueError): riichi_pt_details('advanced','banzhuang','七段',points)
