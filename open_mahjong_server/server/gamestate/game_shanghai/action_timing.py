"""敲麻动作窗口：先步时后局时，按服务端收件时刻结账，重连只投影。

remaining_time 保持整数协议；不足一秒的余额向上取整显示，内部余额不取整。
本模块只由上海敲麻启用，不改变其他回合制规则的计费方式。
"""
from __future__ import annotations

import asyncio
import math
import time
from dataclasses import dataclass

from ..public.lifecycle import start_owned_task


def seconds(value):
    return max(0, math.ceil(max(0.0, value) - 1e-9))


def bank(player):
    value = getattr(player, "_qiaoma_time_bank", None)
    if value is None or seconds(value) != player.remaining_time:
        value = max(0.0, float(player.remaining_time))
        player._qiaoma_time_bank = value
    return value


def set_bank(player, value):
    player._qiaoma_time_bank = max(0.0, value)
    player.remaining_time = seconds(player._qiaoma_time_bank)


class ActionQueue(asyncio.Queue):
    """只接受服务端入队时间；所有真人、离线和机器人入口共用此队列。"""

    def put_nowait(self, item):
        if isinstance(item, dict):
            item = {**item, "_qiaoma_received_at": time.time(),
                    "_qiaoma_received_monotonic": time.monotonic()}
        super().put_nowait(item)


@dataclass
class Window:
    bank: float
    step: float
    origin: float | None = None
    monotonic_origin: float | None = None
    delivery_recorded: bool = False
    settled: bool = False


def window(game, index):
    key = (game.server_action_tick, game.game_status, getattr(game, "_ask_broadcast_time", None))
    if getattr(game, "_qiaoma_window_key", None) != key:
        game._qiaoma_window_key = key
        game._qiaoma_windows = {}
    windows = game._qiaoma_windows
    if index not in windows:
        player = game.player_list[index]
        is_ready = game.game_status == "waiting_ready"
        windows[index] = Window(
            max(0.0, float(player.remaining_time)) if is_ready else bank(player),
            0.0 if is_ready else max(0.0, float(game.step_time)),
        )
    result = windows[index]
    # Actual socket delivery wins over broadcast enqueue time. Resolve this lazily:
    # a fresh clock can be serialized before the send_json coroutine completes.
    if result.origin is None:
        result.origin = (getattr(game, "_ask_delivered_at", None) or {}).get(index)
    return result


def origin(game, value):
    if value.origin is not None:
        return value.origin
    # Missing/offline sockets still have a finite fallback window.
    broadcast = getattr(game, "_ask_broadcast_time", None)
    return time.time() if broadcast is None else broadcast


def monotonic_origin(game, value):
    if value.monotonic_origin is None:
        broadcast_mono = getattr(game, "_qiaoma_broadcast_monotonic", None)
        same_broadcast = getattr(game, "_qiaoma_broadcast_wall", None) == getattr(game, "_ask_broadcast_time", None)
        if not value.delivery_recorded and broadcast_mono is not None and same_broadcast:
            value.monotonic_origin = broadcast_mono
        else:
            # Compatibility fallback for old snapshots without a monotonic broadcast hook.
            value.monotonic_origin = time.monotonic() - max(0.0, time.time() - origin(game, value))
    return value.monotonic_origin


def projected_clock(game, player, reconnecting=False):
    value = window(game, player.player_index)
    if value.settled:
        return bank(player), 0.0
    elapsed = max(0.0, time.monotonic() - monotonic_origin(game, value)) if reconnecting else 0.0
    return max(0.0, value.bank - max(0.0, elapsed - value.step)), max(0.0, value.step - elapsed)


def clock(game, player, reconnecting=False):
    remaining, step = projected_clock(game, player, reconnecting)
    return seconds(remaining), seconds(step)


def clock_fields(game, player, reconnecting=False):
    remaining, step = projected_clock(game, player, reconnecting)
    return {"remaining_time_ms": seconds(remaining * 1000), "step_remaining_ms": seconds(step * 1000)}


def settle(game, index, value, elapsed):
    if value.settled:
        return
    value.settled = True
    if game.game_status != "waiting_ready":
        set_bank(game.player_list[index], value.bank - max(0.0, elapsed - value.step))
    # A submitted response is final for this window, even while other seats think.
    game.action_dict[index] = []
    game.waiting_players_list = [i for i in game.waiting_players_list if i != index]


async def collect_responses(game):
    allowed = {i: list(actions) for i, actions in game.action_dict.items() if actions}
    game.waiting_players_list = list(allowed)
    pending = set(allowed)
    responses = {}
    windows = {i: window(game, i) for i in pending}
    deadlines = {i: monotonic_origin(game, value) + value.bank + value.step for i, value in windows.items()}
    # Monotonic scheduler deadlines are fixed once, never restarted by reconnect.
    game._qiaoma_deadlines = dict(deadlines)

    while pending:
        # Read server-timestamped receipts before checking consumer-time expiry.
        for i in list(pending):
            queue = game.action_queues[i]
            while not queue.empty():
                data = queue.get_nowait()
                if not isinstance(data, dict) or data.get("action_type") not in allowed[i]:
                    continue
                tick = data.get("_action_tick")
                if tick is not None and tick != game.server_action_tick:
                    continue
                if not game.is_valid_timed_response(i, data):
                    continue
                received = data.get("_qiaoma_received_monotonic", time.monotonic())
                if received >= deadlines[i]:
                    continue
                responses[i] = dict(data)
                settle(game, i, windows[i], max(0.0, received - monotonic_origin(game, windows[i])))
                pending.remove(i)
                break
            game.action_events[i].clear()

        now_mono = time.monotonic()
        for i in list(pending):
            if now_mono >= game._qiaoma_deadlines[i]:
                settle(game, i, windows[i], windows[i].bank + windows[i].step)
                pending.remove(i)
        if not pending:
            break

        tasks = [start_owned_task(game, game.action_events[i].wait()) for i in pending]
        timeout = max(0.0, min(game._qiaoma_deadlines[i] for i in pending) - now_mono)
        _, unfinished = await asyncio.wait(tasks, timeout=min(1.0, timeout), return_when=asyncio.FIRST_COMPLETED)
        for task in unfinished:
            task.cancel()
        if unfinished:
            await asyncio.gather(*unfinished, return_exceptions=True)

    game.waiting_players_list = []
    return responses, allowed
