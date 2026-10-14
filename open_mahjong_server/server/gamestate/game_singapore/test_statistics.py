import asyncio
from unittest.mock import Mock
import pytest

from .test_state import make_state
from ...game_calculation.singapore import rules as r
from ...database.shared_record_metrics import analyze_shared_record_for_player
from ...database.record_metrics import _build_metrics_row
from ...database.statistics_catalog import selection
from ...database.taiwan.store_taiwan import store_taiwan_game_record
from ...room.singapore_room import SingaporeRoomValidator


@pytest.mark.parametrize('rotation',range(4))
def test_net_points_include_marriages_and_kongs_but_fan_statistics_disabled(rotation):
    async def run():
        state=make_state();state.current_round=4
        for p in state.player_list:
            p.user_id=11000001+p.original_player_index
            p.original_player_index=(p.player_index+rotation)%4
        from ..public.game_record_manager import init_game_record,init_game_round
        init_game_record(state);init_game_round(state)
        state._pay_instant(0,2,'暗杠')
        state.pending_winners=[state._build_pending_winner(1,'discard','hu_first',r.detail([('动物',1)]),25,0)]
        await state._settle_hand({i:400 for i in range(4)})
        await state._persist_match()
        definition=selection(r.SUB_RULE)
        assert definition['basic_stats'] and not definition['fan_stats']
        for p in state.player_list:
            stats=analyze_shared_record_for_player(state.game_record,p.original_player_index)
            assert stats['total_rounds']==1
            assert stats['total_round_score']==p.score-400
            assert stats['win_count']==int(p.player_index==1)
            assert stats['deal_in_count']==int(p.player_index==0)
            assert stats['total_fan_score']==(4 if p.player_index==1 else 0)
            row=_build_metrics_row('sg',p.user_id,p.username,r.RULE,r.SUB_RULE,'custom',None,None,'1/4',p.score,
                                   p.record_counter.rank_result,state.game_record,p.original_player_index,None)
            assert row[-1] is None and row[25]==p.score-400
        return state
    state=asyncio.run(run())
    conn=Mock();cursor=conn.cursor.return_value;db=Mock();db._get_connection.return_value=conn
    cursor.fetchone.side_effect=[(state.game_record,None)]+[([],None)]*4
    cursor.fetchall.return_value=[(p.user_id,r.RULE,'custom',p.record_counter.rank_result,'1/4',r.SUB_RULE) for p in state.player_list]
    game_id=store_taiwan_game_record(db,state.game_record,state.player_list,'custom','1/4')
    assert game_id and conn.commit.call_count==1 and not conn.rollback.called
    metric_rows=[call for call in cursor.execute.call_args_list if 'INSERT INTO game_player_metrics' in call.args[0]]
    assert len(metric_rows)==4
    assert all('ON CONFLICT (game_id, user_id) DO NOTHING' in call.args[0] for call in metric_rows)
    assert all(call.args[1][3:5]==(r.RULE,r.SUB_RULE) and call.args[1][-1] is None for call in metric_rows)


@pytest.mark.parametrize('invalid',[
    {'sub_rule':'singapore/unknown'},{'game_round':0},{'game_round':5},{'game_round':True},
    {'tips':1},{'use_flowers':False},{'open_cuohe':True},{'tactical_call':True},{'hepai_limit':0},
    {'detailed_config':{'rule_version':'unversioned'}},{'detailed_config':{**r.CONFIG,'fan_cap':10}},
])
def test_room_rejects_incompatible_options(invalid):
    with pytest.raises(ValueError):
        SingaporeRoomValidator(**{'room_name':'SG','round_timer':20,'step_timer':5,**invalid})


def test_result_delivery_failure_does_not_skip_other_players():
    from types import SimpleNamespace
    from unittest.mock import AsyncMock
    async def run():
        state=make_state();state.current_round=4;state.player_list[0].combination_tiles=['g11']
        for p in state.player_list:
            state.game_server.user_id_to_connection[p.user_id]=SimpleNamespace(websocket=SimpleNamespace(send_json=AsyncMock()))
        state.game_server.user_id_to_connection[101].websocket.send_json.side_effect=ConnectionError('disconnected')
        assert await state._settle_hand({i:400 for i in range(4)})
        for uid in (102,103,104):state.game_server.user_id_to_connection[uid].websocket.send_json.assert_awaited_once()
        before=[p.score for p in state.player_list]
        state.game_server.user_id_to_connection[101].websocket.send_json=AsyncMock()
        await state.player_reconnect(101)
        assert [p.score for p in state.player_list]==before
        assert state.game_server.user_id_to_connection[101].websocket.send_json.await_args.args[0]['type'].endswith('/show_result')
    asyncio.run(run())
