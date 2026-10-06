"""Changchun's delivery-based room clock; shared executors keep action semantics.

Each physical ask owns an immutable bank snapshot and one step allowance. Only
wire display rounds up; receipt, deadline and debits retain fractional seconds.
"""
import asyncio
import math
import time
from dataclasses import dataclass

from ..public.lifecycle import start_owned_task


ACTION_PHASES = frozenset(('waiting_hand_action', 'waiting_action_after_cut',
                          'waiting_action_qianggang'))


def display_seconds(value):
    return max(0, math.ceil(max(0.0, value) - 1e-9))


class TimedActionQueue(asyncio.Queue):
    """Only the server writes receipt metadata; the client cannot choose a time."""
    def __init__(self, state):
        super().__init__()
        self.state = state

    def put_nowait(self, item):
        if isinstance(item, dict):
            manager = self.state.action_clock_manager
            item = dict(item, _cc_received_at=time.monotonic(),
                        _cc_window_serial=manager.serial)
        return super().put_nowait(item)


@dataclass
class ActionWindow:
    bank: float
    step: float
    tick: int
    started_at: float = None
    wall_started_at: float = None
    completed: bool = False
    consumes_bank: bool = True

    @property
    def deadline(self):
        return None if self.started_at is None else self.started_at + self.step + self.bank


class ChangchunActionClock:
    def __init__(self, state):
        self.state = state
        self.serial = 0
        self.windows = {}
        self.key = None
        self.paused_claims = []

    def reset_round(self):
        self.windows.clear()
        self.key = None
        self.paused_claims = []
        for player in self.state.player_list:
            player.remaining_time = self.state.round_time

    def _key(self):
        """Invalid choices may re-ask; only a real state transition grants a step."""
        state = self.state
        return (state.round_index, state.current_round, state.game_status,
                state.current_player_index, state.cc_window, state.cc_turn_serial,
                state.cc_bao_revision, len(state.tiles_list),
                tuple((p.player_index, tuple(p.hand_tiles), tuple(p.combination_tiles),
                       tuple(p.discard_tiles), p.last_drawn_tile, p.has_draw_slot,
                       p.declared_ready, p.ready_locked) for p in state.player_list),
                tuple((i, tuple(a)) for i, a in sorted(state.action_dict.items())))

    def begin(self):
        state = self.state
        if state.game_status not in ACTION_PHASES:
            self.windows.clear()
            self.key = None
            return
        recheck = getattr(state, '_cc_tactical_recheck', False)
        if not recheck and state.game_status in ('waiting_action_after_cut', 'waiting_action_qianggang'):
            from ..public.tactical_claim import add_tactical_force_pass_options
            add_tactical_force_pass_options(state, state.action_dict)
        key = self._key()
        if recheck or key != self.key:
            self.serial += 1
            self.key = key
            self.windows = {i: ActionWindow(
                state.tactical_grace_seconds if recheck else max(0.0, float(state.player_list[i].remaining_time)),
                0.0 if recheck else max(0.0, float(state.step_time)), state.server_action_tick,
                consumes_bank=not recheck)
                for i, actions in state.action_dict.items() if actions}
            if not recheck:
                from ..public.tactical_claim import init_tactical_round_state
                init_tactical_round_state(state)
        else:
            # The executor rejected a structurally invalid candidate. Retry within
            # the original budget, projecting from its immutable starting bank.
            for i, window in self.windows.items():
                window.tick = state.server_action_tick
                window.completed = False
                if window.wall_started_at is not None:
                    state._ask_delivered_at[i] = window.wall_started_at
        # Fan-out can await other sockets/spectators. Seat 1 may legitimately
        # reply before collection starts; admit it as soon as broadcasting begins.
        state.waiting_players_list = sorted(self.windows)

    def delivered(self, index):
        window = self.windows.get(index)
        if window is not None and window.started_at is None:
            window.started_at = time.monotonic()
            window.wall_started_at = time.time()

    def is_pending(self, index):
        window = self.windows.get(index)
        if self.state.game_status in ACTION_PHASES and window is not None:
            return not window.completed
        return bool(self.state.action_dict.get(index))

    def precise_clock(self, player, reconnecting=False):
        state = self.state
        if state.game_status == 'waiting_ready':
            from ..public.ask_timing import reconnect_clock
            return reconnect_clock(state, player)
        window = self.windows.get(player.player_index)
        if window is not None:
            if window.completed:
                return (max(0.0, player.remaining_time) if window.consumes_bank else 0.0), 0.0
            elapsed = 0.0 if window.started_at is None else max(0.0, time.monotonic() - window.started_at)
            bank, step = window.bank, window.step
        else:
            bank, step = max(0.0, float(player.remaining_time)), max(0.0, float(state.step_time))
            t0 = (getattr(state, '_ask_delivered_at', None) or {}).get(player.player_index)
            if t0 is None:
                t0 = getattr(state, '_ask_broadcast_time', None)
            elapsed = max(0.0, time.time() - t0) if reconnecting and t0 is not None else 0.0
        return (max(0.0, bank - max(0.0, elapsed - step)), max(0.0, step - elapsed))

    def clock(self, player, reconnecting=False):
        return tuple(display_seconds(value) for value in self.precise_clock(player, reconnecting))

    def wire_fields(self, player, reconnecting=False):
        bank, step = self.precise_clock(player, reconnecting)
        fields = {'remaining_time_ms': display_seconds(bank * 1000),
                  'step_remaining_ms': display_seconds(step * 1000)}
        if reconnecting and getattr(self.state, '_cc_tactical_recheck', False):
            fields['is_tactical_recheck'] = True
        return fields

    def _complete(self, index, at):
        window = self.windows[index]
        if window.completed:
            return
        if window.consumes_bank:
            overtime = max(0.0, at - window.started_at - window.step)
            self.state.player_list[index].remaining_time = max(0.0, window.bank - overtime)
        window.completed = True

    def pause_claim_window(self, at):
        # Freeze at the effective claim's receipt, before any observer/socket await.
        # A slow spectator must not spend another participant's normal bank.
        for index, window in self.windows.items():
            if not window.completed:
                self._complete(index, min(at, window.deadline))
                self.paused_claims.append(index)
        self.state.waiting_players_list = []

    async def finish_claim_window(self):
        ended, self.paused_claims = self.paused_claims, []
        for index, window in self.windows.items():
            if not window.completed:
                self._complete(index, min(time.monotonic(), window.deadline))
                ended.append(index)
        self.state.waiting_players_list = []
        for index in ended:
            await self.state.notify_cc_window_closed(index, include_player=True)

    async def collect(self):
        state = self.state
        allowed = {i: list(actions) for i, actions in state.action_dict.items() if actions}
        # Direct executor tests can enter without broadcasting. Production always
        # begins at broadcast and anchors each seat at its first delivery.
        if self.key != self._key() or any(i not in self.windows for i in allowed):
            self.begin()
        for index in allowed:
            # Missing/failed transport has no delivery mark; do not charge it for
            # someone else's blocked send. Its fallback begins after fan-out.
            self.delivered(index)
        pending = {i for i in allowed if not self.windows[i].completed}
        responses = {}
        try:
            while pending:
                completed = []
                for index in tuple(pending):
                    window = self.windows[index]
                    queue = state.action_queues[index]
                    # Arrival is authoritative, even if later-seat fan-out blocked
                    # the collector beyond this participant's deadline.
                    while not queue.empty():
                        data = queue.get_nowait()
                        if not isinstance(data, dict) or data.get('action_type') not in allowed[index]:
                            continue
                        if data.get('_action_tick') not in (None, window.tick):
                            from ..public.tactical_claim import tactical_force_pass_is_live
                            if not (data.get('action_type') == 'force_pass' and
                                    tactical_force_pass_is_live(state, index, data.get('_action_tick'))):
                                continue
                        if data.get('_cc_window_serial', self.serial) != self.serial:
                            continue
                        received = data.get('_cc_received_at', time.monotonic())
                        if received >= window.deadline:
                            continue
                        responses[index] = data
                        if data.get('action_type') == 'force_pass':
                            from ..public.tactical_claim import tactical_mark_player_force_passed
                            tactical_mark_player_force_passed(state, index)
                        self._complete(index, received)
                        pending.remove(index)
                        completed.append(index)
                        break
                    state.action_events[index].clear()
                    if index in pending and time.monotonic() >= window.deadline:
                        self._complete(index, window.deadline)
                        pending.remove(index)
                        completed.append(index)
                state.waiting_players_list = sorted(pending)
                interrupt = state.tactical_call and state.game_status in (
                    'waiting_action_after_cut', 'waiting_action_qianggang') and any(
                    data['action_type'] not in ('pass', 'force_pass') for data in responses.values())
                if interrupt and not getattr(state, '_cc_tactical_recheck', False):
                    at = min(data.get('_cc_received_at', time.monotonic()) for data in responses.values()
                             if data['action_type'] not in ('pass', 'force_pass'))
                    self.pause_claim_window(at)
                for index in completed:
                    await state.notify_cc_window_closed(index)
                if not pending or interrupt:
                    break
                tasks = {start_owned_task(state, state.action_events[i].wait()) for i in pending}
                try:
                    timeout = max(0.0, min(self.windows[i].deadline for i in pending) - time.monotonic())
                    await asyncio.wait(tasks, timeout=min(1.0, timeout), return_when=asyncio.FIRST_COMPLETED)
                finally:
                    for task in tasks:
                        if not task.done():
                            task.cancel()
                    await asyncio.gather(*tasks, return_exceptions=True)
        finally:
            state.waiting_players_list = []
        return responses, allowed
