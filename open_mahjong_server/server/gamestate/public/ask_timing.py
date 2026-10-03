"""Ask 送达起算：与 outbound_pipe 配合，避免表现延迟偷走受保护观众的操作时间。

- begin_ask_round：每次广播 ask 时清空送达表
- note_ask_delivered：pipe 内真正发出 ask 时记录该座位起点
- get_ask_elapsed：未送达视为 0（不扣时）；送达后按 wall clock 计算
- reconnect_clock：重连补发按「先扣步时、再扣局时」重算剩余局时与剩余步时
"""
from __future__ import annotations

import time
from typing import Dict, Optional, Tuple


def begin_ask_round(game_state) -> None:
    game_state._ask_delivered_at: Dict[int, float] = {}
    game_state._ask_broadcast_time = time.time()


def note_ask_delivered(game_state, viewer_index: int) -> None:
    delivered = getattr(game_state, "_ask_delivered_at", None)
    if delivered is None:
        game_state._ask_delivered_at = {}
        delivered = game_state._ask_delivered_at
    # 只记首次送达，避免重连补发重置时钟
    if viewer_index not in delivered:
        delivered[viewer_index] = time.time()


def get_ask_elapsed(game_state, viewer_index: int) -> float:
    delivered = getattr(game_state, "_ask_delivered_at", None) or {}
    t0 = delivered.get(viewer_index)
    if t0 is None:
        return 0.0
    return max(0.0, time.time() - t0)


def _ceil_remaining_seconds(value: float) -> int:
    if value <= 0:
        return 0
    return int(value + 0.999)


def _reconnect_t0(game_state, player) -> Optional[float]:
    delivered = getattr(game_state, "_ask_delivered_at", None) or {}
    t0 = delivered.get(player.player_index)
    if t0 is None:
        t0 = getattr(game_state, "_ask_broadcast_time", None)
    return t0


def reconnect_clock(game_state, player) -> Tuple[int, int]:
    """重连补发：(剩余局时, 剩余步时)。

    对局询问窗口 = 局时 + 步时，先消耗步时再扣局时，与 wait_action 及客户端
    「局时+步时」显示一致。waiting_ready 没有步时宽限，只扣局时。
    """
    bank = int(getattr(player, "remaining_time", 0) or 0)
    step = int(getattr(game_state, "step_time", 0) or 0)
    if getattr(game_state, "game_status", None) == "waiting_ready":
        step = 0

    t0 = _reconnect_t0(game_state, player)
    if t0 is None:
        return max(0, bank), max(0, step)

    elapsed = max(0.0, time.time() - t0)
    if step <= 0:
        return _ceil_remaining_seconds(bank - elapsed), 0

    step_remaining = _ceil_remaining_seconds(step - elapsed)
    bank_spent = max(0.0, elapsed - step)
    bank_remaining = _ceil_remaining_seconds(bank - bank_spent)
    return bank_remaining, step_remaining


def reconnect_remaining_time(game_state, player) -> int:
    """重连补发剩余局时。显示步时请用 reconnect_clock。"""
    bank_remaining, _ = reconnect_clock(game_state, player)
    return bank_remaining
