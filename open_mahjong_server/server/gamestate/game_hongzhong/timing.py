"""Hongzhong action windows: configured step first, then this hand's bank.

An immutable opening balance and first delivery belong to the physical decision,
not to each broadcast or reconnect. Queue arrival times keep a timely reply valid
even if another viewer's send delays the response collector.
"""
import asyncio
import math
import time
from collections import deque
from dataclasses import dataclass, field

from ..public.lifecycle import start_owned_task

PHASES = frozenset(('waiting_hand_action', 'waiting_action_after_cut'))


def seconds(value):
    return max(0, math.ceil(value - 1e-9))


class TimedActionQueue(asyncio.Queue):
    def __init__(self, clock):
        super().__init__()
        self.clock = clock
        self.arrivals = deque()
        self.last_received_at = None

    def put_nowait(self, item):
        super().put_nowait(item)
        self.arrivals.append(self.clock.now())

    def get_nowait(self):
        item = super().get_nowait()
        self.last_received_at = self.arrivals.popleft()
        return item


@dataclass
class Window:
    key: tuple
    created: float
    step: float
    banks: dict
    delivered: dict = field(default_factory=dict)
    resolved: set = field(default_factory=set)
    broadcasted: bool = False
    prepared: bool = False


class ActionClock:
    def __init__(self, state):
        self.state = state
        self.now = time.monotonic
        self.window = None
        self.balances = {}

    def reset(self):
        self.window = None
        self.balances.clear()

    def __call__(self, player, reconnecting=False):
        projected = self.project(player)
        if projected is not None:
            return projected
        if reconnecting:
            from ..public.ask_timing import reconnect_clock
            return reconnect_clock(self.state, player)
        return player.remaining_time, None

    def key(self):
        s = self.state
        return (s.round_index, s.game_status, s.current_player_index,
                tuple(s.tiles_list),
                tuple((tuple(sorted(p.hand_tiles)), tuple(p.combination_tiles),
                       tuple(p.discard_tiles), p.has_draw_slot, p.last_drawn_tile)
                      for p in s.player_list))

    def ensure(self):
        if self.state.game_status not in PHASES:
            return None
        key = self.key()
        if self.window is None or self.window.key != key:
            banks = {p.player_index: self.balances.setdefault(
                p.player_index, max(0.0, float(p.remaining_time)))
                for p in self.state.player_list}
            self.window = Window(key, self.now(), max(0.0, float(self.state.step_time)), banks)
        return self.window

    def begin(self):
        window = self.ensure()
        if window is None:
            return True
        fresh = not window.broadcasted
        window.broadcasted = True
        return fresh

    def delivered(self, index):
        window = self.ensure()
        if window is not None:
            window.delivered.setdefault(index, self.now())

    def deadline(self, index):
        window = self.ensure()
        return window.delivered.get(index, window.created) + window.step + window.banks[index]

    def project(self, player):
        window = self.ensure()
        if window is None:
            return None
        index = player.player_index
        if index in window.resolved:
            return seconds(self.balances[index]), 0
        # Later seats may be reached after another player's spectator send stalls.
        # Their first payload and server deadline must both contain a full budget.
        elapsed = max(0.0, self.now() - window.delivered[index]) if index in window.delivered else 0.0
        return (seconds(window.banks[index] - max(0.0, elapsed - window.step)),
                seconds(window.step - elapsed))

    def charge(self, index, received_at):
        window = self.ensure()
        elapsed = max(0.0, received_at - window.delivered.get(index, window.created))
        bank = max(0.0, window.banks[index] - max(0.0, elapsed - window.step))
        self.balances[index] = bank
        self.state.player_list[index].remaining_time = seconds(bank)
        window.resolved.add(index)


async def collect_responses(state):
    clock = state.action_clock
    window = clock.ensure()
    allowed = {index: list(actions) for index, actions in state.action_dict.items() if actions}
    pending = set(allowed) - window.resolved
    state.waiting_players_list = sorted(pending)
    responses = {}
    try:
        while pending:
            # Read received-at first, before comparing the collector's current time.
            # A broadcast/spectator send may finish after an already received reply.
            for index in tuple(pending):
                queue = state.action_queues[index]
                while not queue.empty():
                    candidate = queue.get_nowait()
                    received = getattr(queue, 'last_received_at', None)
                    received = clock.now() if received is None else received
                    if (not isinstance(candidate, dict)
                            or candidate.get('action_type') not in allowed[index]
                            or received >= clock.deadline(index)):
                        continue
                    responses[index] = dict(candidate)
                    clock.charge(index, received)
                    pending.remove(index)
                    if state.game_status == 'waiting_action_after_cut':
                        # While other responders remain, reconnect must not reask
                        # an already accepted pass/claim or charge it a second time.
                        state.action_dict[index] = []
                    break
                state.action_events[index].clear()
                if index in pending and clock.now() >= clock.deadline(index):
                    clock.charge(index, clock.deadline(index))
                    pending.remove(index)
                    state.action_dict[index] = []
            state.waiting_players_list = sorted(pending)
            if not pending:
                break
            tasks = [start_owned_task(state, state.action_events[index].wait()) for index in pending]
            timeout = min(1.0, max(0.0, min(clock.deadline(index) for index in pending) - clock.now()))
            try:
                await asyncio.wait(tasks, timeout=timeout, return_when=asyncio.FIRST_COMPLETED)
            finally:
                for task in tasks:
                    if not task.done():
                        task.cancel()
                await asyncio.gather(*tasks, return_exceptions=True)
    finally:
        state.waiting_players_list = []
    return responses, allowed
