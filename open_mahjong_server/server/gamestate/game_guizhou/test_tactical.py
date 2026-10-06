"""Guizhou tactical claims: room switches, ordinary clocks and legal interrupts."""
import asyncio
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from ...room.guizhou_room import GuizhouRoomValidator, apply_bot_tactical_policy, create_guizhou_room
from ...room.room_manager import RoomManager
from ..public.tactical_claim import tactical_force_pass_is_live
from .GuizhouGameState import GuizhouGameState
from .get_action import handle_action
from .state_machine import Phase as P
from .test_contracts import fake_room_manager
from .test_flow import configured_turn
from .test_timing_contract import timer, connect, begin
from .actions import claim_actions
from .test_contracts import attach_record, ticks
from .test_flow import physical
from ...game_calculation.guizhou.rules import TILES
from collections import Counter


def claims(actions=None, *, bank=20, step=5, enabled=True):
    s = configured_turn()
    s.tactical_call, s.tactical_pre_grace_delay = enabled, 0
    s.round_time, s.step_time = bank, step
    for p in s.player_list:
        p.remaining_time = bank
    s.player_list[0].discard_tiles.append(19)
    s.player_list[0].discard_origin_tiles.append(19)
    s.player_list[0].discard_riichi_flags.append(False)
    s.outbound_payloads.clear()
    s.outbound_send_cursor = 0
    s.open_action_window(s._window(P.RESPONSE, 0, 19, actions or
        {0: [], 1: ["peng", "pass"], 2: ["hu", "pass"], 3: []}))
    return s


@pytest.mark.parametrize('enabled', [False, True])
def test_room_explicit_switch_saved_and_new_room_default(enabled):
    config = dict(room_name='test', game_round=1, round_timer=20, step_timer=5)
    assert GuizhouRoomValidator(**config).tactical_call is True
    assert GuizhouRoomValidator(**config, tactical_call=enabled).tactical_call is enabled
    async def run():
        manager, _ = fake_room_manager()
        result = await create_guizhou_room(manager, 'c', room_name='test', tactical_call=enabled)
        assert result.success and manager.rooms[123456]['tactical_call'] is enabled
    asyncio.run(run())


@pytest.mark.parametrize('value', ['true', 1, None, [], {}])
def test_room_switch_is_strict(value):
    with pytest.raises(ValueError):
        GuizhouRoomValidator(room_name='test', game_round=1, round_timer=20, step_timer=5, tactical_call=value)


@pytest.mark.parametrize('users,expected', [([101,102,103,104], True), ([101,0,103,104], False),
    ([101,2,3,104], False), ([101,10,103,104], False)])
def test_startup_is_authoritative_and_old_false_not_reenabled(users, expected):
    room = GuizhouGameState._default_room_data()
    room.update(player_list=users, tactical_call=True)
    s = GuizhouGameState(room_data=room, calculation_service=object())
    assert s.tactical_call is expected
    assert s.build_game_start_payload(0)['game_info']['tactical_call'] is expected
    assert s.tactical_commit_lock is False
    room['tactical_call'] = False
    assert GuizhouGameState(room_data=room, calculation_service=object()).tactical_call is False
    room.pop('tactical_call')
    assert GuizhouGameState(room_data=room, calculation_service=object()).tactical_call is False


def test_lobby_bot_policy_empty_seats_spectators_and_sticky_off():
    room = dict(room_rule='guizhou', player_list=[101], tactical_call=True,
                seat_list=[101,-1,-1,-1], spectator_list=[0], player_settings={0: {}, 101: {}})
    manager = RoomManager.__new__(RoomManager)
    manager._sync_room_host(room)
    assert room['tactical_call'] is True
    room['player_list'].append(2)
    manager._sync_room_host(room)
    assert room['tactical_call'] is False
    room['player_list'].remove(2)
    manager._sync_room_host(room)
    assert room['tactical_call'] is False
    for rule in ['guobiao', 'shanghai', 'hangzhou', 'tuidao']:
        other = dict(room_rule=rule, player_list=[101,2], tactical_call=True)
        apply_bot_tactical_policy(other)
        assert other['tactical_call'] is True


@pytest.mark.parametrize('bank,step', [(20,5),(7,11),(0,8),(0,0)])
@pytest.mark.parametrize('enabled', [False,True])
def test_tactical_option_never_shortens_primary_claim(timer, bank, step, enabled):
    s = claims(bank=bank, step=step, enabled=enabled)
    for i in (1,2):
        c = begin(s, i)
        payload = s.build_pending_action_payload(i)
        info = payload['ask_other_action_info']
        assert c.remaining() == bank+step
        assert info['remaining_time'] == bank and info['step_remaining'] == step
        assert info['is_tactical_recheck'] is False
        assert ('force_pass' in info['action_list']) is enabled
    assert s.tactical_grace_seconds == 5


@pytest.mark.parametrize('reply', ['pass','force_pass','hu'])
@pytest.mark.parametrize('bank,step', [(20,5),(7,11),(0,8)])
def test_valid_application_independent_five_and_no_bank_charge(timer, reply, bank, step):
    async def run():
        s = claims(bank=bank, step=step)
        for i in (1,2): begin(s,i)
        timer.advance(step + min(bank, 2) if bank else max(0, step-.25))
        await s.submit_action(1, 'peng')
        paused_bank = max(0,bank-min(bank,2))
        sockets = connect(s,timer)
        original = sockets[2].send_json
        async def respond(payload):
            await original(payload)
            info = payload.get('ask_other_action_info')
            if info and info.get('is_tactical_recheck'):
                precise = payload['game_info']['guizhou_info']['action_clock']
                assert (info['remaining_time'],info['step_remaining']) == (5,0)
                assert precise['remaining_time'] == 5 and precise['step_remaining'] == 0
                assert s.player_list[2].remaining_time == pytest.approx(paused_bank)
                timer.advance(2)
                await s.submit_action(2,reply)
                timer.advance(4)  # Collector, other views and animation are free.
        sockets[2].send_json = respond
        responses = await s.wait_action()
        assert responses[1]['action_type'] == 'peng'
        assert responses[2]['action_type'] == reply
        assert s.player_list[1].remaining_time == pytest.approx(paused_bank)
        assert s.player_list[2].remaining_time == pytest.approx(paused_bank)
        assert not s.tactical_commit_lock
        assert s.live_pending_window['action_tick'] == s.server_action_tick
    asyncio.run(run())


def test_main_pass_may_recompete_but_force_pass_exits_discard(timer):
    async def run(action):
        s = claims()
        for i in (1,2): begin(s,i)
        await s.submit_action(2,action)
        await s.submit_action(1,'peng')
        sockets = connect(s,timer)
        original = sockets[2].send_json
        async def send(payload):
            await original(payload)
            if payload.get('ask_other_action_info',{}).get('is_tactical_recheck'):
                await s.submit_action(2,'hu')
        sockets[2].send_json = send
        responses = await s.wait_action()
        asks = [p for _,p in sockets[2].sent if p.get('ask_other_action_info')]
        assert bool(asks) is (action=='pass')
        assert responses[2]['action_type'] == ('hu' if action=='pass' else 'force_pass')
    asyncio.run(run('pass'))
    asyncio.run(run('force_pass'))


def test_multiple_ron_and_applicant_higher_amendment_without_commit_lock(timer):
    async def run():
        s = claims({0: [],1: ['peng','hu','pass'],2: ['hu','pass'],3: ['hu','pass']})
        for i in (1,2,3): begin(s,i)
        await s.submit_action(1,'peng')
        sockets = connect(s,timer)
        asks = []
        for i in (1,2,3):
            original = sockets[i].send_json
            async def send(payload, i=i, original=original):
                await original(payload)
                info = payload.get('ask_other_action_info')
                if info:
                    asks.append((i,info['action_tick'],info['remaining_time'],info['step_remaining']))
                    timer.advance(.25)
                    await s.submit_action(i,'hu')
            sockets[i].send_json = send
        result = await s.wait_action()
        assert [result[i]['action_type'] for i in (1,2,3)] == ['hu','hu','hu']
        assert {i for i,_,_,_ in asks} == {1,2,3}
        assert all(bank<=5 and step==0 for _,_,bank,step in asks)
        assert all(p.remaining_time==20 for p in s.player_list)
        assert s._tactical_hu_announced == {1,2,3}
    asyncio.run(run())


def test_timeout_and_late_reply_do_not_empty_normal_bank(timer):
    async def run():
        s = claims()
        begin(s,1);begin(s,2)
        await s.submit_action(1,'peng')
        sockets = connect(s,timer)
        original=sockets[2].send_json
        async def send(payload):
            await original(payload)
            if payload.get('ask_other_action_info'):
                s._start_delivered_clock(2,payload)
                timer.advance(5.01)
                with pytest.raises(ValueError,match='超时'):
                    await s.submit_action(2,'hu')
        sockets[2].send_json=send
        result=await s.wait_action()
        assert result[2]['action_type']=='pass'
        assert s.player_list[2].remaining_time==20
    asyncio.run(run())


def test_reconnect_repeat_stale_packet_and_spectator_preserve_grace_deadline(timer):
    async def run():
        s=claims()
        for i in (1,2):begin(s,i)
        opening=s.server_action_tick
        s.pause_normal_claim_clocks()
        s.action_dict={0:[],1:[],2:['hu','pass','force_pass'],3:[]}
        s.waiting_players_list=[2]
        sockets=connect(s,timer)
        await s._broadcast_tactical_recheck()
        c=s.action_clock(2);started=c.started
        timer.advance(1.25)
        await s.player_reconnect(s.player_list[2].user_id)
        info=sockets[2].sent[-1][1]['ask_other_action_info']
        assert (info['remaining_time'],info['step_remaining'])==(4,0)
        assert c.started==started and c.remaining()==pytest.approx(3.75)
        connection=SimpleNamespace(user_id=s.player_list[2].user_id)
        s.game_server.players={'c':connection}
        s.game_server.gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _:s)
        await handle_action(s.game_server,'c',dict(action='hu',action_tick=opening),sockets[2])
        assert s.action_queues[2].empty() and c.started==started
        observer=SimpleNamespace(send_json=AsyncMock())
        s.game_server.user_id_to_connection[999]=SimpleNamespace(websocket=observer)
        await s.send_realtime_spectator_snapshot(999,2)
        assert c.started==started and s.player_list[2].remaining_time==20
        assert tactical_force_pass_is_live(s,2,opening)
        await handle_action(s.game_server,'c',dict(action='force_pass',action_tick=opening),sockets[2])
        assert s.action_queues[2].get_nowait()['action_type']=='force_pass'
        assert s.player_list[2].remaining_time==20
        assert not tactical_force_pass_is_live(s,2,opening-1)
    asyncio.run(run())


def test_rob_kong_mandatory_hu_does_not_offer_declines_or_override_time(timer):
    s=claims({0:[],1:['hu'],2:[],3:[]})
    assert s.action_dict[1]==['hu']
    assert s.build_pending_action_payload(1)['ask_other_action_info']['step_remaining']==5
    async def run():
        begin(s,1);timer.advance(25.01)
        responses=await s.wait_action()
        assert responses[1]['action_type']=='hu'
        assert s.player_list[1].remaining_time==0
    asyncio.run(run())


def test_slow_later_recipient_cannot_extend_earlier_grace(timer):
    async def run():
        s=claims({0:[],1:['peng','pass'],2:['hu','pass'],3:['hu','pass']})
        for i in (1,2,3):begin(s,i)
        s.pause_normal_claim_clocks()
        s.action_dict={0:[],1:[],2:['hu','pass','force_pass'],3:['hu','pass','force_pass']}
        s.waiting_players_list=[2,3]
        sockets=connect(s,timer,(0,0,0,6))
        await s._broadcast_tactical_recheck()
        assert s.action_clock(2).remaining()==0
        assert s.action_clock(3).remaining()==5
        with pytest.raises(ValueError,match='超时'):
            await s.submit_action(2,'hu')
        await s.submit_action(3,'hu')
        submitted={}
        await s.collect_tactical_recheck(2,submitted)
        assert submitted=={2:'pass',3:'hu'}
        assert [p.remaining_time for p in s.player_list]==[20]*4
    asyncio.run(run())


def actual_claims():
    s=claims()
    for p in s.player_list:
        p.combination_tiles=[];p.combination_mask=[];p.has_draw_slot=False
        p.discard_tiles=[];p.discard_riichi_flags=[];p.won_tiles=[]
    s.player_list[1].hand_tiles=[11]*3+[12]*3+[13]*3+[17,18,19,19]
    s.player_list[2].hand_tiles=[17,18]+[21]*3+[22]*3+[29,29]
    s.player_list[3].hand_tiles=[17,18]+[24]*3+[25]*3+[27,27]
    for i,tile in ((2,31),(3,32)):
        s.player_list[i].combination_tiles=[f'G{tile}']
        s.player_list[i].combination_mask=[[2,tile,0,tile,0,tile,2,tile]]
    s.player_list[0].hand_tiles=[]
    s.player_list[0].discard_tiles=[19]
    s.player_list[0].discard_riichi_flags=[False]
    s.discard_log=[(0,19)]
    all_tiles=Counter({t:4 for t in TILES})
    held=physical(s)-Counter(s.tiles_list)
    assert not held-all_tiles
    rest=list((all_tiles-held).elements())
    s.player_list[0].hand_tiles=rest[:13]
    s.tiles_list=rest[13:]
    attach_record(s)
    s.open_action_window(s._window(P.RESPONSE,0,19,claim_actions(s,0,19)))
    assert 'peng' in s.action_dict[1] and 'hu' in s.action_dict[1]
    assert all('hu' in s.action_dict[i] for i in (1,2,3))
    assert physical(s)==all_tiles
    return s


def test_actual_multi_ron_amendment_preserves_tiles_scores_sounds_and_replay(timer):
    async def run():
        s=actual_claims();expected=physical(s)
        for i in (1,2,3):begin(s,i)
        await s.submit_action(1,'peng')
        sockets=connect(s,timer)
        for i in (1,2,3):
            original=sockets[i].send_json
            async def send(payload,i=i,original=original):
                await original(payload)
                if payload.get('ask_other_action_info'):
                    await s.submit_action(i,'hu')
            sockets[i].send_json=send
        window=s.live_pending_window
        result=await s.wait_action()
        assert physical(s)==expected  # Claims have no physical meld effects.
        s.apply_action_results(window,result)
        assert s.machine.phase==P.END and physical(s)==expected
        assert len(s.deferred_hu_settlements)==3
        assert sum(s.round_settlement.changes)==0
        assert s.player_list[1].combination_tiles==[]
        for viewer in range(4):
            claims_sent=[p['do_action_info'] for _,p in sockets[viewer].sent if p.get('do_action_info',{}).get('is_claim')]
            assert [(p['action_player'],p['action_list'][0]) for p in claims_sent].count((1,'peng'))==1
            assert sorted(p['action_player'] for p in claims_sent if p['action_list']==['hu'])==[1,2,3]
            panels=s.final_settlement_payloads(viewer)
            assert len(panels)==3 and all(p['show_result_info']['silent'] for p in panels)
        applications=[t for t in ticks(s) if t[0]=='ca']
        assert applications==[['ca',1,'p',19]]
    asyncio.run(run())


def test_prequeued_higher_hu_still_allows_lower_applicant_upgrade(timer):
    async def run():
        s=claims({0:[],1:['peng','hu','pass'],2:['hu','pass'],3:[]})
        for i in (1,2):begin(s,i)
        await s.submit_action(1,'peng')
        sockets=connect(s,timer)
        sent=False
        for i in (1,2):
            original=sockets[i].send_json
            async def send(payload,i=i,original=original):
                nonlocal sent
                await original(payload)
                if i==1 and payload.get('do_action_info',{}).get('is_claim') and not sent:
                    sent=True
                    await s.submit_action(2,'hu')
                elif i==1 and payload.get('ask_other_action_info'):
                    await s.submit_action(1,'hu')
            sockets[i].send_json=send
        result=await s.wait_action()
        assert result[1]['action_type']==result[2]['action_type']=='hu'
        assert s.player_list[1].remaining_time==s.player_list[2].remaining_time==20
    asyncio.run(run())


def test_tactical_off_keeps_ordinary_collection_and_new_post_pung_budget(timer):
    async def run():
        s=claims(enabled=False,bank=7,step=11)
        s.player_list[1].hand_tiles=[19,19,11,12,14,16,18,21,24,27,33,35,39]
        s.player_list[2].hand_tiles=[11]*3+[12]*3+[13]*3+[17,18,19,19]
        begin(s,1);begin(s,2)
        timer.advance(12.5)
        await s.submit_action(1,'peng');await s.submit_action(2,'pass')
        result=await s.wait_action()
        window=s.live_pending_window
        assert not s._tactical_announced and result[2]['action_type']=='pass'
        s.apply_action_results(window,result)
        assert s.machine.phase==P.DISCARD_ONLY
        assert s.action_clock(1).remaining()==pytest.approx(16.5)
        assert s.player_list[1].remaining_time==pytest.approx(5.5)
    asyncio.run(run())


def test_mandatory_hu_timeout_in_actual_recheck_preserves_hand_bank(timer):
    async def run():
        s=claims({0:[],1:['peng','pass'],2:['hu'],3:[]})
        begin(s,1);begin(s,2)
        await s.submit_action(1,'peng')
        sockets=connect(s,timer)
        original=sockets[2].send_json
        async def send(payload):
            await original(payload)
            if payload.get('ask_other_action_info'):
                assert payload['ask_other_action_info']['action_list']==['hu']
                s._start_delivered_clock(2,payload)
                timer.advance(5.01)
        sockets[2].send_json=send
        responses=await s.wait_action()
        assert responses[2]['action_type']=='hu'
        assert s.player_list[2].remaining_time==20
    asyncio.run(run())


def test_first_highest_hu_does_not_shorten_other_normal_multi_ron_asks(timer):
    async def run():
        s=claims({0:[],1:['hu','pass'],2:['hu','pass'],3:[]},bank=7,step=11)
        begin(s,1);begin(s,2)
        await s.submit_action(1,'hu')
        task=asyncio.create_task(s.wait_action())
        await asyncio.sleep(0)
        assert not task.done() and not s._tactical_recheck
        timer.advance(13)
        await s.submit_action(2,'hu')
        responses=await task
        assert responses[1]['action_type']==responses[2]['action_type']=='hu'
        assert s.player_list[1].remaining_time==7
        assert s.player_list[2].remaining_time==5
        assert not s._tactical_clocks
    asyncio.run(run())


def test_late_force_pass_cannot_cancel_an_already_accepted_hu(timer):
    async def run():
        s=claims()
        begin(s,2)
        await s.submit_action(2,'hu')
        s.action_queues[2].get_nowait()
        s.waiting_players_list.remove(2);s.action_dict[2]=[]
        with pytest.raises(ValueError,match='和牌不能'):
            await s.submit_tactical_force_pass(2,s.server_action_tick)
        assert 2 not in s._tactical_force_passed_players
    asyncio.run(run())
