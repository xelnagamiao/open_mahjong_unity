import json
from pathlib import Path

import pytest
from test_subrule_statistics import db
from test_subrule_realtime import prepare
from test_regional_record_metrics import identify_players
from server.database.get_rule_stats import get_custom_rule_history
from server.database.taiwan.store_taiwan import store_taiwan_game_record
from server.database.shared_record_metrics import analyze_shared_record_for_player
from server.database.statistics_catalog import selection
from server.gamestate.game_beijing.test_flow import game,turn,apply,response,WAIT


def record(rotation,mode):
    s=game(dingzhuang=True,multi_ron=True)
    before=identify_players(s,rotation)
    turn(s,0,WAIT+[42])
    s.start_game_recording();s.start_round_recording()
    for p in s.player_list:p.discard_origin_tiles=[47]
    s.check_dingzhuang()
    for p in s.player_list:p.discard_origin_tiles=[]
    if mode=='self':apply(s,0,'hu_self')
    elif mode=='multi':
        for i in (1,2):s.player_list[i].hand_tiles=WAIT.copy()
        response(s,0,42)
        choices={i:dict(action_type='hu' if i in (1,2) else 'pass') for i,a in s.action_dict.items() if a}
        s.apply_action_results(s.live_pending_window,choices)
    elif mode=='zero':
        p=s.player_list[0];p.combination_tiles=['s22'];p.combination_mask=[[1,21,0,22,0,23]]
        turn(s,0,[11,12,45,31,32,33,41,41,41,42,42]);apply(s,0,'hu_self')
    else:s.end_draw()
    s.finalize_round_recording();s.finalize_match_result()
    return s,before


def test_catalog_enables_basic_points_and_defers_fan_statistics():
    row=selection('beijing')
    assert row['basic_stats'] and not row['fan_stats'] and row['fan_key'] is None
    assert row['win_stat_unit']==row['deal_in_stat_unit']=='points'
    root=Path(__file__).resolve().parents[3]
    shared=json.loads((root/'open_mahjong_web/shared/statisticsRules.json').read_text(encoding='utf-8'))
    assert row==next(r for r in shared['rules'] if r['key']=='beijing')


@pytest.mark.parametrize('rotation',range(4))
@pytest.mark.parametrize('mode',['self','multi','zero','draw'])
def test_reconstructed_metrics_include_dingzhuang_and_match_live_counters(rotation,mode):
    s,before=record(rotation,mode)
    for p in s.player_list:
        metric=analyze_shared_record_for_player(s.game_record,p.original_player_index)
        assert metric['total_round_score']==p.score-before[p.original_player_index]
        assert metric['self_draw_count']==p.record_counter.zimo_times
        assert metric['win_count']==p.record_counter.zimo_times+p.record_counter.dianhe_times
        assert metric['deal_in_count']==p.record_counter.fangchong_times
        assert metric['total_fan_score']==p.record_counter.win_score
        assert metric['total_fangchong_score']==p.record_counter.fangchong_score


@pytest.mark.parametrize('rotation',range(4))
@pytest.mark.parametrize('mode',['self','multi','zero','draw'])
def test_actual_postgres_storage_isolated_from_other_rules_and_without_fan_stats(db,rotation,mode):
    prepare(db)
    s,before=record(rotation,mode)
    gid=store_taiwan_game_record(db,s.game_record,s.player_list,'custom','1/4')
    assert gid
    for p in s.player_list:
        expected=(p.score-before[p.original_player_index],p.record_counter.zimo_times+p.record_counter.dianhe_times,
                  p.record_counter.zimo_times,p.record_counter.fangchong_times,p.record_counter.win_score,
                  p.record_counter.fangchong_score,None)
        rows=db.sql('SELECT total_round_score,win_count,self_draw_count,deal_in_count,total_fan_score,total_fangchong_score,fan_stats FROM game_player_metrics WHERE game_id=%s AND user_id=%s',(gid,p.user_id))
        assert rows==[expected]
        history=get_custom_rule_history(db,p.user_id,'beijing')
        assert len(history)==1 and history[0]['sub_rule']=='beijing/chipengti'
        assert history[0]['total_round_score']==expected[0] and 'fan_stats' not in history[0]
        assert get_custom_rule_history(db,p.user_id,'shangrao')==[]
