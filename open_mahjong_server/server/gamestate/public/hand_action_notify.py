"""对局内向单个玩家发送校验提示，以及切牌槽位校正。"""
import logging
from typing import Optional, Tuple

from ...response import Response
from .hand_slot_utils import (
    CUT_CLASS_TIP_MESSAGE,
    clear_draw_slot,
    has_draw_slot,
    remove_cut_tile,
    resolve_cut_class,
)

logger = logging.getLogger(__name__)


async def send_game_tip(gamestate, player_index: int, message: str = CUT_CLASS_TIP_MESSAGE) -> None:
    if not getattr(gamestate, "tips", False):
        return
    player = gamestate.player_list[player_index]
    if player.user_id <= 10:
        return
    conn_map = getattr(gamestate.game_server, "user_id_to_connection", None) or {}
    player_conn = conn_map.get(player.user_id)
    if not player_conn:
        return
    try:
        response = Response(
            type="game_tip",
            success=False,
            message=message,
        )
        await player_conn.websocket.send_json(response.dict(exclude_none=True))
    except Exception as e:
        logger.warning("send_game_tip 失败 player=%s: %s", player_index, e)


async def notify_cut_class_corrected(gamestate, player_index: int, was_corrected: bool) -> None:
    if was_corrected:
        await send_game_tip(gamestate, player_index)


async def apply_player_cut(
    gamestate,
    player_index: int,
    action_data: dict,
) -> Tuple[int, bool, Optional[int]]:
    """
    校正摸切/手切并从 hand_tiles 移除切牌。
    cutIndex 不参与服务端删牌，只原样回传供客户端 2D 删牌对齐同牌位置。
    返回 (removed_tile_id, is_moqie, cut_tile_index)。
    """
    player = gamestate.player_list[player_index]
    hand = player.hand_tiles
    tile_id = action_data.get("TileId")
    cut_index = action_data.get("cutIndex")
    draw_slot = has_draw_slot(player)
    is_moqie, corrected = resolve_cut_class(
        hand, tile_id, action_data.get("cutClass"), cut_index, draw_slot=draw_slot
    )
    await notify_cut_class_corrected(gamestate, player_index, corrected)
    removed = remove_cut_tile(hand, tile_id, is_moqie, draw_slot=draw_slot)
    clear_draw_slot(player)
    return removed, is_moqie, cut_index
