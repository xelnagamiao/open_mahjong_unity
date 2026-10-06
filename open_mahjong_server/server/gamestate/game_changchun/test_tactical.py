"""Tactical feature regressions with real queues, priority engine and controlled time."""
import asyncio
import time
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .test_state import make_state, hand, ticks
from .test_timing import ask_state, collect_at
from .test_integration import room_manager
from ..public.ask_timing import begin_ask_round, note_ask_delivered
from ..public.ai.get_action import get_action
from ..public.tactical_claim import get_higher_priority_snapshot
from ..game_taiwan import boardcast
from ...room.changchun_room import (
    ChangchunRoomValidator, create_changchun_room, enforce_changchun_tactical_room,
    handle_create_changchun_room,
)
from ...room.room_manager import RoomManager


def tactical_state(bank=20, step=5, *, enabled=True, status='waiting_action_after_cut'):
    state = ask_state(bank, step, status, actions=('chi_mid', 'pass'))
    state.tactical_call = enabled
    state.tactical_pre_grace_delay = 0
    state.action_dict[2] = ['peng', 'pass']
    return state


async def collect_claims(state):
    return await state._collect_changchun_claims()


@pytest.mark.parametrize('enabled', [True, False])
def test_room_switch_roundtrip_from_validator_handler_saved_room_and_state(enabled):
    async def run():
        config = ChangchunRoomValidator(room_name='长春', round_timer=20, step_timer=5,
                                       tactical_call=enabled).model_dump()
        assert config['tactical_call'] is enabled
        assert config['claim_protection'] is False
        manager = room_manager()
        websocket = SimpleNamespace(send_json=AsyncMock())
        await handle_create_changchun_room(SimpleNamespace(room_manager=manager), 'c',
                                         {'roomname':'长春', 'tactical_call':enabled}, websocket)
        assert websocket.send_json.call_args.args[0]['success']
        assert manager.rooms['123456']['tactical_call'] is enabled
        state = make_state(tactical_call=enabled)
        assert state.tactical_call is enabled and state.tactical_commit_lock is False
        assert state.tactical_grace_seconds == 5
    asyncio.run(run())


def test_new_rooms_default_on_and_legacy_explicit_or_missing_off_stays_off():
    manager = room_manager()
    response = asyncio.run(create_changchun_room(manager, 'c', room_name='长春'))
    assert response.success and response.room_info['tactical_call'] is True
    assert not make_state(tactical_call=False).tactical_call
    assert not make_state().tactical_call


@pytest.mark.parametrize('value', [None, 0, 1, 'true', [], {}])
def test_tactical_configuration_remains_strict_boolean(value):
    with pytest.raises(ValueError):
        ChangchunRoomValidator(room_name='长春',round_timer=20,step_timer=5,tactical_call=value)


@pytest.mark.parametrize('bot_id', [0, 2, 3, 9, 10])
def test_authoritative_room_and_state_disable_for_actual_bot_seats(bot_id):
    room = dict(room_rule='changchun', tactical_call=True, player_list=[101,bot_id,102],
                seat_list=[101,bot_id,102,-1])
    enforce_changchun_tactical_room(room)
    assert room['tactical_call'] is False
    state = make_state(tactical_call=True, player_list=[101,102,103,bot_id])
    assert state.tactical_call is False


@pytest.mark.parametrize('enabled', [False, True])
def test_empty_seats_and_bot_spectators_do_not_disable_or_enable_human_room(enabled):
    room = dict(room_rule='changchun', tactical_call=enabled, player_list=[101,102],
                seat_list=[-1,101,-1,102], spectator_list=[0,2],
                realtime_spectators=[{'user_id':3}])
    enforce_changchun_tactical_room(room)
    assert room['tactical_call'] is enabled
    # Bot leaves: an automatic transition never re-enables a previously disabled room.
    room['tactical_call'] = False
    enforce_changchun_tactical_room(room)
    assert room['tactical_call'] is False


@pytest.mark.parametrize('bot_id', [0,2])
def test_adding_bot_broadcasts_disabled_state_then_keeps_disabled_after_removal(bot_id):
    async def run():
        manager = room_manager()
        await create_changchun_room(manager, 'c', room_name='长春')
        room = manager.rooms['123456']
        manager._sync_room_host = lambda _room: None
        socket = SimpleNamespace(send_json=AsyncMock())
        manager.game_server.user_id_to_connection = {101:SimpleNamespace(websocket=socket)}
        manager._broadcast_room_info = lambda rid: RoomManager._broadcast_room_info(manager,rid)
        response = await RoomManager._add_room_bot(manager,'c','123456',bot_id,1)
        assert response.success and room['tactical_call'] is False
        assert socket.send_json.call_args.args[0]['room_info']['tactical_call'] is False
        room['player_list'].remove(bot_id); room['seat_list'][1] = -1
        await manager._broadcast_room_info('123456')
        assert room['tactical_call'] is False
    asyncio.run(run())


@pytest.mark.parametrize('enabled', [False, True])
@pytest.mark.parametrize('bank,step,at,remaining', [(20,5,8,17),(47,9,13,43),(0,9,8,0),(20,0,2,18)])
def test_enabling_tactical_does_not_shorten_the_initial_ordinary_claim(enabled,bank,step,at,remaining):
    async def run():
        state = ask_state(bank,step)
        state.tactical_call = enabled
        state.tactical_pre_grace_delay = 0
        (responses,_), elapsed = await collect_at(state,[(at,1,{'action_type':'peng'})],invoke=collect_claims)
        assert elapsed == at and responses[1]['action_type'] == 'peng'
        assert state.player_list[1].remaining_time == remaining
        assert state._cc_tactical_recheck is False
    asyncio.run(run())


@pytest.mark.parametrize('bank,step', [(20,5),(47,9),(0,9),(0,0),(20,0)])
def test_tactical_on_without_valid_claim_uses_full_normal_timeout(bank,step):
    async def run():
        state = tactical_state(bank,step)
        (responses,_), elapsed = await collect_at(state,invoke=collect_claims)
        assert responses == {} and elapsed == bank+step
        assert state.player_list[1].remaining_time == state.player_list[2].remaining_time == 0
    asyncio.run(run())


@pytest.mark.parametrize('enabled', [False, True])
def test_effective_claim_is_the_transition_boundary(enabled):
    async def run():
        state = tactical_state(enabled=enabled)
        (responses,_), elapsed = await collect_at(state,[(8,1,{'action_type':'chi_mid'}),
                            (12,2,{'action_type':'peng'})],invoke=collect_claims)
        assert elapsed == 12 and responses[2]['action_type'] == 'peng'
        assert state.player_list[1].remaining_time == 17
        assert state.player_list[2].remaining_time == (17 if enabled else 13)
        assert (responses[1]['action_type'] == 'pass') is enabled
    asyncio.run(run())


@pytest.mark.parametrize('bank,step,at', [(20,5,8),(47,9,13),(0,9,2),(20,0,2)])
def test_recheck_has_five_seconds_only_and_does_not_debit_hand_bank(bank,step,at):
    async def run():
        state = tactical_state(bank,step)
        (responses,_), elapsed = await collect_at(state,[(at,1,{'action_type':'chi_mid'})],invoke=collect_claims)
        assert elapsed == at+5 and responses[1]['action_type'] == 'chi_mid'
        assert responses[2]['action_type'] == 'pass'
        expected = max(0,bank-max(0,at-step))
        assert state.player_list[1].remaining_time == state.player_list[2].remaining_time == expected
        assert not state.is_action_pending(2)
    asyncio.run(run())


@pytest.mark.parametrize('choice,elapsed,winner', [('pass',11,2),('force_pass',8,1)])
def test_main_pass_may_recompete_but_force_pass_exits_this_discard(choice,elapsed,winner):
    async def run():
        state = tactical_state()
        (responses,_), duration = await collect_at(state,[(1,2,{'action_type':choice}),
             (8,1,{'action_type':'chi_mid'}),(11,2,{'action_type':'peng'})],invoke=collect_claims)
        assert duration == elapsed
        assert responses[winner]['action_type'] == ('peng' if winner==2 else 'chi_mid')
        assert state.player_list[2].remaining_time == 20
    asyncio.run(run())


def test_unlocked_chain_allows_original_chow_applicant_to_upgrade_to_hu():
    async def run():
        state = tactical_state()
        state.action_dict[1].insert(0,'hu_first')
        (responses,_), duration = await collect_at(state,[(8,1,{'action_type':'chi_mid'}),
                    (10,2,{'action_type':'peng'}),(13,1,{'action_type':'hu_first'})],invoke=collect_claims)
        assert duration == 13 and responses[1]['action_type'] == 'hu_first'
        assert responses[2]['action_type'] == 'pass'
        assert state.player_list[1].remaining_time == state.player_list[2].remaining_time == 17
        assert ['ca',1,'cm',25] in ticks(state)
        assert ['ca',2,'p',25] in ticks(state)
        assert not any(t[:3]==['ca',1,'hu_first'] for t in ticks(state))
    asyncio.run(run())


def test_closer_hu_can_interrupt_farther_hu_without_losing_head_bump_priority():
    async def run():
        state = tactical_state()
        state.action_dict = {0:[],1:['hu_first','pass'],2:[],3:['hu_third','pass']}
        (responses,_), elapsed = await collect_at(state,[(2,3,{'action_type':'hu_third'}),
                                        (6,1,{'action_type':'hu_first'})],invoke=collect_claims)
        assert elapsed == 6 and responses[1]['action_type'] == 'hu_first'
        assert responses[3]['action_type'] == 'pass'
        assert state.player_list[1].remaining_time == 20
    asyncio.run(run())


@pytest.mark.parametrize('receipt', [12.999,13,13.001])
def test_recheck_arrival_deadline_includes_fractional_boundary_and_rejects_late(receipt):
    async def run():
        state = tactical_state()
        (responses,_), elapsed = await collect_at(state,[(8,1,{'action_type':'chi_mid'}),
                                    (receipt,2,{'action_type':'peng'})],invoke=collect_claims)
        assert elapsed == pytest.approx(min(receipt,13),abs=1e-9)
        assert responses[2]['action_type'] == ('peng' if receipt<13 else 'pass')
        assert state.player_list[2].remaining_time == 17
    asyncio.run(run())


def test_old_tick_in_recheck_does_not_consume_the_higher_priority_opportunity():
    async def run():
        state = tactical_state()
        (responses,_), elapsed = await collect_at(state,[(8,1,{'action_type':'chi_mid'}),
            (9,2,{'action_type':'peng','_action_tick':-1}),(11,2,{'action_type':'peng'})],invoke=collect_claims)
        assert elapsed == 11 and responses[2]['action_type'] == 'peng'
        assert state.player_list[2].remaining_time == 17
    asyncio.run(run())


def test_prequeued_validation_rejects_stale_serial_tick_unknown_and_forged_receipt():
    state = tactical_state()
    state.prepare_action_window(); begin_ask_round(state); note_ask_delivered(state,2)
    window = state.action_clock_manager.windows[2]
    valid = dict(action_type='peng',_action_tick=window.tick,
                 _cc_window_serial=state.action_clock_manager.serial,
                 _cc_received_at=window.started_at+1)
    assert state.validate_tactical_queued_action(2,valid)
    for altered in [None,{},dict(valid,action_type='cut'),dict(valid,_action_tick=-1),
                    dict(valid,_cc_window_serial=-1),dict(valid,_cc_received_at=window.deadline),
                    dict(valid,_cc_received_at=None)]:
        assert not state.validate_tactical_queued_action(2,altered)
    state.action_queues[2].put_nowait(dict(valid,_cc_received_at=-1,_cc_window_serial=-1))
    stamped = state.action_queues[2].get_nowait()
    assert stamped['_cc_received_at'] > 0 and stamped['_cc_window_serial'] != -1


def test_live_force_pass_from_normal_tick_can_end_a_tactical_recheck():
    async def run():
        state = tactical_state()
        (responses,_), elapsed = await collect_at(state,[(8,1,{'action_type':'chi_mid'}),
                      (10,2,{'action_type':'force_pass','_action_tick':0})],invoke=collect_claims)
        assert elapsed == 10 and responses[1]['action_type'] == 'chi_mid'
        assert responses[2]['action_type'] == 'force_pass'
    asyncio.run(run())


def test_recheck_wire_and_reconnect_show_five_plus_zero_without_resetting_deadline():
    async def run():
        state = tactical_state(step=9)
        socket = SimpleNamespace(send_json=AsyncMock())
        state.game_server.user_id_to_connection[102] = SimpleNamespace(websocket=socket)
        packets = []
        async def inspect(clock):
            ask = socket.send_json.call_args.args[0]['ask_other_action_info']
            assert ask['is_tactical_recheck'] is True
            assert (ask['remaining_time'],ask['step_remaining']) == (5,0)
            assert (ask['remaining_time_ms'],ask['step_remaining_ms']) == (5000,0)
            deadline = state.action_clock_manager.windows[1].deadline
            # Viewer 1 passed normally then becomes a higher competitor after player 2's application.
            await boardcast.reconnected_send_pending_ask_for_viewer(state,102,1)
            packets.append(socket.send_json.call_args.args[0])
            assert state.action_clock_manager.windows[1].deadline == deadline
            reconnect = packets[-1]['ask_other_action_info']
            assert reconnect['is_tactical_recheck'] is True
            assert reconnect['step_remaining'] == reconnect['step_remaining_ms'] == 0
            assert reconnect['remaining_time_ms'] == 3000
            assert state.player_list[1].remaining_time == 20
        state.action_dict[1] = ['hu_first','pass']
        state.action_dict[2] = ['peng','pass']
        (responses,_), elapsed = await collect_at(state,[(1,1,{'action_type':'pass'}),
                  (8,2,{'action_type':'peng'})],callbacks=[(10,inspect)],invoke=collect_claims)
        assert elapsed == 13 and packets and responses[2]['action_type']=='peng'
    asyncio.run(run())


@pytest.mark.parametrize('status', ['waiting_action_after_cut','waiting_action_qianggang'])
def test_claim_wait_dispatches_only_final_response_to_existing_executor(status):
    async def run():
        state = tactical_state(status=status)
        initial, interruption = 'chi_mid', 'peng'
        if status == 'waiting_action_qianggang':
            initial, interruption = 'peng', 'hu_second'
            state.action_dict[1] = [initial, 'pass']
            state.action_dict[2] = [interruption, 'pass']
        state.resolve_discard_responses = AsyncMock()
        state.resolve_rob_kong_responses = AsyncMock()
        (_, elapsed) = await collect_at(state,[(8,1,{'action_type':initial}),
                           (11,2,{'action_type':interruption})],invoke=lambda s:s.wait_action())
        target = state.resolve_discard_responses if status.endswith('cut') else state.resolve_rob_kong_responses
        assert elapsed == 11
        responses, allowed = target.call_args.args
        assert responses[1]['action_type'] == 'pass' and responses[2]['action_type']==interruption
        assert initial in allowed[1] and interruption in allowed[2]
        target.assert_awaited_once()
    asyncio.run(run())


def test_chi_peng_hu_application_frames_do_not_move_tiles_and_only_losers_record_ca():
    async def run():
        state = tactical_state()
        state.action_dict[3] = ['hu_third','pass']
        socket = SimpleNamespace(send_json=AsyncMock())
        state.game_server.user_id_to_connection[104] = SimpleNamespace(websocket=socket)
        before = [list(p.hand_tiles) for p in state.player_list]
        (responses,_), elapsed = await collect_at(state,[(8,1,{'action_type':'chi_mid'}),
                         (10,2,{'action_type':'peng'}),(12,3,{'action_type':'hu_third'})],invoke=collect_claims)
        claims = [c.args[0]['do_action_info'] for c in socket.send_json.call_args_list
                  if c.args[0].get('do_action_info',{}).get('is_claim')]
        assert elapsed == 12 and [p['action_list'][0] for p in claims] == ['chi_mid','peng','hu_third']
        assert [list(p.hand_tiles) for p in state.player_list] == before
        assert all(not p.get('combination_mask') for p in claims)
        assert responses[3]['action_type']=='hu_third' and state._tactical_silent_action
        assert ['ca',1,'cm',25] in ticks(state) and ['ca',2,'p',25] in ticks(state)
    asyncio.run(run())


@pytest.mark.parametrize('enabled', [True,False])
@pytest.mark.parametrize('bot', [None,0,2])
def test_actual_start_manager_guards_saved_room_and_reconnect_flag(enabled,bot):
    from ..test_room_lifecycle import make_server,make_room,parked_loop,USERS
    from .ChangchunGameState import ChangchunGameState
    async def run():
        users = USERS[:3]+([USERS[3]] if bot is None else [bot])
        server = make_server(users)
        room = make_room(users,'changchun'); room['tactical_call'] = enabled
        server.room_manager.rooms['1'] = room
        with patch.object(ChangchunGameState,'run_game_loop',parked_loop):
            try:
                assert await server.gamestate_manager.start_game(str(users[0]),'1') is None
                state = server.gamestate_manager.get_game_state_by_room_id('1')
                assert type(state) is ChangchunGameState
                expected = enabled and bot is None
                assert room['tactical_call'] is expected and state.tactical_call is expected
                for index,player in enumerate(state.player_list):
                    player.player_index=player.original_player_index=index
                state.game_status='deal_card'
                await boardcast.send_reconnect_game_state(state,state.player_list[0])
                data = server.players[str(users[0])].websocket.send_json.call_args.args[0]
                assert data['game_info']['tactical_call'] is expected
            finally:
                for gid in list(server.gamestate_manager.gamestate_id_to_game_state):
                    await server.gamestate_manager.cleanup_game_state_complete(gamestate_id=gid)
    asyncio.run(run())


@pytest.mark.parametrize('moment', ['before_delivery','in_step','at_deadline','after_deadline','no_window'])
def test_real_force_pass_ingress_respects_clock_including_early_transport_reply(moment):
    async def run():
        state=tactical_state()
        state.game_server.players={'p':SimpleNamespace(user_id=103)}
        with patch('time.monotonic',return_value=1000),patch('time.time',return_value=1000):
            state.prepare_action_window();begin_ask_round(state)
            if moment!='before_delivery': note_ask_delivered(state,2)
        at={'before_delivery':1000,'in_step':1001,'at_deadline':1025,'after_deadline':1026,'no_window':1000}[moment]
        if moment=='no_window': state.action_clock_manager.windows.pop(2)
        with patch('time.monotonic',return_value=at):
            await get_action(state,'p','force_pass',False,None,None,None,action_tick=state.server_action_tick)
        expected=moment in ('before_delivery','in_step')
        assert (2 in state._tactical_force_passed_players) is expected
        assert (not state.action_queues[2].empty()) is expected
    asyncio.run(run())


def test_real_ingress_old_normal_tick_cannot_submit_peng_during_recheck_but_current_tick_can():
    async def run():
        state=tactical_state()
        state.game_server.players={'p':SimpleNamespace(user_id=103)}
        state.prepare_action_window();begin_ask_round(state)
        old_tick=state.server_action_tick
        state._cc_tactical_recheck=True
        state.action_dict={0:[],1:[],2:['peng','pass','force_pass'],3:[]}
        state.server_action_tick+=1
        begin_ask_round(state);note_ask_delivered(state,2)
        await get_action(state,'p','peng',False,None,None,None,action_tick=old_tick)
        assert state.action_queues[2].empty()
        await get_action(state,'p','peng',False,None,None,None,action_tick=state.server_action_tick)
        assert state.action_queues[2].get_nowait()['action_type']=='peng'
    asyncio.run(run())


def test_prequeued_validation_also_rejects_nonparticipant_and_undelivered_window():
    state=tactical_state();state.prepare_action_window();begin_ask_round(state)
    assert not state.validate_tactical_queued_action(0,{'action_type':'peng'})
    assert not state.validate_tactical_queued_action(2,{'action_type':'peng'})


@pytest.mark.parametrize('submitted', [None,{}])
def test_recheck_collector_selects_best_of_simultaneous_claims_and_preserves_bank(submitted):
    async def run():
        state=tactical_state();state._cc_tactical_recheck=True
        state.action_dict={0:[],1:[],2:['peng','pass','force_pass'],3:['hu_third','pass','force_pass']}
        (best,elapsed)=await collect_at(state,[(1,2,{'action_type':'peng'}),(1,3,{'action_type':'hu_third'})],
                      invoke=lambda s:s.collect_tactical_recheck(1,submitted))
        assert elapsed==1 and best[1:3]==('hu_third',3)
        assert state.player_list[2].remaining_time==state.player_list[3].remaining_time==20
    asyncio.run(run())


def test_recheck_collector_filters_nondecline_below_current_priority():
    async def run():
        state=tactical_state();state._cc_tactical_recheck=True
        state.action_dict={0:[],1:[],2:['peng','pass'],3:[]}
        (best,_)=await collect_at(state,[(1,2,{'action_type':'peng'})],
                                invoke=lambda s:s.collect_tactical_recheck(3,None))
        assert best is None and state.player_list[2].remaining_time==20
    asyncio.run(run())


def test_application_send_failure_does_not_abort_other_clients_or_advance_tick():
    async def run():
        state=tactical_state()
        failed=SimpleNamespace(send_json=AsyncMock(side_effect=ConnectionError('injected transport failure')))
        okay=SimpleNamespace(send_json=AsyncMock())
        state.game_server.user_id_to_connection={102:SimpleNamespace(websocket=failed),103:SimpleNamespace(websocket=okay)}
        previous=state.server_action_tick
        await state._broadcast_cc_claim(state,action_list=['chi_mid'],action_player=1,cut_tile=25,is_claim=True)
        assert state.server_action_tick==previous
        assert okay.send_json.call_args.args[0]['do_action_info']['is_claim'] is True
    asyncio.run(run())


def test_claim_execution_after_recheck_has_new_normal_step_and_unchanged_grace_bank():
    async def run():
        state=tactical_state()
        state.resolve_discard_responses=AsyncMock()
        (_,elapsed)=await collect_at(state,[(8,1,{'action_type':'chi_mid'}),(11,2,{'action_type':'peng'})],
                                    invoke=lambda s:s.wait_action())
        assert elapsed==11 and state.player_list[2].remaining_time==17
        # The existing claim executor transitions here; its physical action grants a fresh step.
        state.current_player_index=2;state.cc_turn_serial+=1
        state.game_status='waiting_hand_action';state.action_dict={0:[],1:[],2:['cut'],3:[]}
        state.server_action_tick+=1;begin_ask_round(state);note_ask_delivered(state,2)
        assert state.action_clock(state.player_list[2])==(17,5)
        assert state.action_clock_manager.windows[2].consumes_bank
        state._reset_hand_runtime()
        assert all(p.remaining_time==20 for p in state.player_list)
    asyncio.run(run())



def test_slow_observer_close_cannot_spend_pending_normal_bank_before_tactical_transition():
    async def run():
        state=tactical_state()
        original=state.notify_cc_window_closed
        async def arm(clock):
            async def delayed(index,*,include_player=False):
                if index==1 and not include_player:
                    clock.value+=30
                await original(index,include_player=include_player)
            state.notify_cc_window_closed=delayed
        (responses,_),elapsed=await collect_at(state,[(8,1,{'action_type':'chi_mid'})],
                                             callbacks=[(0,arm)],invoke=collect_claims)
        assert elapsed==43 and responses[1]['action_type']=='chi_mid'
        assert state.player_list[1].remaining_time==state.player_list[2].remaining_time==17
    asyncio.run(run())


@pytest.mark.parametrize('choice', ['pass', 'force_pass'])
def test_recheck_decline_scope_with_optional_no_submitted_map(choice):
    async def run():
        state=tactical_state()
        state.prepare_action_window();begin_ask_round(state)
        state._cc_tactical_recheck=True
        state.action_dict={0:[],1:[],2:['peng','pass','force_pass'],3:[]}
        (best,_)=await collect_at(state,[(1,2,{'action_type':choice})],
                                invoke=lambda s:s.collect_tactical_recheck(1,None))
        assert best is None and (2 in state._tactical_force_passed_players)==(choice=='force_pass')
        assert 2 in state._tactical_passed_players
        assert state.player_list[2].remaining_time==20
    asyncio.run(run())


def test_simultaneous_higher_hu_candidates_select_closer_player_not_last_iteration():
    async def run():
        state=tactical_state();state._cc_tactical_recheck=True
        state.action_dict={0:[],1:[],2:['hu_second','pass'],3:['hu_third','pass']}
        (best,_)=await collect_at(state,[(1,2,{'action_type':'hu_second'}),(1,3,{'action_type':'hu_third'})],
                                invoke=lambda s:s.collect_tactical_recheck(2,{}))
        assert best[1:3]==('hu_second',2)
    asyncio.run(run())


@pytest.mark.parametrize('winning_action', ['peng', 'hu_second'])
def test_actual_special_added_robbery_uses_tactical_priority_and_conserves_136_tiles(winning_action):
    from collections import Counter
    from .test_integration import assert_supply
    from ...game_calculation.changchun import rules as book
    async def run():
        state=make_state(tactical_call=True)
        state.tactical_pre_grace_delay=0
        original='Cwind:41,42,43:41,42,43'
        source=hand(state,0,[11,12,13,22,23,24,35,36,37,45,31],[original],drawn=31)
        hand(state,1,[31,31,11,14,16,21,24,27,32,35,38,45,45])
        hand(state,2,[11,12,13,21,22,23,32,33,34,44,44,44,31])
        hand(state,3,[12,15,18,22,25,28,33,36,39,41,42,46,47])
        used=Counter(t for p in state.player_list for t in p.hand_tiles)
        used.update(book.parse_meld(original).physical)
        assert max(used.values())<=4
        state.tiles_list=list((Counter({t:4 for t in book.TILES})-used).elements())
        assert_supply(state)
        candidate=next(c for c in state.special_added_candidates(0) if c['tile']==31 and c['represented']==44)
        assert await state.execute_special_added(0,candidate['token'])
        assert state.game_status=='waiting_action_qianggang'
        assert 'peng' in state.action_dict[1] and 'hu_second' in state.action_dict[2]
        assert not any(a.startswith('chi_') for offered in state.action_dict.values() for a in offered)
        assert_supply(state)
        receipts=[(8,1,{'action_type':'peng'})]
        if winning_action=='hu_second': receipts.append((11,2,{'action_type':winning_action}))
        (_,elapsed)=await collect_at(state,receipts,invoke=lambda s:s.wait_action())
        assert elapsed==(11 if winning_action=='hu_second' else 13)
        assert state.player_list[1].remaining_time==state.player_list[2].remaining_time==17
        assert source.combination_tiles==[original] and not state.kong_ledger
        assert state.cc_pending_special is None and state.jiagang_tile is None
        if winning_action=='hu_second':
            assert state.game_status=='END' and [w['index'] for w in state.pending_winners]==[2]
            assert state.pending_winners[0]['source']=='robbing_kong'
            assert state.pending_winners[0]['tile']==31
            assert not state.player_list[1].combination_tiles
            assert ['ca',1,'p',31] in ticks(state)
        else:
            assert state.current_player_index==1 and 'k31' in state.player_list[1].combination_tiles
            assert not source.discard_tiles and source.discard_origin_tiles==[31]
        assert_supply(state)
    asyncio.run(run())
