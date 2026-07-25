"""台湾麻将的并发响应收集与动作分发。"""

import asyncio
import logging
import time
from typing import Dict, Optional, Tuple

from .action_check import HU_ACTIONS, is_forced_declared_ready_win
from .boardcast import broadcast_ready_status


logger = logging.getLogger(__name__)


async def _collect_responses(game_state) -> Tuple[Dict[int, dict], Dict[int, list]]:
    """等待当前询问的所有有权玩家。

    台湾规则需要先知道同一张牌上到底有几家实际声明胡牌，不能沿用国标的
    “收到最高优先级动作即结束”模型。所有玩家并行计时，不会把等待时间串行相加。
    """

    allowed = {
        index: list(actions)
        for index, actions in game_state.action_dict.items()
        if actions
    }
    game_state.waiting_players_list = list(allowed)

    # 台湾状态机会在广播前清空旧窗口；这里不能再清队列，否则会误删广播后
    # 机器人立即提交的本窗口回复。
    for index in range(4):
        event = game_state.action_events[index]
        queue = game_state.action_queues[index]
        if event.is_set() and queue.empty():
            event.clear()

    if not allowed:
        return {}, allowed

    started = time.monotonic()
    grace = 0 if game_state.game_status == "waiting_ready" else game_state.step_time
    deadlines = {
        index: started + max(0, game_state.player_list[index].remaining_time + grace)
        for index in allowed
    }
    pending = set(allowed)
    responses: Dict[int, dict] = {}

    while pending:
        now = time.monotonic()
        expired = [index for index in pending if now >= deadlines[index]]
        for index in expired:
            pending.remove(index)
            game_state.player_list[index].remaining_time = 0
        if not pending:
            break

        tasks = {
            asyncio.create_task(game_state.action_events[index].wait()): index
            for index in pending
        }
        timeout = min(1.0, max(0.0, min(deadlines[index] for index in pending) - now))
        done, unfinished = await asyncio.wait(
            tasks,
            timeout=timeout,
            return_when=asyncio.FIRST_COMPLETED,
        )
        for task in unfinished:
            task.cancel()

        for task in done:
            index = tasks[task]
            game_state.action_events[index].clear()
            try:
                data = game_state.action_queues[index].get_nowait()
            except asyncio.QueueEmpty:
                continue
            action_type = data.get("action_type")
            if action_type not in allowed[index]:
                logger.warning(
                    "台湾麻将丢弃窗口内非法动作 player=%s action=%s allowed=%s",
                    index,
                    action_type,
                    allowed[index],
                )
                continue
            responses[index] = dict(data)
            pending.discard(index)

    elapsed = time.monotonic() - started
    if grace:
        charged = max(0, int(elapsed) - grace)
        if charged:
            for index in responses:
                player = game_state.player_list[index]
                player.remaining_time = max(0, player.remaining_time - charged)

    game_state.waiting_players_list = []
    return responses, allowed


async def wait_action(game_state) -> Optional[bool]:
    responses, allowed = await _collect_responses(game_state)

    if game_state.game_status == "waiting_ready":
        for index in responses:
            game_state.action_dict[index] = []
        await broadcast_ready_status(game_state)
        return bool(responses)

    if game_state.game_status == "waiting_buhua_round":
        index = game_state.current_player_index
        data = responses.get(index)
        return bool(data and data.get("action_type") == "buhua")

    if game_state.game_status == "waiting_flower_choice":
        index = game_state.current_player_index
        data = responses.get(index)
        return bool(data and data.get("action_type") == "hu_flower")

    if game_state.game_status == "waiting_hand_action":
        index = game_state.current_player_index
        data = responses.get(index)
        action_type = data.get("action_type") if data else None
        hu_detail = getattr(game_state, "result_dict", {}).get("hu_self")
        if (
            "hu_self" in allowed.get(index, ())
            and (hu_detail is None or hu_detail.get("is_win", True))
            and is_forced_declared_ready_win(game_state, index)
        ):
            # 禁止拒胡窗口即使没有收到回复（断线或超时）也必须落为胡牌。
            action_type = "hu_self"
        had_normal_hu = game_state.has_normal_self_draw(index)

        if action_type != "hu_self" and had_normal_hu:
            if game_state.enter_water(index):
                await game_state.broadcast_refresh_player_tag_list()
        if game_state.player_list[index].pending_eight_immortals and action_type != "hu_self":
            game_state.decline_eight_immortals(index)

        if action_type == "hu_self":
            game_state.accept_self_draw(index)
        elif action_type == "angang":
            await game_state.execute_angang(index, data.get("target_tile"))
        elif action_type == "jiagang":
            await game_state.execute_jiagang(index, data.get("target_tile"))
        elif action_type == "buhua":
            await game_state.execute_buhua(index)
        elif action_type in ("cut", "riichi_cut"):
            await game_state.execute_cut(index, data, declare_ready=action_type == "riichi_cut")
        elif action_type is None:
            if "buhua" in allowed.get(index, ()):
                await game_state.execute_buhua(index)
            else:
                await game_state.execute_timeout_cut(index)
        else:
            logger.error("台湾麻将摸牌阶段收到未知动作: %s", action_type)
        return True

    if game_state.game_status == "waiting_action_after_cut":
        await game_state.resolve_discard_responses(responses, allowed)
        return True

    if game_state.game_status == "waiting_action_qianggang":
        await game_state.resolve_rob_kong_responses(responses, allowed)
        return True

    logger.error("台湾麻将 wait_action 遇到未知状态: %s", game_state.game_status)
    return False
