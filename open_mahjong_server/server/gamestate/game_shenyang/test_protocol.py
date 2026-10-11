import asyncio
from types import SimpleNamespace

import pytest
from unittest.mock import AsyncMock

from .test_state import make_state,hand
from ...room.shenyang_room import ShenyangRoomValidator
from ...room.shenyang_room import handle_create_shenyang_room
from ...gamestate.gamestate_router import handle_gamestate_message


@pytest.mark.parametrize('field', ['use_flowers','open_cuohe','tactical_call','claim_protection','tian_di_ren_he'])
def test_fixed_mil_room_rejects_other_rule_features(field):
    with pytest.raises(ValueError):
        ShenyangRoomValidator(room_name='沈阳',game_round=1,round_timer=20,step_timer=5,**{field:True})


@pytest.mark.parametrize('config',[{'sub_rule':'shenyang/unknown'},{'detailed_config':{'minimum_fan':8}},
                                  {'detailed_config':{'rule_version':'other'}}])
def test_room_rejects_unknown_editions(config):
    with pytest.raises(ValueError):
        ShenyangRoomValidator(room_name='沈阳',game_round=1,round_timer=20,step_timer=5,**config)


def test_snapshot_hides_other_hands_ankans_tail_and_egg():
    s=make_state()
    hand(s,0,[11,12,13,21,22,23,31,32,33,19,19],('G45',),drawn=19)
    s.egg_offer=31;s.final_four=True;s.tail_tiles={0:27,1:38}
    for viewer in range(4):
        data=s.view(viewer)
        assert data['egg_offer']==(31 if viewer==0 else None)
        assert data['tail_tiles'][0]==(27 if viewer==0 else 0)
        assert all(p['hand'] is None for p in data['players'] if p['player']!=viewer)
        assert type(data['self_last_drawn_tile']) is int
        assert all(type(p['last_drawn_tile']) is int for p in data['players'])
        if viewer!=0:
            assert data['players'][0]['melds']!=['G45']
            assert 45 not in data['players'][0]['masks'][0]


def test_route_checks_rule_identity_tick_and_connection_before_enqueue():
    s=make_state();p=hand(s,0,[45,46,47]+[11]*11,drawn=11)
    connection=SimpleNamespace(user_id=p.user_id)
    s.game_server.players={'local':connection}
    s.game_server.gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda gid:s)
    s.game_status='waiting_hand_action';s.server_action_tick=8;s.action_dict={0:['sy_xi'],1:[],2:[],3:[]}
    s.on_action_window_broadcast();s.on_action_window_delivered(0)
    msg=dict(type='gamestate/shenyang/send_action',gamestate_id=s.gamestate_id,action='sy_xi',action_tick=7)
    async def run():
        await handle_gamestate_message(s.game_server,'missing',msg,None)
        await handle_gamestate_message(s.game_server,'local',msg,None)
        assert s.action_queues[0].empty()
        msg['action_tick']=8;s.room_rule='guobiao'
        await handle_gamestate_message(s.game_server,'local',msg,None)
        assert s.action_queues[0].empty()
        s.room_rule='shenyang'
        await handle_gamestate_message(s.game_server,'local',msg,None)
        assert s.action_queues[0].qsize()==1
        await handle_gamestate_message(s.game_server,'local',msg,None)
        assert s.action_queues[0].qsize()==1
    asyncio.run(run())


def test_initial_replay_contains_each_seats_hints_without_duplicate_ticks():
    s=make_state();s.master_seed=2025;s.init_tiles()
    async def run():
        await s.prepare_private_fields(range(4))
        await s.prepare_private_fields(range(4))
    asyncio.run(run())
    ticks=s.game_record['game_round']['round_index_1']['action_ticks']
    hints=[tick[1] for tick in ticks if tick[0]=='sy' and tick[1]['kind']=='hints']
    assert sorted(item['player'] for item in hints)==list(range(4))
    for item in hints:
        assert item['hints']['source_hand_tiles']==s.player_list[item['player']].hand_tiles


@pytest.mark.parametrize('field', ['use_flowers','open_cuohe','tactical_call','claim_protection','tian_di_ren_he'])
def test_room_wire_rejects_unsupported_switch_before_creating(field):
    from ...room.room_manager import RoomManager
    connection=SimpleNamespace(user_id=10000101,current_room_id=None,username='test')
    server=SimpleNamespace(players={'local':connection},gamestate_manager=SimpleNamespace(is_user_in_active_game=lambda _:False))
    server.room_manager=RoomManager(server)
    socket=SimpleNamespace(send_json=AsyncMock())
    asyncio.run(handle_create_shenyang_room(server,'local',{'roomname':'沈阳测试',field:True},socket))
    response=socket.send_json.call_args.args[0]
    assert response['success'] is False and '不支持' in response['message']
    assert not server.room_manager.rooms and connection.current_room_id is None


@pytest.mark.parametrize('viewer',range(4))
@pytest.mark.parametrize('phase',['normal','egg','tail','terminal'])
def test_reconnect_restores_each_perspective_and_terminal_result(viewer,phase):
    from ...game_calculation.shenyang.test_rules import READY
    s=make_state();s.current_round=4
    for index in range(4):hand(s,index,READY)
    hand(s,0,READY+[19],drawn=19)
    for p in s.player_list:
        s.game_server.user_id_to_connection[p.user_id]=SimpleNamespace(websocket=SimpleNamespace(send_json=AsyncMock()))
    socket=s.game_server.user_id_to_connection[s.player_list[viewer].user_id].websocket
    s.game_status='waiting_hand_action'
    if phase=='egg':s.egg_offer=31
    if phase=='tail':s.final_four=True;s.tail_tiles={1:28}
    s.action_dict=s.check_hand_actions(0)
    async def run():
        if phase=='terminal':
            s.accept_self_draw(0)
            await s._settle_hand(dict.fromkeys(range(4),0))
            socket.send_json.reset_mock()
        await s.player_reconnect(s.player_list[viewer].user_id)
    asyncio.run(run())
    packets=[call.args[0] for call in socket.send_json.call_args_list]
    start=next(packet['game_info'] for packet in packets if packet['type']=='gamestate/shenyang/game_start')
    assert start['sub_rule']=='shenyang/mil2025'
    assert start['shenyang']['self_last_drawn_tile']==(19 if viewer==0 else 0)
    assert all('hand_tiles' not in p for p in start['players_info'] if p['player_index']!=viewer)
    if phase=='egg':assert start['shenyang'].get('egg_offer')==(31 if viewer==0 else None)
    if phase=='tail':assert start['shenyang']['tail_tiles'][1]==(28 if viewer==1 else 0)
    if phase=='terminal':
        result=next(packet['show_result_info'] for packet in packets if packet['type']=='gamestate/shenyang/show_result')
        assert result['silent'] is True and len(result['revealed_hands'])==4
        assert sum(result['shenyang']['round_score_changes'].values())==0


@pytest.mark.parametrize('viewer',range(4))
def test_realtime_spectator_uses_selected_seat_private_hints_and_tail(viewer):
    from ...game_calculation.shenyang.test_rules import READY
    s=make_state()
    for index in range(4):hand(s,index,READY)
    s.final_four=True;s.tail_tiles={0:28,1:29}
    socket=SimpleNamespace(send_json=AsyncMock())
    s.game_server.user_id_to_connection[10000999]=SimpleNamespace(websocket=socket)
    asyncio.run(s.send_realtime_spectator_snapshot(10000999,viewer))
    info=socket.send_json.call_args_list[0].args[0]['game_info']
    assert info['view_player_index']==viewer
    assert [p['player_index'] for p in info['players_info'] if p.get('hand_tiles') is not None]==[viewer]
    assert info['shenyang']['hints']['source_hand_tiles']==READY
    assert all(tile==(s.tail_tiles[index] if viewer==index else 0) for index,tile in info['shenyang']['tail_tiles'].items())
