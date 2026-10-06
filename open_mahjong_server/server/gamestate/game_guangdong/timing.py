"""MIL 花鬼线上计时：送达起算、先步时后局时、以收到回复的时刻结账。

只有广东适配器启用；动作合法性与执行继续由共享状态机负责。服务端队列记录
可信的单调时钟到达时间，广播/观战发送或后续处理耗时不会吞掉已经收到的回复。
"""

import asyncio
import math
import time
from dataclasses import dataclass

from ..public.lifecycle import start_owned_task


def seconds_for_display(value):
    return max(0, math.ceil(round(value, 9)))


@dataclass
class ActionWindow:
    tick: int
    actions: tuple
    bank: float
    step: float
    fallback_start: float
    delivered: float | None = None
    receipt: float | None = None
    closed: bool = False

    @property
    def origin(self):
        return self.fallback_start if self.delivered is None else self.delivered

    @property
    def deadline(self):
        return self.origin + self.step + self.bank

    def bank_at(self, at):
        # Before the first send finishes, a synchronous/very fast reply has
        # already proved delivery. It must not pay earlier broadcasting work.
        elapsed = 0 if self.delivered is None else max(0, at - self.origin)
        return max(0.0, self.bank - max(0.0, elapsed - self.step))


class ReceivedActionQueue(asyncio.Queue):
    """All existing human/bot/offline submission paths use this unbounded queue."""

    def __init__(self, state, index):
        super().__init__()
        self.state = state
        self.index = index

    def put_nowait(self, item):
        if isinstance(item, dict):
            item = {**item, "_guangdong_received_at": time.monotonic(),
                    "_guangdong_window_tick": self.state.server_action_tick}
        super().put_nowait(item)
        self.state.note_action_received(self.index, item)


class GuangdongTimingMixin:
    def initialize_action_timing(self):
        self._action_windows = {}
        self.action_queues = {i: ReceivedActionQueue(self, i) for i in range(4)}

    @staticmethod
    def _bank_seconds(player):
        exact = getattr(player, "_guangdong_exact_bank", None)
        if exact is None or seconds_for_display(exact) != player.remaining_time:
            return max(0.0, float(player.remaining_time))
        return exact

    def prepare_action_window(self):
        super().prepare_action_window()
        # Publish eligibility before send_json can yield to a fast response.
        self.waiting_players_list = [i for i, actions in self.action_dict.items() if actions]

    def on_action_window_broadcast(self):
        self._action_windows = {}
        if self.game_status == "waiting_ready":
            return  # Settlement confirmation keeps its existing separate lifecycle.
        started = time.monotonic()
        recheck = getattr(self, "_guangdong_tactical_recheck", False)
        for i, actions in self.action_dict.items():
            if actions:
                self._action_windows[i] = ActionWindow(
                    self.server_action_tick, tuple(actions),
                    self.tactical_grace_seconds if recheck else self._bank_seconds(self.player_list[i]),
                    0.0 if recheck else max(0.0, float(self.step_time)), started)
        self.waiting_players_list = list(self._action_windows)

    def on_action_window_delivered(self, index):
        window = self._action_windows.get(index)
        if window is not None and window.delivered is None:
            window.delivered = time.monotonic()

    def note_action_received(self, index, item):
        window = self._action_windows.get(index)
        if (window is None or window.closed or window.receipt is not None
                or not self._valid_receipt(window, item)):
            return
        window.receipt = item["_guangdong_received_at"]
        # A second answer cannot replace the first answer, including on reconnect.
        self.waiting_players_list = [i for i in self.waiting_players_list if i != index]

    @staticmethod
    def _valid_receipt(window, item):
        return (isinstance(item, dict) and item.get("action_type") in window.actions
                and item.get("_guangdong_window_tick") == window.tick
                and item.get("_action_tick", window.tick) in (None, window.tick)
                and (item.get("_guangdong_received_at", float("inf")) < window.deadline
                     or (window.delivered is None and window.bank + window.step > 0)))

    def pending_ask_actions(self, index):
        window = self._action_windows.get(index)
        if window is not None and (window.closed or window.receipt is not None
                                   or (window.delivered is not None
                                       and time.monotonic() >= window.deadline)):
            return []
        return self.action_dict.get(index, [])

    def is_action_pending(self, index):
        return bool(self.pending_ask_actions(index))

    def claim_clock(self, player, reconnecting=False):
        return self.action_clock(player, reconnecting=reconnecting)

    def action_clock_fields(self, player, reconnecting=False):
        """Optional millisecond fields retain the actual deadline before UI rounding."""
        window = self._action_windows.get(player.player_index)
        if window is None or self.game_status == "waiting_ready":
            bank, step = self.action_clock(player, reconnecting=reconnecting)
        elif window.closed:
            bank, step = self._bank_seconds(player), 0
        else:
            at = window.receipt if window.receipt is not None else time.monotonic()
            elapsed = 0 if window.delivered is None else max(0.0, at - window.origin)
            bank, step = window.bank_at(at), max(0.0, window.step - elapsed)
        bank_ms = seconds_for_display(bank * 1000)
        total_ms = seconds_for_display((bank + step) * 1000)
        return {"remaining_time_ms": bank_ms,
                "step_remaining_ms": max(0, total_ms - bank_ms)}

    def claim_clock_fields(self, player, reconnecting=False):
        # This existing optional shared hook changes no other rule's timing path.
        return self.action_clock_fields(player, reconnecting=reconnecting)

    def action_clock(self, player, reconnecting=False):
        window = self._action_windows.get(player.player_index)
        if window is None or self.game_status == "waiting_ready":
            from ..public.ask_timing import reconnect_clock
            return reconnect_clock(self, player) if reconnecting else (player.remaining_time, self.step_time)
        if window.closed:
            return seconds_for_display(self._bank_seconds(player)), 0
        at = window.receipt if window.receipt is not None else time.monotonic()
        elapsed = 0 if window.delivered is None else max(0.0, at - window.origin)
        total = seconds_for_display(window.bank + window.step - elapsed)
        bank = seconds_for_display(window.bank_at(at))
        # Round the total once: split fields must not add two rounding allowances.
        return bank, max(0, total - bank)

    def _complete_window(self, index, at):
        window = self._action_windows[index]
        if not window.closed:
            player = self.player_list[index]
            if not getattr(self, "_guangdong_tactical_recheck", False):
                player._guangdong_exact_bank = window.bank_at(at)
                player.remaining_time = seconds_for_display(player._guangdong_exact_bank)
            window.closed = True
        self.waiting_players_list = [i for i in self.waiting_players_list if i != index]

    def _interrupt_ordinary_windows(self, claim_at):
        # Several replies can already be queued when the collector resumes. A
        # later cancel must not pay ordinary bank after the first valid claim.
        # Recalculate from each immutable window, including already collected seats.
        for i, window in self._action_windows.items():
            at = min(claim_at, window.receipt) if window.receipt is not None else claim_at
            player = self.player_list[i]
            player._guangdong_exact_bank = window.bank_at(at)
            player.remaining_time = seconds_for_display(player._guangdong_exact_bank)
            window.closed = True
        self.waiting_players_list = []

    async def collect_action_responses(self):
        if self.game_status == "waiting_ready":
            from ..game_taiwan.wait_action import _collect_responses
            return await _collect_responses(self)
        allowed = {i: list(actions) for i, actions in self.action_dict.items() if actions}
        if not allowed:
            self.waiting_players_list = []
            return {}, allowed
        if not self._action_windows:
            # Explicit callers without a broadcast still get one configured window.
            self.on_action_window_broadcast()
            now, wall = time.monotonic(), time.time()
            for i, window in self._action_windows.items():
                delivered = (getattr(self, "_ask_delivered_at", None) or {}).get(i)
                if delivered is not None:
                    window.delivered = now - max(0, wall - delivered)
        for window in self._action_windows.values():
            if window.delivered is None:
                # No successful send callback: start its logical fallback only
                # after broadcast returns, never tax another socket's send time.
                window.delivered = window.receipt if window.receipt is not None else time.monotonic()
        pending, responses = set(allowed), {}
        self.waiting_players_list = [i for i in pending if self.pending_ask_actions(i)]
        while pending:
            for i in tuple(pending):
                queue = self.action_queues[i]
                accepted = None
                while not queue.empty():
                    item = queue.get_nowait()
                    if self._valid_receipt(self._action_windows[i], item):
                        accepted = item
                        break
                if accepted is not None:
                    self._complete_window(i, accepted["_guangdong_received_at"])
                    responses[i] = {key: value for key, value in accepted.items()
                                    if not key.startswith("_guangdong_")}
                    pending.remove(i)
                    self.action_events[i].clear()
                elif time.monotonic() >= self._action_windows[i].deadline:
                    self._complete_window(i, self._action_windows[i].deadline)
                    pending.remove(i)
                    self.action_events[i].clear()
            if (getattr(self, "tactical_call", False)
                    and self.game_status in ("waiting_action_after_cut", "waiting_action_qianggang")
                    and any(data.get("action_type") not in ("pass", "force_pass") for data in responses.values())):
                # Stop ordinary thinking at the actual first claim arrival,
                # excluding outbound stalls and the later tactical phase.
                claim_at = min(self._action_windows[i].receipt for i, data in responses.items()
                               if data.get("action_type") not in ("pass", "force_pass"))
                self._interrupt_ordinary_windows(claim_at)
                break
            if not pending:
                break
            tasks = {start_owned_task(self, self.action_events[i].wait()): i for i in pending}
            timeout = max(0.0, min(1.0, min(self._action_windows[i].deadline for i in pending) - time.monotonic()))
            try:
                done, unfinished = await asyncio.wait(tasks, timeout=timeout, return_when=asyncio.FIRST_COMPLETED)
                for task in done:
                    self.action_events[tasks[task]].clear()
            finally:
                for task in tasks:
                    if not task.done():
                        task.cancel()
                await asyncio.gather(*tasks, return_exceptions=True)
        self.waiting_players_list = []
        return responses, allowed

    def _reset_hand_runtime(self):
        super()._reset_hand_runtime()
        self._action_windows = {}
        for player in self.player_list:
            player._guangdong_exact_bank = max(0.0, float(self.round_time))
            player.remaining_time = self.round_time
