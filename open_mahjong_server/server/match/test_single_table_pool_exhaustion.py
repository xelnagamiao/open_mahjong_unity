"""Exercise single-table final messages through the real game loops and replay adapters."""
import asyncio
from functools import partial
from threading import Event
from unittest.mock import AsyncMock, Mock

import pytest
from psycopg2.pool import PoolError

from ..database import record_persistence
from ..database.guobiao.store_guobiao import store_guobiao_game_record
from ..database.sichuan.store_sichuan import store_sichuan_game_record
from ..gamestate.game_guobiao.GuobiaoGameState import GuobiaoGameState
from ..gamestate.game_sichuan.SichuanGameState import SichuanGameState
from ..gamestate.public.lifecycle import close_if_all_humans_offline
from ..gamestate.verifier.host import build_db_manager, build_game_server, build_room_data
from .rating_rules import DOUBLES_QUEUE


def finished_table(rule, room_type='match'):
    db = build_db_manager()
    packets = []
    server = build_game_server(db, messages=packets)
    server.gamestate_manager.cleanup_game_state_complete = AsyncMock()
    room = build_room_data(seed=29, room_id='171234')
    room.update(room_rule=rule, room_type=room_type)
    if rule == 'sichuan':
        room.update(sub_rule='sichuan/doubles', team_by_seat=[0, 1, 0, 1], game_round=2)
        state = SichuanGameState(server, room, server.calculation_service, db, 'pool-test')
        state.match_queue_type = DOUBLES_QUEUE
        run = state.game_loop_sichuan
        db.store_sichuan_game_record.side_effect = partial(store_sichuan_game_record, db)
    else:
        state = GuobiaoGameState(server, room, server.calculation_service, db, 'pool-test')
        state.match_queue_type = 'beginner_dongfeng'
        run = state.game_loop_chinese
        db.store_guobiao_game_record.side_effect = partial(store_guobiao_game_record, db)
    # Enter the natural end-of-game path without replaying four/eight boards.
    state.current_round = state.max_round * 4 + 1
    return state, run, packets


def rating_results(state, rule):
    rating = (dict(rating_system='grade', rank_after='10级', score_after=0, rating_pt=0)
              if rule == 'guobiao' else dict(rating_system='elo', elo_before=1500, elo_after=1500, elo_delta=0))
    return {str(p.user_id): dict(rating, rating_games=1) for p in state.player_list}


async def play_through_pool_shortage(rule, room_type, monkeypatch):
    state, finish, packets = finished_table(rule, room_type)
    db = state.db_manager
    db.settle_rated_game.side_effect = [PoolError('connection pool exhausted'), rating_results(state, rule)]
    connection = db._get_connection.return_value
    pool_available = Event()
    committed = asyncio.Event()
    stats_finished = asyncio.Event()
    loop = asyncio.get_running_loop()

    def get_connection():
        if not pool_available.is_set():
            raise PoolError('connection pool exhausted')
        return connection

    db._get_connection.side_effect = get_connection
    db._put_connection.side_effect = lambda conn: loop.call_soon_threadsafe(committed.set)
    db.store_guobiao_fan_stats.side_effect = lambda *args: loop.call_soon_threadsafe(stats_finished.set)
    def publish_reserved_record(_db, _cursor, record, *args, **kwargs):
        return record['game_title']['cloud_game_id'], {'currency_reward': {}}

    publish = Mock(side_effect=publish_reserved_record)
    monkeypatch.setattr(record_persistence, '_publish_record', publish)
    try:
        await asyncio.wait_for(finish(), 5)
        state.game_server.gamestate_manager.cleanup_game_state_complete.assert_awaited_once_with(
            gamestate_id=state.gamestate_id)
        ends = [p for p in packets if p['type'] == f'gamestate/{rule}/game_end']
        assert sorted(p['user_id'] for p in ends) == sorted(p.user_id for p in state.player_list)
        for packet in ends:
            info = packet['message']['game_end_info']
            assert not info['record_detail']['cloud_saved']
            assert info['record_detail']['game_id'] == state.game_record['game_title']['cloud_game_id']
            if room_type == 'match':
                assert all(player['rating_games'] == 1 for player in info['player_final_data'].values())
        publish.assert_not_called()
        original_names = [p.username for p in state.player_list]
        original_id = state.game_record['game_title']['game_instance_id']
        state.player_list[0].username = 'changed after cleanup'
        state.game_record['game_title']['game_instance_id'] = 'changed-after-cleanup'
    finally:
        pool_available.set()

    await asyncio.wait_for(committed.wait(), 10)
    if rule == 'guobiao':
        await asyncio.wait_for(stats_finished.wait(), 5)
        db.store_guobiao_game_stats.assert_called_once()
        db.store_guobiao_fan_stats.assert_called_once()
    publish.assert_called_once()
    saved_record, saved_players = publish.call_args.args[2:4]
    assert saved_record['game_title']['game_instance_id'] == original_id
    assert [p.username for p in saved_players] == original_names
    connection.commit.assert_called_once()
    db._put_connection.assert_called_once_with(connection)
    if room_type == 'match':
        assert db.settle_rated_game.call_count == 2
        assert db.settle_rated_game.call_args_list[0] == db.settle_rated_game.call_args_list[1]
    else:
        db.settle_rated_game.assert_not_called()


@pytest.mark.parametrize(('rule', 'room_type'), [
    ('guobiao', 'match'), ('sichuan', 'match'), ('sichuan', 'custom'),
])
def test_replay_pool_shortage_sends_result_then_stores_once(rule, room_type, monkeypatch):
    asyncio.run(play_through_pool_shortage(rule, room_type, monkeypatch))


def test_custom_bot_table_keeps_local_record_without_waiting_for_database():
    state, finish, packets = finished_table('sichuan', 'custom')
    state.player_list[0].user_id = 0
    asyncio.run(asyncio.wait_for(finish(), 5))
    state.db_manager.store_sichuan_game_record.assert_called_once()
    state.db_manager._get_connection.assert_not_called()
    ends = [p for p in packets if p['type'] == 'gamestate/sichuan/game_end']
    assert len(ends) == 3
    assert all(not p['message']['game_end_info']['record_detail']['cloud_saved'] for p in ends)


def test_all_disconnect_while_waiting_for_ranked_settlement():
    async def run():
        state, finish, _ = finished_table('sichuan')
        db = state.db_manager
        waiting = asyncio.Event()
        available = Event()
        loop = asyncio.get_running_loop()

        def settle(*args, **kwargs):
            if not available.is_set():
                loop.call_soon_threadsafe(waiting.set)
                raise PoolError('connection pool exhausted')
            return rating_results(state, 'sichuan')

        db.settle_rated_game.side_effect = settle
        db.store_sichuan_game_record.side_effect = None
        db.store_sichuan_game_record.return_value = 'PoolTest03'
        task = asyncio.create_task(finish())
        cleanup = state.game_server.gamestate_manager.cleanup_game_state_complete
        try:
            await asyncio.wait_for(waiting.wait(), 5)
            for player in state.player_list:
                player.tag_list.append('offline')
            await close_if_all_humans_offline(state)
            cleanup.assert_not_awaited()
        finally:
            available.set()
            await asyncio.wait_for(task, 10)
        db.store_sichuan_game_record.assert_called_once()
        assert state.game_record['game_title']['rating_result'] == rating_results(state, 'sichuan')
        cleanup.assert_awaited_once_with(gamestate_id=state.gamestate_id)

    asyncio.run(run())
