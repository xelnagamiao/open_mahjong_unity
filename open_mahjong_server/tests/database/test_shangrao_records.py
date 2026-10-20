import pytest
from test_subrule_statistics import db
from test_subrule_realtime import prepare
from test_regional_record_metrics import identify_players
from server.database.get_rule_stats import get_custom_rule_history
from server.database.taiwan.store_taiwan import store_taiwan_game_record
from server.database.shared_record_metrics import analyze_shared_record_for_player
from server.gamestate.game_shangrao.test_state import turn, NORMAL


@pytest.mark.parametrize('rotation', range(4))
@pytest.mark.parametrize('win', [True, False])
@pytest.mark.parametrize('kind,amount', [('gang',1),('angang',2),('four_bao',4),('four_bao',2)])
def test_shangrao_real_records_keep_payments_and_self_draw_stats(db, rotation, win, kind, amount):
    prepare(db)
    s=turn()
    before=identify_players(s,rotation)
    s.start_game_recording();s.start_round_recording()
    if kind == "four_bao" and amount == 2:
        s.detailed_config["four_bao_reward"] = True
        s.player_list[2].drawn_bao_count = 4
    else:
        s.record_payment(2,amount,kind,21)
    s.player_list[1].hand_tiles=[45,45,45,11,12,14,21,23,25,31,32,34,41]
    if win:s.settle_win(0,'self_draw',NORMAL[-1])
    else:s.end_draw()
    s.finalize_round_recording();s.finalize_match_result()
    game_id=store_taiwan_game_record(db,s.game_record,s.player_list,'custom','1/4')
    assert game_id
    for player in s.player_list:
        winner=win and player.player_index==0
        expected=(player.score-before[player.original_player_index],int(winner),int(winner),0,6 if winner else 0,1 if winner else 0,None)
        rows=db.sql('SELECT total_round_score,win_count,self_draw_count,deal_in_count,total_fan_score,total_win_turn,fan_stats FROM game_player_metrics WHERE game_id=%s AND user_id=%s',(game_id,player.user_id))
        assert rows==[expected]
        direct=analyze_shared_record_for_player(s.game_record,player.original_player_index)
        assert direct['total_round_score']==expected[0]
        history=get_custom_rule_history(db,player.user_id,'shangrao')
        assert len(history)==1 and history[0]['sub_rule']=='shangrao/standard'
        assert history[0]['total_round_score']==expected[0] and 'fan_stats' not in history[0]
    assert sum(p.score for p in s.player_list)==sum(before.values())
