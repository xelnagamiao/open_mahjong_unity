"""在川麻询问发出前开放收包，等待阶段继续消费同一窗口。"""
import asyncio

from ..public.lifecycle import start_owned_task
from ..public.tactical_claim import (
    init_tactical_round_state, is_decline_action, tactical_force_pass_is_live,
    tactical_mark_player_force_passed, tactical_mark_player_passed_in_grace,
)


def prepare_action_window(state, *, new_ask=False, is_tactical_recheck=False):
    """同步入口权限和事件；只在新询问发出前清除上一轮积压。

    此函数不 await：登记等待座位、清旧包与发布询问之间不会插入收包任务。
    wait_action 再次进入时必须保留客户端在广播期间提交的本轮响应。
    战术再问沿用本张弃牌的快照及 force_pass 状态。
    """
    window = (state.game_status, state.server_action_tick)
    if new_ask:
        for queue in state.action_queues.values():
            while not queue.empty():
                try:
                    queue.get_nowait()
                except asyncio.QueueEmpty:
                    break
    if not is_tactical_recheck and (
        new_ask or getattr(state, "_sichuan_action_window", None) != window
    ):
        init_tactical_round_state(state)
    state._sichuan_action_window = window
    state._waiting_action_tick = state.server_action_tick
    state.waiting_players_list = [
        seat for seat, actions in state.action_dict.items() if actions
    ]
    for seat, event in state.action_events.items():
        if seat in state.waiting_players_list and not state.action_queues[seat].empty():
            event.set()
        else:
            event.clear()


def validate_tactical_queued_action(state, seat, data):
    """战术抢断只接受本张牌合法选项及当前询问的响应。"""
    action = data.get("action_type")
    from .duplicate_wall import can_draw
    if action in ("gang", "angang", "jiagang") and not can_draw(state, seat):
        return False
    snapshot = getattr(state, "_tactical_action_snapshot", None) or {}
    if action not in snapshot.get(seat, []):
        return False
    if action not in state.action_dict.get(seat, []):
        # 已消费的响应不可被队列中的重复包覆盖。
        return False
    tick = data.get("_action_tick")
    return tick in (None, state.server_action_tick) or (
        action == "force_pass" and tactical_force_pass_is_live(state, seat, tick)
    )


async def collect_tactical_recheck(state, current_priority, submitted_actions):
    """碰杠再询问中逐家确认点和，保留所有报和者而非只取首个抢断者。

    沿用战术窗口的独立时限，不扣普通局时；所有响应仍经动作与轮次校验。
    """
    loop = asyncio.get_running_loop()
    deadline = loop.time() + float(state.tactical_grace_seconds)
    waiting = {seat for seat, actions in state.action_dict.items() if actions}
    best = None
    while waiting:
        for seat in sorted(waiting):
            queue = state.action_queues[seat]
            while not queue.empty():
                data = queue.get_nowait()
                if queue.empty():
                    state.action_events[seat].clear()
                action = data.get("action_type")
                if action not in state.action_dict[seat] or not validate_tactical_queued_action(state, seat, data):
                    continue
                submitted_actions[seat] = action
                state.action_dict[seat] = []
                waiting.remove(seat)
                if is_decline_action(action):
                    if action == "force_pass":
                        tactical_mark_player_force_passed(state, seat)
                    tactical_mark_player_passed_in_grace(state, seat)
                else:
                    priority = state.action_priority[action]
                    if priority > current_priority and (best is None or priority > best[0]):
                        best = (priority, action, seat, dict(data))
                break
        state.waiting_players_list = sorted(waiting)
        if not waiting:
            break
        remaining = deadline - loop.time()
        if remaining <= 0:
            for seat in waiting:
                submitted_actions[seat] = "pass"
                state.action_dict[seat] = []
            state.waiting_players_list = []
            break
        tasks = [start_owned_task(state, state.action_events[seat].wait()) for seat in waiting]
        try:
            await asyncio.wait(tasks, timeout=remaining, return_when=asyncio.FIRST_COMPLETED)
        finally:
            for task in tasks:
                if not task.done():
                    task.cancel()
            await asyncio.gather(*tasks, return_exceptions=True)
    return best
