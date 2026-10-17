"""Publish receive permissions before an ask, then consume the same window."""
import asyncio
from collections.abc import Mapping

from .ask_timing import begin_ask_round, note_ask_delivered
from .tactical_claim import init_tactical_round_state


def _seat_items(values):
    return values.items() if isinstance(values, Mapping) else enumerate(values)


def retain_current_tick_actions(state):
    """Discard stale replies without losing a current reply or its wake-up."""
    tick = getattr(state, "server_action_tick", None)
    for seat, queue in _seat_items(getattr(state, "action_queues", {})):
        retained = []
        while not queue.empty():
            try:
                data = queue.get_nowait()
            except asyncio.QueueEmpty:
                break
            if isinstance(data, dict) and data.get("_action_tick") in (None, tick):
                retained.append(data)
        for data in retained:
            queue.put_nowait(data)
        event = state.action_events[seat]
        if retained:
            event.set()
        else:
            event.clear()


def prepare_action_window(state, *, new_ask=False, is_tactical_recheck=False,
                          tactical=True):
    """Synchronously open ingress; only a new, unpublished ask may clear queues.

    Call after advancing the tick and before the first await in a broadcaster.
    Calling again in a waiter preserves replies received during broadcasting.
    A tactical recheck keeps the opening snapshot and explicit force-pass state.
    """
    window = (state.game_status, getattr(state, "server_action_tick", None))
    if new_ask:
        for _, queue in _seat_items(getattr(state, "action_queues", {})):
            while not queue.empty():
                try:
                    queue.get_nowait()
                except asyncio.QueueEmpty:
                    break
    else:
        retain_current_tick_actions(state)
    if tactical and not is_tactical_recheck and (
        new_ask or getattr(state, "_prepared_action_window", None) != window
    ):
        init_tactical_round_state(state)
    state._prepared_action_window = window
    open_action_ingress(state)


def open_action_ingress(state):
    """Publish eligibility without replacing a rule's existing window or clock."""
    state._waiting_action_tick = getattr(state, "server_action_tick", None)
    state.waiting_players_list = [seat for seat, actions in state.action_dict.items() if actions]
    for seat, event in _seat_items(getattr(state, "action_events", {})):
        if seat in state.waiting_players_list and not state.action_queues[seat].empty():
            event.set()
        else:
            event.clear()


def start_waiting_clocks(state):
    """Start fallback clocks only for seats without a pending online delivery."""
    pending = getattr(state, "_ask_pending_delivery", {})
    connections = getattr(getattr(state, "game_server", None), "user_id_to_connection", {})
    for seat in state.waiting_players_list:
        if (state.game_status == "waiting_ready" or seat not in pending
                or pending[seat] != getattr(state, "server_action_tick", None)):
            note_ask_delivered(state, seat)
            continue
        player = state.player_list[seat]
        conn = connections.get(player.user_id)
        if player.user_id < 10 or "offline" in player.tag_list or conn is None:
            note_ask_delivered(state, seat)


def prepare_ready_window(state):
    """Ready broadcasts also accept replies immediately and use a fresh clock."""
    if state.game_status != "waiting_ready":
        return
    window = (state.game_status, getattr(state, "server_action_tick", None))
    if getattr(state, "_prepared_action_window", None) != window:
        prepare_action_window(state, new_ask=True, tactical=False)
        begin_ask_round(state)
    else:
        open_action_ingress(state)
