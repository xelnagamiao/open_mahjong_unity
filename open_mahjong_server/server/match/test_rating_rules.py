import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch
import pytest

from .rating_rules import QUEUES, RULES, elo_deltas, default_rating
from .rank_calculator import queue_type_to_room_config, queue_type_to_match_type
from .test_lifecycle import server_fixture, USERS
from .match_manager import MatchManager
from .settlement import settle_ranked_game
from ..database.data_router import handle_get_leaderboard, handle_get_rank_record_list
from ..database.data_router import handle_get_ranked_stats


def test_elo_equal_players_and_ties():
    assert elo_deltas([1500]*4,[1,2,3,4]) == [16,5.33,-5.33,-16]
    assert elo_deltas([1500]*4,[1,1,1,1]) == [0]*4
    assert elo_deltas([1500]*4,[1,1,3,4]) == [10.67,10.67,-5.33,-16.01]
    assert sum(elo_deltas([1000,1500,1800,2200],[1,2,3,4])) == pytest.approx(0)
    assert elo_deltas([1000,1500,1800,2200],[1,2,3,4])[0] > 16
    with pytest.raises(ValueError): elo_deltas([float('nan')]*4,[1,2,3,4])


@pytest.mark.parametrize('place,expected', [(1,10.68),(2,0.02),(3,-10.65),(4,-21.32)])
def test_large_gap_uses_2000_point_expectation(place,expected):
    places=[place]+[p for p in (1,2,3,4) if p!=place]
    deltas=elo_deltas([2100,1500,1500,1500],places)
    assert deltas[0]==expected
    assert sum(deltas)==pytest.approx(0)


def test_elo_mapping_is_symmetric_and_finite_at_extremes():
    for ratings in ([0,2000,4000,6000],[-1e300,0,1500,1e300]):
        deltas=elo_deltas(ratings,[1,2,3,4])
        assert sum(deltas)==pytest.approx(0)
        assert all(-32.02<=d<=32.02 for d in deltas)
    assert elo_deltas([1500,1500,1500,1500],[1,2,3,4])==elo_deltas([2000]*4,[1,2,3,4])


@pytest.mark.parametrize('rule', ['qingque', 'sichuan'])
def test_elo_full_game_starts_with_four_players_regardless_of_rating(rule):
    async def run():
        server=server_fixture();manager=server.match_manager
        manager.committed_users.clear();manager.winning_queues.clear()
        queue=next(q for q,s in QUEUES.items() if s.rule==rule)
        assert [(s.mode,s.rounds) for s in QUEUES.values() if s.rule==rule]==[('quanzhuang',4)]
        ratings=dict(zip(USERS,[600,2600,1100,1900]))
        server.db_manager.get_rank_data.side_effect=lambda uid: {'ratings':{rule:{**default_rating(rule),'elo':ratings[uid]}}}
        started=asyncio.Event()
        async def hold(*args):
            started.set()
            await asyncio.Event().wait()
        server.gamestate_manager.run_game=hold
        for uid in USERS[:3]:
            assert (await manager.join_queue(str(uid),queue)).success
            assert not manager.committed_users
            assert not server.gamestate_manager.gamestate_id_to_game_state
        assert (await manager.join_queue(str(USERS[3]),queue)).success
        await asyncio.wait_for(started.wait(),1)
        game=next(iter(server.gamestate_manager.gamestate_id_to_game_state.values()))
        assert game.max_round==4
        assert {p.user_id for p in game.player_list}==set(USERS)
        assert manager.committed_users==set(USERS)
        assert not manager.queues[queue]
        for uid in USERS:
            payload=server.user_id_to_connection[uid].websocket.send_json.call_args.args[0]
            assert payload['type']=='match/match_found'
            assert '全庄战' in payload['message']
        await server.gamestate_manager.cleanup_game_state_complete(game.gamestate_id)
    asyncio.run(run())


@pytest.mark.parametrize('queue',['qingque_elo_dongfeng','qingque_elo_banzhuang'])
def test_removed_short_elo_queues_cannot_be_joined(queue):
    async def run():
        server=server_fixture();server.match_manager.committed_users.clear()
        assert not (await server.match_manager.join_queue(str(USERS[0]),queue)).success
        assert not server.match_manager.user_to_queues
    asyncio.run(run())


@pytest.mark.parametrize('queue',['qingque_elo_quanzhuang','sichuan_elo_xuezhan'])
def test_elo_queue_uses_arrival_order_and_keeps_the_fifth_player(queue):
    async def run():
        server=server_fixture();manager=server.match_manager
        manager.committed_users.clear();manager.winning_queues.clear()
        order=[USERS[2],USERS[0],USERS[3],USERS[1],999005]
        manager.queues[queue]=list(order)
        manager.user_to_queues={uid:[queue] for uid in order}
        manager._start_game=AsyncMock()
        await manager._try_start_match(queue)
        await asyncio.sleep(0)
        manager._start_game.assert_awaited_once_with(queue,order[:4])
        assert manager.queues[queue]==order[4:]
        assert manager.user_to_queues=={999005:[queue]}
    asyncio.run(run())


@pytest.mark.parametrize('queue',list(QUEUES))
def test_queue_config_and_real_game_construction(queue):
    async def run():
        server=server_fixture();spec=QUEUES[queue]
        async def hold(*args): await asyncio.Event().wait()
        server.gamestate_manager.run_game=hold
        with patch('server.match.match_manager.asyncio.sleep',new=AsyncMock()):
            await server.match_manager._start_game(queue,USERS)
        game=next(iter(server.gamestate_manager.gamestate_id_to_game_state.values()))
        assert game.room_rule==spec.rule
        assert game.max_round==spec.rounds
        expected_fan=(spec.tier=='beginner' if spec.rule=='guobiao' else spec.rule!='riichi')
        expected_count=spec.rule=='riichi' and spec.tier=='beginner'
        assert game.tips==expected_fan
        assert game.count_tips==expected_count
        if spec.rule=='riichi':
            from ..game_calculation.riichi.rule_config import preset_room_config
            from ..gamestate.game_riichi.boardcast import _build_base_game_info
            preset = preset_room_config('tenhou')
            assert game.detailed_config == preset['detailed_config']
            for key, value in preset.items():
                if key not in ('game_round', 'detailed_config'):
                    assert getattr(game, key) == value, key
            assert game.hepai_way == 'three_ron_abort'
            assert game.detailed_config['double_yakuman'] is False
            assert game.detailed_config['kokushi_ankan_ron'] is False
            assert game.detailed_config['furiten_clear'] == 'discard'
            info=_build_base_game_info(game)
            assert info['tips'] is False
            assert info['count_tips']==expected_count
            assert info['detailed_config'] == preset['detailed_config']
            assert info['hepai_way'] == 'three_ron_abort'
        assert game.match_queue_type==queue
        assert queue_type_to_match_type(queue)==f'{spec.rounds}/4_rank'
        assert len(game.player_list)==4
        assert server.match_manager.playing_counts[queue]==4
        await server.gamestate_manager.cleanup_game_state_complete(game.gamestate_id)
        assert not server.match_manager.committed_users
        assert not server.room_manager.match_room_ids
    asyncio.run(run())


@pytest.mark.parametrize('rule',RULES)
def test_ranked_stats_api_receives_rule_and_fan_data(rule):
    async def run():
        server=SimpleNamespace(players={'c':SimpleNamespace(user_id=11000001)},db_manager=object())
        ws=SimpleNamespace(send_json=AsyncMock())
        with patch('server.database.rule_ratings.get_ranked_history',return_value=[dict(rule=rule,mode='1/4_rank',total_games=3)]) as history, \
             patch('server.database.riichi.get_riichi_stats.get_riichi_fan_stats_total',return_value={'riichi':2}), \
             patch('server.database.qingque.get_qingque_stats.get_qingque_fan_stats_total',return_value={'qingyise':1}):
            await handle_get_ranked_stats(server,'c',dict(rule=rule,userid='11000002',data_request_id='stats-1'),ws)
            body=ws.send_json.call_args.args[0]
            assert body['success'] and body['data_request_id']=='stats-1'
            assert body['rule_stats']['history_stats'][0]['total_games']==3
            history.assert_called_once_with(server.db_manager,11000002,rule)
            if rule in ('riichi','qingque'):assert body['rule_stats']['ranked_fan_stats']
            await handle_get_ranked_stats(server,'missing',dict(rule=rule),ws)
            assert not ws.send_json.call_args.args[0]['success']
    asyncio.run(run())


def test_ranked_stats_api_invalid_and_database_failure():
    async def run():
        server=SimpleNamespace(players={'c':SimpleNamespace(user_id=11000001)},db_manager=object())
        ws=SimpleNamespace(send_json=AsyncMock())
        for rule in ('free',None,[]):
            await handle_get_ranked_stats(server,'c',dict(rule=rule),ws)
            assert not ws.send_json.call_args.args[0]['success']
        with patch('server.database.rule_ratings.get_ranked_history',side_effect=RuntimeError('offline')):
            await handle_get_ranked_stats(server,'c',dict(rule='sichuan'),ws)
            assert not ws.send_json.call_args.args[0]['success']
    asyncio.run(run())


@pytest.mark.parametrize('rule',RULES)
def test_rule_specific_join_guest_cancel_and_commit(rule):
    async def run():
        server=server_fixture();manager=server.match_manager
        manager.committed_users.clear();manager.winning_queues.clear()
        manager._start_game=AsyncMock()
        queue=next(q for q,s in QUEUES.items() if s.rule==rule)
        ratings={r:default_rating(r) for r in RULES}
        server.db_manager.get_rank_data.return_value={'guobiao_rank':'四段','ratings':ratings}
        server.players[str(USERS[0])].is_tourist=True
        assert not (await manager.join_queue(str(USERS[0]),queue)).success
        server.players[str(USERS[0])].is_tourist=False
        assert (await manager.join_queue(str(USERS[0]),queue)).success
        assert (await manager.leave_queue(str(USERS[0]),queue)).success
        assert not manager.queues[queue]
        for uid in USERS: assert (await manager.join_queue(str(uid),queue)).success
        await asyncio.sleep(0)
        assert manager.committed_users==set(USERS)
        assert not manager.queues[queue]
        manager._start_game.assert_awaited_once()
        assert not (await manager.leave_queue(str(USERS[0]))).success
    asyncio.run(run())


def test_riichi_eligibility_uses_own_rank():
    async def run():
        server=server_fixture();manager=server.match_manager;manager.committed_users.clear()
        assert not (await manager.join_queue(str(USERS[0]),'riichi_advanced_banzhuang')).success
        ratings={'riichi':{**default_rating('riichi'),'rank_name':'四段'}}
        server.db_manager.get_rank_data.return_value={'guobiao_rank':'10级','ratings':ratings}
        assert (await manager.join_queue(str(USERS[0]),'riichi_advanced_banzhuang')).success
    asyncio.run(run())


@pytest.mark.parametrize('queue',list(QUEUES))
def test_settlement_dispatch_only_for_matching_rule(queue):
    spec=QUEUES[queue]
    players=[SimpleNamespace(user_id=u,record_counter=SimpleNamespace(rank_result=i+1)) for i,u in enumerate(USERS)]
    result={str(u):{'rating_rule':spec.rule,'rank_after':'9级','score_after':3} for u in USERS}
    db=SimpleNamespace(settle_rated_game=Mock(return_value=result))
    state=SimpleNamespace(room_type='match',room_rule=spec.rule,match_queue_type=queue,max_round=spec.rounds,game_record={'game_title':{}},player_list=players,db_manager=db,gamestate_id='sample')
    assert settle_ranked_game(state)==f'{spec.rounds}/4_rank'
    assert all(p.rating_rule==spec.rule for p in players)
    state.room_type='custom';db.settle_rated_game.reset_mock()
    assert settle_ranked_game(state)==f'{spec.rounds}/4'
    db.settle_rated_game.assert_not_called()
    state.room_type='match';state.room_rule='unsupported'
    with pytest.raises(ValueError):settle_ranked_game(state)


@pytest.mark.parametrize('handler,method,key',[(handle_get_leaderboard,'get_rule_leaderboard','leaderboard_list'),(handle_get_rank_record_list,'get_rank_record_list','record_list')])
@pytest.mark.parametrize('rule',list(RULES)+['free',None,[]])
def test_data_api_rules_empty_error_and_correlation(handler,method,key,rule):
    async def run():
        getter=Mock(return_value=[]);server=SimpleNamespace(players={'c':SimpleNamespace(user_id=10000001)},db_manager=SimpleNamespace(**{method:getter}))
        ws=SimpleNamespace(send_json=AsyncMock());msg={'rule':rule,'data_request_id':'request-1'}
        await handler(server,'c',msg,ws);body=ws.send_json.call_args.args[0]
        valid=isinstance(rule,str) and rule in RULES
        assert body['success']==valid and body['data_request_id']=='request-1'
        assert body[key]==[]
        if valid:
            assert getter.call_args.kwargs['rule']==rule
            getter.side_effect=RuntimeError('offline')
            await handler(server,'c',msg,ws)
            assert not ws.send_json.call_args.args[0]['success']
        await handler(server,'missing',msg,ws)
        assert not ws.send_json.call_args.args[0]['success']
    asyncio.run(run())
