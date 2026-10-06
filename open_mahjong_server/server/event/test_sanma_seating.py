"""Sanma uses three reserved seats in manual and automatic venue tables."""
import asyncio
import copy
from unittest.mock import Mock

import pytest

from . import test_auto_match as helpers
from ..database.db_manager import DatabaseManager
from ..game_calculation.riichi.rule_config import preset_room_config


def environment(preset='sanma_majsoul'):
    test = helpers.EventAutoMatchTest()
    test.setUp()
    config = preset_room_config(preset)
    config.update(game_round=1, round_timer=20, step_timer=5)
    auto = test.db.events['venue']['room_settings']['auto_match']
    auto.update(room_rule='riichi', room_config=config)
    def claim(event_id, user_ids, automatic=False):
        rows = [row for row in test.db.ready if row['event_id'] == event_id and row['user_id'] in user_ids]
        if len(rows) != len(user_ids):
            return []
        test.db.ready = [row for row in test.db.ready if row not in rows]
        return copy.deepcopy(rows)
    test.db.claim_event_ready_players = claim
    test.db.list_auto_match_event_ids = lambda: ['venue'] if len(test.queued_ids()) >= 3 else []
    return test, config


@pytest.mark.parametrize('preset', ['sanma_majsoul', 'sanma_tenhou'])
def test_automatic_fifo_uses_three_and_leaves_incomplete_table(preset):
    async def run():
        test, config = environment(preset)
        test.add_players(2)
        await test.matcher.run_once()
        assert not test.started
        test.add_players(5, start=103)
        await test.matcher.run_once()
        assert [room['player_list'] for room in test.started] == [[101, 102, 103], [104, 105, 106]]
        assert all(room['max_player'] == 3 and room['detailed_config'] == config['detailed_config'] for room in test.started)
        assert test.queued_ids() == [107]
    asyncio.run(run())


@pytest.mark.parametrize('count', [2, 3, 4])
def test_manual_seating_requires_exactly_three(count):
    async def run():
        test, config = environment()
        ids = test.add_players(count)
        response = await test.manager.seat_event_table(900, 'venue', ids, 'riichi', config)
        assert response.success == (count == 3)
        if count == 3:
            assert next(iter(test.manager.rooms.values()))['player_list'] == ids
            assert not test.queued_ids()
        else:
            test.assert_no_orphan_room()
    asyncio.run(run())


@pytest.mark.parametrize('rows,automatic,sub_rule,enabled,expected', [
    (3, True, 'riichi/sanma', True, True),
    (2, True, 'riichi/sanma', True, False),
    (3, True, 'riichi/standard', True, False),
    (3, True, 'riichi/sanma', False, False),
    (3, False, 'riichi/standard', True, True),
])
def test_three_seat_claim_transaction(rows, automatic, sub_rule, enabled, expected):
    db = DatabaseManager.__new__(DatabaseManager)
    cursor = Mock()
    cursor.__enter__ = Mock(return_value=cursor)
    cursor.__exit__ = Mock(return_value=False)
    cursor.fetchone.return_value = {'status': 'active', 'room_settings': {'auto_match': {
        'enabled': enabled, 'room_rule': 'riichi', 'room_config': {'sub_rule': sub_rule}}}}
    cursor.fetchall.return_value = [{'user_id': uid} for uid in range(rows)]
    connection = Mock()
    connection.cursor.return_value = cursor
    db._get_connection = Mock(return_value=connection)
    db._put_connection = Mock()
    result = db.claim_event_ready_players('venue', [101, 102, 103], automatic=automatic)
    assert bool(result) == expected
    assert connection.commit.call_count == int(expected)
    assert connection.rollback.call_count == int(not expected)
