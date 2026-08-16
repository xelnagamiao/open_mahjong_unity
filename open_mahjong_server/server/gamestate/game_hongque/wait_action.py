"""虹雀弃牌响应与战术鸣牌状态机。

与国标/川麻服务端采用相同的组件边界：GameState 只负责编排，所有弃牌后的
等待、优先级仲裁和超时都集中在本模块。战术窗口仅保存响应元数据；手牌、河牌
和副露只在最终仲裁完成后修改，不保存或回滚任何中间牌面。

虹雀优先级：和(7) > 虹(6) > 碰(5) > 下家吃(4) > 对家吃(3) > 上家吃(2)。
同一张弃牌允许日麻式多家荣和；其他同级动作先申请者胜。
"""
from __future__ import annotations

import asyncio
import time
from dataclasses import dataclass, field
from typing import Optional

from .action_check import check_action_after_cut
from .ron_resolution import resolve_collected_rons
from .state_machine import HongqueStatus


@dataclass
class ClaimSubmission:
    player_index: int
    candidate: dict


@dataclass
class ClaimWindow:
    """一张弃牌对应的瞬时等待状态，不包含任何牌面变更。"""

    options: dict[int, list[dict]]
    pending: set[int]
    stage: str = "initial"
    active: Optional[ClaimSubmission] = None
    accepted_rons: dict[int, ClaimSubmission] = field(default_factory=dict)
    accepted_ticks: dict[int, set[int]] = field(default_factory=dict)
    deadline: Optional[float] = None

    def submit(self, player_index: int, candidate: dict) -> ClaimSubmission:
        submission = ClaimSubmission(player_index, dict(candidate))
        self.active = submission
        if candidate.get("kind") == "win":
            self.accepted_rons[player_index] = submission
        return submission

    def note_asked(self, player_indexes, action_tick: int) -> None:
        for player_index in player_indexes:
            self.accepted_ticks.setdefault(player_index, set()).add(action_tick)

    def accepts_tick(self, player_index: int, action_tick: int) -> bool:
        return action_tick in self.accepted_ticks.get(player_index, set())


def candidate_rank(candidate: dict) -> int:
    return int(candidate.get("priority", 0) or 0)


# 「完整快照重新过滤」：开窗时冻结全部候选；每次有人申请，都从原快照筛出
# 更高优先级动作重新询问。旧选择不锁定，玩家被别人打断后可以改选。
def _filtered_snapshot(window: ClaimWindow, player_index: int) -> list[dict]:
    options = window.options.get(player_index, [])
    if window.stage != "tactical" or window.active is None:
        return list(options)
    if player_index == window.active.player_index:
        return []
    active = window.active.candidate
    active_rank = candidate_rank(active)
    return [
        candidate for candidate in options
        if candidate_rank(candidate) > active_rank
        or (
            active.get("kind") == "win"
            and candidate_rank(candidate) == active_rank
            and candidate.get("kind") == "win"
            and player_index not in window.accepted_rons
        )
    ]


def _eligible_recheck_players(window: ClaimWindow) -> set[int]:
    """从开窗快照筛出可抢断者；仅当前申请者不立即询问自己。"""
    return {
        player_index for player_index in window.options
        if _filtered_snapshot(window, player_index)
    }


def actions_for_viewer(game_state, player_index: int) -> tuple[list[str], list[dict]]:
    """当前窗口向指定玩家开放的动作；不从历史 response 反推。"""
    if game_state.phase != "claim":
        return [], []
    window: Optional[ClaimWindow] = getattr(game_state, "claim_window", None)
    if window is not None:
        if player_index not in window.pending:
            return [], []
        options = _filtered_snapshot(window, player_index)
        return (["pass", "claim"], list(options)) if options else ([], [])
    if player_index not in getattr(game_state, "claim_options", {}):
        return [], []
    existing = game_state.claim_responses.get(player_index)
    upgrade_players = getattr(game_state, "_claim_upgrade_players", set())
    if (existing is not None and existing.get("action") == "claim"
            and player_index in upgrade_players):
        existing_priority = int(existing["candidate"].get("priority", 0) or 0)
        higher = [
            item for item in game_state.claim_options[player_index]
            if int(item.get("priority", 0) or 0) > existing_priority
        ]
        return (["claim"], higher) if higher else ([], [])
    if player_index not in game_state.claim_responses:
        return ["pass", "claim"], list(game_state.claim_options[player_index])
    return [], []


async def open_claim_window(game_state) -> None:
    options = check_action_after_cut(game_state)
    game_state.claim_options = options
    game_state.claim_responses = {}
    game_state._claim_apply_broadcast.clear()
    game_state.claim_window = ClaimWindow(options=options, pending=set(options)) if options else None
    if not options:
        await advance_after_unclaimed_discard(game_state)
        return

    # 弃牌已经单独送达；有询问时开启新的事件批次，避免客户端重播出牌动画。
    game_state.events = []
    # Snapshot recovery may resume from a saved turn plus last_discard. Route
    # that compatibility entry through the normal resolving state as well.
    if game_state.state_machine.status == HongqueStatus.WAITING_HAND_ACTION:
        game_state._transition(HongqueStatus.RESOLVING_DISCARD)
    game_state._transition(HongqueStatus.WAITING_ACTION_AFTER_CUT)
    game_state._start_claim_clock()
    game_state.message = "等待亮牌或捉和"
    game_state._advance_tick()
    game_state.claim_window.note_asked(game_state.claim_window.pending, game_state.action_tick)
    await game_state.broadcast_state()
    game_state.events = []
    _schedule_claim_timeout(game_state)

    # 普通机器人仍使用自己的 AI；调试模式绝不把真人加入自动任务。
    for player_index in tuple(options):
        player = game_state.players[player_index]
        if player.user_id in (2, 3):
            game_state._schedule_bot_claim(player_index, game_state.action_tick)
        elif player.is_bot:
            await handle_claim_action(game_state, player, "pass", None)


async def wait_action(game_state) -> None:
    """与其他规则同名的等待动作入口。"""
    await open_claim_window(game_state)


async def handle_claim_action(game_state, player, action: str,
                              candidate_id: Optional[str]) -> None:
    window: Optional[ClaimWindow] = getattr(game_state, "claim_window", None)
    if game_state.phase != "claim" or window is None:
        raise ValueError("当前不在亮牌等待阶段")
    if player.index not in window.pending:
        raise ValueError("你没有待回应的亮牌操作")

    _, candidates = actions_for_viewer(game_state, player.index)

    if action == "pass":
        if window.stage == "initial":
            game_state._consume_time_bank(player, game_state.claim_started_at)
        game_state.claim_responses[player.index] = {"action": "pass"}
        window.pending.discard(player.index)
        if not window.pending:
            await resolve_claims(game_state)
        else:
            await game_state.broadcast_state()
        return

    if action != "claim":
        raise ValueError("未知的亮牌操作")
    candidate = next((item for item in candidates if item.get("id") == candidate_id), None)
    if candidate is None:
        raise ValueError("亮牌候选无效或优先级不足")

    if window.stage == "initial":
        game_state._consume_time_bank(player, game_state.claim_started_at)
    submission = window.submit(player.index, candidate)
    game_state.claim_responses[player.index] = {
        "action": "claim",
        "candidate": submission.candidate,
    }
    await broadcast_claim_application(game_state, player.index, submission.candidate)
    await _open_tactical_recheck(game_state)


async def broadcast_claim_application(game_state, player_index: int, candidate: dict) -> None:
    """广播申请动画但不修改牌面；最终执行通过 silent 避免重复发声。"""
    candidate_id = candidate.get("id")
    if game_state._claim_apply_broadcast.get(player_index) == candidate_id:
        return
    game_state._claim_apply_broadcast[player_index] = candidate_id
    game_state.events = []
    game_state._record_event(
        "claim_apply",
        player=player_index,
        kind=candidate.get("kind"),
        base_kind=candidate.get("base_kind", candidate.get("kind")),
        tile=game_state.last_discard["tile"] if game_state.last_discard else None,
        tiles=list(candidate.get("tiles", ())),
        hand_tiles=list(candidate.get("hand_tiles", ())),
    )
    await game_state.broadcast_state()
    game_state.events = []


async def _open_tactical_recheck(game_state) -> None:
    window: ClaimWindow = game_state.claim_window
    window.stage = "tactical"
    window.pending = _eligible_recheck_players(window)
    # 回应只属于上一轮询问；保留提交历史用于最终仲裁，但被重新询问的
    # 玩家必须能够重新选择开窗快照中仍高于当前申请的动作。
    for player_index in window.pending:
        game_state.claim_responses.pop(player_index, None)
    game_state.claim_started_at = time.monotonic()
    window.deadline = game_state.claim_started_at + game_state.tactical_grace_seconds
    game_state.claim_deadlines.clear()
    game_state.message = "战术鸣牌：等待更高优先级操作"
    game_state._advance_tick()
    window.note_asked(window.pending, game_state.action_tick)
    await game_state.broadcast_state()

    if not window.pending:
        await resolve_claims(game_state)
        return
    _schedule_claim_timeout(game_state)
    for player_index in tuple(window.pending):
        if game_state.players[player_index].user_id in (2, 3):
            game_state._schedule_bot_claim(player_index, game_state.action_tick)


def _schedule_claim_timeout(game_state) -> None:
    current = asyncio.current_task()
    task = getattr(game_state, "_claim_timeout_task", None)
    if task is not None and task is not current and not task.done():
        task.cancel()
    game_state._claim_timeout_task = asyncio.create_task(
        claim_timeout(game_state, game_state.action_tick)
    )


async def claim_timeout(game_state, tick: int) -> None:
    try:
        while True:
            async with game_state._lock:
                window: Optional[ClaimWindow] = getattr(game_state, "claim_window", None)
                if game_state.phase != "claim" or game_state.action_tick != tick or window is None:
                    return
                now = time.monotonic()
                if window.stage == "tactical":
                    deadline = window.deadline or now
                    if now >= deadline:
                        await resolve_claims(game_state)
                        return
                    sleep_seconds = deadline - now
                else:
                    expired = {
                        player_index for player_index in window.pending
                        if now >= game_state.claim_deadlines.get(player_index, now)
                    }
                    for player_index in expired:
                        game_state.players[player_index].remaining_time = 0
                        game_state.claim_responses[player_index] = {"action": "pass"}
                    window.pending.difference_update(expired)
                    if not window.pending:
                        await resolve_claims(game_state)
                        return
                    sleep_seconds = min(
                        game_state.claim_deadlines[player_index]
                        for player_index in window.pending
                    ) - now
            await asyncio.sleep(max(0.0, sleep_seconds))
    except asyncio.CancelledError:
        return


async def resolve_claims(game_state) -> None:
    window: Optional[ClaimWindow] = getattr(game_state, "claim_window", None)
    game_state._cancel_bot_claim_tasks()
    task = getattr(game_state, "_claim_timeout_task", None)
    if task is not None and task is not asyncio.current_task():
        task.cancel()

    if window is None or window.active is None:
        await advance_after_unclaimed_discard(game_state)
        return
    winner_claim = window.active
    ron_claims = list(window.accepted_rons.values())
    discarder = game_state.last_discard["player"]
    if await resolve_collected_rons(game_state, ron_claims):
        return

    candidate = winner_claim.candidate
    winner = game_state.players[winner_claim.player_index]
    for code in candidate["hand_tiles"]:
        winner.hand.remove(code)
    winner.melds.append({
        "kind": candidate["kind"],
        "tiles": list(candidate["tiles"]),
        "from_player": discarder,
        "claimed_tile": game_state.last_discard["tile"],
    })
    discarder_player = game_state.players[discarder]
    if discarder_player.discards and discarder_player.discards[-1] == game_state.last_discard["tile"]:
        discarder_player.discards.pop()
    game_state.current_player_index = winner.index
    winner.drawn_tile = None
    game_state._transition(HongqueStatus.WAITING_HAND_ACTION)
    game_state._start_turn_clock()
    game_state._record_event(
        candidate["kind"],
        player=winner.index,
        from_player=discarder,
        tile=game_state.last_discard["tile"],
        tiles=list(candidate["tiles"]),
        hand_tiles=list(candidate["hand_tiles"]),
        base_kind=candidate.get("base_kind", candidate["kind"]),
        silent=True,
    )
    game_state.message = f"{winner.username} 亮牌（{candidate['kind']}）"
    clear_claim_window(game_state)
    game_state._advance_tick()
    await game_state.broadcast_state()
    game_state._schedule_turn_timeout()
    game_state._schedule_bot_if_needed()


async def advance_after_unclaimed_discard(game_state) -> None:
    clear_claim_window(game_state)
    if not game_state.wall:
        await game_state._finish_round([], "draw")
        return
    game_state.current_player_index = (game_state.last_discard["player"] + 1) % 4
    game_state._start_turn_clock()
    game_state._draw_for_current_player()
    game_state._transition(HongqueStatus.WAITING_HAND_ACTION)
    game_state.message = f"轮到 {game_state.players[game_state.current_player_index].username}"
    game_state._advance_tick()
    await game_state.broadcast_state()
    game_state._schedule_turn_timeout()
    game_state._schedule_bot_if_needed()


def clear_claim_window(game_state) -> None:
    game_state.claim_window = None
    game_state.claim_options.clear()
    game_state.claim_responses.clear()
    game_state.claim_started_at = None
    game_state.claim_deadlines.clear()
