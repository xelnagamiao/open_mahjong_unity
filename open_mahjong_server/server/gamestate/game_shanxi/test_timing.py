"""MIL Shanxi uses the room's step-then-bank contract, not a venue claim cap."""
import asyncio
import importlib
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .test_state import BASE, make_state, ready
from ..game_taiwan.boardcast import (
    broadcast_ask_hand_action, broadcast_ask_other_action,
    reconnected_send_pending_ask_for_viewer,
)
from ..game_taiwan.wait_action import _build_ask_deadlines, _collect_responses as legacy_collect
from ..public.ask_timing import begin_ask_round, note_ask_delivered, reconnect_clock
from ..public.ai.get_action import get_action
from ..public.next_game_round import next_game_round_classical_switchseat

wait_module = importlib.import_module('server.gamestate.game_taiwan.wait_action')
ask_module = importlib.import_module('server.gamestate.public.ask_timing')
shanxi_module = importlib.import_module('server.gamestate.game_shanxi.ShanxiGameState')


async def _collect_responses(state):
    collector = getattr(state, 'collect_action_responses', None)
    return await collector() if collector else await legacy_collect(state)


class Clock:
    def __init__(self):
        self.value = 0.0
        self.events = []
        self.observed_waits = []

    def monotonic(self):
        return 100.0 + self.value

    def time(self):
        return 1000.0 + self.value

    def advance(self, seconds):
        self.value += seconds

    def schedule(self, when, callback):
        self.events.append((when, callback))
        self.events.sort(key=lambda item: item[0])

    async def wait(self, tasks, timeout, return_when):
        # Yield already-set events without adding simulated thinking time.
        await asyncio.sleep(0)
        await asyncio.sleep(0)
        done = {task for task in tasks if task.done()}
        if done:
            return done, set(tasks) - done
        target = self.value + timeout
        self.observed_waits.append((self.value, timeout))
        if self.events and self.events[0][0] <= target + 1e-9:
            target = self.events[0][0]
            self.value = target
            _, callback = self.events.pop(0)
            result = callback()
            if asyncio.iscoroutine(result):
                await result
        else:
            self.value = target
        await asyncio.sleep(0)
        await asyncio.sleep(0)
        done = {task for task in tasks if task.done()}
        return done, set(tasks) - done


def state_at_clock(monkeypatch, bank=20, step=5, status='waiting_action_after_cut', actions=None):
    state = make_state()
    state.round_time, state.step_time = bank, step
    for p in state.player_list:
        p.remaining_time = bank
    state.game_status = status
    state.action_dict = actions or {0: [], 1: ['peng', 'pass'], 2: [], 3: []}
    state.server_action_tick = 7
    state.player_list[0].discard_tiles = [47]
    clock = Clock()
    monkeypatch.setattr(wait_module, 'time', clock)
    monkeypatch.setattr(ask_module, 'time', clock)
    if hasattr(shanxi_module, 'time'):
        monkeypatch.setattr(shanxi_module, 'time', clock)
    room_clock = getattr(state, 'room_action_clock', None)
    if room_clock is not None:
        room_clock.now = clock.monotonic
    monkeypatch.setattr(asyncio, 'wait', clock.wait)
    return state, clock


def begin_delivered(state, *indices):
    begin_ask_round(state)
    for index in indices:
        note_ask_delivered(state, index)


def enqueue(state, index, action='pass', **fields):
    state.action_queues[index].put_nowait({'action_type': action, '_action_tick': state.server_action_tick, **fields})
    state.action_events[index].set()


def claim_snapshot(state, player):
    hook = getattr(state, 'claim_clock', None)
    return hook(player, reconnecting=True) if hook else reconnect_clock(state, player)


@pytest.mark.parametrize('bank,step', [(20, 5), (11, 9), (0, 9), (11, 0), (0, 0)])
def test_claim_budget_is_actual_room_bank_plus_step(monkeypatch, bank, step):
    state, clock = state_at_clock(monkeypatch, bank, step)
    begin_delivered(state, 1)
    # This is the legacy helper; the enabled clock's window is additionally
    # exercised by the collection tests below.
    deadlines = _build_ask_deadlines(state, [1], step, clock.monotonic())
    assert deadlines[1] - clock.monotonic() == bank + step
    assert claim_snapshot(state, state.player_list[1]) == (bank, step)


@pytest.mark.parametrize('elapsed,expected', [(0, (20, 5)), (2, (20, 3)),
                                           (5, (20, 0)), (8, (17, 0)), (25, (0, 0))])
def test_claim_snapshot_preserves_original_budget(monkeypatch, elapsed, expected):
    state, clock = state_at_clock(monkeypatch)
    begin_delivered(state, 1)
    clock.advance(elapsed)
    assert claim_snapshot(state, state.player_list[1]) == expected
    assert state.player_list[1].remaining_time == 20
    assert state._ask_delivered_at[1] == 1000


@pytest.mark.parametrize('status,index,action', [
    ('waiting_action_after_cut', 1, 'pass'),
    ('waiting_hand_action', 0, 'cut'),
])
@pytest.mark.parametrize('elapsed,expected', [(4, 20), (5, 20), (8, 17)])
def test_receipt_charges_step_then_bank(monkeypatch, status, index, action, elapsed, expected):
    async def run():
        state, clock = state_at_clock(monkeypatch, status=status,
                                     actions={i: [action] if i == index else [] for i in range(4)})
        begin_delivered(state, index)
        clock.schedule(elapsed, lambda: enqueue(state, index, action))
        responses, allowed = await _collect_responses(state)
        assert responses[index]['action_type'] == action
        assert allowed[index] == [action]
        assert state.player_list[index].remaining_time == expected
        assert state.waiting_players_list == []
    asyncio.run(run())


def test_fast_response_during_broadcast_is_accepted(monkeypatch):
    async def run():
        state, clock = state_at_clock(monkeypatch)
        sockets = {}
        state.game_server.players = {}
        for p in state.player_list:
            state.game_server.players[f'c{p.player_index}'] = SimpleNamespace(user_id=p.user_id)
            async def send(payload, player=p):
                sockets[player.player_index] = payload
                if player.player_index == 1:
                    # A real protocol receipt can run before send_json returns.
                    await get_action(state, 'c1', 'pass', False, None, None, None,
                                     action_tick=state.server_action_tick)
            state.game_server.user_id_to_connection[p.user_id] = SimpleNamespace(websocket=SimpleNamespace(send_json=send))
        state.send_to_realtime_spectators = AsyncMock()
        await broadcast_ask_other_action(state)
        assert not state.action_queues[1].empty()
        responses, _ = await _collect_responses(state)
        assert responses[1]['action_type'] == 'pass'
        info = sockets[1]['ask_other_action_info']
        assert (info['remaining_time'], info.get('step_remaining', state.step_time)) == (20, 5)
    asyncio.run(run())


def test_timely_queued_reply_survives_delayed_collection(monkeypatch):
    async def run():
        state, clock = state_at_clock(monkeypatch)
        begin_delivered(state, 1)
        clock.advance(1)
        enqueue(state, 1)
        # Another connection blocks the broadcaster for longer than our entire
        # budget. Timeliness is about arrival, not consumer scheduling.
        clock.advance(30)
        responses, _ = await _collect_responses(state)
        assert responses[1]['action_type'] == 'pass'
        assert state.player_list[1].remaining_time == 20
    asyncio.run(run())


@pytest.mark.parametrize('bank,step', [(20, 5), (11, 9), (0, 9), (11, 0), (0, 0)])
def test_claim_timeout_exhausts_budget_and_passes(monkeypatch, bank, step):
    async def run():
        state, clock = state_at_clock(monkeypatch, bank, step)
        begin_delivered(state, 1)
        await state.wait_action()
        assert clock.value == bank + step
        assert state.player_list[1].remaining_time == 0
        assert 47 in state.player_list[1].passed_pungs
        assert state.game_status == 'deal_card'
    asyncio.run(run())


def test_reconnect_during_pending_ask_does_not_reset_or_charge(monkeypatch):
    async def run():
        state, clock = state_at_clock(monkeypatch)
        begin_delivered(state, 1)
        p = state.player_list[1]
        socket = SimpleNamespace(send_json=AsyncMock())
        state.game_server.user_id_to_connection[p.user_id] = SimpleNamespace(websocket=socket)
        clock.advance(8)
        for _ in range(3):
            await reconnected_send_pending_ask_for_viewer(state, p.user_id, 1)
        payloads = [call.args[0]['ask_other_action_info'] for call in socket.send_json.await_args_list]
        assert all((info['remaining_time'], info['step_remaining'], info['action_tick']) == (17, 0, 7) for info in payloads)
        assert p.remaining_time == 20
        assert state._ask_delivered_at[1] == 1000
        enqueue(state, 1)
        await _collect_responses(state)
        assert p.remaining_time == 17
    asyncio.run(run())


def test_reconnect_after_reply_is_closed_while_another_seat_waits(monkeypatch):
    async def run():
        state, clock = state_at_clock(monkeypatch,
            actions={0: [], 1: ['pass'], 2: ['pass'], 3: []})
        begin_delivered(state, 1, 2)
        p = state.player_list[1]
        socket = SimpleNamespace(send_json=AsyncMock())
        state.game_server.user_id_to_connection[p.user_id] = SimpleNamespace(websocket=socket)
        clock.schedule(8, lambda: enqueue(state, 1))
        async def reconnect():
            assert p.remaining_time == 17
            assert 1 not in state.waiting_players_list
            await reconnected_send_pending_ask_for_viewer(state, p.user_id, 1)
            socket.send_json.assert_not_awaited()
        clock.schedule(9, reconnect)
        clock.schedule(12, lambda: enqueue(state, 2))
        responses, _ = await _collect_responses(state)
        assert set(responses) == {1, 2}
        assert [state.player_list[i].remaining_time for i in (1, 2)] == [17, 13]
    asyncio.run(run())


@pytest.mark.parametrize('keep', [False, True])
def test_next_hand_restores_configured_bank_and_fresh_step(monkeypatch, keep):
    state, clock = state_at_clock(monkeypatch, bank=11, step=9)
    begin_delivered(state, 1)
    for p in state.player_list:
        p.remaining_time = 0
    next_game_round_classical_switchseat(state, keep_current_round=keep, keep_dealer_seat=keep)
    state._reset_hand_runtime()
    assert all(p.remaining_time == 11 for p in state.player_list)
    state.game_status = 'waiting_hand_action'
    state.action_dict = {0: ['cut'], 1: [], 2: [], 3: []}
    state.server_action_tick += 1
    begin_delivered(state, 0)
    assert reconnect_clock(state, state.player_list[0]) == (11, 9)


def test_fractional_overtime_accumulates_across_windows(monkeypatch):
    async def run():
        state, clock = state_at_clock(monkeypatch)
        for expected in (19.1, 18.2):
            state.action_dict = {0: [], 1: ['pass'], 2: [], 3: []}
            state.server_action_tick += 1
            begin_delivered(state, 1)
            clock.advance(5.9)
            enqueue(state, 1)
            await _collect_responses(state)
            assert state.room_action_clock.bank(state.player_list[1]) == pytest.approx(expected)
        assert state.player_list[1].remaining_time == 19
        state.action_dict = {0: [], 1: ['pass'], 2: [], 3: []}
        state.server_action_tick += 1
        begin_delivered(state, 1)
        window = state.room_action_clock.windows[1]
        assert window.deadline - clock.monotonic() == pytest.approx(23.2)
        clock.advance(0.2)
        bank, step = state.claim_clock(state.player_list[1], True)
        assert bank + step == 23  # do not independently ceil to 19+5
    asyncio.run(run())


@pytest.mark.parametrize('elapsed,expected', [(4, (0, 5)), (9, (0, 0))])
def test_zero_bank_custom_step_survives_reconnect(monkeypatch, elapsed, expected):
    state, clock = state_at_clock(monkeypatch, bank=0, step=9)
    begin_delivered(state, 1)
    clock.advance(elapsed)
    assert state.action_clock(state.player_list[1], True) == expected


def test_validated_receipt_freezes_prompt_before_collector_runs(monkeypatch):
    async def run():
        state, clock = state_at_clock(monkeypatch)
        begin_delivered(state, 1)
        p = state.player_list[1]
        socket = SimpleNamespace(send_json=AsyncMock())
        state.game_server.user_id_to_connection[p.user_id] = SimpleNamespace(websocket=socket)
        state.game_server.players = {'c1': SimpleNamespace(user_id=p.user_id)}
        clock.advance(8)
        await get_action(state, 'c1', 'pass', False, None, None, None, action_tick=7)
        clock.advance(30)
        assert state.action_clock(p, True) == (17, 0)
        await reconnected_send_pending_ask_for_viewer(state, p.user_id, 1)
        socket.send_json.assert_not_awaited()
        await get_action(state, 'c1', 'peng', False, None, None, None, action_tick=7)
        assert state.action_queues[1].qsize() == 1
        responses, _ = await _collect_responses(state)
        assert responses[1]['action_type'] == 'pass'
        assert p.remaining_time == 17
        assert await _collect_responses(state) == ({}, {})
        assert p.remaining_time == 17
    asyncio.run(run())


@pytest.mark.parametrize('kind', ['late', 'stale', 'illegal', 'forged_receipt'])
def test_bad_receipts_do_not_reopen_or_extend_window(monkeypatch, kind):
    async def run():
        state, clock = state_at_clock(monkeypatch, bank=2, step=3)
        begin_delivered(state, 1)
        if kind == 'late':
            clock.advance(5.1)
            enqueue(state, 1)
        elif kind == 'stale':
            clock.advance(1)
            enqueue(state, 1, _action_tick=6)
        elif kind == 'illegal':
            clock.advance(1)
            enqueue(state, 1, 'chi_left')
        else:
            clock.advance(5.1)
            enqueue(state, 1, _received_at=100)
        responses, _ = await _collect_responses(state)
        assert responses == {}
        assert state.player_list[1].remaining_time == 0
        assert clock.value >= 5
    asyncio.run(run())


def test_each_player_delivery_has_independent_deadline_and_bank(monkeypatch):
    async def run():
        state, clock = state_at_clock(monkeypatch,
            actions={0: [], 1: ['pass'], 2: ['pass'], 3: []})
        begin_delivered(state, 1)
        clock.advance(2)
        note_ask_delivered(state, 2)
        clock.schedule(8, lambda: enqueue(state, 1))
        clock.schedule(10, lambda: enqueue(state, 2))
        await _collect_responses(state)
        assert [state.player_list[i].remaining_time for i in (1, 2)] == [17, 17]
        assert state.room_action_clock.windows[2].deadline - state.room_action_clock.windows[1].deadline == 2
    asyncio.run(run())


@pytest.mark.parametrize('phase,index', [('waiting_hand_action', 0), ('waiting_action_after_cut', 1)])
def test_reconnect_and_realtime_spectator_receive_same_remaining_clock(monkeypatch, phase, index):
    async def run():
        state, clock = state_at_clock(monkeypatch, status=phase,
            actions={i: ['cut'] if phase == 'waiting_hand_action' and i == index
                     else ['peng', 'pass'] if i == index else [] for i in range(4)})
        begin_delivered(state, index)
        original_origin = state.room_action_clock.windows[index].delivered_at
        sockets = {}
        for uid in (state.player_list[index].user_id, 999):
            sockets[uid] = SimpleNamespace(send_json=AsyncMock())
            state.game_server.user_id_to_connection[uid] = SimpleNamespace(websocket=sockets[uid])
        clock.advance(8)
        for uid in sockets:
            await reconnected_send_pending_ask_for_viewer(state, uid, index)
            payload = sockets[uid].send_json.await_args.args[0]
            info = payload['ask_hand_action_info' if phase == 'waiting_hand_action' else 'ask_other_action_info']
            assert (info['remaining_time'], info['step_remaining'], info['action_tick']) == (17, 0, 7)
        assert state.room_action_clock.windows[index].delivered_at == original_origin
        assert state.player_list[index].remaining_time == 20
    asyncio.run(run())


def test_hand_first_packet_has_same_budget_as_claim_packet(monkeypatch):
    async def run():
        state, clock = state_at_clock(monkeypatch, status='waiting_hand_action',
                                     actions={0: ['cut'], 1: [], 2: [], 3: []})
        p = state.player_list[0]
        socket = SimpleNamespace(send_json=AsyncMock())
        state.game_server.user_id_to_connection[p.user_id] = SimpleNamespace(websocket=socket)
        state.send_to_realtime_spectators = AsyncMock()
        await broadcast_ask_hand_action(state)
        info = socket.send_json.await_args.args[0]['ask_hand_action_info']
        assert (info['remaining_time'], info['step_remaining']) == (20, 5)
        assert state.room_action_clock.windows[0].deadline - clock.monotonic() == 25
    asyncio.run(run())


def test_wall_clock_adjustment_does_not_change_monotonic_window_or_charge(monkeypatch):
    async def run():
        state, clock = state_at_clock(monkeypatch)
        begin_delivered(state, 1)
        deadline = state.room_action_clock.windows[1].deadline
        clock.advance(8)
        # NTP / a manual clock change affects the legacy display clock only.
        monkeypatch.setattr(ask_module, 'time', SimpleNamespace(time=lambda: -3600.0))
        assert state.action_clock(state.player_list[1]) == (17, 0)
        enqueue(state, 1)
        await _collect_responses(state)
        assert state.player_list[1].remaining_time == 17
        assert state.room_action_clock.windows[1].deadline == deadline
    asyncio.run(run())


def test_ready_declaration_consumes_own_hand_window(monkeypatch):
    async def run():
        state, clock = state_at_clock(monkeypatch, status='waiting_hand_action',
                                     actions={0: ['cut', 'riichi_cut'], 1: [], 2: [], 3: []})
        p = state.player_list[0]
        p.hand_tiles = BASE + [47, 16]
        p.has_draw_slot, p.last_drawn_tile = True, 16
        p.discard_tiles = []
        begin_delivered(state, 0)
        clock.schedule(8, lambda: enqueue(state, 0, 'riichi_cut', TileId=16, cutClass=True, cutIndex=13))
        await state.wait_action()
        assert p.declared_ready and p.discard_tiles == [0]
        assert p.remaining_time == 17
        assert not any(state.action_dict.values())
    asyncio.run(run())


def test_pung_and_followup_cut_get_separate_steps_but_share_bank(monkeypatch):
    async def run():
        state, clock = state_at_clock(monkeypatch)
        state.player_list[0].discard_tiles = [11]
        p = state.player_list[1]
        p.hand_tiles = [11, 11, 12, 13, 21, 21, 21, 21, 31, 32, 33, 47, 47]
        state.action_dict = state.check_discard_actions(11)
        begin_delivered(state, 1)
        clock.schedule(8, lambda: enqueue(state, 1, 'peng'))
        await state.wait_action()
        assert p.remaining_time == 17 and p.combination_tiles == ['k11']
        assert state.game_status == 'onlycut_after_action'
        state.action_dict = state.after_claim_actions()
        state.game_status = 'waiting_hand_action'
        state.server_action_tick += 1
        begin_delivered(state, 1)
        assert state.action_clock(p) == (17, 5)
        clock.schedule(12, lambda: enqueue(state, 1, 'cut', TileId=47, cutClass=False, cutIndex=9))
        await state.wait_action()
        assert p.remaining_time == 17  # four seconds entirely in the new step
        assert p.discard_tiles[-1] == 47
    asyncio.run(run())


@pytest.mark.parametrize('kind', ['direct', 'concealed', 'added'])
def test_kong_replacement_gets_new_hand_window_with_same_bank(monkeypatch, kind):
    async def run():
        index = 1 if kind == 'direct' else 0
        action = {'direct': 'gang', 'concealed': 'angang', 'added': 'jiagang'}[kind]
        phase = 'waiting_action_after_cut' if kind == 'direct' else 'waiting_hand_action'
        state, clock = state_at_clock(monkeypatch, status=phase,
                                     actions={i: [action] if i == index else [] for i in range(4)})
        p = state.player_list[index]
        if kind == 'direct':
            state.player_list[0].discard_tiles = [11]
            p.hand_tiles = [11]*3 + [21,22,23,24,25,26,31,32,33,47]
        elif kind == 'concealed':
            p.hand_tiles = [11]*4 + [21,22,23,24,25,26,31,32,33,47]
            p.has_draw_slot, p.last_drawn_tile = True, 11
        else:
            p.hand_tiles = [47,11,12,13,21,22,23,31,32,33,19]
            p.combination_tiles, p.combination_mask = ['k47'], [[0,47,1,47,0,47]]
            p.has_draw_slot, p.last_drawn_tile = True, 19
        begin_delivered(state, index)
        clock.schedule(8, lambda: enqueue(state, index, action, target_tile=47 if kind == 'added' else 11))
        await state.wait_action()
        assert state.game_status == 'deal_card_after_gang' and p.remaining_time == 17
        assert not any(state.check_added_kong_actions(47).values())  # no rob-kong window in Shanxi
        state.tiles_list[-2] = 18
        await state._deal_supplement()
        assert state.game_status == 'waiting_hand_action'
        state.server_action_tick += 1
        begin_delivered(state, index)
        assert state.action_clock(p) == (17, 5)
        assert p.last_drawn_tile == 18
        assert 'buhua' not in state.action_dict[index]
    asyncio.run(run())


@pytest.mark.parametrize('draw_slot,locked,expected', [(False, False, 47), (True, False, 16), (True, True, 16)])
def test_hand_timeout_uses_correct_default_discard(monkeypatch, draw_slot, locked, expected):
    async def run():
        state, clock = state_at_clock(monkeypatch, bank=2, step=3, status='waiting_hand_action',
                                     actions={0: ['cut'], 1: [], 2: [], 3: []})
        p = state.player_list[0]
        p.hand_tiles = BASE + [47, 16]
        p.has_draw_slot, p.last_drawn_tile = draw_slot, 16 if draw_slot else None
        if locked:
            p.declared_ready = p.ready_locked = True
        state.opening_dealer_action = not draw_slot
        begin_delivered(state, 0)
        await state.wait_action()
        assert clock.value == 5 and p.remaining_time == 0
        assert p.discard_tiles[-1] == expected
    asyncio.run(run())


@pytest.mark.parametrize('phase', ['self', 'ron'])
def test_forced_win_survives_missing_response_and_timeout(monkeypatch, phase):
    async def run():
        index = 0 if phase == 'self' else 1
        action = 'hu_self' if phase == 'self' else 'hu_first'
        state, clock = state_at_clock(monkeypatch, bank=2, step=3,
            status='waiting_hand_action' if phase == 'self' else 'waiting_action_after_cut',
            actions={i: [action] if i == index else [] for i in range(4)})
        p = state.player_list[index]
        ready(p, BASE + ([47, 47] if phase == 'self' else [47]))
        if phase == 'self':
            p.has_draw_slot, p.last_drawn_tile = True, 47
        else:
            state.action_dict = state.check_discard_actions(47)
        begin_delivered(state, index)
        await state.wait_action()
        assert state.game_status == 'END' and state.pending_winners[0]['index'] == index
    asyncio.run(run())
