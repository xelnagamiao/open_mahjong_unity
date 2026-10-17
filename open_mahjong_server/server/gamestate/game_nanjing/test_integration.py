import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock
import pytest
from .test_state import state,turn,act,PLAIN
from .get_action import handle_action
from .timing import ActionClock
from ...room.nanjing_room import NanjingRoomValidator
from ..gamestate_manager import GameStateManager


def test_room_profile_and_running_indexes():
    room=NanjingRoomValidator(room_name='南京测试',round_timer=20,step_timer=5)
    assert room.sub_rule=='nanjing/mil2024' and room.use_flowers
    manager=GameStateManager(SimpleNamespace())
    assert manager._rule_room_indexes()['nanjing'] is manager.room_id_to_NanjingGameState


@pytest.mark.parametrize('bad',[{'game_round':True},{'step_timer':'5'},{'round_timer':-1},{'sub_rule':'nanjing/standard'},
    {'use_flowers':False},{'tactical_call':True},{'claim_protection':True},{'open_cuohe':True},{'detailed_config':{'seven_pairs':False}}])
def test_room_rejects_unsupported_values(bad):
    with pytest.raises(ValueError):NanjingRoomValidator(**{'room_name':'南京测试','round_timer':20,'step_timer':5,**bad})


def test_room_reaction_clock_reconnect_expiry_and_bank_consumption():
    async def check():
        s=turn();s.step_time=5;s.player_list[1].hand_tiles=PLAIN[:-1];s.player_list[1].remaining_time=20
        act(s,action_type='cut',TileId=22,cutClass=True)
        s._timing_now=lambda:100
        clock=s.action_clocks[1];clock.start(100)
        s._timing_now=lambda:101.5
        info=s.build_pending_action_payload(1)['ask_other_action_info']
        assert info['remaining_time']==20 and info['step_remaining']==4
        s._timing_now=lambda:125
        with pytest.raises(ValueError,match='耗尽'):await s.submit_action(1,'hu')
        s._timing_now=lambda:107
        await s.submit_action(1,'hu')
        for i in list(s.waiting_players_list):
            if i!=1:await s.submit_action(i,'pass')
        responses=await s.wait_action()
        assert s.player_list[1].remaining_time==18 and responses[1]['action_type']=='hu'
    asyncio.run(check())


def test_bank_clock_parts_only_round_total_once():
    c=ActionClock(2.4,1);c.start(100);c.start(200)
    assert c.deadline==103.4 and c.remaining_bank(101.5)==pytest.approx(1.9)
    assert c.parts(101.5)==(2,0)


def test_unity_integer_clock_fields_preserve_fractional_server_budget():
    s=turn()
    for p in s.player_list:p.remaining_time=19.25
    for viewer in range(4):
        start=s.build_game_start_payload(viewer)
        assert all(type(p['remaining_time']) is int and p['remaining_time']==20 for p in start['game_info']['players_info'])
        pending=s.build_pending_action_payload(viewer)
        assert type(pending['ask_hand_action_info']['remaining_time']) is int
    assert all(p.remaining_time==19.25 for p in s.player_list)


def test_authenticated_router_stale_wrong_user_and_valid_action():
    async def run():
        s=turn();ws=SimpleNamespace(send_json=AsyncMock())
        server=SimpleNamespace(gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _:s),players={'self':SimpleNamespace(user_id=101),'other':SimpleNamespace(user_id=999)})
        msg=dict(type='gamestate/nanjing/cut_tile',gamestate_id=s.gamestate_id,action_tick=s.server_action_tick,TileId=22,cutClass=True)
        await handle_action(server,'other',msg,ws)
        assert s.action_queues[0].empty()
        await handle_action(server,'self',{**msg,'action_tick':s.server_action_tick-1},ws)
        assert s.action_queues[0].empty() and ws.send_json.await_count==1
        await handle_action(server,'self',msg,ws)
        assert not s.action_queues[0].empty()
    asyncio.run(run())


def test_ready_phase_clears_expired_action_clocks_and_old_responses():
    async def run():
        s=turn();s.end_draw()
        s.action_clocks[0].start(1)
        s._timing_now=lambda:100
        await s.action_queues[0].put(dict(action_type='cut',TileId=22))
        observed=[]
        async def deliver():
            if s.waiting_players_list:
                assert not s.action_clocks and all(q.empty() for q in s.action_queues.values())
                for i in list(s.waiting_players_list):
                    await s.submit_action(i,'ready')
                observed.append(s.server_action_tick)
        s.flush_outbound_payloads=deliver
        await s.run_round_ready_phase(timeout=.01)
        assert len(observed)==1 and not s.waiting_players_list
        assert not any(s.action_dict.values())
    asyncio.run(run())
