"""战术鸣牌共享逻辑（国标 / 青雀 / 四川 / 长沙）。

开局 ask 时冻结 _tactical_action_snapshot（只读）；主询问阶段的 pass 不记入 passed，
低优先级鸣牌申请后仍从完整快照重算更高优先级竞争者并再次询问（含主阶段已 pass 者）。
force_pass 记入 _tactical_force_passed_players，本张弃牌不再作为竞争者，也不被
clear_tactical_grace_passes 清掉。

仅在当前打断窗口内 pass 的玩家在本轮申请等待中不再重复询问；切换到新的低优先级申请时清空。

国标（tactical_commit_lock=True）：玩家成功提交非 pass 鸣牌后本张弃牌区间内不可改选其他鸣牌；
grace 多轮抢断时已承诺者不再作为竞争者。川麻 / 青雀不启用承诺锁。
"""
from __future__ import annotations

from .player_count import game_player_count
import asyncio
import logging
import math
import time
from typing import Awaitable, Callable, Any

logger = logging.getLogger(__name__)

TACTICAL_PRE_GRACE_DELAY = 0.5
TACTICAL_GRACE_SECONDS = 5.0

_CHI_ACTIONS = frozenset({"chi_left", "chi_mid", "chi_right"})
_DECLINE_ACTIONS = frozenset({"pass", "force_pass"})
# 荣和/抢杠和：执行不走带音效的 do_action，须靠 is_claim 申请帧发声
_HU_CLAIM_ACTIONS = frozenset({
    "hu", "hu_self", "hu_first", "hu_second", "hu_third",
})

BroadcastFn = Callable[..., Awaitable[Any]]


def is_chi_action(action_type: str) -> bool:
    return action_type in _CHI_ACTIONS


def is_decline_action(action_type: str) -> bool:
    return action_type in _DECLINE_ACTIONS


def add_tactical_force_pass_options(gs, action_dict):
    """所有战术鸣牌询问都在取消右侧提供放弃；手牌与补花询问不调用。"""
    if getattr(gs, "tactical_call", False):
        for actions in action_dict.values():
            if "pass" in actions and "force_pass" not in actions:
                actions.append("force_pass")
    return action_dict


def is_hu_claim_action(action_type: str) -> bool:
    return action_type in _HU_CLAIM_ACTIONS


def tactical_commit_lock_enabled(gs) -> bool:
    return bool(getattr(gs, "tactical_commit_lock", False))


# 「保存已确定行为」：玩家提交非 pass 后即锁定本次选择；后续只询问尚未
# 承诺且能以更高优先级打断的玩家，已确定者不再改选。
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
    clear_tactical_round_state(gs)
    if (
        getattr(gs, "tactical_call", False)
        and gs.game_status in ("waiting_action_after_cut", "waiting_action_qianggang")
    ):
        gs._tactical_action_snapshot = {
            pid: list(alist) for pid, alist in gs.action_dict.items()
        }
        gs._tactical_opening_action_tick = getattr(gs, "server_action_tick", None)


def clear_tactical_round_state(gs, submitted_actions=None) -> None:
    if submitted_actions is not None:
        for pid in getattr(gs, "_tactical_force_passed_players", ()):
            submitted_actions[pid] = "force_pass"
    gs._tactical_action_snapshot = None
    gs._tactical_opening_action_tick = None
    gs._tactical_passed_players = set()
    gs._tactical_force_passed_players = set()
    gs._tactical_committed_players = set()


def tactical_opening_snapshot(gs):
    return getattr(gs, "_tactical_action_snapshot", None)


def tactical_force_pass_is_live(gs, player_index: int, action_tick=None) -> bool:
    """仅接受本张弃牌内的放弃；允许主询问回复迟于战术再问到达。"""
    snapshot = tactical_opening_snapshot(gs)
    if snapshot is None:
        return False
    if getattr(gs, "game_status", None) not in (
        "waiting_action_after_cut",
        "waiting_action_qianggang",
    ):
        return False
    if action_tick is not None:
        current_tick = getattr(gs, "server_action_tick", None)
        opening_tick = getattr(gs, "_tactical_opening_action_tick", None)
        if opening_tick is None or current_tick is None:
            return False
        if not opening_tick <= action_tick <= current_tick:
            return False
    return "force_pass" in snapshot.get(player_index, [])


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


def tactical_mark_player_force_passed(gs, player_index: int) -> None:
    """本张弃牌彻底退出竞争；不被 clear_tactical_grace_passes 清掉。"""
    force_passed = getattr(gs, "_tactical_force_passed_players", None)
    if force_passed is None:
        gs._tactical_force_passed_players = {player_index}
    else:
        force_passed.add(player_index)


def tactical_player_has_force_passed(gs, player_index: int) -> bool:
    force_passed = getattr(gs, "_tactical_force_passed_players", None)
    return force_passed is not None and player_index in force_passed


def tactical_action_rank(gs, action_type, player_index):
    rank = getattr(gs, "tactical_action_rank", None)
    return rank(action_type, player_index) if callable(rank) else (gs.action_priority.get(action_type, -1),)


def get_higher_priority_snapshot(gs, action_type, player_index):
    """从开局冻结快照重算「更高优先级竞争者」；主询问 pass 不排除。"""
    current_rank = tactical_action_rank(gs, action_type, player_index)
    higher_action_dict = {pid: [] for pid in range(game_player_count(gs))}
    any_higher = False
    source = tactical_opening_snapshot(gs) or gs.action_dict
    for pid in range(game_player_count(gs)):
        if pid == player_index or tactical_player_has_passed(gs, pid):
            continue
        if tactical_player_has_force_passed(gs, pid):
            continue
        if tactical_player_is_committed(gs, pid):
            continue
        source_actions = source.get(pid, [])
        declines = [a for a in source_actions if is_decline_action(a)]
        filtered = [
            a for a in source_actions
            if not is_decline_action(a) and tactical_action_rank(gs, a, pid) > current_rank
        ]
        if filtered:
            higher_action_dict[pid] = filtered + declines
            any_higher = True
    return higher_action_dict, any_higher


def should_enter_tactical_grace(gs, action_type, player_index) -> bool:
    """快照中仍有高于当前申请优先级的竞争者时进入战术等待（主询问 pass 仍算竞争者）。"""
    _, any_higher = get_higher_priority_snapshot(gs, action_type, player_index)
    return any_higher


def _pop_queued_actions(gs, pid):
    items = []
    gs.action_events[pid].clear()
    while not gs.action_queues[pid].empty():
        try:
            item = gs.action_queues[pid].get_nowait()
            if _valid_tactical_receipt(gs, pid, item):
                items.append(item)
        except asyncio.QueueEmpty:
            break
    return items


def _valid_tactical_receipt(gs, pid, item):
    """Optional authoritative arrival/deadline validation for timed adapters."""
    hook = getattr(gs, "tactical_action_receipt_valid", None)
    return hook(pid, item) if callable(hook) else True


def _take_pre_submitted_claim(gs, competitors, current_priority, submitted_actions=None, *, current_rank=None):
    """抽队列里的抢断；force_pass 记入放弃集合，不当垃圾丢掉。"""
    pre_submitted = None
    current_rank = (current_priority,) if current_rank is None else current_rank
    competitor_set = set(competitors)
    for pid in range(game_player_count(gs)):
        queued_actions = _pop_queued_actions(gs, pid)
        validate = getattr(gs, "validate_tactical_queued_action", None)
        if callable(validate):
            queued_actions = [item for item in queued_actions if validate(pid, item)]
        if submitted_actions is not None:
            for item in queued_actions:
                if item.get("action_type") in (tactical_opening_snapshot(gs) or {}).get(pid, []):
                    submitted_actions[pid] = item["action_type"]
        if any(item.get("action_type") == "force_pass" for item in queued_actions):
            tactical_mark_player_force_passed(gs, pid)
        if pid not in competitor_set or tactical_player_has_force_passed(gs, pid):
            continue
        for drained in queued_actions:
            d_type = drained.get("action_type")
            if not d_type:
                continue
            if is_decline_action(d_type):
                continue
            d_priority = gs.action_priority.get(d_type, -1)
            candidate_rank = tactical_action_rank(gs, d_type, pid)
            if candidate_rank <= current_rank:
                continue
            if pre_submitted is None or candidate_rank > tactical_action_rank(gs, pre_submitted[1], pre_submitted[2]):
                pre_submitted = (d_priority, d_type, pid, dict(drained))
    return pre_submitted


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
    submitted_actions=None,
):
    """战术鸣牌打断阶段。返回 (action_type, player_index, action_data, claim_broadcasted)。"""
    grace_seconds = float(getattr(gs, "tactical_grace_seconds", TACTICAL_GRACE_SECONDS))
    skip_claim_broadcast = initial_claim_broadcasted
    while True:
        clear_tactical_grace_passes(gs)
        higher_action_dict, _ = get_higher_priority_snapshot(
            gs, action_type, player_index
        )

        current_priority = gs.action_priority[action_type]
        competitors = [pid for pid, alist in higher_action_dict.items() if alist]
        current_rank = tactical_action_rank(gs, action_type, player_index)
        pre_submitted = _take_pre_submitted_claim(gs, competitors, current_priority, submitted_actions, current_rank=current_rank)

        if pre_submitted is not None:
            _, action_type, player_index, action_data = pre_submitted
            # 预提交抢断会替换已广播的低优先级动作；申请标记必须随最终动作重置，
            # 否则新动作不会发 is_claim，结算却会被错误标记为 silent。
            skip_claim_broadcast = False
            tactical_mark_player_committed(gs, player_index)
            logger.info(
                "战术鸣牌打捞到更高优先级抢断 action_type=%s player_index=%s",
                action_type,
                player_index,
            )
            continue

        if not is_decline_action(action_type) and not skip_claim_broadcast:
            await broadcast_do_action(
                gs,
                action_list=[action_type],
                action_player=player_index,
                cut_tile=cut_tile,
                is_claim=True,
            )
        skip_claim_broadcast = False

        # 抽队列或 await 申请广播期间都可能收到 force_pass，必须按最新状态再问。
        higher_action_dict, any_higher = get_higher_priority_snapshot(
            gs, action_type, player_index
        )
        if not any_higher:
            return action_type, player_index, action_data, True
        competitors = [pid for pid, alist in higher_action_dict.items() if alist]
        gs.action_dict = higher_action_dict
        gs.waiting_players_list = list(competitors)

        await broadcast_ask_other_action(
            gs,
            remaining_time_override=math.ceil(grace_seconds),
            is_tactical_recheck=True,
        )
        # 再问会递增 server_action_tick；AI 用 _waiting_action_tick 校验本轮，必须同步，
        # 否则机器人会拒动并空等到打断窗口超时（表现为低优先级吃完后卡住约 5s）。
        gs._waiting_action_tick = getattr(gs, "server_action_tick", None)

        elapsed = 0.0
        collector = getattr(gs, "collect_tactical_recheck", None)
        new_claim = await collector(current_priority, submitted_actions) if callable(collector) else None
        while not callable(collector) and elapsed < grace_seconds:
            remaining = grace_seconds - elapsed
            task_list = []
            task_to_player = {}
            for pid in range(game_player_count(gs)):
                if gs.action_dict[pid]:
                    t = asyncio.create_task(gs.action_events[pid].wait())
                    task_list.append(t)
                    task_to_player[t] = pid
            timer_task = asyncio.create_task(asyncio.sleep(remaining))
            task_list.append(timer_task)

            start = time.time()
            try:
                done, pending = await asyncio.wait(task_list, return_when=asyncio.FIRST_COMPLETED)
            finally:
                for task in task_list:
                    if not task.done():
                        task.cancel()
                await asyncio.gather(*task_list, return_exceptions=True)
            end = time.time()
            elapsed += end - start

            best_submitted = None
            for t in done:
                if t is timer_task:
                    continue
                temp_pid = task_to_player[t]
                temp_data = await gs.action_queues[temp_pid].get()
                if not _valid_tactical_receipt(gs, temp_pid, temp_data):
                    gs.action_events[temp_pid].clear()
                    # Preserve another already queued valid response's wake-up.
                    if not gs.action_queues[temp_pid].empty():
                        gs.action_events[temp_pid].set()
                    continue
                temp_type = temp_data.get("action_type")
                if submitted_actions is not None:
                    submitted_actions[temp_pid] = temp_type
                gs.action_events[temp_pid].clear()
                if is_decline_action(temp_type):
                    if temp_type == "force_pass":
                        tactical_mark_player_force_passed(gs, temp_pid)
                    tactical_mark_player_passed_in_grace(gs, temp_pid)
                    gs.action_dict[temp_pid] = []
                    continue
                gs.action_dict[temp_pid] = []
                temp_priority = gs.action_priority.get(temp_type, -1)
                candidate_rank = tactical_action_rank(gs, temp_type, temp_pid)
                if candidate_rank <= current_rank:
                    continue
                if best_submitted is None or candidate_rank > tactical_action_rank(gs, best_submitted[1], best_submitted[2]):
                    best_submitted = (temp_priority, temp_type, temp_pid, dict(temp_data))

            if best_submitted is not None:
                new_claim = best_submitted
                break

            if not any(gs.action_dict[pid] for pid in range(game_player_count(gs))):
                break

        if new_claim is None:
            if submitted_actions is not None:
                for pid in competitors:
                    if gs.action_dict[pid]:
                        submitted_actions[pid] = "pass" # 打断窗口超时
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
    submitted_actions=None,
):
    """主询问结束后：战术鸣牌申请广播；有更高竞争者则再进入打断窗口。

    荣和即使无更高优先级竞争者也发 is_claim：和牌不走带音效的 do_action，
    否则申请阶段无声，鸣牌保护 gap 后才在 show_result 听到。结算侧靠
    _tactical_silent_action 跳过重复和牌音效。
    submitted_actions 可保留所有候选的回复，供国标抢杠错和后续判。
    """
    if not (
        getattr(gs, "tactical_call", False)
        and action_data
        and not is_decline_action(action_type)
        and gs.game_status in ("waiting_action_after_cut", "waiting_action_qianggang")
    ):
        clear_tactical_round_state(gs, submitted_actions)
        return action_type, player_index, action_data, False

    need_grace = should_enter_tactical_grace(gs, action_type, player_index)
    need_hu_claim_sfx = is_hu_claim_action(action_type) and not need_grace
    if not need_grace and not need_hu_claim_sfx:
        clear_tactical_round_state(gs, submitted_actions)
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
        clear_tactical_round_state(gs, submitted_actions)
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
        submitted_actions=submitted_actions,
    )
    clear_tactical_round_state(gs, submitted_actions)
    if claim_broadcasted:
        gs._tactical_silent_action = True
    return action_type, player_index, action_data, claim_broadcasted
