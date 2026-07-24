"""战术鸣牌共享逻辑（国标 / 青雀 / 四川）。

开局 ask 时冻结 _tactical_action_snapshot（只读）；主询问阶段的 pass 不记入 passed，
低优先级鸣牌申请后仍从完整快照重算更高优先级竞争者并再次询问（含主阶段已 pass 者）。
仅在当前打断窗口内 pass 的玩家在本轮申请等待中不再重复询问；切换到新的低优先级申请时清空。

国标（tactical_commit_lock=True）：玩家成功提交非 pass 鸣牌后本张弃牌区间内不可改选其他鸣牌；
grace 多轮抢断时已承诺者不再作为竞争者。川麻 / 青雀不启用承诺锁。
"""
from __future__ import annotations

import asyncio
import logging
import math
import time
from typing import Awaitable, Callable, Any

logger = logging.getLogger(__name__)

TACTICAL_PRE_GRACE_DELAY = 0.5
TACTICAL_GRACE_SECONDS = 5.0

_CHI_ACTIONS = frozenset({"chi_left", "chi_mid", "chi_right"})
# 荣和/抢杠和：执行不走带音效的 do_action，须靠 is_claim 申请帧发声
_HU_CLAIM_ACTIONS = frozenset({
    "hu", "hu_self", "hu_first", "hu_second", "hu_third",
})

BroadcastFn = Callable[..., Awaitable[Any]]


def is_chi_action(action_type: str) -> bool:
    return action_type in _CHI_ACTIONS


def is_hu_claim_action(action_type: str) -> bool:
    return action_type in _HU_CLAIM_ACTIONS


def tactical_commit_lock_enabled(gs) -> bool:
    return bool(getattr(gs, "tactical_commit_lock", False))


def tactical_mark_player_committed(gs, player_index: int) -> None:
    """国标：玩家已成功提交非 pass 鸣牌，本张弃牌区间内不可改选。"""
    if not tactical_commit_lock_enabled(gs):
        return
    committed = getattr(gs, "_tactical_committed_players", None)
    if committed is None:
        gs._tactical_committed_players = {player_index}
    else:
        committed.add(player_index)


def tactical_player_is_committed(gs, player_index: int) -> bool:
    if not tactical_commit_lock_enabled(gs):
        return False
    committed = getattr(gs, "_tactical_committed_players", None)
    return committed is not None and player_index in committed


def init_tactical_round_state(gs) -> None:
    """wait_action 主循环开始前：冻结本张弃牌的鸣牌选项快照。"""
    if (
        getattr(gs, "tactical_call", False)
        and gs.game_status in ("waiting_action_after_cut", "waiting_action_qianggang")
    ):
        gs._tactical_action_snapshot = {
            pid: list(alist) for pid, alist in gs.action_dict.items()
        }
        gs._tactical_passed_players = set()
        gs._tactical_committed_players = set()
    else:
        gs._tactical_action_snapshot = None
        gs._tactical_passed_players = set()
        gs._tactical_committed_players = set()


def clear_tactical_round_state(gs) -> None:
    gs._tactical_action_snapshot = None
    gs._tactical_passed_players = set()
    gs._tactical_committed_players = set()


def tactical_opening_snapshot(gs):
    return getattr(gs, "_tactical_action_snapshot", None)


def clear_tactical_grace_passes(gs) -> None:
    """新一轮低优先级申请进入打断窗口前清空；主询问 pass 不在此集合。"""
    gs._tactical_passed_players = set()


def tactical_mark_player_passed_in_grace(gs, player_index: int) -> None:
    """仅打断窗口内的 pass：本轮回询问等待中不再问该家。"""
    passed = getattr(gs, "_tactical_passed_players", None)
    if passed is not None:
        passed.add(player_index)


def tactical_player_has_passed(gs, player_index: int) -> bool:
    passed = getattr(gs, "_tactical_passed_players", None)
    return passed is not None and player_index in passed


def get_higher_priority_snapshot(gs, action_type, player_index):
    """从开局冻结快照重算「更高优先级竞争者」；主询问 pass 不排除。"""
    current_priority = gs.action_priority[action_type]
    higher_action_dict = {pid: [] for pid in range(4)}
    any_higher = False
    source = tactical_opening_snapshot(gs) or gs.action_dict
    for pid in range(4):
        if pid == player_index or tactical_player_has_passed(gs, pid):
            continue
        if tactical_player_is_committed(gs, pid):
            continue
        filtered = [
            a for a in source.get(pid, [])
            if a != "pass" and gs.action_priority[a] > current_priority
        ]
        if filtered:
            higher_action_dict[pid] = filtered + ["pass"]
            any_higher = True
    return higher_action_dict, any_higher


def should_enter_tactical_grace(gs, action_type, player_index) -> bool:
    """快照中仍有高于当前申请优先级的竞争者时进入战术等待（主询问 pass 仍算竞争者）。"""
    _, any_higher = get_higher_priority_snapshot(gs, action_type, player_index)
    return any_higher


async def tactical_grace_phase(
    gs,
    action_type,
    player_index,
    action_data,
    cut_tile,
    *,
    broadcast_do_action: BroadcastFn,
    broadcast_ask_other_action: BroadcastFn,
    initial_claim_broadcasted: bool = False,
):
    """战术鸣牌打断阶段。返回 (action_type, player_index, action_data, claim_broadcasted)。"""
    grace_seconds = float(getattr(gs, "tactical_grace_seconds", TACTICAL_GRACE_SECONDS))
    skip_claim_broadcast = initial_claim_broadcasted
    while True:
        clear_tactical_grace_passes(gs)
        higher_action_dict, any_higher = get_higher_priority_snapshot(
            gs, action_type, player_index
        )

        if not is_chi_action(action_type) and not any_higher:
            # 无更高竞争者：若本轮尚未发过申请帧（例如 grace 内被更高优抢断），补发 is_claim
            if action_type != "pass" and not skip_claim_broadcast:
                await broadcast_do_action(
                    gs,
                    action_list=[action_type],
                    action_player=player_index,
                    cut_tile=cut_tile,
                    is_claim=True,
                )
                return action_type, player_index, action_data, True
            return action_type, player_index, action_data, True

        current_priority = gs.action_priority[action_type]
        competitors = [pid for pid, alist in higher_action_dict.items() if alist]

        pre_submitted = None
        for pid in competitors:
            gs.action_events[pid].clear()
            while not gs.action_queues[pid].empty():
                try:
                    drained = gs.action_queues[pid].get_nowait()
                except Exception:
                    break
                d_type = drained.get("action_type")
                if not d_type or d_type == "pass":
                    continue
                d_priority = gs.action_priority.get(d_type, -1)
                if d_priority <= current_priority:
                    continue
                if pre_submitted is None or d_priority > pre_submitted[0]:
                    pre_submitted = (d_priority, d_type, pid, dict(drained))
        for pid in range(4):
            if pid in competitors:
                continue
            gs.action_events[pid].clear()
            while not gs.action_queues[pid].empty():
                try:
                    gs.action_queues[pid].get_nowait()
                except Exception:
                    break

        if pre_submitted is not None:
            _, action_type, player_index, action_data = pre_submitted
            tactical_mark_player_committed(gs, player_index)
            logger.info(
                "战术鸣牌打捞到更高优先级抢断 action_type=%s player_index=%s",
                action_type,
                player_index,
            )
            continue

        if action_type != "pass" and not skip_claim_broadcast:
            await broadcast_do_action(
                gs,
                action_list=[action_type],
                action_player=player_index,
                cut_tile=cut_tile,
                is_claim=True,
            )
        skip_claim_broadcast = False

        gs.action_dict = higher_action_dict
        gs.waiting_players_list = list(competitors)

        if any_higher:
            await broadcast_ask_other_action(
                gs,
                remaining_time_override=math.ceil(grace_seconds),
                is_tactical_recheck=True,
            )
            # 再问会递增 server_action_tick；AI 用 _waiting_action_tick 校验本轮，必须同步，
            # 否则机器人会拒动并空等到打断窗口超时（表现为低优先级吃完后卡住约 5s）。
            gs._waiting_action_tick = getattr(gs, "server_action_tick", None)

        elapsed = 0.0
        new_claim = None
        while elapsed < grace_seconds:
            remaining = grace_seconds - elapsed
            task_list = []
            task_to_player = {}
            for pid in range(4):
                if gs.action_dict[pid]:
                    t = asyncio.create_task(gs.action_events[pid].wait())
                    task_list.append(t)
                    task_to_player[t] = pid
            timer_task = asyncio.create_task(asyncio.sleep(remaining))
            task_list.append(timer_task)

            start = time.time()
            done, pending = await asyncio.wait(task_list, return_when=asyncio.FIRST_COMPLETED)
            end = time.time()
            elapsed += end - start
            for t in pending:
                t.cancel()

            best_submitted = None
            for t in done:
                if t is timer_task:
                    continue
                temp_pid = task_to_player[t]
                temp_data = await gs.action_queues[temp_pid].get()
                temp_type = temp_data.get("action_type")
                gs.action_events[temp_pid].clear()
                if temp_type == "pass":
                    tactical_mark_player_passed_in_grace(gs, temp_pid)
                    gs.action_dict[temp_pid] = []
                    continue
                gs.action_dict[temp_pid] = []
                temp_priority = gs.action_priority.get(temp_type, -1)
                if temp_priority <= current_priority:
                    continue
                if best_submitted is None or temp_priority > best_submitted[0]:
                    best_submitted = (temp_priority, temp_type, temp_pid, dict(temp_data))

            if best_submitted is not None:
                new_claim = best_submitted
                break

            if not any(gs.action_dict[pid] for pid in range(4)):
                break

        if new_claim is None:
            return action_type, player_index, action_data, True

        _, action_type, player_index, action_data = new_claim
        tactical_mark_player_committed(gs, player_index)


async def apply_tactical_claim_if_needed(
    gs,
    action_type,
    player_index,
    action_data,
    *,
    broadcast_do_action: BroadcastFn,
    broadcast_ask_other_action: BroadcastFn,
):
    """主询问结束后：战术鸣牌申请广播；有更高竞争者则再进入打断窗口。

    荣和即使无更高优先级竞争者也发 is_claim：和牌不走带音效的 do_action，
    否则申请阶段无声，鸣牌保护 gap 后才在 show_result 听到。结算侧靠
    _tactical_silent_action 跳过重复和牌音效。
    """
    if not (
        getattr(gs, "tactical_call", False)
        and action_data
        and action_type != "pass"
        and gs.game_status in ("waiting_action_after_cut", "waiting_action_qianggang")
    ):
        return action_type, player_index, action_data, False

    need_grace = should_enter_tactical_grace(gs, action_type, player_index)
    need_hu_claim_sfx = is_hu_claim_action(action_type) and not need_grace
    if not need_grace and not need_hu_claim_sfx:
        return action_type, player_index, action_data, False

    if gs.game_status == "waiting_action_qianggang" and getattr(gs, "jiagang_tile", None) is not None:
        cut_tile_for_claim = gs.jiagang_tile
    else:
        cut_tile_for_claim = gs.player_list[gs.current_player_index].discard_tiles[-1]
    await broadcast_do_action(
        gs,
        action_list=[action_type],
        action_player=player_index,
        cut_tile=cut_tile_for_claim,
        is_claim=True,
    )
    await asyncio.sleep(float(getattr(gs, "tactical_pre_grace_delay", TACTICAL_PRE_GRACE_DELAY)))

    if not need_grace:
        clear_tactical_round_state(gs)
        gs._tactical_silent_action = True
        return action_type, player_index, action_data, True

    action_type, player_index, action_data, claim_broadcasted = await tactical_grace_phase(
        gs,
        action_type,
        player_index,
        action_data,
        cut_tile_for_claim,
        broadcast_do_action=broadcast_do_action,
        broadcast_ask_other_action=broadcast_ask_other_action,
        initial_claim_broadcasted=True,
    )
    clear_tactical_round_state(gs)
    if claim_broadcasted:
        gs._tactical_silent_action = True
    return action_type, player_index, action_data, claim_broadcasted
