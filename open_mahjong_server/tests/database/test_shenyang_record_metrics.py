import pytest

from test_subrule_statistics import db
from test_subrule_realtime import prepare
from server.gamestate.game_shenyang.test_metrics import settled_record
from server.database.taiwan.store_taiwan import store_taiwan_game_record
from server.database.get_rule_stats import get_custom_rule_history


@pytest.mark.parametrize('rotation',range(4))
@pytest.mark.parametrize('win',[True,False])
def test_real_shenyang_egg_record_is_stored_with_rotated_metrics(db,rotation,win):
    prepare(db)
    state,before=settled_record(rotation,win)
    gid=store_taiwan_game_record(db,state.game_record,state.player_list,'custom','1/4')
    assert gid
    for p in state.player_list:
        rows=db.sql('SELECT total_round_score,win_count,self_draw_count,deal_in_count,fulu_round_count,fan_stats,total_win_turn '
                    'FROM game_player_metrics WHERE game_id=%s AND user_id=%s',(gid,p.user_id))
        winner=int(win and p.player_index==0)
        assert rows==[(p.score-before[p.original_player_index],winner,winner,0,0,None,p.record_counter.win_turn)]
        stats=get_custom_rule_history(db,p.user_id,'shenyang')
        assert len(stats)==1 and stats[0]['sub_rule']=='shenyang/mil2025'
        assert stats[0]['total_round_score']==p.score-before[p.original_player_index]
        assert 'fan_stats' not in stats[0]
