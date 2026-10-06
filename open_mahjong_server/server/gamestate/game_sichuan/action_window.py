"""在川麻询问发出前开放收包，等待阶段继续消费同一窗口。"""
import asyncio

from ..public.tactical_claim import init_tactical_round_state


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
