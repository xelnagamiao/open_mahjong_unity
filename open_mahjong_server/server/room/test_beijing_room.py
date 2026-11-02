import asyncio
from types import SimpleNamespace

import pytest
from pydantic import ValidationError

from .beijing_room import BeijingRoomValidator, handle_create_beijing_room


@pytest.mark.parametrize('rounds', [1,2,3,4])
def test_beijing_room_defaults_and_circle_count(rounds):
    room = BeijingRoomValidator(room_name='北京测试',game_round=rounds)
    assert room.starting_score == room.hepai_limit == 0
    assert room.sub_rule == 'beijing/chipengti'
    assert room.tips and room.pointer_tips and not room.use_flowers
    assert room.detailed_config['half_hun'] is False


@pytest.mark.parametrize('fields', [dict(starting_score=1),dict(starting_score=True),dict(hepai_limit=1),
    dict(hepai_limit='0'),dict(game_round=0),dict(game_round=5),dict(game_round=True),dict(sub_rule='beijing/unknown'),
    dict(detailed_config={'half_hun_threshold':1}),dict(detailed_config={'half_hun':1}),dict(use_flowers=True),
    dict(claim_protection=True),dict(tips='false'),dict(round_timer=-1),dict(step_timer=101)])
def test_beijing_rejects_unsupported_or_coerced_room_config(fields):
    with pytest.raises(ValidationError):
        BeijingRoomValidator(room_name='北京测试',**fields)


@pytest.mark.parametrize('field,value', [('starting_score',500),('hepai_limit',1),('use_flowers',True)])
def test_network_create_does_not_silently_ignore_invalid_fixed_values(field,value):
    class Socket:
        packets=[]
        async def send_json(self,packet):self.packets.append(packet)
    connection=SimpleNamespace(user_id=123,current_room_id=None,username='测试')
    server=SimpleNamespace(players={'c':connection})
    manager=SimpleNamespace(game_server=server,_reject_room_entry_conflicts=lambda *a:None,
        _normalize_event_id=lambda v:v,_validate_event_for_room=lambda *a:None)
    server.room_manager=manager
    socket=Socket()
    asyncio.run(handle_create_beijing_room(server,'c',dict(roomname='北京测试',**{field:value}),socket))
    assert socket.packets[-1]['success'] is False and not connection.current_room_id


def test_beijing_room_starts_and_releases_all_game_indexes():
    from unittest.mock import patch
    from server.gamestate.game_beijing.BeijingGameState import BeijingGameState
    from server.gamestate.test_room_lifecycle import make_server, make_room, parked_loop, USERS

    async def run():
        server = make_server(USERS)
        room = make_room(USERS, 'beijing')
        room['sub_rule'] = 'beijing/chipengti'
        server.room_manager.rooms['1'] = room
        manager = server.gamestate_manager
        with patch.object(BeijingGameState, 'run_game_loop', parked_loop):
            result = await manager.start_game(str(USERS[0]), '1')
            assert result is None, result
            state = manager.get_game_state_by_room_id('1')
            assert isinstance(state, BeijingGameState)
            assert all(manager.get_game_state_by_user_id(uid) is state for uid in USERS)
            await manager.cleanup_game_state_complete(gamestate_id=state.gamestate_id)
            assert manager.get_game_state_by_room_id('1') is None
            assert all(manager.get_game_state_by_user_id(uid) is None for uid in USERS)
    asyncio.run(run())
