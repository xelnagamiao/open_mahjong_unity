import logging

from ...response import Response, Sticker_info

logger = logging.getLogger(__name__)


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
            if "offline" in player.tag_list:
                continue
            if player.user_id < 10:
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
