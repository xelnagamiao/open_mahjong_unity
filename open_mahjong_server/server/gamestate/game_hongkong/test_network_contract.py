import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock

import pytest

from .test_flow import make_state, open_round, physical_tiles
from .get_action import handle_action
from .state_machine import HongKongPhase as P
from ...game_calculation.hongkong.models import PROFILES
from ...room.hongkong_room import HongKongRoomValidator, create_hongkong_room
from .hints import private_hints


@pytest.mark.parametrize('profile',PROFILES)
def test_room_profile_normalizes_known_version_and_rejects_invented_rules(profile):
    base = dict(room_name=' 港麻 ',game_round=1,round_timer=20,step_timer=5,sub_rule=profile)
    room = HongKongRoomValidator(**base)
    assert room.room_name=='港麻'
    assert room.detailed_config['flowers']==(profile==PROFILES[2])
    for config in ([],{'flowers':'false'},{'flowers':1},{'minimum_fan':0},{'new13_full_shoot':None}):
        with pytest.raises(ValueError):
            HongKongRoomValidator(**base,detailed_config=config)
    for key,value in (('game_round',0),('game_round',5),('round_timer',-1),('step_timer',101),('open_cuohe',True),('sub_rule','taiwan/standard')):
        with pytest.raises(ValueError):
            HongKongRoomValidator(**{**base,key:value})


def test_network_identity_tick_and_invalid_payload_cannot_enter_queue():
    async def run():
        state = make_state()
        window = open_round(state)
        owner = window['player']
        socket = SimpleNamespace(send_json=AsyncMock())
        server = SimpleNamespace(
            gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _:state),
            players={'owner':SimpleNamespace(user_id=state.player_list[owner].user_id),
                     'spectator':SimpleNamespace(user_id=900)})
        request = dict(type='gamestate/hongkong/cut_tile',gamestate_id=state.gamestate_id,
                       action_tick=state.server_action_tick,TileId=state.player_list[owner].hand_tiles[-1],
                       cutIndex=len(state.player_list[owner].hand_tiles)-1)
        before = physical_tiles(state)
        for connection,message in [('spectator',request),('owner',{**request,'action_tick':-1}),
                                   ('owner',{**request,'TileId':999}),('owner',{**request,'cutIndex':500}),
                                   ('owner',{**request,'cutIndex':True})]:
            await handle_action(server,connection,message,socket)
            assert state.action_queues[owner].empty() and physical_tiles(state)==before
        await handle_action(server,'owner',request,socket)
        await handle_action(server,'owner',request,socket)
        assert state.action_queues[owner].qsize()==1
        assert physical_tiles(state)==before
    asyncio.run(run())


@pytest.mark.parametrize('profile',PROFILES)
def test_private_wait_previews_match_authoritative_baseline(profile):
    state = make_state(profile)
    state.tips = True
    open_round(state)
    hand = [11,12,13,21,22,23,31,32,33,41,41,41,45]
    if profile==PROFILES[2]:
        hand += [14,15,16]
    p = state.player_list[2]
    p.hand_tiles = hand
    p.combination_tiles=[]
    p.huapai_list=[]
    hints = private_hints(state,2)
    key = ','.join(map(str,sorted(hand)))
    assert any(h['tile']==45 for h in hints[key])
    assert state.build_game_start_payload(2)['game_info']['hongkong_waits']==hints
    assert key not in state.build_game_start_payload(1)['game_info']['hongkong_waits']
    state.tips = False
    assert not private_hints(state,2)


def test_ready_reconnect_and_observer_restore_rule_metadata_and_never_other_hands():
    async def run():
        state = make_state(PROFILES[2])
        open_round(state)
        state.end_draw()
        state.apply_deferred_score_changes()
        state.machine.transition(P.READY)
        state.waiting_players_list = [0,1,2,3]
        state.action_dict = {i:['ready'] for i in range(4)}
        state.send_payload_to_player = AsyncMock()
        state.player_list[1].tag_list.append('offline')
        await state.player_reconnect(state.player_list[1].user_id)
        payloads = [c.args[1] for c in state.send_payload_to_player.await_args_list]
        assert payloads[-1]['ready_status_info']['hongkong_info']['rule_version']==state.rules.version
        assert 'offline' not in state.player_list[1].tag_list
        view = payloads[0]['game_info']['players_info']
        assert all(p['hand_tiles'] is None for i,p in enumerate(view) if i!=1)
        socket = SimpleNamespace(send_json=AsyncMock())
        state.game_server = SimpleNamespace(user_id_to_connection={999:SimpleNamespace(websocket=socket)})
        await state.send_realtime_spectator_snapshot(999,1)
        assert [c.args[0] for c in socket.send_json.await_args_list]==payloads
    asyncio.run(run())


@pytest.mark.parametrize('profile',PROFILES)
def test_room_creation_uses_hongkong_and_preserves_only_public_settings(profile):
    async def run():
        connection = SimpleNamespace(user_id=101,username='test',current_room_id=None)
        manager = SimpleNamespace(game_server=SimpleNamespace(players={'c':connection},db_manager=SimpleNamespace(
            get_user_settings=lambda _:{'username':'test','private_setting':'not public'})),
            rooms={},room_passwords={},_reject_room_entry_conflicts=lambda *_:None,
            _normalize_event_id=lambda _:None,_validate_event_for_room=lambda *_:None,
            _apply_event_fields=lambda *_:None,_generate_room_id=lambda:'123456',_broadcast_room_info=AsyncMock())
        result = await create_hongkong_room(manager,'c',room_name='港麻',gameround=1,sub_rule=profile)
        assert result.success
        room = manager.rooms['123456']
        assert room['room_rule']=='hongkong' and room['sub_rule']==profile
        assert 'private_setting' not in room['player_settings'][101]
        assert connection.current_room_id=='123456'
    asyncio.run(run())
