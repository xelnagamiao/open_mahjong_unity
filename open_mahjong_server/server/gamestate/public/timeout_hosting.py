"""Server-authoritative timeout auto-moqie (AFK hosting) shared across rules.

Tracks consecutive timeout cuts per human user_id on a GameState. After
TIMEOUT_CUTS_BEFORE_AUTO_MOQIE consecutive timeout cuts, the seat enters
timeout-hosted mode:
- Subsequent asks are automatically answered by the server after a short delay
  (reusing offline_auto_action semantics, without first-cut protection).
- Every game_start/reconnect/resync snapshot carries `timeout_auto_moqie: bool`
  for the receiving seat, and state transitions emit `gamestate/timeout_auto_moqie`,
  so clients render the return modal as a pure projection of server state.
- Any manual action or explicit `cancel_timeout_auto_moqie` clears hosting.
"""
from __future__ import annotations

import asyncio
import logging
from typing import Any, Dict, Optional, Tuple

from .connection_delivery import send_bounded
from .hand_slot_utils import bot_ask_hand_game_status
from .lifecycle import start_owned_task

logger = logging.getLogger(__name__)

TIMEOUT_CUTS_BEFORE_AUTO_MOQIE = 3
HOSTED_ACTION_DELAY_SECONDS = 0.5

_PASS_WAIT_STATUSES = (
    "waiting_action_after_cut",
    "waiting_action_qianggang",
    "waiting_initial_hu",
    "waiting_sea_bottom",
)


def _store(state) -> Dict[int, Dict[str, Any]]:
    store = getattr(state, "_timeout_hosting_by_uid", None)
    if store is None:
        store = {}
        state._timeout_hosting_by_uid = store
    return store


def _entry(state, user_id: int) -> Dict[str, Any]:
    store = _store(state)
    entry = store.get(user_id)
    if entry is None:
        entry = {
            "consecutive_cuts": 0,
            "active": False,
            "last_cut_key": None,
            "last_scheduled_key": None,
        }
        store[user_id] = entry
    return entry


def is_timeout_hosted(state, user_id: Optional[int]) -> bool:
    if state is None or user_id is None or user_id < 10:
        return False
    entry = _store(state).get(user_id)
    return bool(entry and entry.get("active"))


def get_timeout_cut_count(state, user_id: Optional[int]) -> int:
    if state is None or user_id is None or user_id < 10:
        return 0
    entry = _store(state).get(user_id)
    return int(entry.get("consecutive_cuts", 0)) if entry else 0


def clear_timeout_hosting(state, user_id: Optional[int]) -> bool:
    """Clear consecutive timeout counter and hosting flag. Returns True if active changed True -> False."""
    if state is None or user_id is None or user_id < 10:
        return False
    entry = _store(state).get(user_id)
    if entry is None:
        return False
    was_active = bool(entry.get("active"))
    entry["consecutive_cuts"] = 0
    entry["active"] = False
    entry["last_scheduled_key"] = None
    return was_active


def record_do_action_from_packet(state, packet: dict) -> Optional[Tuple[int, bool]]:
    """Inspect a do_action packet once per broadcast and update timeout hosting state.

    Returns (user_id, True) only when the seat transitions into timeout-hosted mode.
    """
    info = packet.get("do_action_info")
    if not isinstance(info, dict):
        return None
    actions = info.get("action_list")
    if not isinstance(actions, list) or "cut" not in actions:
        return None
    action_player = info.get("action_player")
    players = getattr(state, "player_list", None)
    if not isinstance(action_player, int) or not players or not (0 <= action_player < len(players)):
        return None
    player = players[action_player]
    user_id = getattr(player, "user_id", None)
    if not isinstance(user_id, int) or user_id < 10:
        return None

    is_timeout = bool(info.get("is_timeout_action"))
    cut_key = (
        getattr(state, "current_round", None),
        getattr(state, "server_action_tick", None),
        action_player,
        info.get("cut_tile"),
        len(getattr(player, "discard_tiles", ()) or ()),
        is_timeout,
    )
    entry = _entry(state, user_id)
    if entry["last_cut_key"] == cut_key:
        return None
    entry["last_cut_key"] = cut_key

    if not is_timeout:
        # While hosted, server-driven auto-cuts are emitted without is_timeout_action;
        # only explicit manual input or cancel_timeout_auto_moqie clears active hosting.
        if not entry["active"]:
            entry["consecutive_cuts"] = 0
        return None

    entry["consecutive_cuts"] = int(entry["consecutive_cuts"]) + 1
    logger.info(
        "超时切牌计数 game=%s uid=%s count=%s/%s",
        getattr(state, "gamestate_id", None),
        user_id,
        entry["consecutive_cuts"],
        TIMEOUT_CUTS_BEFORE_AUTO_MOQIE,
    )
    if entry["active"] or entry["consecutive_cuts"] < TIMEOUT_CUTS_BEFORE_AUTO_MOQIE:
        return None

    entry["active"] = True
    logger.info(
        "开启服务端超时自动摸切 game=%s uid=%s",
        getattr(state, "gamestate_id", None),
        user_id,
    )
    return (user_id, True)


async def notify_timeout_hosting_state(state, user_id: int, active: bool) -> None:
    """Push authoritative timeout_auto_moqie state to the affected player."""
    game_server = getattr(state, "game_server", None)
    connections = getattr(game_server, "user_id_to_connection", None) or {}
    conn = connections.get(user_id)
    if conn is None or getattr(conn, "websocket", None) is None:
        return
    payload = {
        "type": "gamestate/timeout_auto_moqie",
        "success": True,
        "gamestate_id": str(getattr(state, "gamestate_id", "") or ""),
        "active": bool(active),
        "timeout_auto_moqie": bool(active),
    }
    try:
        await send_bounded(conn.websocket, payload)
    except Exception:
        logger.warning(
            "发送 timeout_auto_moqie 状态失败 game=%s uid=%s active=%s",
            getattr(state, "gamestate_id", None),
            user_id,
            active,
        )


async def _run_timeout_hosted_action(
    game_state,
    player_index: int,
    user_id: int,
    action_list: list,
    game_status: str,
    expected_tick: Any,
) -> None:
    from .ai.get_action import get_ai_action
    from .claim_protection import get_meld_post_gap
    from .offline.offline_auto_action import (
        _auto_dingque_on_disconnect,
        _pick_offline_cut_tile,
        _submit_pass_when_ready,
    )

    try:
        if game_status == "waiting_dingque" and "dingque" in action_list:
            await asyncio.sleep(HOSTED_ACTION_DELAY_SECONDS)
            if not is_timeout_hosted(game_state, user_id):
                return
            if getattr(game_state, "server_action_tick", None) != expected_tick:
                return
            await _auto_dingque_on_disconnect(game_state, player_index)
            return

        if game_status in _PASS_WAIT_STATUSES:
            await asyncio.sleep(HOSTED_ACTION_DELAY_SECONDS)
            if not is_timeout_hosted(game_state, user_id):
                return
            if getattr(game_state, "server_action_tick", None) != expected_tick:
                return
            current_player = game_state.player_list[player_index]
            await _submit_pass_when_ready(game_state, player_index, action_list, current_player)
            return

        if game_status == "waiting_buhua_round":
            await asyncio.sleep(HOSTED_ACTION_DELAY_SECONDS)
            if not is_timeout_hosted(game_state, user_id):
                return
            if getattr(game_state, "server_action_tick", None) != expected_tick:
                return
            if "pass" in action_list:
                await get_ai_action(game_state, player_index, "pass", None, None, None, None)
            return

        if game_status in ("waiting_hand_action", "onlycut_after_action"):
            delay = HOSTED_ACTION_DELAY_SECONDS
            if game_status == "onlycut_after_action" and bool(getattr(game_state, "claim_protection", False)):
                delay += get_meld_post_gap(game_state)
            await asyncio.sleep(delay)
            if not is_timeout_hosted(game_state, user_id):
                return
            if getattr(game_state, "server_action_tick", None) != expected_tick:
                return
            current_player = game_state.player_list[player_index]
            if "cut" in action_list and getattr(current_player, "hand_tiles", None):
                tile_id, cut_index, is_moqie = _pick_offline_cut_tile(current_player)
                if getattr(game_state, "sub_rule", "") == "guobiao/huahun":
                    from ...game_calculation.guobiao_huahun import auto_cut
                    tile_id, cut_index, is_moqie = auto_cut(current_player, (tile_id, cut_index, is_moqie))
                await get_ai_action(game_state, player_index, "cut", is_moqie, tile_id, cut_index, None)
    except asyncio.CancelledError:
        raise
    except Exception:
        logger.exception(
            "超时托管自动执行失败 game=%s seat=%s uid=%s status=%s",
            getattr(game_state, "gamestate_id", None),
            player_index,
            user_id,
            game_status,
        )


def schedule_timeout_hosted_action(game_state, viewer_index: int) -> bool:
    """Schedule fast server-side moqie/pass when a timeout-hosted seat is asked."""
    players = getattr(game_state, "player_list", None)
    if not players or not (0 <= viewer_index < len(players)):
        return False
    player = players[viewer_index]
    user_id = getattr(player, "user_id", None)
    if not is_timeout_hosted(game_state, user_id):
        return False
    tags = getattr(player, "tag_list", None) or ()
    if "offline" in tags:
        return False
    action_list = list((getattr(game_state, "action_dict", None) or {}).get(viewer_index) or [])
    if not action_list:
        return False
    tick = getattr(game_state, "server_action_tick", None)
    raw_status = getattr(game_state, "game_status", None)
    status = bot_ask_hand_game_status(game_state, viewer_index) if raw_status in ("waiting_hand_action", "onlycut_after_action") else raw_status
    schedule_key = (status, tick, viewer_index)
    entry = _entry(game_state, user_id)
    if entry["last_scheduled_key"] == schedule_key:
        return False
    try:
        asyncio.get_running_loop()
    except RuntimeError:
        return False
    entry["last_scheduled_key"] = schedule_key
    start_owned_task(
        game_state,
        _run_timeout_hosted_action(game_state, viewer_index, user_id, action_list, status, tick),
    )
    return True
