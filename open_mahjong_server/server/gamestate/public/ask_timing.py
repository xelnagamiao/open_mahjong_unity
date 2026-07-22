"""Ask 送达起算：与 outbound_pipe 配合，避免表现延迟偷走受保护观众的操作时间。

- begin_ask_round：每次广播 ask 时清空送达表
- note_ask_delivered：pipe 内真正发出 ask 时记录该座位起点
- get_ask_elapsed：未送达视为 0（不扣时）；送达后按 wall clock 计算
- reconnect_remaining_time：优先按该座位送达时刻重算
"""
from __future__ import annotations

import time
from typing import Dict


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


def reconnect_remaining_time(game_state, player) -> int:
    """重连补发：按该座位 ask 送达时刻（若无则回退广播时刻）重算剩余局时。"""
    delivered = getattr(game_state, "_ask_delivered_at", None) or {}
    t0 = delivered.get(player.player_index)
    if t0 is None:
        t0 = getattr(game_state, "_ask_broadcast_time", None)
    if t0 is None:
        return player.remaining_time
    elapsed = max(0, time.time() - t0)
    return max(0, player.remaining_time - int(elapsed))
