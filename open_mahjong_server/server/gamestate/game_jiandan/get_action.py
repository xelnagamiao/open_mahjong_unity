from __future__ import annotations

import logging
from typing import Any, Optional

from ..public.hand_slot_utils import hand_contains_tile

logger = logging.getLogger(__name__)


def _player_index_for_user(game_state: Any, user_id: int) -> Optional[int]:
    for index, player in enumerate(game_state.player_list):
        if player.user_id == user_id:
            return index
    return None


def _validate_action_payload(
    game_state: Any,
    player_index: int,
    action_type: str,
    TileId: Optional[int],
    target_tile: Optional[int],
) -> tuple[bool, Optional[int]]:
    player = game_state.player_list[player_index]
    if target_tile is not None and target_tile <= 0:
        target_tile = None

    if action_type == "cut":
        if TileId is None or not hand_contains_tile(player.hand_tiles, TileId):
            logger.warning(
                "Jiandan cut rejected: player_index=%s TileId=%s hand_tiles=%s",
                player_index,
                TileId,
                player.hand_tiles,
            )
            return False, target_tile
        return True, target_tile

    if action_type == "angang":
        if target_tile is None or player.hand_tiles.count(target_tile) < 4:
            logger.warning(
                "Jiandan concealed kong rejected: player_index=%s target_tile=%s hand_tiles=%s",
                player_index,
                target_tile,
                player.hand_tiles,
            )
            return False, target_tile
        return True, target_tile

    if action_type == "jiagang":
        if target_tile is None or target_tile not in player.hand_tiles or f"k{target_tile}" not in player.combination_tiles:
            logger.warning(
                "Jiandan added kong rejected: player_index=%s target_tile=%s hand_tiles=%s combination_tiles=%s",
                player_index,
                target_tile,
                player.hand_tiles,
                player.combination_tiles,
            )
            return False, target_tile
        return True, target_tile

    return True, target_tile


async def get_ai_action(
    game_state: Any,
    player_index: int,
    action_type: str,
    cutClass: bool = False,
    TileId: Optional[int] = None,
    cutIndex: int = -1,
    target_tile: Optional[int] = None,
    **_: Any,
) -> None:
    if player_index not in range(4):
        logger.warning("Invalid Jiandan AI player index: %s", player_index)
        return
    if player_index not in getattr(game_state, "waiting_players_list", []):
        logger.info(
            "Jiandan late action ignored: player_index=%s action=%s status=%s",
            player_index,
            action_type,
            getattr(game_state, "game_status", None),
        )
        return
    if action_type not in getattr(game_state, "action_dict", {}).get(player_index, []):
        logger.info(
            "Jiandan illegal/late action ignored: player_index=%s action=%s legal=%s status=%s",
            player_index,
            action_type,
            getattr(game_state, "action_dict", {}).get(player_index, []),
            getattr(game_state, "game_status", None),
        )
        return
    is_valid, target_tile = _validate_action_payload(game_state, player_index, action_type, TileId, target_tile)
    if not is_valid:
        return
    await game_state.submit_action(
        player_index,
        action_type,
        target_tile=target_tile,
        TileId=TileId,
        cutIndex=cutIndex if cutIndex is not None else -1,
        cutClass=cutClass,
    )


async def get_action(
    game_state: Any,
    player_id: str,
    action_type: str,
    cutClass: bool = False,
    TileId: Optional[int] = None,
    cutIndex: int = -1,
    target_tile: Optional[int] = None,
    action_tick: Optional[int] = None,
    **kwargs: Any,
) -> None:
    player_conn = game_state.game_server.players.get(player_id) if game_state.game_server else None
    if not player_conn or not getattr(player_conn, "user_id", None):
        logger.warning("Jiandan action rejected: missing player connection %s", player_id)
        return

    player_index = _player_index_for_user(game_state, player_conn.user_id)
    if player_index is None:
        logger.warning("Jiandan action rejected: user_id=%s not in game", player_conn.user_id)
        return

    if getattr(game_state, "game_status", None) == "END":
        logger.info(
            "Jiandan late action ignored after END: player_index=%s action=%s",
            player_index,
            action_type,
        )
        return

    if (
        action_type != "ready"
        and
        action_tick is not None
        and action_tick != getattr(game_state, "server_action_tick", action_tick)
    ):
        logger.info(
            "Jiandan stale action ignored: player_index=%s action=%s client_tick=%s server_tick=%s",
            player_index,
            action_type,
            action_tick,
            getattr(game_state, "server_action_tick", None),
        )
        return

    await get_ai_action(
        game_state,
        player_index,
        action_type,
        cutClass=cutClass,
        TileId=TileId,
        cutIndex=cutIndex,
        target_tile=target_tile,
        **kwargs,
    )
