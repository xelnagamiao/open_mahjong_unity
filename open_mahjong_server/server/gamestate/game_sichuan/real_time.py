"""川麻复式的真实时间：短时结束只解除阻塞，弃牌窗口结束才撤销操作权。"""
import asyncio
import math
import time
from dataclasses import dataclass

from .action_check import _xueliu_post_hu_kong_allowed
from .duplicate_wall import can_draw
from ..public.lifecycle import start_owned_task
from ..public.tactical_claim import (
    is_decline_action, tactical_mark_player_committed,
    tactical_mark_player_force_passed, tactical_mark_player_passed_in_grace,
)


DUPLICATE_SUB_RULES = frozenset({"sichuan/doubles_duplicate", "sichuan/xueliu_doubles_duplicate"})


@dataclass
class ClaimClock:
    tick: int
    short: bool
    seconds: float
    bank: float
    step: float
    delivered: float | None = None
    stopped_at: float | None = None
    closed: bool = False

    def elapsed(self, at=None):
        if at is None:
            at = self.stopped_at if self.stopped_at is not None else time.monotonic()
        return 0.0 if self.delivered is None else max(0.0, at - self.delivered)

    def remaining(self):
        return max(0.0, self.seconds - self.elapsed())


class ReceivedClaimQueue(asyncio.Queue):
    """Stamp human, bot and offline submissions before any later processing awaits."""
    def __init__(self, state, seat):
        super().__init__()
        self.state, self.seat = state, seat

    def put_nowait(self, item):
        if isinstance(item, dict) and "_sichuan_received_at" not in item:
            item = {**item, "_sichuan_received_at": time.monotonic(),
                    "_sichuan_received_tick": self.state.server_action_tick}
        super().put_nowait(item)


def uses_real_time(state):
    return (getattr(state, "real_time", False)
            and getattr(state, "sub_rule", None) in DUPLICATE_SUB_RULES)


def initial_real_time(state):
    return uses_real_time(state) and state.game_status == "waiting_action_after_cut"


class RealTimeClaimMixin:
    def initialize_real_time(self, room_data):
        self.real_time = (self.sub_rule in DUPLICATE_SUB_RULES and room_data.get("real_time") is True)
        self._real_time_claim_clocks = {}
        self._real_time_recheck = False
        self._real_time_transition = False
        if self.real_time:
            self.action_queues = {seat: ReceivedClaimQueue(self, seat) for seat in range(4)}

    def on_action_window_broadcast(self):
        self._real_time_claim_clocks = {}
        self._real_time_transition = False
        if not initial_real_time(self):
            return
        next_player = self._next_active_index(self.current_player_index)
        for seat, actions in self.action_dict.items():
            if actions:
                short = self._real_time_recheck or seat != next_player
                bank, step = float(self.player_list[seat].remaining_time), float(self.step_time)
                seconds = float(self.tactical_grace_seconds) if short else bank + step
                self._real_time_claim_clocks[seat] = ClaimClock(self.server_action_tick, short, seconds, bank, step)

    def on_action_window_delivered(self, seat):
        clock = self._real_time_claim_clocks.get(seat)
        if clock is not None and clock.tick == self.server_action_tick and clock.delivered is None:
            clock.delivered = time.monotonic()

    def accept_action_ingress(self, seat, action):
        clock = self._real_time_claim_clocks.get(seat)
        if not initial_real_time(self) or clock is None or clock.tick != self.server_action_tick:
            return True
        # Existing force-pass semantics also cover the interval before a recheck.
        if action == "force_pass" and self.tactical_call:
            return True
        return (not clock.closed and (self._real_time_transition
                or clock.short and not self._real_time_recheck or clock.remaining() > 0))

    def tactical_action_receipt_valid(self, seat, data):
        return receipt_valid(self, seat, data)

    def tactical_extra_competitor(self, action, seat, current_rank):
        # Initial real-time claims may leave other winners still undecided;
        # a nearer winner must not cut off farther seats in multi-ron.
        return (initial_real_time(self) and action == "hu" and current_rank[0] == self.action_priority["hu"]
                and seat not in getattr(self, "_real_time_submitted", {}))


def receipt_valid(state, seat, data):
    clock = getattr(state, "_real_time_claim_clocks", {}).get(seat)
    if not initial_real_time(state) or clock is None:
        return True
    if data.get("_action_tick") not in (None, clock.tick):
        return False
    if data.get("_sichuan_received_tick", clock.tick) != clock.tick:
        return False
    received = data.get("_sichuan_received_at", time.monotonic())
    if clock.delivered is None:
        # A reply from inside send_json proves the prompt has already arrived.
        clock.delivered = received
    return (not clock.closed
            and (state._real_time_transition or clock.short and not state._real_time_recheck
                 or clock.elapsed(received) <= clock.seconds))


def claim_clock_fields(state, seat):
    """Initial ask and reconnect use the same clock; neither restarts a deadline."""
    clock = getattr(state, "_real_time_claim_clocks", {}).get(seat)
    if not initial_real_time(state) or clock is None or clock.tick != state.server_action_tick:
        return {}
    if clock.closed:
        return {"action_list": [], "step_remaining": 0}
    remaining = clock.remaining()
    if state._real_time_recheck:
        return {"remaining_time": math.ceil(remaining), "step_remaining": 0,
                "is_tactical_recheck": True}
    if clock.short:
        return {"remaining_time": math.ceil(remaining), "step_remaining": 0,
                "claim_wait_mode": "real_time" if remaining > 0 else "waiting_draw",
                "claim_seconds_remaining": remaining}
    elapsed = clock.elapsed()
    return {"remaining_time": math.ceil(max(0.0, clock.bank - max(0.0, elapsed - clock.step))),
            "step_remaining": math.ceil(max(0.0, clock.step - elapsed))}


def close_claim_clock(state, *, clear_actions=False, settle_normal=False, transition=False):
    if not initial_real_time(state):
        return
    for seat, clock in state._real_time_claim_clocks.items():
        if transition and not clock.closed:
            clock.stopped_at = time.monotonic()
        if settle_normal and not clock.short and not clock.closed:
            spent = max(0, int(clock.elapsed()) - clock.step)
            state.player_list[seat].remaining_time = max(0, int(clock.bank - spent))
        if not transition:
            clock.closed = True
    state._real_time_transition = transition
    if not transition:
        state.waiting_players_list = []
    if clear_actions:
        state.action_dict = {seat: [] for seat in range(4)}


def _valid_action(state, seat, data):
    action = data.get("action_type")
    if action not in state.action_dict.get(seat, []) or not receipt_valid(state, seat, data):
        return False
    if action == "gang":
        if not can_draw(state, seat):
            return False
        player = state.player_list[seat]
        if getattr(state, "is_xueliu", False) and player.post_hu_lock:
            discards = state.player_list[state.current_player_index].discard_tiles
            return _xueliu_post_hu_kong_allowed(state, player, action, discards[-1] if discards else None)
    return True


async def collect_real_time_claims(state, submitted, action_type=None, player_index=None, action_data=None,
                                   *, recheck=False):
    """Keep soft-expired seats eligible while only live, relevant clocks block."""
    if not recheck:
        state._real_time_submitted = submitted
    pending = {seat for seat, actions in state.action_dict.items() if actions}
    while pending:
        # Consume all already-arrived replies before testing the final boundary.
        for seat in sorted(pending):
            queue = state.action_queues[seat]
            while not queue.empty():
                data = queue.get_nowait()
                if queue.empty():
                    state.action_events[seat].clear()
                if not _valid_action(state, seat, data):
                    continue
                action = data["action_type"]
                clock = state._real_time_claim_clocks[seat]
                if not clock.short:
                    spent = max(0, int(clock.elapsed(data.get("_sichuan_received_at"))) - clock.step)
                    state.player_list[seat].remaining_time = max(0, int(clock.bank - spent))
                submitted[seat] = action
                state.action_dict[seat] = []
                clock.closed = True
                pending.remove(seat)
                if action == "hu":
                    tactical_mark_player_committed(state, seat)
                if action == "force_pass":
                    tactical_mark_player_force_passed(state, seat)
                if recheck and is_decline_action(action):
                    tactical_mark_player_passed_in_grace(state, seat)
                better = (action_type is None
                          or (state.tactical_action_rank(action, seat) > state.tactical_action_rank(action_type, player_index)
                              if recheck else state.action_priority[action] > state.action_priority[action_type]))
                if better:
                    action_type, player_index, action_data = action, seat, dict(data)
                break

        blockers = []
        for seat in list(pending):
            clock = state._real_time_claim_clocks[seat]
            left = clock.remaining()
            if left <= 0 and (recheck or not clock.short):
                if not clock.short:
                    state.player_list[seat].remaining_time = 0
                submitted[seat] = "pass"
                state.action_dict[seat] = []
                clock.closed = True
                pending.remove(seat)
                continue
            # A committed hu cannot be held up by a lower-priority peng/杠.
            relevant = (action_type is None or is_decline_action(action_type)
                        or any(state.action_priority[action] > state.action_priority[action_type]
                               or action == action_type == "hu" for action in state.action_dict[seat]))
            if left > 0 and (recheck or relevant):
                blockers.append(left)
        state.waiting_players_list = sorted(pending)
        if not recheck and state.tactical_call and action_type is not None and not is_decline_action(action_type):
            # A valid claim starts its own complete interruption budget now,
            # even while the next player's ordinary bank still has time.
            break
        if not blockers:
            break
        tasks = [start_owned_task(state, state.action_events[seat].wait()) for seat in pending]
        try:
            await asyncio.wait(tasks, timeout=min(blockers), return_when=asyncio.FIRST_COMPLETED)
        finally:
            for task in tasks:
                if not task.done():
                    task.cancel()
            await asyncio.gather(*tasks, return_exceptions=True)
    transition = (not recheck and state.tactical_call and action_type is not None and not is_decline_action(action_type))
    close_claim_clock(state, settle_normal=not recheck, transition=transition)
    return action_type, player_index, action_data
