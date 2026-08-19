"""虹雀机器人行动适配层。

与其他麻将规则的 get_action 模块一致：快照在房间锁内取得，CPU 决策在锁外执行，
最终动作回到锁内并再次校验 action_tick。真人永远不会进入本模块。
"""
from __future__ import annotations

import asyncio
import logging
import time

from .efficiency_bot import choose_claim_plan, choose_turn_plan
from .heuristic_bot import (
    OpponentView,
    choose_claim_plan as choose_claim_plan_v3,
    choose_turn_plan as choose_turn_plan_v3,
)
from .rules import kong_candidates, kong_win_candidates
from .scoring import best_win_result
from .hongque_debug import get_debug_forced_discard
from .wait_action import actions_for_viewer
from ..public.ai.bot_executor import run_room_bot_cpu

logger = logging.getLogger(__name__)
BOT_ACTION_DELAY = 0.5


def schedule_bot_if_needed(game_state) -> None:
    if game_state.phase != "turn" or not game_state.players[game_state.current_player_index].is_bot:
        return
    current = asyncio.current_task()
    if game_state._bot_task and game_state._bot_task is not current \
            and not game_state._bot_task.done():
        game_state._bot_task.cancel()
    game_state._bot_task = asyncio.create_task(
        bot_turn(game_state, game_state.action_tick)
    )


async def bot_turn(game_state, tick: int) -> None:
    started_at = time.perf_counter()
    async with game_state._lock:
        if game_state.phase != "turn" or game_state.action_tick != tick:
            return
        player = game_state.players[game_state.current_player_index]
        hand = tuple(player.hand)
        melds = tuple(dict(meld) for meld in player.melds)
        player_index = player.index
        smart = player.user_id == 2
        heuristic = player.user_id == 3
        kong = tuple(dict(item) for item in (
            kong_candidates(player.hand, player.melds)
            + kong_win_candidates(player.hand, player.melds)
        ))
        before_first_discard = not any(item.discards for item in game_state.players)
        wall_empty = not game_state.wall
        if smart or heuristic:
            visible = visible_codes_for(game_state, player.index)
            supplements = player.supplements
            wall_count = len(game_state.wall)
            drawn_tile = player.drawn_tile
            last_draw_was_supplement = player.last_draw_was_supplement
        if heuristic:
            opponents = tuple(
                OpponentView.from_player(item) for item in game_state.players
                if item.index != player_index
            )

    if smart:
        plan = await run_room_bot_cpu(
            game_state, choose_turn_plan, hand, melds, visible, kong,
            supplements=supplements, wall_count=wall_count,
            drawn_tile=drawn_tile,
            last_draw_was_supplement=last_draw_was_supplement,
        )
    elif heuristic:
        plan = await run_room_bot_cpu(
            game_state, choose_turn_plan_v3, hand, melds, visible, kong,
            supplements=supplements, wall_count=wall_count,
            drawn_tile=drawn_tile,
            last_draw_was_supplement=last_draw_was_supplement,
            opponents=opponents,
        )
    else:
        result = await run_room_bot_cpu(
            game_state, best_win_result, hand, melds,
            self_draw=True, before_first_discard=before_first_discard,
            wall_empty=wall_empty,
        )
        if result is not None or any(item.get("kind") == "kong_win" for item in kong):
            plan = {"action": "win"}
        else:
            plan = {
                "action": "discard",
                "tile": game_state._rng.choice(hand) if hand else None,
            }

    delay = BOT_ACTION_DELAY - (time.perf_counter() - started_at)
    if delay > 0:
        await asyncio.sleep(delay)
    async with game_state._lock:
        if game_state.phase != "turn" or game_state.action_tick != tick \
                or game_state.current_player_index != player_index:
            return
        game_state.events = []
        player = game_state.players[player_index]
        action = plan.get("action")
        if game_state.game_status == "onlycut_after_action" and action == "win":
            if player.supplements < 2 and game_state.wall:
                action = "supplement"
            else:
                action = "discard"
        if action in {"win", "supplement"}:
            await game_state._handle_turn_action(player, action, None, None)
            return
        if action == "kong":
            await game_state._handle_turn_action(
                player, "kong", None, plan.get("candidate_id")
            )
            return
        code = plan.get("tile")
        if game_state.Debug:
            forced = get_debug_forced_discard(game_state, player.index)
            if forced in player.hand:
                code = forced
        if code not in player.hand:
            code = player.drawn_tile if player.drawn_tile in player.hand else (
                game_state._rng.choice(player.hand) if player.hand else None
            )
        if code is not None:
            await game_state._discard_and_open_claim(player, code)


def visible_codes_for(game_state, player_index: int) -> tuple[str, ...]:
    visible = set(game_state.players[player_index].hand)
    for player in game_state.players:
        visible.update(player.discards)
        for meld in player.melds:
            visible.update(meld.get("tiles", ()))
    if game_state.last_discard is not None:
        visible.add(game_state.last_discard["tile"])
    return tuple(sorted(visible))


def schedule_bot_claim(game_state, player_index: int, tick: int) -> None:
    previous = game_state._bot_claim_tasks.get(player_index)
    current = asyncio.current_task()
    if previous is not None and previous is not current and not previous.done():
        previous.cancel()
    game_state._bot_claim_tasks[player_index] = asyncio.create_task(
        bot_claim(game_state, player_index, tick)
    )


def cancel_bot_claim_tasks(game_state) -> None:
    current = asyncio.current_task()
    for task in game_state._bot_claim_tasks.values():
        if task is not current and not task.done():
            task.cancel()
    game_state._bot_claim_tasks.clear()


async def bot_claim(game_state, player_index: int, tick: int) -> None:
    started_at = time.perf_counter()
    try:
        async with game_state._lock:
            if game_state.phase != "claim" or game_state.action_tick != tick:
                return
            actions, candidates = actions_for_viewer(game_state, player_index)
            if "claim" not in actions:
                return
            player = game_state.players[player_index]
            hand = tuple(player.hand)
            melds = tuple(dict(meld) for meld in player.melds)
            candidates = tuple(dict(item) for item in candidates)
            visible = visible_codes_for(game_state, player_index)
        claim_fn = choose_claim_plan_v3 if player.user_id == 3 else choose_claim_plan
        plan = await run_room_bot_cpu(
            game_state, claim_fn, hand, melds, candidates, visible
        )
        delay = BOT_ACTION_DELAY - (time.perf_counter() - started_at)
        if delay > 0:
            await asyncio.sleep(delay)
        async with game_state._lock:
            if game_state.phase != "claim" or game_state.action_tick != tick:
                return
            actions, _ = actions_for_viewer(game_state, player_index)
            if "pass" not in actions:
                return
            action = "claim" if plan.get("action") == "claim" else "pass"
            await game_state._handle_claim_action(
                player, action, plan.get("candidate_id")
            )
    except asyncio.CancelledError:
        return
    except Exception:
        logger.exception("虹雀机器人亮牌决策失败: player=%s", player_index)
        async with game_state._lock:
            if game_state.phase == "claim" and game_state.action_tick == tick:
                actions, _ = actions_for_viewer(game_state, player_index)
                if "pass" in actions:
                    await game_state._handle_claim_action(player, "pass", None)
    finally:
        task = asyncio.current_task()
        if game_state._bot_claim_tasks.get(player_index) is task:
            game_state._bot_claim_tasks.pop(player_index, None)

