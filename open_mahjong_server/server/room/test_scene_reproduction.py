"""Direct seed reproduction is independent of persisted duplicate-wall keys."""
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock
import pytest
from .test_sichuan_duplicate import rooms
from ..gamestate.game_sichuan.test_duplicate import run_async
from ..gamestate.game_sichuan.SichuanGameState import SichuanGameState
from ..gamestate.game_sichuan.XueliuGameState import XueliuGameState
from ..gamestate.game_sichuan.XueliuDoublesGameState import XueliuDoublesGameState
from ..gamestate.public.spectator_rules import deliver_realtime_spectator_message

SEED = 'abcdef0123456789' * 4

@pytest.mark.parametrize('sub_rule,cls', [('sichuan/standard',SichuanGameState),('sichuan/doubles',SichuanGameState),('sichuan/xueliu',XueliuGameState),('sichuan/xueliu_exchange',XueliuGameState),('sichuan/xueliu_doubles',XueliuDoublesGameState)])
@pytest.mark.parametrize('allowed', [True,False])
@pytest.mark.parametrize('players', [[101,102,103,104],[101,1,2,3]])
@run_async
async def test_ordinary_fixed_seed_respects_spectator_option_and_ai_limit(rooms,sub_rule,cls,allowed,players):
    result = await rooms.create_empty_event_room('evt_pair','sichuan', dict(sub_rule=sub_rule,random_seed=SEED,allow_spectator=allowed),created_by=101)
    assert result.success,result.message
    room=result.room_info
    assert not room.get('duplicate_key') and room['is_player_set_random_seed']
    assert room['allow_spectator'] is allowed
    assert not rooms.game_server.gamestate_manager.sichuan_duplicate_matches
    state=cls(rooms.game_server,dict(room,player_list=players,team_by_seat=[0,1,0,1]),None,rooms.game_server.db_manager,'scene-test')
    expected=allowed and all(uid>10 for uid in players)
    assert state.spectator_enabled is expected and state.spectator_manager.enabled is expected

@pytest.mark.parametrize('sub_rule', ['sichuan/doubles_duplicate','sichuan/xueliu_doubles_duplicate'])
@run_async
async def test_formal_pairs_reject_scene_reproduction_before_creating_rooms(rooms,sub_rule):
    response=await rooms.create_empty_event_room('evt_pair','sichuan',dict(sub_rule=sub_rule,random_seed=SEED,duplicate_round_count=4),created_by=101)
    assert not response.success and '场景复现' in response.message
    response=await rooms.create_empty_event_room('evt_pair','sichuan',dict(sub_rule=sub_rule,random_seed=SEED,duplicate_key='dup_'+'a'*32),created_by=101)
    assert not response.success
    assert not rooms.rooms and not rooms.game_server.gamestate_manager.sichuan_duplicate_matches

@run_async
async def test_scene_reproduction_can_stream_realtime_while_key_matches_cannot():
    socket=NS(send_json=AsyncMock())
    state=NS(duplicate_key=None,realtime_spectators=[NS(user_id=201,host_user_id=101)],
             player_list=[NS(user_id=101,player_index=0)],
             game_server=NS(user_id_to_connection={201:NS(websocket=socket)}))
    await deliver_realtime_spectator_message(state,0,{'type':'scene-event','success':True})
    socket.send_json.assert_awaited_once()
    state.duplicate_key='dup_'+'a'*32
    await deliver_realtime_spectator_message(state,0,{'type':'key-event','success':True})
    socket.send_json.assert_awaited_once()

@pytest.mark.parametrize('rule',['guobiao','riichi','qingque','classical','sichuan','taiwan','hongkong','shanxi','guangdong','hongque','changsha','guizhou','yixing','malaysia','nanjing','fuzhou','wenzhou','nanchang','shangrao','beijing','duocai','hangzhou','hongzhong','jiujiang','singapore','changchun','shenyang','wuhan'])
@run_async
async def test_event_factory_preserves_hex_seed_and_independent_hint_switches(rooms,rule):
    response=await rooms.create_empty_event_room('evt_pair',rule,dict(random_seed=SEED,count_tips=True,pointer_tips=False),broadcast=False)
    assert response.success,response.message
    room=response.room_info
    assert room['random_seed']==int(SEED,16) and room['is_player_set_random_seed']
    assert room['count_tips'] and not room['pointer_tips']

@run_async
async def test_event_guobiao_blood_battle_uses_the_same_fixed_options_as_personal_rooms(rooms):
    response=await rooms.create_empty_event_room('evt_pair','guobiao',dict(sub_rule='guobiao/blood_battle',hepai_limit=16,open_cuohe=True,tactical_call=True,random_seed=SEED),broadcast=False)
    assert response.success,response.message
    assert response.room_info['hepai_limit']==8
    assert not response.room_info['open_cuohe'] and not response.room_info['tactical_call']
