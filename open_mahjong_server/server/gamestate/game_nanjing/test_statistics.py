from ...database.shared_record_metrics import analyze_shared_record_for_player
from .test_state import state,turn,act,PLAIN
from .state_machine import Phase as P
from .bot import choose_action


def record(s):
    s.start_game_recording();s.start_round_recording()
    return s


def verify(s):
    s.finalize_round_recording()
    s.finalize_game_recording()
    rows=[analyze_shared_record_for_player(s.game_record,i) for i in range(4)]
    for p in s.player_list:
        assert rows[p.original_player_index]['total_round_score']==p.score
    assert sum(x['total_round_score'] for x in rows)==0
    return rows


def test_record_multiron_actual_payments_and_discarder_count():
    s=turn()
    for i in (1,2):s.player_list[i].hand_tiles=PLAIN[:-1]
    record(s)
    act(s,action_type='cut',TileId=22,cutClass=True)
    act(s,{1:'hu',2:'hu'})
    rows=verify(s)
    assert [x['win_count'] for x in rows]==[0,1,1,0]
    assert [x['deal_in_count'] for x in rows]==[2,0,0,0]
    assert all(x['self_draw_count']==0 for x in rows)


def test_record_three_concealed_kongs_split_ron_remainder_to_actual_discarder():
    s=turn([11]*4+[22]*4+[33]*4+[44,44])
    s.tiles_list=[17,18,41,41,41]
    record(s)
    for tile in (11,22,33):
        act(s,action_type='angang',target_tile=tile);act(s)
    assert s.contracts[0]=={1,2,3}
    s.player_list[2].hand_tiles=PLAIN[:-1]+[44]
    s.player_list[2].has_draw_slot=True
    act(s,action_type='cut',TileId=44,cutClass=False)
    s.open_action_window(s.begin_turn(2))
    act(s,action_type='cut',TileId=44,cutClass=True)
    s.player_list[0].passed_win_value=None
    s.open_action_window(s._window(P.RESPONSE,2,44,{0:['hu','pass'],1:[],2:[],3:[]}))
    act(s,{0:'hu'})
    win=s.deferred_hu_settlements[0]
    total=win['points'];base,remainder=divmod(total,3)
    assert remainder>0
    assert win['score_changes']==[total,-base,-base-remainder,-base]
    rows=verify(s)
    assert rows[0]['win_count']==1 and rows[2]['deal_in_count']==1
    assert rows[1]['deal_in_count']==rows[3]['deal_in_count']==0
    assert rows[2]['total_fangchong_score']==-win['score_changes'][2]
    assert s.player_list[2].record_counter.fangchong_score==-win['score_changes'][2]


def test_record_flowers_follow_four_discards_and_draw_keep_payments():
    s=record(turn(PLAIN[:-1]+[45]))
    s.player_list[0].huapai_list=[45]*3
    act(s,action_type='buhua')
    s.discard_log=[(1,18),(2,18),(3,18)]
    s.player_list[0].hand_tiles[-1]=18
    s.player_list[0].discard_origin_tiles=[18]*3
    s.open_action_window(s.begin_turn(0))
    act(s,action_type='cut',TileId=18,cutClass=True)
    act(s)
    s.end_draw()
    rows=verify(s)
    assert {x['kind'] for x in s.ledger}=={'flower_kong','follow','four_discards'}
    assert all(x['win_count']==x['deal_in_count']==0 for x in rows)
    assert any(x['total_round_score'] for x in rows)


def test_record_full_match_statistics_after_all_four_rotations():
    s=state(57);s.start_game_recording()
    expected_turns=[0]*4
    for _ in range(4):
        s.initialize_round();s.start_round_recording();s.open_action_window(s.opening_window())
        for step in range(600):
            if s.machine.phase==P.END:break
            s.apply_action_results(s.live_pending_window,{i:choose_action(s,i,a) for i,a in s.action_dict.items() if a})
        assert s.machine.phase==P.END
        for win in s.deferred_hu_settlements:
            winner=s.player_list[win['winner']]
            expected_turns[winner.original_player_index]+=len(winner.discard_origin_tiles)+1
        s.finalize_round_recording()
        if not s.match_finishing:s.machine.transition(P.READY);s.advance_round_after_ready()
    rows=verify(s)
    assert all(x['total_rounds']==4 for x in rows)
    for p in s.player_list:
        stats=rows[p.original_player_index]
        assert stats['win_count']==p.record_counter.dianhe_times+p.record_counter.zimo_times
        assert stats['self_draw_count']==p.record_counter.zimo_times
        assert stats['deal_in_count']==p.record_counter.fangchong_times
        assert stats['total_win_turn']==p.record_counter.win_turn==expected_turns[p.original_player_index]
        assert stats['total_fangchong_score']==p.record_counter.fangchong_score


def test_kong_record_keeps_dealer_ownership_after_other_players_replace_flowers():
    s=state(1);s.start_game_recording();s.initialize_round();s.start_round_recording()
    s.open_action_window(s.opening_window())
    while s.machine.phase==P.REPLACEMENT:
        s.apply_action_results(s.live_pending_window,{i:choose_action(s,i,a) for i,a in s.action_dict.items() if a})
    ticks=s.game_record['game_round']['round_index_1']['action_ticks']
    assert [t for t in ticks if t[0]=='bd'][-1][2]==2
    assert s.current_player_index==0 and 'angang' in s.action_dict[0]
    act(s,action_type='angang',target_tile=38)
    act(s)
    at=next(i for i,t in enumerate(ticks) if t[0]=='ag')
    assert ticks[at-1]==['reset',0]
