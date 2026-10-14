"""Fuzhou room entry uses the common identity/event gates and strict options."""
import asyncio
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock, Mock

import pytest

from .fuzhou_room import create_fuzhou_room, handle_create_fuzhou_room
from ..response import Response


def manager():
    connection = NS(user_id=101,username='测试',current_room_id=None)
    return NS(game_server=NS(players={'c':connection},db_manager=NS(get_user_settings=Mock(return_value={'username':'昵称'}))),
              _reject_room_entry_conflicts=Mock(return_value=None), _normalize_event_id=lambda x:x,
              _validate_event_for_room=Mock(return_value=None), _generate_room_id=lambda:'FZ001',
              _apply_event_fields=lambda room,event:room.update(event_id=event),
              _broadcast_room_info=AsyncMock(),rooms={},room_passwords={})


@pytest.mark.parametrize('failure', ['missing','anonymous','already_in_room','conflict','event','settings'])
def test_creation_failure_does_not_mutate_rooms(failure):
    async def run():
        m = manager()
        if failure=='missing': m.game_server.players.clear()
        elif failure=='anonymous': m.game_server.players['c'].user_id=None
        elif failure=='already_in_room': m.game_server.players['c'].current_room_id='other'
        elif failure=='conflict': m._reject_room_entry_conflicts.return_value=Response(type='tips',success=False,message='冲突')
        elif failure=='event': m._validate_event_for_room.return_value=Response(type='tips',success=False,message='赛事未开放')
        else: m.game_server.db_manager.get_user_settings.return_value=None
        result = await create_fuzhou_room(m,'c',room_name='福州')
        assert not result.success and not m.rooms and not m.room_passwords
        m._broadcast_room_info.assert_not_awaited()
    asyncio.run(run())


@pytest.mark.parametrize('options', [dict(detailed_config={'base_score':11}),dict(gameround=True),
    dict(tips=1),dict(use_flowers=False),dict(claim_protection=True),dict(sub_rule='fuzhou/unknown')])
def test_invalid_settings_fail_atomically(options):
    async def run():
        m=manager();result=await create_fuzhou_room(m,'c',room_name='福州',**options)
        assert not result.success and not m.rooms
        assert not m.game_server.players['c'].current_room_id
    asyncio.run(run())


@pytest.mark.parametrize('base', [10,15,20])
@pytest.mark.parametrize('password', ['', 'test-password'])
def test_valid_creation_preserves_exact_preset_and_profile(base,password):
    async def run():
        m=manager()
        result=await create_fuzhou_room(m,'c',room_name='福州',gameround=4,password=password,
             detailed_config={'base_score':base},event_id=7,count_tips=True,pointer_tips=False,random_seed='a'*64,
             use_flowers=True,claim_protection=False,open_cuohe=False,tactical_call=False,tian_di_ren_he=False)
        assert result.success, result.message
        room=m.rooms['FZ001']
        assert room['room_rule']=='fuzhou' and room['sub_rule']=='fuzhou/standard'
        assert room['detailed_config']['base_score']==base and room['game_round']==4
        assert room['use_flowers'] and room['count_tips'] and not room['pointer_tips']
        assert room['player_list']==[101] and room['player_settings'][101]['username']=='昵称'
        assert room['event_id']==7 and room['is_player_set_random_seed']
        assert room['has_password']==bool(password)
        assert m.room_passwords == ({'FZ001':password} if password else {})
        assert m.game_server.players['c'].current_room_id=='FZ001'
        m._broadcast_room_info.assert_awaited_once_with('FZ001')
    asyncio.run(run())


def test_wire_handler_uses_same_validation_and_serializes_response():
    async def run():
        m=manager();ws=NS(send_json=AsyncMock())
        await handle_create_fuzhou_room(NS(room_manager=m),'c',dict(roomname='福州',use_flowers=False),ws)
        assert not ws.send_json.await_args.args[0]['success'] and not m.rooms
        await handle_create_fuzhou_room(NS(room_manager=m),'c',dict(roomname='福州',detailed_config={'base_score':15}),ws)
        payload=ws.send_json.await_args.args[0]
        assert payload['success'] and payload['room_info']['detailed_config']['base_score']==15
    asyncio.run(run())
