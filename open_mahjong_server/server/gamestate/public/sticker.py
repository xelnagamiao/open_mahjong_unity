import logging
from typing import Any, Optional, Tuple

from ...response import Response, Sticker_info

logger = logging.getLogger(__name__)


def resolve_sticker_sender(player: Any) -> Tuple[Optional[int], Optional[int]]:
    """解析表情包发送者座位。兼容共享规则的 player_index 与虹雀的 index。"""
    seat = getattr(player, "player_index", None)
    if seat is None:
        seat = getattr(player, "index", None)
    original = getattr(player, "original_player_index", None)
    if original is None:
        original = seat
    return seat, original


def _is_offline(player: Any) -> bool:
    tags = getattr(player, "tag_list", None) or []
    return "offline" in tags or getattr(player, "online", True) is False


def _is_bot(player: Any) -> bool:
    if getattr(player, "is_bot", False):
        return True
    user_id = getattr(player, "user_id", None)
    return isinstance(user_id, int) and user_id <= 10


async def broadcast_sticker(
    game_state,
    player_index: int,
    original_player_index: int,
    sticker: str,
) -> None:
    """向对局内所有在线玩家与实时观战者广播表情包。"""
    response = Response(
        type="gamestate/broadcast_sticker",
        success=True,
        message="",
        sticker_info=Sticker_info(
            player_index=player_index,
            original_player_index=original_player_index,
            sticker=sticker,
        ),
    )
    payload = response.dict(exclude_none=True)
    game_server = game_state.game_server
    sent_user_ids = set()

    for player in game_state.player_list:
        try:
            if _is_bot(player) or _is_offline(player):
                continue
            conn = game_server.user_id_to_connection.get(player.user_id)
            if conn is None:
                continue
            await conn.websocket.send_json(payload)
            sent_user_ids.add(player.user_id)
        except Exception as e:
            logger.error(
                f"广播表情包给玩家 user_id={player.user_id} 失败: {e}",
                exc_info=True,
            )

    spectators = getattr(game_state, "realtime_spectators", None) or []
    for spectator in spectators:
        user_id = getattr(spectator, "user_id", None)
        if not user_id or user_id in sent_user_ids:
            continue
        conn = game_server.user_id_to_connection.get(user_id)
        if conn is None:
            continue
        try:
            await conn.websocket.send_json(payload)
            sent_user_ids.add(user_id)
        except Exception as e:
            logger.error(
                f"广播表情包给实时观战者 user_id={user_id} 失败: {e}",
                exc_info=True,
            )
