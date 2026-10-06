"""MIL 改报、截和及普通/战鸣边界的实际状态机回归。"""
import asyncio
from unittest.mock import AsyncMock, patch
import pytest

from .test_timing import clock, state_for_claim, deliver, submit
from .test_state import HAND, hand, ticks, draw
from ..game_taiwan import boardcast
from ..public.ai.get_action import get_action
from ..public.ask_timing import note_ask_delivered
from ..public.tactical_claim import get_higher_priority_snapshot
from ...room.guangdong_room import GuangdongMilRoomValidator


def test_new_room_defaults_on_and_explicit_false_survives():
    fields = dict(room_name='广东', round_timer=20, step_timer=5)
    assert GuangdongMilRoomValidator(**fields).tactical_call is True
    assert GuangdongMilRoomValidator(**fields, tactical_call=False).tactical_call is False
    assert GuangdongMilRoomValidator(**fields, tactical_call=True).tactical_call is True


@pytest.mark.parametrize('bank,step', [(20, 5), (31, 8), (0, 8)])
def test_enabling_tactical_does_not_shorten_opening_ask(clock, bank, step):
    state = state_for_claim(tactical_call=True, round_timer=bank, step_timer=step)
    assert state.tactical_call and not state.tactical_commit_lock and not state.claim_protection
    deliver(state, (1,))
    assert state.claim_clock(state.player_list[1]) == (bank, step)
    assert set(state.action_dict[1]) == {'peng', 'pass', 'force_pass'}


def test_head_bump_and_peng_gang_priorities(clock):
    state = state_for_claim(tactical_call=True)
    state.action_dict = {0: [], 1: ['hu_first', 'pass'], 2: ['hu_second', 'peng', 'gang', 'pass'], 3: ['hu_third', 'pass']}
    deliver(state, (1, 2, 3))
    higher, available = get_higher_priority_snapshot(state, 'hu_second', 2)
    assert available and higher[1] == ['hu_first', 'pass', 'force_pass'] and not higher[3]
    assert state.action_priority['peng'] == state.action_priority['gang']


def test_valid_claim_returns_before_unanswered_ordinary_window(clock):
    async def run():
        state = state_for_claim(tactical_call=True)
        state.action_dict[2] = ['hu_second', 'pass']
        deliver(state, (1, 2))
        clock.advance(8.25)
        submit(state, 1, 'peng')
        with patch('asyncio.wait', side_effect=AssertionError('must interrupt ordinary wait')):
            replies, allowed = await state.collect_action_responses()
        assert replies[1]['action_type'] == 'peng' and 2 in allowed
        assert state._bank_seconds(state.player_list[1]) == pytest.approx(16.75)
        assert state._bank_seconds(state.player_list[2]) == pytest.approx(16.75)
    asyncio.run(run())


@pytest.mark.parametrize('uid', [0, 2, 3, 9])
def test_actual_bot_disables_requested_tactical(uid):
    state = state_for_claim(tactical_call=True, player_list=[101, uid, 103, 104])
    assert not state.tactical_call


@pytest.mark.parametrize('switch,players,expected', [(True,[101,102,103,104],True),(False,[101,102,103,104],False),(True,[101,2,103,104],False)])
def test_effective_setting_in_actual_game_info_reconnect_and_replay_title(clock,switch,players,expected):
    async def run():
        state=state_for_claim(tactical_call=switch,player_list=players,step_timer=8,round_timer=31)
        await boardcast.broadcast_game_start(state)
        info=state.game_server.user_id_to_connection[101].websocket.messages[-1]['game_info']
        assert info['tactical_call'] is expected
        fields=state.build_record_title_fields()
        assert fields['tactical_call'] is expected
        assert (fields['round_timer'],fields['step_timer'],fields['tactical_grace_seconds'])==(31,8,5.0)
    asyncio.run(run())


@pytest.mark.parametrize('action,expected', [('pass', 1), ('force_pass', 1), ('hu_second', 2), ('late_hu', 1), ('old_hu', 1)])
def test_real_claim_recheck_branch_and_clock_charge(clock, action, expected):
    async def run():
        state = state_for_claim(tactical_call=True, step_timer=8, round_timer=31)
        state.tactical_pre_grace_delay = 0
        hand(state, 1, [11, 11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41])
        hand(state, 2, [11, 11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41])
        state.action_dict[2] = ['hu_second', 'pass']
        state.result_dict['hu_second'] = {'base_score': 8, 'is_win': True}
        deliver(state, (1, 2))
        opening = state.server_action_tick
        clock.advance(11.125)
        submit(state, 1, 'peng')
        replies, allowed = await state.collect_action_responses()
        charged = [state._bank_seconds(p) for p in state.player_list]
        asks = []
        async def answer(payload):
            if not payload['type'].endswith('ask_other_action'):
                return
            info = payload['ask_other_action_info']
            asks.append(info)
            note_ask_delivered(state, 2)
            tick = opening if action == 'old_hu' else info['action_tick']
            clock.advance(5.125 if action == 'late_hu' else 2.125)
            chosen = 'hu_second' if action in ('late_hu', 'old_hu') else action
            await get_action(state, '103', chosen, False, None, None, None, action_tick=tick)
        state.game_server.user_id_to_connection[103].websocket.callback = answer
        real_wait = asyncio.wait
        async def wait(tasks, **kwargs):
            if any(t.done() or (getattr(t.get_coro(), '__name__', '') == 'wait' and state.action_events[2].is_set()) for t in tasks):
                return await real_wait(tasks, **kwargs)
            clock.advance(5)
            return set(), set(tasks)
        with patch('asyncio.wait', wait):
            await state.resolve_discard_responses(replies, allowed)
        assert len(asks) == 1 and asks[0]['is_tactical_recheck']
        assert (asks[0]['remaining_time_ms'], asks[0]['step_remaining_ms']) == (5000, 0)
        assert [state._bank_seconds(p) for p in state.player_list] == charged
        assert charged[1:3] == [pytest.approx(27.875)] * 2
        if expected == 2:
            assert state.pending_winners[0]['index'] == 2
            assert not state.player_list[1].combination_tiles
            assert ['ca', 1, 'p', 11] in ticks(state)
        else:
            assert state.current_player_index == 1 and state.player_list[1].combination_tiles == ['k11']
            assert not any(t[0] == 'ca' for t in ticks(state))
            payloads = state.game_server.user_id_to_connection[101].websocket.messages
            final = [p['do_action_info'] for p in payloads if p['type'].endswith('do_action') and p['do_action_info'].get('combination_target')]
            assert len(final) == 1 and final[0]['silent'] is True
        assert not state._guangdong_tactical_recheck and state._tactical_action_snapshot is None
    asyncio.run(run())


@pytest.mark.parametrize('forced', [False, True])
def test_opening_pass_can_compete_but_force_pass_cannot(clock, forced):
    async def run():
        state = state_for_claim(tactical_call=True)
        state.tactical_pre_grace_delay = 0
        state.action_dict[2] = ['hu_second', 'pass']
        state.result_dict['hu_second'] = {'base_score': 8, 'is_win': True}
        deliver(state, (1, 2))
        submit(state, 2, 'force_pass' if forced else 'pass')
        submit(state, 1, 'peng')
        replies, allowed = await state.collect_action_responses()
        calls = []
        async def answer(payload):
            if payload['type'].endswith('ask_other_action'):
                calls.append(payload)
                await get_action(state, '103', 'hu_second', False, None, None, None,
                                 action_tick=payload['ask_other_action_info']['action_tick'])
        state.game_server.user_id_to_connection[103].websocket.callback = answer
        state.execute_claim = AsyncMock()
        await state.resolve_discard_responses(replies, allowed)
        assert bool(calls) is not forced
        if forced:
            state.execute_claim.assert_awaited_once_with(1, 'peng')
        else:
            assert state.pending_winners[0]['index'] == 2
    asyncio.run(run())


def test_original_pung_declaration_can_raise_to_nearer_hu(clock):
    async def run():
        state = state_for_claim(tactical_call=True)
        state.tactical_pre_grace_delay = 0
        state.action_dict = {0: [], 1: ['hu_first', 'peng', 'pass'], 2: ['hu_second', 'pass'], 3: []}
        for action in ('hu_first', 'hu_second'):
            state.result_dict[action] = {'base_score': 8, 'is_win': True}
        deliver(state, (1, 2))
        submit(state, 1, 'peng')
        replies, allowed = await state.collect_action_responses()
        claims, asks = [], []
        async def answer(index, payload):
            if payload['type'].endswith('do_action') and payload['do_action_info'].get('is_claim') and index == 1:
                claims.append(payload['do_action_info']['action_list'][0])
            if payload['type'].endswith('ask_other_action'):
                asks.append(index)
                chosen = 'hu_second' if index == 2 else 'hu_first'
                await get_action(state, str(state.player_list[index].user_id), chosen, False, None, None, None,
                                 action_tick=payload['ask_other_action_info']['action_tick'])
        for index in (1, 2):
            async def callback(payload, index=index): await answer(index, payload)
            state.game_server.user_id_to_connection[state.player_list[index].user_id].websocket.callback = callback
        await state.resolve_discard_responses(replies, allowed)
        assert asks == [2, 1] and claims == ['peng', 'hu_second', 'hu_first']
        assert state.pending_winners[0]['index'] == 1 and not state._tactical_committed_players
        assert ['ca', 1, 'p', 11] in ticks(state)
        assert ['ca', 2, 'hu_second', 11] in ticks(state)
    asyncio.run(run())


def test_independent_recheck_timeout_then_pung_gets_fresh_configured_step(clock):
    async def run():
        state = state_for_claim(tactical_call=True, step_timer=8, round_timer=31)
        state.tactical_pre_grace_delay = 0
        hand(state, 1, [11, 11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41])
        state.action_dict[2] = ['hu_second', 'pass']
        state.result_dict['hu_second'] = {'base_score': 8, 'is_win': True}
        deliver(state, (1, 2))
        clock.advance(11.5)
        submit(state, 1, 'peng')
        replies, allowed = await state.collect_action_responses()
        original = clock.now
        async def expire(tasks, **kwargs):
            clock.advance(5)
            return set(), set(tasks)
        with patch('asyncio.wait', expire):
            await state.resolve_discard_responses(replies, allowed)
        assert clock.now == original + 5
        assert state._bank_seconds(state.player_list[1]) == pytest.approx(27.5)
        assert state._bank_seconds(state.player_list[2]) == pytest.approx(27.5)
        assert state.game_status == 'onlycut_after_action'
        state.game_status = 'waiting_hand_action'
        state.action_dict = state.after_claim_actions()
        state.prepare_action_window()
        await boardcast.broadcast_ask_hand_action(state)
        packet = state.game_server.user_id_to_connection[102].websocket.messages[-1]['ask_hand_action_info']
        assert (packet['remaining_time_ms'],packet['step_remaining_ms']) == (27500,8000)
        state._reset_hand_runtime()
        assert state._bank_seconds(state.player_list[1]) == 31 and not state._guangdong_tactical_recheck
    asyncio.run(run())


def test_switch_off_waits_all_ordinary_responses_and_never_rechecks(clock):
    async def run():
        state = state_for_claim(tactical_call=False, round_timer=0, step_timer=8)
        state.action_dict[2] = ['hu_second', 'pass']
        deliver(state, (1,2))
        clock.advance(1)
        submit(state,1,'peng')
        calls = []
        async def expire(tasks, timeout, **kwargs):
            calls.append(timeout)
            clock.advance(timeout)
            return set(), set(tasks)
        with patch('asyncio.wait',expire):
            replies, allowed = await state.collect_action_responses()
        assert sum(calls) == 7 and replies[1]['action_type'] == 'peng'
        assert not state._tactical_action_snapshot and 'force_pass' not in allowed[1]
        state.execute_claim = AsyncMock()
        await state.resolve_discard_responses(replies, allowed)
        assert not state.game_server.user_id_to_connection[103].websocket.messages
        state.execute_claim.assert_awaited_once_with(1,'peng')
    asyncio.run(run())


def test_no_higher_hu_only_broadcasts_claim_and_does_not_open_recheck(clock):
    async def run():
        state = state_for_claim(tactical_call=True)
        state.tactical_pre_grace_delay = 0
        state.action_dict = {0:[],1:['hu_first','pass'],2:[],3:[]}
        state.result_dict['hu_first'] = {'base_score':8,'is_win':True}
        deliver(state,(1,))
        submit(state,1,'hu_first')
        replies,allowed = await state.collect_action_responses()
        await state.resolve_discard_responses(replies,allowed)
        packets = state.game_server.user_id_to_connection[102].websocket.messages
        assert not any(p['type'].endswith('ask_other_action') for p in packets)
        assert [p['do_action_info']['action_list'] for p in packets if p['type'].endswith('do_action')] == [['hu_first']]
        assert state.pending_winners[0]['index']==1 and state._tactical_silent_action
    asyncio.run(run())


def test_claim_send_failure_and_offline_player_do_not_swallow_spectators(clock):
    async def run():
        state=state_for_claim(tactical_call=True)
        state.tactical_pre_grace_delay=0
        state.action_dict={0:[],1:['hu_first','pass'],2:[],3:[]}
        state.result_dict['hu_first']={'base_score':8,'is_win':True}
        deliver(state,(1,))
        submit(state,1,'hu_first')
        replies,allowed=await state.collect_action_responses()
        state.game_server.user_id_to_connection[101].websocket.send_json=AsyncMock(side_effect=ConnectionError('dropped'))
        state.player_list[2].tag_list.append('offline')
        state.send_to_realtime_spectators=AsyncMock()
        await state.resolve_discard_responses(replies,allowed)
        assert state.pending_winners[0]['index']==1
        assert state.send_to_realtime_spectators.await_count==4
        for call in state.send_to_realtime_spectators.await_args_list:
            info=call.args[1].model_dump(exclude_none=True)['do_action_info']
            assert info['is_claim'] and 'hand_tiles' not in info
    asyncio.run(run())


def test_queued_later_answer_does_not_charge_ordinary_bank_after_first_claim(clock):
    async def run():
        state = state_for_claim(tactical_call=True)
        state.action_dict[2] = ['hu_second','pass']
        deliver(state,(1,2))
        clock.advance(1)
        submit(state,1,'peng')
        clock.advance(7.25)
        submit(state,2,'pass')
        clock.advance(20)  # Collector resumes after an outbound stall.
        replies,_ = await state.collect_action_responses()
        assert replies[1]['action_type']=='peng'
        assert [state._bank_seconds(state.player_list[i]) for i in (1,2)] == [20,20]
    asyncio.run(run())


@pytest.mark.parametrize('nearer_wins',[True,False])
def test_real_added_kong_robbed_head_bump_or_nearer_declines(clock,nearer_wins):
    async def run():
        state = state_for_claim(tactical_call=True)
        state.tactical_pre_grace_delay = 0
        source = hand(state,0,[11,12,13,21,22,23,31,32,33,41],['k45'])
        draw(source,45)
        for index in (1,2): hand(state,index,HAND)
        await state.execute_jiagang(0,45)
        assert state.game_status == 'waiting_action_qianggang'
        state.prepare_action_window()
        await boardcast.broadcast_ask_other_action(state)
        submit(state,2,'hu_second')
        replies,allowed = await state.collect_action_responses()
        async def answer(payload):
            if payload['type'].endswith('ask_other_action'):
                assert payload['ask_other_action_info']['cut_tile']==45
                await get_action(state,'102','hu_first' if nearer_wins else 'pass',False,None,None,None,
                                 action_tick=payload['ask_other_action_info']['action_tick'])
        state.game_server.user_id_to_connection[102].websocket.callback = answer
        await state.resolve_rob_kong_responses(replies,allowed)
        assert state.pending_winners[0]['index'] == (1 if nearer_wins else 2)
        assert state.game_status == 'END' and source.combination_tiles==['k45'] and 45 not in source.hand_tiles
        assert not state.kong_ledger and not state._pending_jiagang
        state.current_round = 4
        await state._settle_hand({i:0 for i in range(4)})
        winner = state.pending_winners[0]['index']
        packets = state.game_server.user_id_to_connection[101].websocket.messages
        results = [p['show_result_info'] for p in packets if p['type'].endswith('show_result')]
        assert len(results)==1 and results[0]['silent'] is True
        assert len([t for t in ticks(state) if t[0].startswith('hu_')])==1
        if nearer_wins: assert ['ca',2,'hu_second',45] in ticks(state)
    asyncio.run(run())


def test_recheck_has_five_seconds_no_bank_debit_and_reconnect(clock):
    async def run():
        state = state_for_claim(tactical_call=True, step_timer=8, round_timer=31)
        deliver(state, (1,))
        clock.advance(11.25)
        submit(state, 1, 'peng')
        await state.collect_action_responses()
        state.action_dict = {0: [], 1: [], 2: ['hu_second', 'pass', 'force_pass'], 3: []}
        bank = state._bank_seconds(state.player_list[2])
        await state.broadcast_tactical_recheck(state, remaining_time_override=5, is_tactical_recheck=True)
        info = state.game_server.user_id_to_connection[103].websocket.messages[-1]['ask_other_action_info']
        assert (info['remaining_time'], info['step_remaining'], info['remaining_time_ms'], info['step_remaining_ms']) == (5, 0, 5000, 0)
        assert info['is_tactical_recheck'] is True
        clock.advance(2.125)
        await boardcast.reconnected_send_pending_ask_for_viewer(state, 103, 2)
        info = state.game_server.user_id_to_connection[103].websocket.messages[-1]['ask_other_action_info']
        assert (info['remaining_time_ms'], info['step_remaining_ms']) == (2875, 0)
        assert info['is_tactical_recheck'] is True and state._bank_seconds(state.player_list[2]) == bank
    asyncio.run(run())
