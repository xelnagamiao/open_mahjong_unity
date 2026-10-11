"""Defensive clock paths must preserve the same room budget and receipt rules."""
import asyncio

import pytest

from .test_timing import begin_delivered, enqueue, state_at_clock
from ..public.ask_timing import begin_ask_round


@pytest.mark.parametrize('bank,step', [(20, 5), (11, 9), (0, 9)])
def test_missing_broadcast_is_bounded_by_the_actual_room_budget(monkeypatch, bank, step):
    async def run():
        state, clock = state_at_clock(monkeypatch, bank=bank, step=step)
        responses, allowed = await state.collect_action_responses()
        assert responses == {} and allowed == {1: ['peng', 'pass']}
        assert clock.value == bank + step
        assert state.player_list[1].remaining_time == 0
        assert state.room_action_clock.snapshot(state.player_list[1]) == (0, 0)
        assert state.waiting_players_list == []
    asyncio.run(run())


def test_missing_delivery_callback_does_not_restart_the_existing_window(monkeypatch):
    async def run():
        state, clock = state_at_clock(monkeypatch)
        begin_ask_round(state)
        origin = clock.monotonic()
        clock.advance(8)
        clock.schedule(9, lambda: enqueue(state, 1))
        responses, _ = await state.collect_action_responses()
        window = state.room_action_clock.windows[1]
        assert responses[1]['action_type'] == 'pass'
        assert window.delivered_at == origin and window.deadline == origin + 25
        assert state.player_list[1].remaining_time == 16
    asyncio.run(run())


@pytest.mark.parametrize('candidate', [None, 'pass', {'action_type': 'chi_left'},
                                     {'action_type': 'pass', '_action_tick': 6}])
def test_invalid_queued_receipt_cannot_freeze_or_steal_a_valid_reply(monkeypatch, candidate):
    async def run():
        state, clock = state_at_clock(monkeypatch)
        begin_delivered(state, 1)
        clock.advance(1)
        state.action_queues[1].put_nowait(candidate)
        state.action_events[1].set()
        assert state.room_action_clock.is_pending(1)
        assert state.action_clock(state.player_list[1], True) == (20, 4)
        clock.schedule(8, lambda: enqueue(state, 1))
        responses, _ = await state.collect_action_responses()
        assert responses[1]['action_type'] == 'pass'
        assert state.player_list[1].remaining_time == 17
        clock.advance(40)
        assert state.action_clock(state.player_list[1], True) == (17, 0)
        assert not state.is_action_pending(1)
    asyncio.run(run())


def test_late_receipt_does_not_remove_the_player_before_timeout_collection(monkeypatch):
    async def run():
        state, clock = state_at_clock(monkeypatch, bank=2, step=3)
        begin_delivered(state, 1)
        clock.advance(5)
        enqueue(state, 1)
        assert state.room_action_clock.is_pending(1)
        assert state.waiting_players_list == [1]
        assert await state.collect_action_responses() == ({}, {1: ['peng', 'pass']})
        assert state.action_clock(state.player_list[1], True) == (0, 0)
    asyncio.run(run())


def test_empty_action_window_leaves_banks_and_waiters_untouched(monkeypatch):
    async def run():
        state, _ = state_at_clock(monkeypatch)
        state.action_dict = {i: [] for i in range(4)}
        assert await state.collect_action_responses() == ({}, {})
        assert state.room_action_clock.windows == {}
        assert state.waiting_players_list == []
        assert all(p.remaining_time == 20 for p in state.player_list)
    asyncio.run(run())


def test_result_confirmation_does_not_open_a_thinking_step_or_charge_the_bank(monkeypatch):
    async def run():
        state, clock = state_at_clock(monkeypatch, bank=20, step=9,
            status='waiting_ready', actions={0: ['ready'], 1: [], 2: [], 3: []})
        begin_ask_round(state)
        assert state.room_action_clock.windows == {}
        clock.advance(8)
        assert state.action_clock(state.player_list[0], True) == (12, 0)
        clock.schedule(10, lambda: enqueue(state, 0, 'ready'))
        responses, _ = await state.collect_action_responses()
        assert responses[0]['action_type'] == 'ready'
        assert state.player_list[0].remaining_time == 20
        assert state.room_action_clock.banks == {}
    asyncio.run(run())
