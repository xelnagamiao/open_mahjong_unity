"""Shanxi room clocks using the shared Taiwan-derived response dispatch.

One immutable bank snapshot per ask. Server queue receipts, rather than the
consumer's scheduling time, decide timeliness and the charge. The displayed
integer fields round the total once; fractional overtime remains in the bank.
"""
from __future__ import annotations

import asyncio
import math
import time
from dataclasses import dataclass

from ..public.lifecycle import start_owned_task


@dataclass
class ActionWindow:
    bank: float
    step: float
    delivered_at: float | None = None
    closed: bool = False
    received_at: float | None = None

    @property
    def deadline(self):
        return None if self.delivered_at is None else self.delivered_at + self.step + self.bank


class TimedActionQueue(asyncio.Queue):
    """Receipts are generated inside the server, never accepted from clients."""
    def __init__(self, clock, index):
        super().__init__()
        self.clock, self.index = clock, index

    def put_nowait(self, item):
        if isinstance(item, dict):
            item = {**item, '_received_at': self.clock.now()}
            self.clock.received(self.index, item)
        return super().put_nowait(item)


class RoomActionClock:
    # Shanxi has no chow, flower or rob-kong response phase.
    statuses = frozenset(('waiting_hand_action', 'waiting_action_after_cut'))

    def __init__(self, state):
        self.state = state
        self.now = time.monotonic
        self.windows = {}
        self.banks = {}
        self.tick = None
        self.began_at = None

    def active(self):
        return self.state.game_status in self.statuses

    def reset_hand(self):
        self.windows.clear()
        self.banks.clear()
        self.tick = self.began_at = None
        for player in self.state.player_list:
            player.remaining_time = self.state.round_time

    def bank(self, player):
        public = max(0, player.remaining_time)
        saved = self.banks.get(id(player))
        # Honor a new round/configuration and legacy external setters.
        return float(public) if saved is None or saved[0] != public else saved[1]

    def begin(self):
        self.tick = self.state.server_action_tick
        self.began_at = self.now()
        self.windows = {}
        if not self.active():
            return
        self.windows = {i: ActionWindow(self.bank(self.state.player_list[i]),
                                        max(0.0, float(self.state.step_time)))
                        for i, actions in self.state.action_dict.items() if actions}
        # Open receipt permission before the first websocket send, not after
        # every other seat and spectator has finished receiving the broadcast.
        self.state.waiting_players_list = list(self.windows)

    def delivered(self, index):
        window = self.windows.get(index)
        if window is not None and window.delivered_at is None:
            window.delivered_at = self.now()

    def is_pending(self, index):
        window = self.windows.get(index)
        return window is not None and not window.closed and window.received_at is None

    def received(self, index, data):
        """Freeze a validated receipt before another websocket delays collection."""
        window = self.windows.get(index)
        if not self.active() or window is None or window.closed or window.received_at is not None:
            return
        tick = data.get('_action_tick')
        if tick is not None and tick != self.tick:
            return
        if data.get('action_type') not in self.state.action_dict.get(index, ()):
            return
        received_at = data['_received_at']
        # A reply can arrive before send_json's await returns; its presence
        # proves the ask was already delivered. Never move that anchor later.
        if window.delivered_at is None:
            window.delivered_at = received_at
        if received_at >= window.deadline:
            return
        window.received_at = received_at
        if index in self.state.waiting_players_list:
            self.state.waiting_players_list.remove(index)

    @staticmethod
    def _ceil(value):
        return max(0, math.ceil(value - 1e-9))

    def snapshot(self, player):
        window = self.windows.get(player.player_index)
        if window is None or self.tick != self.state.server_action_tick:
            return self._ceil(self.bank(player)), max(0, self.state.step_time)
        if window.closed:
            return self._ceil(self.bank(player)), 0
        timestamp = self.now() if window.received_at is None else window.received_at
        elapsed = 0 if window.delivered_at is None else max(0, timestamp - window.delivered_at)
        bank = max(0, window.bank - max(0, elapsed - window.step))
        if window.received_at is not None:
            return self._ceil(bank), 0
        total = max(0, window.bank + window.step - elapsed)
        remaining = self._ceil(bank)
        # Rounding both halves independently can add an extra full second.
        return remaining, max(0, self._ceil(total) - remaining)

    def _close(self, index, pending, received_at=None):
        window = self.windows[index]
        if window.closed:
            return
        elapsed = window.bank + window.step if received_at is None else max(0, received_at - window.delivered_at)
        bank = max(0.0, window.bank - max(0.0, elapsed - window.step))
        player = self.state.player_list[index]
        player.remaining_time = self._ceil(bank)
        self.banks[id(player)] = (player.remaining_time, bank)
        window.closed = True
        pending.discard(index)
        self.state.waiting_players_list = sorted(i for i in pending if self.is_pending(i))
        self.state.action_dict[index] = []
        self.state.action_events[index].clear()

    async def collect(self):
        state = self.state
        allowed = {i: list(a) for i, a in state.action_dict.items() if a}
        if self.tick != state.server_action_tick or not self.windows:
            self.begin()
            # No transport delivery can occur for an isolated unit invocation
            # or a failed/disconnected send. Keep that fallback bounded.
            for i in allowed:
                self.delivered(i)
        else:
            for i in allowed:
                if self.windows[i].delivered_at is None:
                    self.windows[i].delivered_at = self.began_at
        pending = {i for i in allowed if not self.windows[i].closed}
        state.waiting_players_list = sorted(i for i in pending if self.is_pending(i))
        responses = {}
        try:
            while pending:
                for index in tuple(pending):
                    queue = state.action_queues[index]
                    window = self.windows[index]
                    while not queue.empty():
                        data = queue.get_nowait()
                        if not isinstance(data, dict) or data.get('action_type') not in allowed[index]:
                            continue
                        tick = data.get('_action_tick')
                        if tick is not None and tick != self.tick:
                            continue
                        received = data.get('_received_at', self.now())
                        if received >= window.deadline:
                            continue
                        responses[index] = dict(data)
                        self._close(index, pending, received)
                        break
                    # Check receipts before timeout: a timely queued answer
                    # stays valid even if another websocket stalls collection.
                    if index in pending and self.now() >= window.deadline:
                        self._close(index, pending)
                    if index in pending:
                        state.action_events[index].clear()
                if not pending:
                    break
                tasks = {start_owned_task(state, state.action_events[i].wait()) for i in pending}
                try:
                    timeout = min(1.0, max(0.0, min(self.windows[i].deadline for i in pending) - self.now()))
                    _, unfinished = await asyncio.wait(tasks, timeout=timeout, return_when=asyncio.FIRST_COMPLETED)
                finally:
                    unfinished = {task for task in tasks if not task.done()}
                    for task in unfinished:
                        task.cancel()
                    if unfinished:
                        await asyncio.gather(*unfinished, return_exceptions=True)
        finally:
            state.waiting_players_list = []
        return responses, allowed
