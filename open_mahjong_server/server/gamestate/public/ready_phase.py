"""局终 / 错和结算后的 waiting_ready 阶段（与客户端 EndResultPanel 演出对齐）。"""

import math
import time
from typing import Awaitable, Callable

from .round_end_timing import (
    ROUND_END_PRESENTATION_FADE_SEC,
    hu_result_ready_pre_panel_seconds,
    hu_result_ready_wait_seconds,
    sichuan_liuju_final_ready_wait_seconds,
)
from .ask_timing import begin_ask_round
from .action_window import prepare_action_window


def ready_status_by_seat(game_state) -> dict[int, bool]:
    """Read acknowledgements, never the collector's temporary waiting list."""
    return {
        player.player_index: "ready" not in game_state.action_dict.get(player.player_index, ())
        for player in game_state.player_list
    }


async def run_result_ready_phase(
    game_state,
    wait_time: float,
    broadcast_ready_status: Callable[..., Awaitable[None]],
    *,
    panel_visible_delay_sec: float | None = None,
) -> None:
    """Keep one fixed budget for every seat until confirmation or expiry.

    wait_action already subtracts elapsed ask time. Replacing that budget with
    deadline - now after a ready reply would charge every other seat twice.
    """
    started_at = time.time()
    deadline = started_at + wait_time
    panel_visible_at = (None if panel_visible_delay_sec is None
                        else started_at + panel_visible_delay_sec)
    game_state.action_dict = {}
    for player in game_state.player_list:
        if player.user_id <= 10:
            game_state.action_dict[player.player_index] = []
        else:
            game_state.action_dict[player.player_index] = ["ready"]
            player.remaining_time = int(math.ceil(wait_time))

    game_state.game_status = "waiting_ready"
    prepare_action_window(game_state, new_ask=True, tactical=False)
    begin_ask_round(game_state)
    await broadcast_ready_status(game_state)

    while True:
        pending = [i for i, actions in game_state.action_dict.items() if "ready" in actions]
        now = time.time()
        if panel_visible_at is not None and now >= panel_visible_at:
            await broadcast_ready_status(game_state)
            panel_visible_at = None
        if not pending:
            break
        if now >= deadline:
            for player_index in pending:
                game_state.action_dict[player_index] = []
            await broadcast_ready_status(game_state)
            break
        await game_state.wait_action()


async def run_hu_result_ready_phase(
    game_state,
    fan_count: int,
    broadcast_ready_status: Callable[..., Awaitable[None]],
    fu_fan_count: int = 0,
    pre_panel_delay_sec: float | None = None,
) -> None:
    """进入 waiting_ready，等待人类确认或超时。

    机器人 (user_id<=10) 开局即视为已准备；首次 ready_status 往往在客户端结算面板
    出现前发出，因此在「倒牌 + 渐显」结束后再广播一次，以便显示自动准备标记。
    四个座位都就绪后立即继续；deadline 仅用于防止掉线玩家永久卡住续局。
    """
    if pre_panel_delay_sec is None:
        pre_panel_delay_sec = hu_result_ready_pre_panel_seconds()
    wait_time = hu_result_ready_wait_seconds(
        fan_count, fu_fan_count, pre_panel_delay_sec=pre_panel_delay_sec
    )
    await run_result_ready_phase(
        game_state, wait_time, broadcast_ready_status,
        panel_visible_delay_sec=pre_panel_delay_sec + ROUND_END_PRESENTATION_FADE_SEC,
    )


async def run_sichuan_liuju_final_ready_phase(
    game_state,
    broadcast_ready_status: Callable[..., Awaitable[None]],
) -> None:
    """四川血战流局末步：与 EndResultPanel 8s 倒计时对齐，机器人默认已准备。"""
    await run_result_ready_phase(
        game_state, sichuan_liuju_final_ready_wait_seconds(), broadcast_ready_status,
        panel_visible_delay_sec=ROUND_END_PRESENTATION_FADE_SEC,
    )
