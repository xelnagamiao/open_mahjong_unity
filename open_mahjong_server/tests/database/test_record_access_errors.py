"""Cloud replay lookups distinguish locked duplicate keys from missing records."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch

import pytest

from server.database.db_manager import DatabaseManager
from server.database.data_router import handle_get_record_by_id
from server.database.duplicate_walls import DuplicateRecordLockedError


def database_with_record(record):
    cursor = Mock()
    cursor.fetchone.return_value = record
    cursor.fetchall.return_value = []
    connection = Mock()
    connection.cursor.return_value = cursor
    database = SimpleNamespace(
        _get_connection=Mock(return_value=connection), _put_connection=Mock(),
    )
    return database, connection, cursor


@pytest.mark.parametrize('rule', ['guobiao', 'sichuan'])
def test_locked_record_does_not_return_record_or_player_data(rule):
    record = {'game_title': {'rule': rule, 'duplicate_seed': 'private-seed'}}
    database, connection, cursor = database_with_record({
        'game_id': 'locked123', 'record': record, 'created_at': '2026-11-01',
    })
    with patch('server.database.duplicate_walls.duplicate_record_is_visible', return_value=False):
        with pytest.raises(DuplicateRecordLockedError, match='密钥未解禁') as error:
            DatabaseManager.get_record_by_id(database, 'locked123')
    assert 'private-seed' not in str(error.value)
    assert cursor.execute.call_count == 1
    cursor.fetchall.assert_not_called()
    cursor.close.assert_called_once()
    database._put_connection.assert_called_once_with(connection)


def test_missing_record_still_returns_none():
    database, connection, cursor = database_with_record(None)
    with patch('server.database.duplicate_walls.duplicate_record_is_visible') as visibility:
        assert DatabaseManager.get_record_by_id(database, 'missing123') is None
    visibility.assert_not_called()
    database._put_connection.assert_called_once_with(connection)


def test_unlocked_record_still_loads_in_full():
    record = {'game_title': {'rule': 'sichuan', 'duplicate_seed': 'released-seed'}}
    database, connection, cursor = database_with_record({
        'game_id': 'open123', 'record': record, 'created_at': '2026-11-01',
    })
    with patch('server.database.duplicate_walls.duplicate_record_is_visible', return_value=True):
        detail = DatabaseManager.get_record_by_id(database, 'open123')
    assert detail['record'] == record
    assert detail['game_id'] == 'open123'
    cursor.fetchall.assert_called_once()
    database._put_connection.assert_called_once_with(connection)


@pytest.mark.parametrize('state', ['locked', 'missing', 'open'])
def test_websocket_reports_distinct_failure_without_disclosing_locked_record(state):
    result = {
        'game_id': 'replay123', 'rule': 'guobiao',
        'record': {'game_title': {'rule': 'guobiao'}, 'game_round': {}},
        'created_at': '2026-11-01', 'players': [],
    }
    lookup = Mock(return_value=result if state == 'open' else None)
    if state == 'locked':
        lookup.side_effect = DuplicateRecordLockedError()
    server = SimpleNamespace(
        players={'connection': SimpleNamespace(user_id=10001)},
        db_manager=SimpleNamespace(get_record_by_id=lookup),
    )
    socket = SimpleNamespace(send_json=AsyncMock())
    asyncio.run(handle_get_record_by_id(server, 'connection', {'game_id': 'replay123'}, socket))
    lookup.assert_called_once_with('replay123')
    socket.send_json.assert_awaited_once()
    response = socket.send_json.call_args.args[0]
    assert response['success'] is (state == 'open')
    if state == 'locked':
        assert response['message'] == '该复式密钥未解禁，牌谱暂不可查看或下载'
        assert 'record_detail' not in response
    elif state == 'missing':
        assert response['message'] == '未找到牌谱 replay123'
        assert 'record_detail' not in response
    else:
        assert response['record_detail']['record'] == result['record']


@pytest.mark.parametrize('logged_in,game_id', [(False, 'replay123'), (True, '')])
def test_invalid_lookup_is_rejected_before_database_access(logged_in, game_id):
    lookup = Mock()
    server = SimpleNamespace(
        players={'connection': SimpleNamespace(user_id=10001)} if logged_in else {},
        db_manager=SimpleNamespace(get_record_by_id=lookup),
    )
    socket = SimpleNamespace(send_json=AsyncMock())
    asyncio.run(handle_get_record_by_id(server, 'connection', {'game_id': game_id}, socket))
    lookup.assert_not_called()
    assert socket.send_json.call_args.args[0]['success'] is False
