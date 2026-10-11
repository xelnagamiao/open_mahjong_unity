import pytest

from .room_seats import get_seats, seat_player, unseat_player
from .room_teams import assign_team, validate_team_start
from .room_validators import SichuanRoomValidator


def room():
    value = dict(sub_rule="sichuan/doubles", player_list=[101,102,0,0], host_user_id=101, max_player=4, ready_list=[102])
    get_seats(value)
    return value


def test_create_preset_forces_sbr_options():
    data = SichuanRoomValidator(room_name="双打", sub_rule="sichuan/doubles", game_round=1, round_timer=20, step_timer=5, blood_battle=False, hepai_limit=64)
    assert data.blood_battle and data.hepai_limit == 0


def test_bot_seats_are_independent_and_balanced():
    value = room()
    assert value["team_by_seat"] == [0,1,0,1]
    validate_team_start(value, 0)
    assign_team(value, 101, 2, -1, 0)
    assert value["team_by_seat"] == [0,1,-1,1]
    assert value["ready_list"] == [] and value["roster_revision"] == 1
    with pytest.raises(ValueError):
        validate_team_start(value, 1)


@pytest.mark.parametrize("actor,seat,team,revision", [(102,0,-1,0),(999,1,-1,0),(101,0,1,0),(101,4,0,0),(101,True,0,0),(101,0,3,0),(101,0,True,0),(101,0,-1,None),(101,0,-1,1)])
def test_invalid_team_change_leaves_roster_untouched(actor,seat,team,revision):
    value = room()
    with pytest.raises(ValueError):
        assign_team(value,actor,seat,team,revision)
    assert value["team_by_seat"] == [0,1,0,1] and value["roster_revision"] == 0


def test_player_can_choose_own_team_and_stale_requests_fail():
    value = room()
    assign_team(value,102,1,-1,0)
    with pytest.raises(ValueError):
        assign_team(value,102,1,1,0)
    assign_team(value,102,1,1,1)
    validate_team_start(value,2)


def test_team_changes_are_locked_during_game():
    value=room()
    value["is_game_running"] = True
    with pytest.raises(ValueError):
        assign_team(value,101,0,-1,0)
    assert value["team_by_seat"] == [0,1,0,1]


def test_leave_join_preserves_holes_and_clears_ready():
    value=room()
    unseat_player(value,102)
    assert value["team_by_seat"] == [0,-1,0,1]
    assert value["ready_list"] == []
    seat_player(value,103)
    assert value["team_by_seat"] == [0,1,0,1]
    validate_team_start(value,2)


@pytest.mark.parametrize("mode", ["sichuan/doubles", "sichuan/xueliu_doubles"])
def test_room_flow_create_join_teams_ready_start_lock_and_return(mode):
    import asyncio
    from unittest.mock import AsyncMock
    from .test_room_seats import manager
    from ..game_calculation.game_calculation_service import GameCalculationService
    from ..gamestate.gamestate_manager import GameStateManager

    async def run():
        rooms = manager()
        server = rooms.game_server
        server.room_manager = rooms
        server.calculation_service = GameCalculationService()
        server.gamestate_manager = GameStateManager(server)
        server.gamestate_manager.run_game = AsyncMock()
        rooms.active_game_room_ids = {}
        value = rooms.rooms['123']
        value.update(room_rule='sichuan', sub_rule=mode, game_round=1, round_timer=20, step_timer=5, tips=True, room_type='custom')
        for uid in (101,102,103):
            assert (await rooms.join_room(str(uid),'123','')).success
        get_seats(value)
        for uid in (101,102,103):
            assert await rooms.set_player_ready(str(uid),'123',True,value['roster_revision']) is None
        revision=value['roster_revision']
        assert await rooms.set_room_team('100','123',revision=revision,seat_index=0,team_id=-1) is None
        assert value['ready_list'] == []
        assert await rooms.set_room_team('100','123',revision=value['roster_revision'],seat_index=0,team_id=0) is None
        stale = await server.gamestate_manager.start_game('100','123',roster_revision=revision)
        assert not stale.success and '变化' in stale.message
        assert not (await rooms.set_player_ready('101','123',True,revision)).success
        for uid in (101,102,103):
            await rooms.set_player_ready(str(uid),'123',True,value['roster_revision'])
        assert await server.gamestate_manager.start_game('100','123',roster_revision=value['roster_revision']) is None
        state=server.gamestate_manager.get_game_state_by_room_id('123')
        await state.game_task
        teams=list(value['team_by_seat'])
        assert state.is_team_game and [p.team_id for p in state.player_list] == teams
        assert not (await rooms.set_room_team('100','123',revision=value['roster_revision'],seat_index=0,team_id=-1)).success
        assert not (await rooms.leave_room('101','123')).success
        old_revision=value['roster_revision']
        await rooms.finish_custom_game_room('123',expected_gamestate_id=state.gamestate_id,expected_instance_id=value['instance_id'])
        assert value['team_by_seat'] == teams and value['roster_revision'] > old_revision
        assert value['ready_list'] == [] and not value['is_game_running']
    asyncio.run(run())
