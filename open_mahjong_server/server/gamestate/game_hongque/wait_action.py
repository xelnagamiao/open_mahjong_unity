"""虹雀等待动作：手牌、亮牌后 onlycut、弃牌响应与历时摸牌。

与国标相同：``wait_action`` 阻塞等待玩家入队或超时，循环结束后再处理
操作 / pass / 超时默认动作。``submit_action`` 只负责入队。

战术窗口仅保存响应元数据；手牌、河牌和副露只在最终仲裁完成后修改。

虹雀优先级：和 > 虹 > 碰 > 下家吃 > 对家吃 > 上家吃。
同一张弃牌允许日麻式多家荣和；其他同级动作先申请者胜。
"""
from __future__ import annotations

import asyncio
import time
from dataclasses import dataclass, field
from typing import Optional

from .action_check import check_action_after_cut, check_action_hand_action
from .hongque_debug import get_debug_forced_discard, resolve_debug_scenario
from .init_tiles import pop_supplement_tile
from .ron_resolution import resolve_collected_rons
from .rules import kong_candidates
from .scoring import best_win_result
from .state_machine import HongqueStatus
from .tile import HongqueTile
from .record import (
    record_claim_apply,
    record_claim_execute,
    record_cut,
    record_deal,
    record_kong,
    record_unclaimed_discard,
)


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
    if player_index not in game_state.claim_responses:
        return ["pass", "claim"], list(game_state.claim_options[player_index])
    return [], []


def refresh_hand_action_dict(game_state) -> None:
    game_state.action_dict = {index: [] for index in range(4)}
    player = game_state.players[game_state.current_player_index]
    actions, _ = check_action_hand_action(game_state, player)
    game_state.action_dict[player.index] = actions


def refresh_claim_action_dict(game_state) -> None:
    game_state.action_dict = {index: [] for index in range(4)}
    window: Optional[ClaimWindow] = getattr(game_state, "claim_window", None)
    if window is None:
        return
    for player_index in window.pending:
        actions, _ = actions_for_viewer(game_state, player_index)
        game_state.action_dict[player_index] = actions


def validate_submitted_action(game_state, player, action: str,
                              tile: Optional[str], candidate_id: Optional[str]) -> None:
    """入队前校验，避免非法动作进入 wait_action 后打断主循环。"""
    status = game_state.state_machine.status
    if action == "ready":
        if game_state.phase != "round_end":
            raise ValueError("当前没有需要确认的和牌结算")
        return
    if status in (
        HongqueStatus.WAITING_HAND_ACTION,
        HongqueStatus.ONLYCUT_AFTER_ACTION,
    ):
        if player.index != game_state.current_player_index:
            raise ValueError("还没有轮到你")
        after_claim = status == HongqueStatus.ONLYCUT_AFTER_ACTION
        if action == "discard":
            code = HongqueTile.parse(tile or "").code
            if code not in player.hand:
                raise ValueError("手牌中没有这张牌")
            return
        if action == "supplement":
            if player.supplements >= 2 or not game_state.wall:
                raise ValueError("本局补牌次数已用尽或牌库已空")
            return
        if action == "kong":
            if not after_claim and len(player.hand) == 1:
                raise ValueError("手牌只剩一张时请使用和")
            if not any(item["id"] == candidate_id for item in kong_candidates(player.hand, player.melds)):
                raise ValueError("杠牌候选无效")
            return
        if action == "win":
            if after_claim:
                raise ValueError("亮牌后须先补牌才能和牌")
            result = best_win_result(
                player.hand,
                player.melds,
                self_draw=True,
                before_first_discard=not any(item.discards for item in game_state.players),
                wall_empty=not game_state.wall,
                allow_kong_win=True,
            )
            if result is None:
                raise ValueError("当前手牌不能和牌")
            return
        raise ValueError("未知的回合操作")
    if status == HongqueStatus.WAITING_ACTION_AFTER_CUT:
        window: Optional[ClaimWindow] = getattr(game_state, "claim_window", None)
        if window is None or player.index not in window.pending:
            raise ValueError("你没有待回应的亮牌操作")
        if action == "pass":
            return
        if action != "claim":
            raise ValueError("未知的亮牌操作")
        _, candidates = actions_for_viewer(game_state, player.index)
        if not any(item.get("id") == candidate_id for item in candidates):
            raise ValueError("亮牌候选无效或优先级不足")
        return
    raise ValueError("当前阶段不能操作")


async def wait_action(game_state) -> None:
    """阻塞等待当前询问结束：有操作则执行，否则走超时默认动作。"""
    game_state._in_wait_action = True
    try:
        status = game_state.state_machine.status
        if status in (
            HongqueStatus.WAITING_HAND_ACTION,
            HongqueStatus.ONLYCUT_AFTER_ACTION,
        ):
            await _wait_hand_action(game_state)
        elif status == HongqueStatus.WAITING_ACTION_AFTER_CUT:
            await _wait_claim_action(game_state)
    finally:
        game_state._in_wait_action = False


def _retain_current_tick_actions(game_state, player_indexes) -> None:
    expected_tick = game_state.action_tick
    for player_index in player_indexes:
        retained = []
        while not game_state.action_queues[player_index].empty():
            queued = game_state.action_queues[player_index].get_nowait()
            queued_tick = queued.get("_action_tick")
            if queued_tick is None or queued_tick == expected_tick:
                retained.append(queued)
        for queued in retained:
            game_state.action_queues[player_index].put_nowait(queued)
        game_state.action_events[player_index].clear()
        if retained:
            game_state.action_events[player_index].set()


async def _wait_hand_action(game_state) -> None:
    refresh_hand_action_dict(game_state)
    player = game_state.players[game_state.current_player_index]
    if not game_state.action_dict.get(player.index):
        return
    if game_state.turn_started_at is None:
        game_state._start_turn_clock()
    _retain_current_tick_actions(game_state, [player.index])
    game_state.waiting_players_list = [player.index]
    game_state._schedule_bot_if_needed()

    action_data = None
    deadline = game_state.turn_deadline
    if deadline is None:
        deadline = game_state.turn_started_at + game_state.step_time + player.remaining_time
    while game_state.waiting_players_list:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            break
        action_task = asyncio.create_task(game_state.action_events[player.index].wait())
        timer_task = asyncio.create_task(asyncio.sleep(remaining))
        done, pending = await asyncio.wait(
            {action_task, timer_task},
            return_when=asyncio.FIRST_COMPLETED,
        )
        for task in pending:
            task.cancel()
        if action_task in done:
            try:
                action_data = dict(game_state.action_queues[player.index].get_nowait())
            except asyncio.QueueEmpty:
                game_state.action_events[player.index].clear()
                continue
            game_state.action_events[player.index].clear()
            if not game_state.action_queues[player.index].empty():
                game_state.action_events[player.index].set()
            game_state.action_dict[player.index] = []
            game_state.waiting_players_list = []
            break

    if game_state.waiting_players_list:
        player.remaining_time = 0
        game_state.waiting_players_list = []

    if action_data:
        await handle_hand_action(
            game_state,
            player,
            action_data.get("action_type"),
            action_data.get("tile"),
            action_data.get("candidate_id"),
        )
        return
    await _timeout_hand_action(game_state, player)


async def _timeout_hand_action(game_state, player) -> None:
    """手牌询问超时：与国标一样在 wait_action 尾部走默认切 / 补 / 和。"""
    game_state.events = []
    if player.hand:
        if game_state.Debug:
            forced = get_debug_forced_discard(game_state, player.index)
            if forced and forced in player.hand:
                code = forced
            else:
                code = (
                    player.drawn_tile
                    if player.drawn_tile in player.hand
                    else player.hand[-1]
                )
        else:
            code = (
                player.drawn_tile
                if player.drawn_tile in player.hand
                else player.hand[-1]
            )
        await apply_discard(game_state, player, code)
        return
    if (game_state.game_status == "onlycut_after_action"
            and player.supplements < 2 and game_state.wall):
        await handle_hand_action(game_state, player, "supplement", None, None)
        return
    if game_state.game_status == "onlycut_after_action":
        return
    result = best_win_result(
        player.hand,
        player.melds,
        self_draw=True,
        before_first_discard=not any(item.discards for item in game_state.players),
        wall_empty=not game_state.wall,
        allow_kong_win=True,
    )
    if result is not None:
        result["winning_hand"] = list(player.hand)
        await game_state._finish_round([(player, result)], "self_draw")


async def _wait_claim_action(game_state) -> None:
    while (
        game_state.phase == "claim"
        and getattr(game_state, "claim_window", None) is not None
    ):
        window: ClaimWindow = game_state.claim_window
        refresh_claim_action_dict(game_state)
        waiting = [
            player_index for player_index in window.pending
            if game_state.action_dict.get(player_index)
        ]
        if not waiting:
            await resolve_claims(game_state)
            return
        _retain_current_tick_actions(game_state, waiting)
        game_state.waiting_players_list = list(waiting)
        now = time.monotonic()
        if window.stage == "tactical":
            deadline = window.deadline or now
        else:
            deadline = min(
                game_state.claim_deadlines.get(player_index, now)
                for player_index in waiting
            )
        remaining = max(0.0, deadline - now)
        task_to_player = {}
        task_list = []
        for player_index in waiting:
            action_task = asyncio.create_task(
                game_state.action_events[player_index].wait()
            )
            task_list.append(action_task)
            task_to_player[action_task] = player_index
        timer_task = asyncio.create_task(asyncio.sleep(remaining))
        task_list.append(timer_task)
        done, pending = await asyncio.wait(
            task_list, return_when=asyncio.FIRST_COMPLETED
        )
        for task in pending:
            task.cancel()

        submissions = []
        for task in done:
            if task is timer_task:
                continue
            player_index = task_to_player[task]
            try:
                action_data = dict(
                    game_state.action_queues[player_index].get_nowait()
                )
            except asyncio.QueueEmpty:
                game_state.action_events[player_index].clear()
                continue
            submissions.append((player_index, action_data))
            game_state.action_events[player_index].clear()
            if not game_state.action_queues[player_index].empty():
                game_state.action_events[player_index].set()

        if submissions:
            for player_index, action_data in submissions:
                if game_state.claim_window is None or game_state.phase != "claim":
                    return
                if player_index not in game_state.claim_window.pending:
                    continue
                await handle_claim_action(
                    game_state,
                    game_state.players[player_index],
                    action_data.get("action_type"),
                    action_data.get("candidate_id"),
                )
            continue

        # 等待结束且无人提交：超时。战术窗口执行当前申请；初始询问把未回应视为 pass。
        window = game_state.claim_window
        if window is None or game_state.phase != "claim":
            return
        if window.stage == "tactical":
            await resolve_claims(game_state)
            return
        expired = [
            player_index for player_index in list(window.pending)
            if time.monotonic() >= game_state.claim_deadlines.get(player_index, 0)
        ]
        for player_index in expired:
            game_state.players[player_index].remaining_time = 0
            await handle_claim_action(
                game_state, game_state.players[player_index], "pass", None
            )
            if game_state.claim_window is None:
                return


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
    if game_state.state_machine.status in (
        HongqueStatus.WAITING_HAND_ACTION,
        HongqueStatus.ONLYCUT_AFTER_ACTION,
    ):
        game_state._transition(HongqueStatus.RESOLVING_DISCARD)
    game_state._transition(HongqueStatus.WAITING_ACTION_AFTER_CUT)
    game_state._start_claim_clock()
    game_state.message = "等待亮牌或捉和"
    game_state._advance_tick()
    game_state.claim_window.note_asked(game_state.claim_window.pending, game_state.action_tick)
    refresh_claim_action_dict(game_state)
    await game_state.broadcast_state()
    game_state.events = []

    for player_index in tuple(options):
        player = game_state.players[player_index]
        debug_force_ron = (
            game_state.Debug
            and resolve_debug_scenario(game_state) == "double_ron"
            and player_index == 1
            and any(option.get("kind") == "win" for option in options[player_index])
        )
        if player.user_id in (2, 3) or debug_force_ron:
            game_state._schedule_bot_claim(player_index, game_state.action_tick)
        elif player.is_bot:
            await handle_claim_action(game_state, player, "pass", None)


async def apply_discard(game_state, player, code: str) -> None:
    """权威出牌：广播切牌后进入 resolving_discard，由主循环开鸣牌窗。"""
    game_state.events = []
    player.hand.remove(code)
    player.discards.append(code)
    cut_class = code == player.drawn_tile
    player.drawn_tile = None
    player.last_draw_was_supplement = False
    game_state.last_discard = {"player": player.index, "tile": code}
    game_state._record_event(
        "discard",
        player=player.index,
        tile=code,
        cut_class=cut_class,
    )
    record_cut(game_state, player, code, is_moqie=cut_class)
    game_state._transition(HongqueStatus.RESOLVING_DISCARD)
    game_state.turn_deadline = None
    game_state.turn_started_at = None
    game_state.message = f"{player.username} 出牌"
    game_state._advance_tick()
    await game_state.broadcast_state()


async def handle_hand_action(game_state, player, action: str,
                             tile: Optional[str], candidate_id: Optional[str]) -> None:
    """摸牌后 / 亮牌后的手牌操作，对应其他规则 wait_action 的 hand / onlycut case。"""
    status = game_state.state_machine.status
    if status not in (
        HongqueStatus.WAITING_HAND_ACTION,
        HongqueStatus.ONLYCUT_AFTER_ACTION,
    ):
        raise ValueError("当前不在手牌操作阶段")
    if player.index != game_state.current_player_index:
        raise ValueError("还没有轮到你")

    after_claim = status == HongqueStatus.ONLYCUT_AFTER_ACTION

    if action == "discard":
        code = HongqueTile.parse(tile or "").code
        if code not in player.hand:
            raise ValueError("手牌中没有这张牌")
        game_state._consume_time_bank(player, game_state.turn_started_at)
        await apply_discard(game_state, player, code)
        return
    if action == "supplement":
        if player.supplements >= 2 or not game_state.wall:
            raise ValueError("本局补牌次数已用尽或牌库已空")
        game_state._consume_time_bank(player, game_state.turn_started_at)
        game_state.events = []
        player.supplements += 1
        drawn = pop_supplement_tile(game_state)
        player.hand.append(drawn)
        player.drawn_tile = drawn
        player.last_draw_was_supplement = True
        game_state._record_event("supplement", player=player.index, tile=drawn)
        record_deal(game_state, drawn, "bd", player.index)
        game_state.message = f"{player.username} 补牌"
        if after_claim:
            game_state._transition(HongqueStatus.WAITING_HAND_ACTION)
    elif action == "kong":
        if not after_claim and len(player.hand) == 1:
            raise ValueError("手牌只剩一张时请使用和")
        candidates = kong_candidates(player.hand, player.melds)
        candidate = next((item for item in candidates if item["id"] == candidate_id), None)
        if candidate is None:
            raise ValueError("杠牌候选无效")
        game_state._consume_time_bank(player, game_state.turn_started_at)
        game_state.events = []
        game_state._apply_kong(player, candidate)
        record_kong(game_state, player, candidate)
        game_state.message = f"{player.username} 杠牌"
    elif action == "win":
        if after_claim:
            raise ValueError("亮牌后须先补牌才能和牌")
        result = best_win_result(
            player.hand,
            player.melds,
            self_draw=True,
            before_first_discard=not any(item.discards for item in game_state.players),
            wall_empty=not game_state.wall,
            allow_kong_win=True,
        )
        if result is None:
            raise ValueError("当前手牌不能和牌")
        game_state._consume_time_bank(player, game_state.turn_started_at)
        result["winning_hand"] = list(player.hand)
        await game_state._finish_round([(player, result)], "self_draw")
        return
    else:
        raise ValueError("未知的回合操作")
    game_state._start_turn_clock()
    game_state._advance_tick()
    await game_state.broadcast_state()
    game_state._schedule_bot_if_needed()


async def deal_card(game_state) -> None:
    """无人鸣牌后的历时摸牌，对齐其他规则的 ``deal_card``。"""
    if game_state.state_machine.status != HongqueStatus.DEAL_CARD:
        game_state._transition(HongqueStatus.DEAL_CARD)
    if not game_state.wall:
        await game_state._finish_round([], "draw")
        return
    game_state._start_turn_clock()
    game_state.events = []
    game_state._draw_for_current_player()
    drawn = game_state.players[game_state.current_player_index].drawn_tile
    if drawn:
        record_deal(game_state, drawn, "d")
    game_state._transition(HongqueStatus.WAITING_HAND_ACTION)
    game_state.message = f"轮到 {game_state.players[game_state.current_player_index].username}"
    game_state._advance_tick()
    await game_state.broadcast_state()
    game_state._schedule_bot_if_needed()


async def enter_onlycut_after_action(game_state) -> None:
    """亮牌落地后进入 onlycut，由 check_only_cut 决定切 / 加杠 / 补。"""
    game_state.players[game_state.current_player_index].drawn_tile = None
    game_state._transition(HongqueStatus.ONLYCUT_AFTER_ACTION)
    game_state._start_turn_clock()
    game_state._advance_tick()
    await game_state.broadcast_state()
    game_state._schedule_bot_if_needed()


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
            refresh_claim_action_dict(game_state)
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
    record_claim_apply(game_state, player_index, candidate)
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
    for player_index in window.pending:
        game_state.claim_responses.pop(player_index, None)
    game_state.claim_started_at = time.monotonic()
    window.deadline = game_state.claim_started_at + game_state.tactical_grace_seconds
    game_state.claim_deadlines.clear()
    game_state.message = "战术鸣牌：等待更高优先级操作"
    game_state._advance_tick()
    window.note_asked(window.pending, game_state.action_tick)
    refresh_claim_action_dict(game_state)
    await game_state.broadcast_state()

    if not window.pending:
        await resolve_claims(game_state)
        return
    for player_index in tuple(window.pending):
        if game_state.players[player_index].user_id in (2, 3):
            game_state._schedule_bot_claim(player_index, game_state.action_tick)


async def resolve_claims(game_state) -> None:
    window: Optional[ClaimWindow] = getattr(game_state, "claim_window", None)
    game_state._cancel_bot_claim_tasks()

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
    record_claim_execute(game_state, winner, candidate)
    game_state.message = f"{winner.username} 亮牌（{candidate['kind']}）"
    clear_claim_window(game_state)
    await enter_onlycut_after_action(game_state)


async def advance_after_unclaimed_discard(game_state) -> None:
    record_unclaimed_discard(game_state)
    clear_claim_window(game_state)
    if not game_state.wall:
        await game_state._finish_round([], "draw")
        return
    game_state.current_player_index = (game_state.last_discard["player"] + 1) % 4
    game_state._transition(HongqueStatus.DEAL_CARD)


def clear_claim_window(game_state) -> None:
    game_state.claim_window = None
    game_state.claim_options.clear()
    game_state.claim_responses.clear()
    game_state.claim_started_at = None
    game_state.claim_deadlines.clear()
