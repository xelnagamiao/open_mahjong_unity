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


async def run_hu_result_ready_phase(
    game_state,
    fan_count: int,
    broadcast_ready_status: Callable[..., Awaitable[None]],
    fu_fan_count: int = 0,
) -> None:
    """进入 waiting_ready，等待人类确认或超时。

    机器人 (user_id<=10) 开局即视为已准备；首次 ready_status 往往在客户端结算面板
    出现前发出，因此在「倒牌 + 渐显」结束后再广播一次，以便显示自动准备标记。
    四个座位都就绪后立即继续；deadline 仅用于防止掉线玩家永久卡住续局。
    """
    wait_time = hu_result_ready_wait_seconds(fan_count, fu_fan_count)
    deadline = time.time() + wait_time
    panel_visible_at = time.time() + hu_result_ready_pre_panel_seconds() + ROUND_END_PRESENTATION_FADE_SEC

    game_state.action_dict = {}
    for player in game_state.player_list:
        if player.user_id <= 10:
            game_state.action_dict[player.player_index] = []
        else:
            game_state.action_dict[player.player_index] = ["ready"]
            player.remaining_time = int(math.ceil(wait_time))

    game_state.game_status = "waiting_ready"
    # waiting_ready starts a fresh ask round. Reusing delivery timestamps from
    # the preceding action can make other seats time out after one player readies.
    begin_ask_round(game_state)
    await broadcast_ready_status(game_state)

    rebroadcasted_for_panel = False
    while True:
        pending = [i for i in game_state.action_dict if game_state.action_dict.get(i)]
        now = time.time()
        if not rebroadcasted_for_panel and now >= panel_visible_at:
            await broadcast_ready_status(game_state)
            rebroadcasted_for_panel = True
        if not pending:
            break
        if now >= deadline:
            for player_index in pending:
                game_state.action_dict[player_index] = []
            await broadcast_ready_status(game_state)
            break
        # wait_action compares remaining_time with elapsed time since delivery.
        # Replacing remaining_time with (deadline - now) would subtract elapsed
        # time twice after each individual ready response.
        await game_state.wait_action()


async def run_sichuan_liuju_final_ready_phase(
    game_state,
    broadcast_ready_status: Callable[..., Awaitable[None]],
) -> None:
    """四川血战流局末步：与 EndResultPanel 8s 倒计时对齐，机器人默认已准备。"""
    wait_time = sichuan_liuju_final_ready_wait_seconds()
    deadline = time.time() + wait_time
    panel_visible_at = time.time() + ROUND_END_PRESENTATION_FADE_SEC

    game_state.action_dict = {}
    for player in game_state.player_list:
        if player.user_id <= 10:
            game_state.action_dict[player.player_index] = []
        else:
            game_state.action_dict[player.player_index] = ["ready"]
            player.remaining_time = int(math.ceil(wait_time))

    game_state.game_status = "waiting_ready"
    begin_ask_round(game_state)
    await broadcast_ready_status(game_state)

    rebroadcasted_for_panel = False
    while True:
        pending = [i for i in game_state.action_dict if game_state.action_dict.get(i)]
        now = time.time()
        if not rebroadcasted_for_panel and now >= panel_visible_at:
            await broadcast_ready_status(game_state)
            rebroadcasted_for_panel = True
        if not pending:
            break
        if now >= deadline:
            for player_index in pending:
                game_state.action_dict[player_index] = []
            await broadcast_ready_status(game_state)
            break
        await game_state.wait_action()
