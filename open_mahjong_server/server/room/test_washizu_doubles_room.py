"""Captain role selection through the same lobby contract used by clients."""
import asyncio
from copy import deepcopy
from itertools import permutations
from unittest.mock import AsyncMock

import pytest

from .room_seats import get_seats, seat_player, unseat_player
from .room_teams import assign_team, validate_team_start
from .room_validators import RiichiRoomValidator
from ..game_calculation.riichi.washizu_doubles import SUB_RULE, FIXED_OPTIONS


def room():
    value = dict(sub_rule=SUB_RULE, player_list=[101, 102, 0, 0], host_user_id=101,
                 max_player=4, ready_list=[102])
    get_seats(value)
    return value


def test_room_preset_and_duplicate_bot_roles():
    value = room()
    assert value['team_role_by_seat'] == [0, 1, 2, 3]
    assert value['team_by_seat'] == [0, 0, 1, 1]
    validate_team_start(value, 0)
    data = RiichiRoomValidator(room_name='鹫巢双打', sub_rule=SUB_RULE,
        game_round=2, round_timer=20, step_timer=5, red_dora=True, open_xiru=True,
        open_tobi=False, hepai_way='multi_ron',
        detailed_config={'pao': True, 'rank_points': '10_30', 'tenpai_renchan': False})
    assert not data.red_dora and not data.open_xiru and data.open_tobi
    assert data.hepai_way == 'head_bump' and data.starting_score == 25000
    assert all(data.detailed_config[k] == v for k, v in FIXED_OPTIONS.items())
    assert data.detailed_config['tenpai_renchan'] is True


@pytest.mark.parametrize('roles', list(permutations(range(4))))
def test_host_can_reach_every_unique_role_assignment_without_unassigning(roles):
    value = room()
    for seat, role in enumerate(roles):
        assign_team(value, 101, seat, None, value['roster_revision'], role_id=role)
    assert value['team_role_by_seat'] == list(roles)
    assert value['team_by_seat'] == [role // 2 for role in roles]
    validate_team_start(value, value['roster_revision'])


@pytest.mark.parametrize('actor,seat,role,revision', [
    (102, 0, 2, 0), (999, 1, 2, 0), (102, 1, 2, 0),
    (101, True, 0, 0), (101, -1, 0, 0), (101, 4, 0, 0),
    (101, 0, True, 0), (101, 0, '1', 0), (101, 0, -1, 0), (101, 0, 4, 0),
    (101, 0, None, 0), (101, 0, 2, None), (101, 0, 2, True), (101, 0, 2, 1),
])
def test_invalid_or_unauthorized_requests_are_atomic(actor, seat, role, revision):
    value = room(); before = deepcopy(value)
    with pytest.raises(ValueError):
        assign_team(value, actor, seat, None, revision, role_id=role)
    assert value == before


def test_empty_role_selection_leave_join_noop_and_match_lock():
    value = room()
    assign_team(value, 102, 1, None, 0, role_id=1)
    assert value['roster_revision'] == 0 and value['ready_list'] == [102]
    unseat_player(value, 0, 2)
    assert value['team_role_by_seat'] == [0, 1, -1, 3]
    assign_team(value, 102, 1, None, 1, role_id=2)
    assert value['ready_list'] == []
    seat_player(value, 103)
    assert value['team_role_by_seat'] == [0, 2, 1, 3]
    validate_team_start(value, 3)
    value['is_game_running'] = True
    before = deepcopy(value)
    with pytest.raises(ValueError): assign_team(value, 101, 0, None, 3, role_id=3)
    assert value == before


def test_create_join_swap_ready_start_reconnect_identity_and_return():
    from .test_room_seats import manager
    from ..game_calculation.game_calculation_service import GameCalculationService
    from ..gamestate.gamestate_manager import GameStateManager
    async def run():
        rooms = manager(); server = rooms.game_server
        server.room_manager = rooms
        server.calculation_service = GameCalculationService()
        server.gamestate_manager = GameStateManager(server)
        server.gamestate_manager.run_game = AsyncMock()
        rooms.active_game_room_ids = {}
        value = rooms.rooms['123']
        value.update(room_rule='riichi', sub_rule=SUB_RULE, game_round=2,
                     round_timer=20, step_timer=5, tips=True, room_type='custom')
        for uid in (101, 102, 103): assert (await rooms.join_room(str(uid), '123', '')).success
        for uid in (101, 102, 103):
            assert await rooms.set_player_ready(str(uid), '123', True, value['roster_revision']) is None
        old = value['roster_revision']
        assert await rooms.set_room_team('100', '123', revision=old, seat_index=0, role_id=3) is None
        assert not value['ready_list']
        assert not (await server.gamestate_manager.start_game('100', '123', roster_revision=old)).success
        assert not (await rooms.set_player_ready('101', '123', True, old)).success
        for uid in (101, 102, 103):
            await rooms.set_player_ready(str(uid), '123', True, value['roster_revision'])
        assert await server.gamestate_manager.start_game('100', '123', roster_revision=value['roster_revision']) is None
        state = server.gamestate_manager.get_game_state_by_room_id('123'); await state.game_task
        assert [p.team_role_id for p in state.player_list] == [3, 1, 2, 0]
        assert not (await rooms.set_room_team('100', '123', revision=value['roster_revision'], seat_index=0, role_id=0)).success
        assert not (await rooms.leave_room('101', '123')).success
        await rooms.finish_custom_game_room('123', expected_gamestate_id=state.gamestate_id,
                                           expected_instance_id=value['instance_id'])
        assert value['team_role_by_seat'] == [3, 1, 2, 0]
        assert not value['is_game_running'] and not value['ready_list']
    asyncio.run(run())
