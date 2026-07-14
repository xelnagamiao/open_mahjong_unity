from __future__ import annotations

import asyncio
import logging
from typing import Any, Optional

from ..public.ai.smart_bot_logic import (
    count_melds,
    count_visible_tiles,
    cut_candidate_hand,
    evaluate_hand,
    find_best_cut,
    find_best_cut_score,
    should_accept_hu,
)
from ..public.hand_slot_utils import has_draw_slot, infer_bot_cut_class
from .get_action import get_ai_action

logger = logging.getLogger(__name__)

BOT_DELAY = 0.5
PASS_WAIT_STATUSES = {"waiting_action_after_cut", "waiting_action_qianggang"}
RON_HU_ACTIONS = ("hu",)


async def jiandan_bot_action(
    game_state: Any,
    player_index: int,
    action_list: list[str],
    game_status: str,
    action_tick: int,
) -> None:
    try:
        if getattr(game_state, "game_status", None) == "END":
            return
        player = game_state.player_list[player_index]
        if getattr(player, "user_id", None) == 2:
            await smart_jiandan_bot_action(game_state, player_index, action_list, game_status, action_tick)
        else:
            await auto_jiandan_bot_action(game_state, player_index, action_list, game_status, action_tick)
    except asyncio.CancelledError:
        raise
    except Exception as exc:
        logger.error("Jiandan bot action failed: player_index=%s error=%s", player_index, exc, exc_info=True)


async def auto_jiandan_bot_action(
    game_state: Any,
    player_index: int,
    action_list: list[str],
    game_status: str,
    action_tick: int,
) -> None:
    player = game_state.player_list[player_index]

    if game_status in PASS_WAIT_STATUSES:
        await _wait_until_actionable(game_state, player_index, action_tick)
        await _submit_if_current(game_state, player_index, action_tick, action_list, "pass")
        return

    if game_status == "waiting_hand_action":
        await asyncio.sleep(BOT_DELAY)
        if "cut" in action_list and player.hand_tiles:
            tile, cut_position, cut_class = _pick_auto_cut_tile(player)
            await _submit_if_current(
                game_state,
                player_index,
                action_tick,
                action_list,
                "cut",
                cutClass=cut_class,
                TileId=tile,
                cutIndex=cut_position,
            )
            return
        await _submit_if_current(game_state, player_index, action_tick, action_list, "pass")
        return

    if game_status == "onlycut_after_action":
        claim_protection = bool(getattr(game_state, "claim_protection", False))
        await asyncio.sleep(BOT_DELAY * (2 if claim_protection else 1))
        if "cut" in action_list and player.hand_tiles:
            tile, cut_position, cut_class = _pick_auto_cut_tile(player)
            await _submit_if_current(
                game_state,
                player_index,
                action_tick,
                action_list,
                "cut",
                cutClass=cut_class,
                TileId=tile,
                cutIndex=cut_position,
            )


async def smart_jiandan_bot_action(
    game_state: Any,
    player_index: int,
    action_list: list[str],
    game_status: str,
    action_tick: int,
) -> None:
    player = game_state.player_list[player_index]

    if game_status in {"waiting_hand_action", "onlycut_after_action"}:
        claim_protection = bool(getattr(game_state, "claim_protection", False))
        delay = BOT_DELAY * (2 if game_status == "onlycut_after_action" and claim_protection else 1)
        await asyncio.sleep(delay)
        await _handle_smart_hand_action(game_state, player_index, action_list, player, action_tick)
        return

    if game_status == "waiting_action_after_cut":
        await _handle_smart_after_cut(game_state, player_index, action_list, player, action_tick)
        return

    if game_status == "waiting_action_qianggang":
        await _handle_smart_qianggang(game_state, player_index, action_list, player, action_tick)


async def _handle_smart_hand_action(
    game_state: Any,
    player_index: int,
    action_list: list[str],
    player: Any,
    action_tick: int,
) -> None:
    if "hu_self" in action_list and should_accept_hu(game_state, player_index, "hu_self"):
        await _submit_if_current(game_state, player_index, action_tick, action_list, "hu_self")
        return

    hand = player.hand_tiles[:]
    meld_count = count_melds(getattr(player, "combination_tiles", []))
    visible = count_visible_tiles(game_state)

    if "angang" in action_list:
        for tile in sorted(set(hand)):
            if hand.count(tile) < 4:
                continue
            test_hand = [item for item in hand if item != tile]
            if evaluate_hand(test_hand, meld_count + 1, visible) >= evaluate_hand(hand, meld_count, visible):
                await _submit_if_current(
                    game_state,
                    player_index,
                    action_tick,
                    action_list,
                    "angang",
                    target_tile=tile,
                )
                return

    if "jiagang" in action_list:
        for meld in getattr(player, "combination_tiles", []):
            if not meld.startswith("k"):
                continue
            try:
                tile = int(meld[1:])
            except ValueError:
                continue
            if tile not in hand:
                continue
            test_hand = hand[:]
            test_hand.remove(tile)
            if evaluate_hand(test_hand, meld_count, visible) >= evaluate_hand(hand, meld_count, visible):
                await _submit_if_current(
                    game_state,
                    player_index,
                    action_tick,
                    action_list,
                    "jiagang",
                    target_tile=tile,
                )
                return

    if "cut" in action_list and hand:
        cut_hand = cut_candidate_hand(hand, getattr(player, "dingque_suit", 0))
        tile, _ = find_best_cut(cut_hand, meld_count, visible)
        cut_position = hand.index(tile)
        cut_class = infer_bot_cut_class(hand, tile, cut_position, draw_slot=has_draw_slot(player))
        await _submit_if_current(
            game_state,
            player_index,
            action_tick,
            action_list,
            "cut",
            cutClass=cut_class,
            TileId=tile,
            cutIndex=cut_position,
        )
        return

    await _submit_if_current(game_state, player_index, action_tick, action_list, "pass")


async def _handle_smart_after_cut(
    game_state: Any,
    player_index: int,
    action_list: list[str],
    player: Any,
    action_tick: int,
) -> None:
    if not await _wait_until_actionable(game_state, player_index, action_tick):
        return

    for hu_action in RON_HU_ACTIONS:
        if hu_action in action_list and should_accept_hu(game_state, player_index, hu_action):
            await asyncio.sleep(BOT_DELAY)
            await _submit_if_current(game_state, player_index, action_tick, action_list, hu_action)
            return

    cut_tile = _current_cut_tile(game_state)
    if cut_tile is None:
        await _submit_if_current(game_state, player_index, action_tick, action_list, "pass")
        return

    hand = player.hand_tiles[:]
    meld_count = count_melds(getattr(player, "combination_tiles", []))
    visible = count_visible_tiles(game_state)
    base_score = evaluate_hand(hand, meld_count, visible)
    best_action = "pass"
    best_action_score = base_score

    if "peng" in action_list and hand.count(cut_tile) >= 2:
        test_hand = hand[:]
        test_hand.remove(cut_tile)
        test_hand.remove(cut_tile)
        if test_hand:
            score = find_best_cut_score(test_hand, meld_count + 1, visible)
            if score > best_action_score:
                best_action = "peng"
                best_action_score = score

    for chi_action, need in _chi_needs(cut_tile).items():
        if chi_action not in action_list:
            continue
        test_hand = hand[:]
        if not _remove_all(test_hand, need) or not test_hand:
            continue
        score = find_best_cut_score(test_hand, meld_count + 1, visible)
        if score > best_action_score:
            best_action = chi_action
            best_action_score = score

    if "gang" in action_list and hand.count(cut_tile) >= 3:
        test_hand = hand[:]
        if _remove_all(test_hand, [cut_tile, cut_tile, cut_tile]):
            score = evaluate_hand(test_hand, meld_count + 1, visible)
            if score > best_action_score:
                best_action = "gang"

    if best_action != "pass":
        await asyncio.sleep(BOT_DELAY)
    await _submit_if_current(game_state, player_index, action_tick, action_list, best_action)


async def _handle_smart_qianggang(
    game_state: Any,
    player_index: int,
    action_list: list[str],
    player: Any,
    action_tick: int,
) -> None:
    del player
    if not await _wait_until_actionable(game_state, player_index, action_tick):
        return
    for hu_action in RON_HU_ACTIONS:
        if hu_action in action_list and should_accept_hu(game_state, player_index, hu_action):
            await asyncio.sleep(BOT_DELAY)
            await _submit_if_current(game_state, player_index, action_tick, action_list, hu_action)
            return
    await _submit_if_current(game_state, player_index, action_tick, action_list, "pass")


async def _submit_if_current(
    game_state: Any,
    player_index: int,
    action_tick: int,
    action_list: list[str],
    action_type: str,
    *,
    cutClass: bool = False,
    TileId: Optional[int] = None,
    cutIndex: int = -1,
    target_tile: Optional[int] = None,
) -> bool:
    if action_type not in action_list:
        return False
    if getattr(game_state, "server_action_tick", None) != action_tick:
        return False
    if player_index not in getattr(game_state, "waiting_players_list", []):
        return False
    if action_type not in getattr(game_state, "action_dict", {}).get(player_index, []):
        return False
    try:
        await get_ai_action(
            game_state,
            player_index,
            action_type,
            cutClass=cutClass,
            TileId=TileId,
            cutIndex=cutIndex,
            target_tile=target_tile,
        )
    except ValueError:
        logger.info("Jiandan bot submitted a stale action: player_index=%s action=%s", player_index, action_type)
        return False
    return True


async def _wait_until_actionable(
    game_state: Any,
    player_index: int,
    action_tick: int,
    attempts: int = 200,
    interval: float = 0.01,
) -> bool:
    for _ in range(attempts):
        if getattr(game_state, "server_action_tick", None) != action_tick:
            return False
        if player_index in getattr(game_state, "waiting_players_list", []):
            return True
        await asyncio.sleep(interval)
    return False


def _pick_auto_cut_tile(player: Any) -> tuple[int, int, bool]:
    hand = player.hand_tiles
    tile = hand[-1]
    cut_position = len(hand) - 1
    cut_class = infer_bot_cut_class(hand, tile, cut_position, draw_slot=has_draw_slot(player))
    return tile, cut_position, cut_class


def _current_cut_tile(game_state: Any) -> Optional[int]:
    window = getattr(game_state, "live_pending_window", None) or {}
    if window.get("status") == "waiting_action_after_cut":
        return window.get("tile")
    discarder_index = getattr(game_state, "current_player_index", None)
    if discarder_index is None:
        return None
    discards = getattr(game_state.player_list[discarder_index], "discard_tiles", [])
    return discards[-1] if discards else None


def _chi_needs(cut_tile: int) -> dict[str, list[int]]:
    return {
        "chi_left": [cut_tile - 2, cut_tile - 1],
        "chi_mid": [cut_tile - 1, cut_tile + 1],
        "chi_right": [cut_tile + 1, cut_tile + 2],
    }


def _remove_all(items: list[int], needed: list[int]) -> bool:
    for tile in needed:
        if tile not in items:
            return False
        items.remove(tile)
    return True
