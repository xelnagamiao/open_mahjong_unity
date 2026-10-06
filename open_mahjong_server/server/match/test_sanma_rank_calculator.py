from itertools import permutations
import pytest
from .sanma_rank_calculator import sanma_pt_details, SANMA_PT_ALGORITHM
from .rating_rules import QUEUES, default_rating
from .rank_calculator import RANK_TABLE, apply_pt, queue_type_to_room_config


@pytest.mark.parametrize('tier,award', [('beginner',30),('intermediate',50),('advanced',70)])
@pytest.mark.parametrize('mode,multiplier', [('dongfeng',1),('banzhuang',1.5)])
@pytest.mark.parametrize('places', list(permutations((1,2,3))) + [(1,1,3),(1,2,2),(1,1,1)])
def test_placement_points_and_ties(tier, award, mode, multiplier, places):
    expected = {1:award*multiplier,2:0,3:-award*multiplier}
    if places == (1,1,3): expected[1] /= 2
    if places == (1,2,2): expected[2] = -award*multiplier/2
    if places == (1,1,1): expected[1] = 0
    results = [sanma_pt_details(tier,mode,'四段',p,places) for p in places]
    assert [r['rating_pt'] for r in results] == [expected[p] for p in places]
    assert sum(r['rating_pt'] for r in results) == 0
    assert all(r['rating_algorithm'] == SANMA_PT_ALGORITHM for r in results)


@pytest.mark.parametrize('rank,start,threshold,demotes', RANK_TABLE)
def test_every_grade_uses_shared_progression_and_fixed_top(rank,start,threshold,demotes):
    results = [sanma_pt_details('advanced','banzhuang',rank,p,[1,2,3])['rating_pt'] for p in (1,2,3)]
    assert results == ([0,0,0] if rank=='十段' else [105,0,-105])
    assert apply_pt(rank,start,results[1]) == (rank,float(start))
    if rank=='十段': assert apply_pt(rank,9999,105) == ('十段',100)
    if not demotes and rank!='十段': assert apply_pt(rank,0,results[2]) == (rank,0)


@pytest.mark.parametrize('tier,mode,rank,place,places', [
    ('bad','dongfeng','四段',1,[1,2,3]), ('beginner','quanzhuang','四段',1,[1,2,3]),
    ('beginner','dongfeng','bad',1,[1,2,3]), ('beginner','dongfeng','四段',4,[1,2,3]),
    ('beginner','dongfeng','四段',1,[1,1,2]), ('beginner','dongfeng','四段',1,[1,2,4]),
    ('beginner','dongfeng','四段',1,[1,2]), ('beginner','dongfeng','四段',1,[True,2,3]),
])
def test_invalid_configuration_and_rankings_are_rejected(tier,mode,rank,place,places):
    with pytest.raises(ValueError): sanma_pt_details(tier,mode,rank,place,places)


def test_six_queues_use_three_players_tenhou_and_separate_pool():
    queues = {k:v for k,v in QUEUES.items() if v.player_count==3}
    assert len(queues)==6
    for queue,spec in queues.items():
        room=queue_type_to_room_config(queue)
        assert spec.rule=='riichi' and spec.rating_rule=='riichi_sanma' and spec.graded
        assert room['sub_rule']=='riichi/sanma' and room['starting_score']==35000
        assert room['detailed_config']['tie_break']=='initial'
        assert room['detailed_config']['sanma_tsumo']=='loss'
        assert room['game_round']==spec.rounds and room['count_tips']==(spec.tier=='beginner')
        assert room['step_timer']==(8 if spec.tier=='intermediate' else 5)
    assert default_rating('riichi_sanma')['rank_name']=='10级'
    assert 'elo' not in default_rating('riichi_sanma')
