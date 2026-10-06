"""Tuidao's room clock: one step allowance, then the hand's remaining bank.

Only the existing integer wire display is rounded; the authoritative bank and
per-seat deadline retain fractions. Delivery and action receipt use monotonic
time, so slow fan-out or spectator transport cannot consume a submitted action.
"""
import asyncio
import math
from dataclasses import dataclass
from time import monotonic

from ..public.lifecycle import start_owned_task


ACTION_PHASES = frozenset(("waiting_hand_action", "waiting_action_after_cut",
                          "waiting_action_qianggang"))


def display_seconds(value):
    return max(0, math.ceil(max(0.0, value) - 1e-9))


class TimedActionQueue(asyncio.Queue):
    """Timestamp ingress, before waiting for another seat's broadcast or action."""
    def put_nowait(self, item):
        if isinstance(item, dict):
            item = dict(item, _tuidao_received_at=monotonic())
        return super().put_nowait(item)

    def first_received_at(self):
        if not self.empty() and isinstance(self._queue[0], dict):
            return float(self._queue[0].get('_tuidao_received_at', float('inf')))
        return float('inf')


@dataclass
class ActionWindow:
    bank: float
    step: float
    tick: int
    started_at: float = None
    completed: bool = False
    tactical: bool = False

    @property
    def deadline(self):
        return None if self.started_at is None else self.started_at + self.step + self.bank


async def wait_for_events(tasks, timeout):
    return await asyncio.wait(tasks, timeout=timeout, return_when=asyncio.FIRST_COMPLETED)


class TuidaoActionClock:
    def __init__(self, state):
        self.state = state
        self.windows = {}
        self.reset_round()

    def reset_round(self):
        self.windows.clear()
        for player in self.state.player_list:
            self._set_bank(player, self.state.round_time)

    @staticmethod
    def _set_bank(player, bank):
        player._tuidao_time_bank = max(0.0, float(bank))
        player.remaining_time = display_seconds(player._tuidao_time_bank)

    @staticmethod
    def bank(player):
        bank = getattr(player, "_tuidao_time_bank", float(player.remaining_time))
        # Respect explicit room/fixture state changes to the public bank field.
        if display_seconds(bank) != player.remaining_time:
            bank = max(0.0, float(player.remaining_time))
            player._tuidao_time_bank = bank
        return bank

    def begin(self):
        state = self.state
        recheck = bool(getattr(state, '_tuidao_recheck', False))
        self.windows = {
            index: ActionWindow(float(state.tactical_grace_seconds) if recheck else self.bank(state.player_list[index]),
                                0 if recheck else max(0.0, float(state.step_time)),
                                state.server_action_tick, tactical=recheck)
            for index, actions in state.action_dict.items() if actions
        } if state.game_status in ACTION_PHASES else {}
        # Open ingress before fan-out: a human/bot may answer while another
        # seat's websocket or spectator delivery is still pending.
        state.waiting_players_list = sorted(self.windows)

    def delivered(self, index):
        window = self.windows.get(index)
        if window is not None and window.started_at is None:
            window.started_at = monotonic()

    def is_pending(self, index):
        window = self.windows.get(index)
        return not window.completed if window is not None else bool(self.state.action_dict.get(index))

    def clock(self, player):
        bank, step = self.seconds(player)
        return display_seconds(bank), display_seconds(step)

    def seconds(self, player):
        window = self.windows.get(player.player_index)
        if window is None:
            return self.bank(player), max(0.0, float(self.state.step_time))
        if window.completed:
            return 0.0, 0.0
        elapsed = 0.0 if window.started_at is None else max(0.0, monotonic() - window.started_at)
        return (max(0.0, window.bank - max(0.0, elapsed - window.step)),
                max(0.0, window.step - elapsed))

    def _complete(self, index, at):
        window = self.windows[index]
        if window.completed:
            return
        elapsed = max(0.0, at - window.started_at)
        if not window.tactical:
            self._set_bank(self.state.player_list[index], window.bank - max(0.0, elapsed - window.step))
        window.completed = True

    def valid_tactical_action(self, index, data):
        from ..public.tactical_claim import tactical_opening_snapshot, tactical_force_pass_is_live
        window = self.windows.get(index)
        if not isinstance(data, dict) or window is None:
            return False
        action = data.get('action_type')
        if action not in (tactical_opening_snapshot(self.state) or self.state.action_dict).get(index, []):
            return False
        tick = data.get('_action_tick')
        if tick not in (None, window.tick) and not (
            action == 'force_pass' and tactical_force_pass_is_live(self.state, index, tick)):
            return False
        received = float(data.get('_tuidao_received_at', monotonic()))
        return window.deadline is None or received < window.deadline

    async def collect(self, *, interrupt_on_claim=False):
        state = self.state
        allowed = {index: list(actions) for index, actions in state.action_dict.items() if actions}
        # Fixtures may call wait_action directly; the production path begins at broadcast.
        if any(index not in self.windows or self.windows[index].tick != state.server_action_tick
               for index in allowed):
            self.begin()
        # A failed/missing websocket has no delivery callback. Give a full window
        # from the end of fan-out; do not tax an undelivered seat from broadcast time.
        for index in allowed:
            self.delivered(index)
        pending, responses = set(allowed), {}
        state.waiting_players_list = sorted(pending)
        try:
            while pending:
                ordered = sorted(pending, key=lambda index: state.action_queues[index].first_received_at())
                interrupted_at = None
                for index in ordered:
                    queue = state.action_queues[index]
                    window = self.windows[index]
                    # Drain BEFORE expiration: an on-time ingress stays valid even
                    # when another websocket or spectator delayed this collector.
                    while not queue.empty():
                        data = queue.get_nowait()
                        if not isinstance(data, dict) or data.get("action_type") not in allowed[index]:
                            continue
                        if data.get("_action_tick") not in (None, window.tick) and not (
                            window.tactical and data.get('action_type') == 'force_pass'
                            and self.valid_tactical_action(index, data)):
                            continue
                        received = data.get("_tuidao_received_at", monotonic())
                        if received >= window.deadline:
                            continue
                        responses[index] = data
                        self._complete(index, received)
                        pending.remove(index)
                        if interrupt_on_claim and data['action_type'] not in ('pass', 'force_pass'):
                            interrupted_at = received
                        break
                    state.action_events[index].clear()
                    if interrupted_at is not None:
                        break
                    if index in pending and monotonic() >= window.deadline:
                        self._complete(index, window.deadline)
                        pending.remove(index)
                if interrupted_at is not None:
                    # A valid claim pauses ordinary thinking; the independent recheck does not charge it.
                    for index in pending:
                        self._complete(index, interrupted_at)
                    break
                state.waiting_players_list = sorted(pending)
                if not pending:
                    break
                tasks = {start_owned_task(state, state.action_events[index].wait()) for index in pending}
                try:
                    timeout = max(0.0, min(self.windows[i].deadline for i in pending) - monotonic())
                    await wait_for_events(tasks, min(1.0, timeout))
                finally:
                    for task in tasks:
                        if not task.done():
                            task.cancel()
                    await asyncio.gather(*tasks, return_exceptions=True)
        finally:
            state.waiting_players_list = []
        return responses, allowed
