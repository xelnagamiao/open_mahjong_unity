"""Room-contract regressions using production queues/collector and a controlled clock."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .test_state import make_state, hand
from .test_integration import room_manager
from ..game_taiwan import boardcast
from ..game_taiwan.wait_action import _build_ask_deadlines, _collect_responses
from ..public.ask_timing import begin_ask_round, note_ask_delivered
from ...room.changchun_room import handle_create_changchun_room


class Clock:
    def __init__(self):
        self.value = 1000.0

    def now(self):
        return self.value


async def collect_at(state, receipts=(), *, lag=0, callbacks=(), invoke=None):
    """Run the real collector, advancing only its event wait, never sleeping seconds."""
    clock = Clock()
    real_wait = asyncio.wait
    scheduled = sorted(receipts)
    calls = sorted(callbacks)

    async def enqueue_due():
        while scheduled and scheduled[0][0] <= clock.value - 1000:
            at, seat, data = scheduled.pop(0)
            original = clock.value
            clock.value = 1000 + at
            await state.action_queues[seat].put(dict(data) if isinstance(data,dict) else data)
            state.action_events[seat].set()
            clock.value = original
        while calls and calls[0][0] <= clock.value - 1000:
            _, callback = calls.pop(0)
            await callback(clock)

    async def wait_events(tasks, timeout, return_when):
        await asyncio.sleep(0)
        done, pending = await real_wait(tasks, timeout=0, return_when=return_when)
        if done:
            return done, pending
        next_times = [1000 + row[0] for row in (scheduled[:1] + calls[:1])]
        clock.value = min([clock.value + timeout, *next_times])
        await enqueue_due()
        await asyncio.sleep(0)
        return await real_wait(tasks, timeout=0, return_when=return_when)

    with patch('time.time', side_effect=clock.now), \
         patch('time.monotonic', side_effect=clock.now), \
         patch('server.gamestate.game_taiwan.wait_action.asyncio.wait', side_effect=wait_events):
        state.prepare_action_window()
        begin_ask_round(state)
        for seat, offered in state.action_dict.items():
            if offered:
                note_ask_delivered(state, seat)
        clock.value += lag
        await enqueue_due()
        result = await invoke(state) if invoke else await state.collect_action_responses()
        return result, clock.value - 1000


def ask_state(bank=20, step=5, status='waiting_action_after_cut', actions=('peng', 'pass'), seat=1):
    state = make_state(round_timer=bank, step_timer=step)
    state.game_status = status
    state.action_dict = {i: list(actions) if i == seat else [] for i in range(4)}
    state.player_list[0].discard_tiles = [25]
    state.player_list[0].last_discarded_tile = 25
    if status == 'waiting_action_qianggang':
        state.jiagang_tile = 25
    return state


@pytest.mark.parametrize('field', ['claim_protection'])
def test_changchun_room_rejects_forbidden_tactical_or_protection_flag(field):
    async def run():
        manager = room_manager()
        websocket = SimpleNamespace(send_json=AsyncMock())
        await handle_create_changchun_room(SimpleNamespace(room_manager=manager), 'c',
            {'roomname':'长春计时边界', field:True}, websocket)
        assert not websocket.send_json.call_args.args[0]['success']
        assert not manager.rooms
    asyncio.run(run())


def test_changchun_legacy_disabled_tactical_setting_preserves_normal_claim_clock():
    async def run():
        for bank, step, at, expected in [(20,5,8,17), (0,9,8,0), (47,9,13,43)]:
            state = ask_state(bank, step)
            assert state.tactical_call is False
            assert state.claim_protection is False
            assert state.tactical_commit_lock is False
            (responses, _), elapsed = await collect_at(state, [(at,1,{'action_type':'peng'})])
            assert responses[1]['action_type'] == 'peng' and elapsed == at
            assert state.player_list[1].remaining_time == expected
    asyncio.run(run())


def test_answered_spectator_prompt_closes_while_another_claimant_keeps_thinking():
    async def run():
        state = ask_state()
        state.action_dict[2] = ['peng', 'pass']
        socket = SimpleNamespace(send_json=AsyncMock())
        state.realtime_spectators = [SimpleNamespace(user_id=90001,
            host_user_id=state.player_list[1].user_id)]
        state.game_server.user_id_to_connection[90001] = SimpleNamespace(websocket=socket)

        async def midway(clock):
            messages = [c.args[0] for c in socket.send_json.call_args_list]
            assert len(messages) == 1
            close = messages[0]
            assert close['type'] == 'gamestate/changchun/ask_closed'
            info = close['ask_other_action_info']
            assert info['player_index'] == 1 and info['action_tick'] == state.server_action_tick
            assert info['action_list'] == []
            assert (info['remaining_time'], info['step_remaining']) == (17, 0)
            assert state.waiting_players_list == [2]

        (responses, _), _ = await collect_at(state,
            [(8, 1, {'action_type':'pass'}), (12, 2, {'action_type':'pass'})],
            callbacks=[(10, midway)])
        assert set(responses) == {1, 2}
        assert socket.send_json.await_count == 1  # Other seats never leak to this viewer.
        assert state.player_list[1].remaining_time == 17
    asyncio.run(run())


def test_unsent_retry_keeps_one_window_and_starts_only_at_first_delivery():
    async def run():
        state = ask_state()
        clock = Clock()
        with patch('time.time', side_effect=clock.now), patch('time.monotonic', side_effect=clock.now):
            state.prepare_action_window()
            begin_ask_round(state)
            serial = state.action_clock_manager.serial
            assert state.action_clock_manager.windows[1].deadline is None
            clock.value = 1002
            state.server_action_tick += 1
            begin_ask_round(state)  # Transport failed before the first logical delivery.
            assert state.action_clock_manager.serial == serial
            assert state._ask_delivered_at == {}
            assert state.action_clock(state.player_list[1], True) == (20, 5)
            clock.value = 1004
            note_ask_delivered(state, 1)
            assert state.action_clock_manager.windows[1].deadline == 1029
            clock.value = 1006
            await state.action_queues[1].put({'action_type':'pass', '_action_tick':state.server_action_tick})
            responses, _ = await state.collect_action_responses()
            assert responses[1]['action_type'] == 'pass'
            assert state.player_list[1].remaining_time == 20
    asyncio.run(run())


@pytest.mark.parametrize('kind', ['player_reconnect', 'spectator_snapshot', 'broadcast_game_start'])
def test_full_snapshot_serializes_fractional_bank_without_changing_authoritative_budget(kind):
    async def run():
        state = ask_state()
        state.player_list[1].remaining_time = 19.25
        uid = state.player_list[1].user_id if kind != 'spectator_snapshot' else 90001
        socket = SimpleNamespace(send_json=AsyncMock())
        state.game_server.user_id_to_connection[uid] = SimpleNamespace(websocket=socket)
        if kind == 'player_reconnect':
            await boardcast.send_reconnect_game_state(state, state.player_list[1])
        elif kind == 'spectator_snapshot':
            await boardcast.send_realtime_spectator_snapshot(state, uid, 1)
        else:
            await boardcast.broadcast_game_start(state)
        packets = [c.args[0] for c in socket.send_json.call_args_list]
        start = next(p['game_info'] for p in packets if p.get('game_info'))
        assert start['players_info'][1]['remaining_time'] == 20
        assert state.player_list[1].remaining_time == 19.25
        if kind != 'broadcast_game_start':
            ask = next(p['ask_other_action_info'] for p in packets if p.get('ask_other_action_info'))
            assert ask['remaining_time_ms'] == 19250
    asyncio.run(run())


@pytest.mark.parametrize('status,seat,field', [
    ('waiting_hand_action', 0, 'ask_hand_action_info'),
    ('waiting_action_after_cut', 1, 'ask_other_action_info'),
    ('waiting_action_qianggang', 1, 'ask_other_action_info')])
@pytest.mark.parametrize('elapsed,bank_ms,step_ms', [(0,19250,5000), (2.25,19250,2750),
    (5.75,18500,0), (25,0,0)])
def test_fractional_bank_and_reconnect_send_exact_wire_budget(status, seat, field, elapsed, bank_ms, step_ms):
    async def run():
        state = ask_state(status=status, seat=seat, actions=('cut',) if seat == 0 else ('peng','pass'))
        state.player_list[seat].remaining_time = 19.25
        socket = SimpleNamespace(send_json=AsyncMock())
        uid = state.player_list[seat].user_id
        state.game_server.user_id_to_connection[uid] = SimpleNamespace(websocket=socket)
        clock = Clock()
        with patch('time.time', side_effect=clock.now), patch('time.monotonic', side_effect=clock.now):
            state.prepare_action_window()
            if seat == 0:
                await boardcast.broadcast_ask_hand_action(state)
            else:
                await boardcast.broadcast_ask_other_action(state)
            first = socket.send_json.call_args.args[0][field]
            assert first.get('remaining_time_ms') == 19250
            assert first.get('step_remaining_ms') == 5000
            deadline = state.action_clock_manager.windows[seat].deadline
            clock.value += elapsed
            await boardcast.reconnected_send_pending_ask_for_viewer(state, uid, seat)
            info = socket.send_json.call_args.args[0][field]
            assert info.get('remaining_time_ms') == bank_ms
            assert info.get('step_remaining_ms') == step_ms
            assert deadline == 1024.25
            assert state.action_clock_manager.windows[seat].deadline == deadline
            assert (info['remaining_time'], info['step_remaining']) == (
                (bank_ms+999)//1000, (step_ms+999)//1000)
    asyncio.run(run())


@pytest.mark.parametrize('bank,step', [(20, 5), (47, 9), (0, 7), (11, 0), (0, 0)])
def test_room_request_save_initialization_preserve_defaults_and_custom_times(bank, step):
    async def run():
        manager = room_manager()
        websocket = SimpleNamespace(send_json=AsyncMock())
        message = {'roomname': '长春计时'}
        if (bank, step) != (20, 5):
            message.update(roundTimerValue=bank, stepTimerValue=step)
        await handle_create_changchun_room(SimpleNamespace(room_manager=manager), 'c', message, websocket)
        room = websocket.send_json.call_args.args[0]['room_info']
        assert room['round_timer'] == bank and room['step_timer'] == step
        assert manager.rooms['123456']['round_timer'] == bank
        assert manager.rooms['123456']['step_timer'] == step
        state = make_state(round_timer=room['round_timer'], step_timer=room['step_timer'])
        assert state.round_time == bank and state.step_time == step
        assert [p.remaining_time for p in state.player_list] == [bank] * 4
    asyncio.run(run())


@pytest.mark.parametrize('status', ['waiting_action_after_cut', 'waiting_action_qianggang'])
@pytest.mark.parametrize('bank,step,at,left', [(20, 5, 4.5, 20), (20, 5, 8, 17),
    (47, 9, 13, 43), (0, 7, 6, 0), (11, 0, 2, 9), (20, 3, 8, 15), (20, 5, 5.75, 19.25)])
def test_claim_responses_use_room_budget_and_debit_actual_overtime(status, bank, step, at, left):
    state = ask_state(bank, step, status)
    (responses, _), elapsed = asyncio.run(collect_at(state, [(at, 1, {'action_type': 'peng'})]))
    assert responses[1]['action_type'] == 'peng'
    assert elapsed == pytest.approx(at)
    assert state.player_list[1].remaining_time == pytest.approx(left)
    assert state.player_list[0].remaining_time == bank


@pytest.mark.parametrize('bank,step', [(20, 5), (47, 9), (0, 7), (11, 0), (0, 0)])
def test_expiry_uses_full_room_budget_and_passes(bank, step):
    state = ask_state(bank, step)
    (responses, _), elapsed = asyncio.run(collect_at(state))
    assert not responses and elapsed == bank + step
    assert state.player_list[1].remaining_time == 0
    assert not state.waiting_players_list


@pytest.mark.parametrize('action', ['cut', 'riichi_cut', 'angang', 'jiagang', 'cc_special',
    'cc_added', 'cc_change_bao', 'cc_draw', 'cc_pass', 'hu_self'])
def test_hand_and_special_player_choices_share_the_same_clock(action):
    state = ask_state(20, 5, 'waiting_hand_action', (action,), seat=0)
    (responses, _), elapsed = asyncio.run(collect_at(state, [(8, 0, {'action_type': action})]))
    assert responses[0]['action_type'] == action and elapsed == 8
    assert state.player_list[0].remaining_time == 17


def test_repeated_fractional_overtime_is_not_free():
    async def run():
        state = ask_state()
        for _ in range(8):
            state.cc_turn_serial += 1  # Different real action windows.
            (responses, _), _ = await collect_at(state, [(5.75, 1, {'action_type': 'pass'})])
            assert responses[1]['action_type'] == 'pass'
        assert state.player_list[1].remaining_time == 14
    asyncio.run(run())


def test_receipt_before_deadline_survives_delayed_collection_and_uses_receipt_for_debit():
    state = ask_state()
    (responses, _), _ = asyncio.run(collect_at(state, [(8, 1, {'action_type': 'peng'})], lag=30))
    assert responses[1]['action_type'] == 'peng'
    assert state.player_list[1].remaining_time == 17


def test_receipt_after_deadline_is_rejected_even_if_prequeued():
    state = ask_state()
    (responses, _), _ = asyncio.run(collect_at(state, [(25.1, 1, {'action_type': 'peng'})], lag=30))
    assert not responses and state.player_list[1].remaining_time == 0


def test_reconnect_does_not_reset_or_double_debit_and_does_not_reask_resolved_seat():
    async def run():
        state = ask_state()
        state.action_dict[2] = ['peng', 'pass']
        sockets = {i: SimpleNamespace(send_json=AsyncMock()) for i in (1, 2)}
        for i, socket in sockets.items():
            uid = state.player_list[i].user_id
            state.game_server.user_id_to_connection[uid] = SimpleNamespace(websocket=socket)
        delivered_before = {}
        async def reconnect(clock):
            delivered_before.update(state._ask_delivered_at)
            for i in (1, 2):
                await boardcast.reconnected_send_pending_ask_for_viewer(state, state.player_list[i].user_id, i)
            assert state._ask_delivered_at == delivered_before
            assert state.player_list[1].remaining_time == 17
        (responses, _), _ = await collect_at(state,
            [(8, 1, {'action_type': 'pass'}), (12, 2, {'action_type': 'peng'})],
            callbacks=[(10, reconnect), (11, reconnect)])
        assert responses[2]['action_type'] == 'peng'
        assert sockets[1].send_json.call_count == 0
        asks = [c.args[0]['ask_other_action_info'] for c in sockets[2].send_json.call_args_list]
        assert [(a['remaining_time'], a['step_remaining']) for a in asks] == [(15, 0), (14, 0)]
        assert state.player_list[1].remaining_time == 17
        assert state.player_list[2].remaining_time == 13
    asyncio.run(run())


def test_fresh_and_reconnect_wire_clocks_match_exact_deadline():
    state = ask_state(20, 5)
    player = state.player_list[1]
    player.remaining_time = 19.25
    state._ask_broadcast_time = 1000
    state._ask_delivered_at = {1: 1000}
    with patch('time.time', return_value=1002):
        assert state.claim_clock(player) == (20, 5)
        assert state.claim_clock(player, True) == (20, 3)
        assert _build_ask_deadlines(state, [1], 5, 1002)[1] == 1024.25
    with patch('time.time', return_value=1008):
        assert state.claim_clock(player, True) == (17, 0)
    assert player.remaining_time == 19.25


@pytest.mark.parametrize('window,method', [('normal', 'execute_timeout_cut'),
    ('before_draw', 'draw_current_player'), ('final_four', 'pass_final_tile')])
def test_timeout_uses_correct_changchun_phase_default(window, method):
    state = ask_state(0, 7, 'waiting_hand_action', ('cut',), seat=0)
    state.cc_window = window
    setattr(state, method, AsyncMock())
    _, elapsed = asyncio.run(collect_at(state, invoke=lambda current: current.wait_action()))
    getattr(state, method).assert_awaited_once()
    assert elapsed == 7 and state.player_list[0].remaining_time == 0


@pytest.mark.parametrize('bank,step', [(20, 5), (47, 9), (0, 7)])
def test_next_hand_restores_bank_without_changing_custom_step(bank, step):
    async def run():
        state = make_state(round_timer=bank, step_timer=step)
        state.db_manager = SimpleNamespace(store_changchun_game_record=lambda *args: 'timing-test')
        state.game_server.gamestate_manager.cleanup_game_state_complete = AsyncMock()
        starts = []
        async def play():
            starts.append([p.remaining_time for p in state.player_list])
            assert state.step_time == step
            for player in state.player_list:
                player.remaining_time = 0
        async def settle(_):
            return len(starts) == 4
        state._run_hand = play
        state._settle_hand = settle
        with patch('server.gamestate.game_changchun.lifecycle.broadcast_game_start', new=AsyncMock()), \
             patch('server.gamestate.game_changchun.lifecycle.broadcast_game_end', new=AsyncMock()):
            await state.game_loop_chinese()
        assert starts == [[bank] * 4] * 4
    asyncio.run(run())


@pytest.mark.parametrize('claim,tile,tiles', [
    ('peng', 25, [25,25,11,12,13,22,23,24,35,36,37,45,45]),
    ('chi_right', 11, [12,13,22,23,24,31,32,33,35,36,37,45,45]),
])
def test_chi_peng_then_discard_gets_one_new_step_and_keeps_remaining_bank(claim, tile, tiles):
    async def run():
        state = ask_state(actions=(claim, 'pass'))
        hand(state, 1, tiles)
        state.player_list[0].last_discarded_tile = tile
        state.player_list[0].discard_tiles = [tile]
        state.last_cut_tile = tile
        await collect_at(state, [(8, 1, {'action_type': claim})], invoke=lambda s: s.wait_action())
        assert state.current_player_index == 1 and state.game_status == 'onlycut_after_action'
        assert state.player_list[1].remaining_time == 17
        state.action_dict = state.after_claim_actions()
        state.game_status = 'waiting_hand_action'
        (responses, _), elapsed = await collect_at(state, [(6, 1, {'action_type': 'cut'})])
        assert responses[1]['action_type'] == 'cut' and elapsed == 6
        assert state.action_clock_manager.windows[1].bank == 17
        assert state.action_clock_manager.windows[1].step == 5
        assert state.player_list[1].remaining_time == 16
    asyncio.run(run())


def test_invalid_special_choice_retries_in_original_window_without_free_step_or_double_debit():
    async def run():
        from ...game_calculation.changchun.test_rules import READY
        state = ask_state(status='waiting_hand_action', actions=('cc_special', 'cut'), seat=0)
        hand(state, 0, READY + [28], drawn=28)
        await collect_at(state, [(8, 0, {'action_type': 'cc_special', 'target_tile': -1})],
                         invoke=lambda s: s.wait_action())
        assert state.game_status == 'waiting_hand_action'
        assert state.player_list[0].remaining_time == 17
        before = state.action_clock_manager.windows[0]
        clock = Clock()
        clock.value = 1009
        with patch('time.time', side_effect=clock.now), patch('time.monotonic', side_effect=clock.now):
            state.prepare_action_window()
            state.server_action_tick += 1
            begin_ask_round(state)
            note_ask_delivered(state, 0)
            assert state.action_clock_manager.windows[0] is before
            assert before.deadline == 1025
            assert state.action_clock(state.player_list[0]) == (16, 0)
            clock.value = 1010
            await state.action_queues[0].put({'action_type': 'cut'})
            state.action_events[0].set()
            responses, _ = await state.collect_action_responses()
            assert responses[0]['action_type'] == 'cut'
            assert state.player_list[0].remaining_time == 15
    asyncio.run(run())


def test_production_broadcast_admits_early_human_reply_and_preserves_per_seat_delivery():
    async def run():
        from ..public.ai.get_action import get_action
        state = ask_state()
        state.action_dict[2] = ['peng', 'pass']
        clock = Clock()
        captured = {i: [] for i in (1, 2)}
        state.game_server.players = {}
        for i in (1, 2):
            async def send(data, seat=i):
                captured[seat].append(data)
            conn = SimpleNamespace(user_id=state.player_list[i].user_id,
                                   websocket=SimpleNamespace(send_json=send))
            state.game_server.players[f'c{i}'] = conn
            state.game_server.user_id_to_connection[conn.user_id] = conn
        async def spectators(seat, response):
            if seat == 1:
                clock.value = 1008
                await get_action(state, 'c1', 'peng', False, None, None, None,
                                 action_tick=state.server_action_tick)
                # A later spectator/transport blocks the collector, not the reply.
                clock.value = 1030
        state.send_to_realtime_spectators = spectators
        with patch('time.time', side_effect=clock.now), patch('time.monotonic', side_effect=clock.now):
            state.prepare_action_window()
            await boardcast.broadcast_ask_other_action(state)
            clock.value = 1032
            await get_action(state, 'c2', 'pass', False, None, None, None,
                             action_tick=state.server_action_tick)
            responses, _ = await state.collect_action_responses()
        assert responses[1]['action_type'] == 'peng' and responses[2]['action_type'] == 'pass'
        assert [state.player_list[i].remaining_time for i in (1, 2)] == [17, 20]
        assert [state.action_clock_manager.windows[i].deadline for i in (1, 2)] == [1025, 1055]
        asks = [captured[i][0]['ask_other_action_info'] for i in (1, 2)]
        assert [(a['remaining_time'], a['step_remaining']) for a in asks] == [(20, 5), (20, 5)]
    asyncio.run(run())


@pytest.mark.parametrize('status,field', [('waiting_hand_action', 'ask_hand_action_info'),
    ('waiting_action_after_cut', 'ask_other_action_info'),
    ('waiting_action_qianggang', 'ask_other_action_info')])
@pytest.mark.parametrize('bank,step,elapsed,expected', [(20,5,2,(20,3)),
    (20,5,8,(17,0)), (0,7,6,(0,1)), (47,9,13,(43,0))])
def test_reconnect_and_realtime_spectator_use_same_remaining_bank_and_step(status, field, bank, step, elapsed, expected):
    async def run():
        seat = 0 if status == 'waiting_hand_action' else 1
        state = ask_state(bank, step, status, ('cut',) if seat == 0 else ('peng','pass'), seat)
        clock = Clock()
        sockets = [SimpleNamespace(send_json=AsyncMock()) for _ in range(2)]
        ids = [state.player_list[seat].user_id, 90001]
        state.game_server.user_id_to_connection = {
            uid: SimpleNamespace(websocket=socket) for uid, socket in zip(ids, sockets)
        }
        with patch('time.time', side_effect=clock.now), patch('time.monotonic', side_effect=clock.now):
            begin_ask_round(state)
            note_ask_delivered(state, seat)
            deadline = state.action_clock_manager.windows[seat].deadline
            clock.value += elapsed
            for uid in ids:
                await boardcast.reconnected_send_pending_ask_for_viewer(state, uid, seat)
            first = [socket.send_json.call_args.args[0][field] for socket in sockets]
            assert [(a['remaining_time'],a['step_remaining']) for a in first] == [expected] * 2
            assert state.player_list[seat].remaining_time == bank
            assert state.action_clock_manager.windows[seat].deadline == deadline
            note_ask_delivered(state, seat)
            assert state.action_clock_manager.windows[seat].deadline == deadline
    asyncio.run(run())


def test_duplicate_response_cannot_reenter_or_charge_completed_player():
    async def run():
        from ..public.ai.get_action import get_action
        state = ask_state()
        state.action_dict[2] = ['pass']
        state.game_server.players = {'c1': SimpleNamespace(user_id=state.player_list[1].user_id)}
        async def repeat(clock):
            count = state.action_queues[1].qsize()
            await get_action(state, 'c1', 'peng', False, None, None, None,
                             action_tick=state.server_action_tick)
            assert state.action_queues[1].qsize() == count
            assert state.player_list[1].remaining_time == 17
        await collect_at(state, [(8,1,{'action_type':'pass'}),(12,2,{'action_type':'pass'})],
                         callbacks=[(10,repeat)])
        assert state.player_list[1].remaining_time == 17
    asyncio.run(run())


def test_invalid_and_stale_queued_actions_do_not_close_or_tax_a_valid_window():
    state = ask_state()
    state.server_action_tick = 9
    (responses, _), elapsed = asyncio.run(collect_at(state, [
        (1,1,{'action_type':'not_allowed'}),
        (2,1,{'action_type':'peng','_action_tick':8}),
        (8,1,{'action_type':'peng','_action_tick':9}),
    ]))
    assert responses[1]['action_type'] == 'peng' and elapsed == 8
    assert state.player_list[1].remaining_time == 17


def test_client_supplied_receipt_metadata_cannot_save_a_late_action():
    state = ask_state()
    (responses, _), _ = asyncio.run(collect_at(state, [(26,1,{
        'action_type':'peng','_cc_received_at':1000,'_cc_window_serial':1,
    })], lag=30))
    assert not responses and state.player_list[1].remaining_time == 0


def test_action_at_exact_deadline_is_expired():
    state = ask_state()
    (responses, _), _ = asyncio.run(collect_at(state, [(25,1,{'action_type':'peng'})], lag=30))
    assert not responses and state.player_list[1].remaining_time == 0


def test_failed_delivery_does_not_charge_another_sockets_broadcast_delay():
    async def run():
        from . import timing
        from ..public import ask_timing
        clock = Clock()
        time_api = SimpleNamespace(time=clock.now, monotonic=clock.now)
        state = ask_state()
        with patch.object(timing,'time',time_api), patch.object(ask_timing,'time',time_api):
            begin_ask_round(state)
            clock.value = 1030  # This seat did not get a delivery callback.
            await state.action_queues[1].put({'action_type':'pass'})
            responses, _ = await state.collect_action_responses()
            assert responses[1]['action_type'] == 'pass'
            assert state.player_list[1].remaining_time == 20
            assert state.action_clock_manager.windows[1].deadline == 1055
    asyncio.run(run())


def test_wall_clock_adjustment_cannot_move_authoritative_deadline_or_bank():
    async def run():
        from . import timing
        from ..public import ask_timing
        state = ask_state()
        mono = Clock()
        wall = Clock()
        time_api = SimpleNamespace(time=wall.now, monotonic=mono.now)
        with patch.object(timing,'time',time_api), patch.object(ask_timing,'time',time_api):
            begin_ask_round(state)
            note_ask_delivered(state,1)
            mono.value = 1008
            wall.value = 500
            assert state.action_clock(state.player_list[1],True) == (17,0)
            await state.action_queues[1].put({'action_type':'pass'})
            responses, _ = await state.collect_action_responses()
            assert responses[1]['action_type'] == 'pass'
            assert state.player_list[1].remaining_time == 17
            assert state.action_clock_manager.windows[1].deadline == 1025
    asyncio.run(run())


@pytest.mark.parametrize('receipt,expected_elapsed', [(2,2),(None,8)])
def test_ready_confirmation_keeps_its_separate_presentation_timer(receipt, expected_elapsed):
    state = ask_state(47,9,'waiting_ready',('ready',),seat=0)
    state.player_list[0].remaining_time = 8
    receipts = [] if receipt is None else [(receipt,0,{'action_type':'ready'})]
    (responses, _), elapsed = asyncio.run(collect_at(state,receipts))
    assert elapsed == expected_elapsed
    assert bool(responses) == (receipt is not None)
    assert state.player_list[0].remaining_time == (8 if receipt is not None else 0)


def test_cancelled_collector_releases_waiters_and_owned_event_tasks():
    async def run():
        state = ask_state()
        begin_ask_round(state)
        note_ask_delivered(state,1)
        task = asyncio.create_task(state.collect_action_responses())
        await asyncio.sleep(0)
        task.cancel()
        with pytest.raises(asyncio.CancelledError):
            await task
        assert not state.waiting_players_list
        assert state.player_list[1].remaining_time == 20
        assert all(t.done() for t in state._lifecycle_tasks)
    asyncio.run(run())


def test_completed_window_projection_and_duplicate_settlement_preserve_bank():
    async def run():
        state = ask_state()
        await collect_at(state, [(8,1,{'action_type':'pass'})])
        assert state.action_clock(state.player_list[1],True) == (17,0)
        assert not state.is_action_pending(1)
        # A repeated completion notification must be idempotent.
        state.action_clock_manager._complete(1,1015)
        responses, _ = await state.collect_action_responses()
        assert not responses and state.player_list[1].remaining_time == 17
    asyncio.run(run())


def test_no_action_window_and_direct_zero_budget_collector_return_without_waiting():
    async def run():
        state = ask_state(0,0)
        assert state.is_action_pending(1) and not state.is_action_pending(3)
        responses, _ = await state.collect_action_responses()
        assert not responses and not state.waiting_players_list
        state.action_dict = {i:[] for i in range(4)}
        begin_ask_round(state)
        begin_ask_round(state)
        assert await state.collect_action_responses() == ({},{})
    asyncio.run(run())


def test_ready_reconnect_projection_does_not_add_room_step():
    state = ask_state(47,9,'waiting_ready',('ready',),seat=0)
    state.player_list[0].remaining_time = 8
    state._ask_delivered_at = {0:1000}
    with patch('time.time',return_value=1002):
        assert state.action_clock(state.player_list[0],True) == (6,0)


def test_queued_reply_from_previous_physical_window_cannot_close_new_window():
    async def run():
        from . import timing
        from ..public import ask_timing
        state = ask_state()
        clock = Clock()
        time_api = SimpleNamespace(time=clock.now,monotonic=clock.now)
        with patch.object(timing,'time',time_api), patch.object(ask_timing,'time',time_api):
            begin_ask_round(state)
            note_ask_delivered(state,1)
            clock.value = 1001
            await state.action_queues[1].put({'action_type':'peng'})
            state.cc_turn_serial += 1
            begin_ask_round(state)
            note_ask_delivered(state,1)
            clock.value = 1009
            await state.action_queues[1].put({'action_type':'pass'})
            responses, _ = await state.collect_action_responses()
            assert responses[1]['action_type'] == 'pass'
            assert state.player_list[1].remaining_time == 17
    asyncio.run(run())


def test_malformed_queue_item_does_not_close_or_debit_the_window():
    state = ask_state()
    (responses,_), elapsed = asyncio.run(collect_at(state,[(1,1,None),(8,1,{'action_type':'pass'})]))
    assert responses[1]['action_type'] == 'pass' and elapsed == 8
    assert state.player_list[1].remaining_time == 17
