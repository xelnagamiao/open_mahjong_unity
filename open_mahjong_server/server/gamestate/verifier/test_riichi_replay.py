import pytest

from .record_sim import RecordSim, stringify_tick
from .record_sim.decoder import accumulate_score_changes_from_tick


def record(ticks):
    return {'game_title': {'rule': 'riichi', 'sub_rule': 'riichi/standard'}, 'game_round': {
        'round_index_1': {'start_player_index': 0, 'seats': [0,1,2,3],
            'p0_tiles': [11,11,11,21,22,23,31,32,33,41,41,45,45],
            'p1_tiles': [11,12,13,21,22,23,31,32,33,41,41,41,45],
            'p2_tiles': [], 'p3_tiles': [], 'riichi': {'honba': 0, 'riichi_sticks': 2},
            'action_ticks': ticks}}}


@pytest.mark.parametrize('seats',[[0,1,2,3],[3,0,1,2],[2,3,0,1],[1,2,3,0]])
def test_riichi_and_win_changes_follow_original_players_across_dealer_rotation(seats):
    change=accumulate_score_changes_from_tick(None,['riichi',0,1],seats)
    change=accumulate_score_changes_from_tick(change,
        ['hu_riichi',1,'hu_first',2,30,[],[-2000,5000,0,0],[],[],0,0,3,2000],seats)
    assert change==[[-3000,5000,0,0][seat] for seat in seats]


def test_riichi_payment_advances_neither_actor_nor_draw_and_win_collects_deposits():
    ticks=[['d',47],['c',47,'T','H'],['riichi',0,1],['d',45],
        ['hu_riichi',1,'hu_self',2,30,['门前清自摸和'],[-1000,5000,-500,-500],[],[],0,0,3,2000]]
    sim=RecordSim(record(ticks));sim.load_round('round_index_1')
    for t in ticks[:3]:sim.apply_tick(stringify_tick(t))
    assert sim.current_player_index==1
    assert sim.players[0].score==-1000 and sim.record_riichi_sticks==3
    sim.apply_tick(stringify_tick(ticks[3]))
    assert sim.players[1].tile_list[-1]==45
    sim.apply_tick(stringify_tick(ticks[4]))
    assert sim.record_riichi_sticks==0
    assert [sim.players[i].score for i in range(4)]==[-2000,5000,-500,-500]


@pytest.mark.parametrize('cuohe',[False,True])
def test_ron_replay_appends_winning_tile_only_for_real_win_and_refunds_sticks(cuohe):
    ticks=[['d',47],['c',47,'T','H'],['riichi',0,1],['d',45],['c',45,'T'],
        ['hu_riichi',0,'hu_third',0 if cuohe else 1,30,['错和'] if cuohe else ['役牌'],
         [-8000,3000,3000,3000] if cuohe else [4000,-1000,0,0],[],[],0,0,0 if cuohe else 3,9000 if cuohe else 1000]]
    sim=RecordSim(record(ticks));frames=sim.apply_all('round_index_1')
    assert len(sim.players[0].tile_list)==(13 if cuohe else 14)
    assert sim.players[0].is_hu is not cuohe
    assert sim.record_riichi_sticks==(2 if cuohe else 0)
    assert RecordSim(record(ticks)).goto_action('round_index_1',len(ticks))==frames[-1]


@pytest.mark.parametrize('kind',['angang','jiagang'])
def test_rob_kan_keeps_three_tiles_or_original_pon_and_ron_adds_one(kind):
    source=record([])
    source['game_round']['round_index_1']['p0_tiles']=[11]*3+[12]*10
    sim=RecordSim(source);sim.load_round('round_index_1')
    if kind=='jiagang':
        sim.players[0].tile_list=[12]*10
        sim.players[0].combination_tiles=['k11']
    sim.apply_tick(['d','11'])
    before=list(sim.players[0].tile_list)
    sim.apply_tick(stringify_tick(['rk',0,11,1,kind,[11]*4]))
    assert len(sim.players[0].tile_list)==len(before)-1
    assert sim.players[0].combination_tiles==(['k11'] if kind=='jiagang' else [])
    sim.apply_tick(stringify_tick(['hu_riichi',1,'hu_first',13,25,['国士无双'],[-32000,32000,0,0],[],[],0,0,0,32000]))
    assert sim.players[1].tile_list[-1]==11 and len(sim.players[1].tile_list)==14


def test_final_deposit_scores_are_applied_only_at_last_hand_end_with_rotated_seats():
    source=record([['end']])
    source['game_round']['round_index_2']={**source['game_round']['round_index_1'],'seats':[3,0,1,2]}
    source['game_title'].update(riichi_final_scores=[30800,30600,30600,8000],riichi_final_sticks=0)
    sim=RecordSim(source)
    assert sim.apply_all('round_index_1')[-1]['players']['0']['score']==0
    last=sim.apply_all('round_index_2')[-1]
    assert [last['players'][str(i)]['score'] for i in range(4)]==[30600,30600,8000,30800]
    assert sim.record_riichi_sticks==0
