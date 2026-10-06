import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock
import pytest
from .test_guangdong_room import manager, create, BASE
from .guangdong_room import GuangdongMilRoomValidator, enforce_guangdong_tactical, handle_create_guangdong_room
from .room_manager import RoomManager
from ..game_calculation.guangdong.config import SUB_RULE

@pytest.mark.parametrize('value', [True, False])
def test_create_and_api_preserve_switch(value):
    m = manager()
    response = create(m, tactical_call=value)
    assert response.success and m.rooms['gd-unit-room']['tactical_call'] is value
    m = manager()
    ws = SimpleNamespace(send_json=AsyncMock())
    asyncio.run(handle_create_guangdong_room(m.game_server, 'conn', dict(roomname='广东', sub_rule=SUB_RULE, tactical_call=value), ws))
    assert ws.send_json.call_args.args[0]['room_info']['tactical_call'] is value

@pytest.mark.parametrize('bad', [None, 0, 1, 'true', 'false', [], {}])
def test_switch_is_strict_boolean(bad):
    with pytest.raises(ValueError): GuangdongMilRoomValidator(**BASE, tactical_call=bad)

@pytest.mark.parametrize('players,seats,expected', [([101], [101,-1,-1,-1],True), ([101,102], [101,-1,102,-1],True), ([101,0], [101,0,-1,-1],False), ([101,2], [101,2,-1,-1],False), ([101], [101,3,-1,-1],False)])
def test_bot_fallback_ignores_empty_seats_and_spectators_and_never_reenables(players, seats, expected):
    room = dict(room_rule='guangdong', sub_rule=SUB_RULE, player_list=players, seat_list=seats, tactical_call=True, spectators=[0])
    enforce_guangdong_tactical(room)
    assert room['tactical_call'] is expected
    room['tactical_call'] = False
    room['player_list'],room['seat_list'] = [101],[101,-1,-1,-1]
    enforce_guangdong_tactical(room)
    assert room['tactical_call'] is False

@pytest.mark.parametrize('route', ['broadcast', 'sync'])
def test_actual_room_refresh_and_reconnect_broadcast_effective_false(route):
    m = manager()
    response = create(m, tactical_call=True)
    room = m.rooms['gd-unit-room']
    room['player_list'].append(2)
    connection = m.game_server.players['conn']
    connection.websocket = SimpleNamespace(send_json=AsyncMock())
    m.game_server.user_id_to_connection = {101: connection}
    m._sync_room_host = Mock()
    if route == 'broadcast':
        asyncio.run(RoomManager._broadcast_room_info(m, 'gd-unit-room'))
        effective = connection.websocket.send_json.call_args.args[0]['room_info']
    else:
        effective = asyncio.run(RoomManager.sync_my_room(m, 'conn')).room_info
    assert effective['tactical_call'] is False

@pytest.mark.parametrize('uid',[0,2])
def test_actual_add_bot_route_closes_existing_tactical_switch(uid):
    m = manager()
    create(m,tactical_call=True)
    m._sync_room_host = Mock()
    # Use the actual broadcaster rather than the original creation stub.
    m._broadcast_room_info = RoomManager._broadcast_room_info.__get__(m)
    connection = m.game_server.players['conn']
    connection.websocket = SimpleNamespace(send_json=AsyncMock())
    m.game_server.user_id_to_connection = {101:connection}
    response = asyncio.run(RoomManager._add_room_bot(m,'conn','gd-unit-room',uid,1))
    assert response.success and m.rooms['gd-unit-room']['tactical_call'] is False
    assert connection.websocket.send_json.call_args.args[0]['room_info']['tactical_call'] is False

@pytest.mark.parametrize('switch,bot,expected',[(True,False,True),(False,False,False),(True,True,False)])
def test_actual_start_game_persists_bot_fallback_and_state_initialization(switch,bot,expected):
    from ..gamestate.gamestate_manager import GameStateManager
    m=manager()
    create(m,tactical_call=switch)
    async def run():
        room=m.rooms['gd-unit-room']
        room['player_list']=[101,102,103,2 if bot else 104]
        room['seat_list']=list(room['player_list'])
        m._sync_room_host=Mock()
        m.all_players_ready=Mock(return_value=True)
        m.active_game_room_ids={}
        m.game_server.calculation_service=None
        m.game_server.user_id_to_connection={}
        m.game_server.db_manager.get_user_settings=Mock(return_value={})
        m.game_server.db_manager.get_user_profile=Mock(return_value={})
        state_manager=GameStateManager(m.game_server)
        m.game_server.gamestate_manager=state_manager
        state_manager.run_game=AsyncMock()
        response=await state_manager.start_game('conn','gd-unit-room')
        assert response is None
        state=state_manager.get_game_state_by_room_id('gd-unit-room')
        assert state.tactical_call is expected and room['tactical_call'] is expected
        await state.game_task
    asyncio.run(run())
