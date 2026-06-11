"""对局内向单个玩家发送校验提示，以及切牌槽位校正。"""
import logging
from typing import Optional, Tuple

from ...response import Response
from .critical_log import log_critical_gamestate
from .hand_slot_utils import (
    TILE_NOT_IN_HAND_TIP_MESSAGE,
    clear_draw_slot,
    hand_contains_tile,
    has_draw_slot,
    remove_cut_tile,
    resolve_cut_class,
)

logger = logging.getLogger(__name__)

# apply_player_cut 成功时 (removed_tile_id, is_moqie, cut_tile_index)
CutApplyResult = Tuple[int, bool, Optional[int]]


async def send_game_tip(gamestate, player_index: int, message: str = TILE_NOT_IN_HAND_TIP_MESSAGE) -> None:
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


async def apply_player_cut(
    gamestate,
    player_index: int,
    action_data: dict,
) -> Optional[CutApplyResult]:
    """
    以 tileId 判定摸切/手切并从 hand_tiles 移除切牌。
    cutIndex 不参与服务端删牌，只原样回传供客户端 2D 删牌对齐同牌位置。
    切型声称与槽位冲突时静默校正（warning + critical 快照），不打断对局；
    仅当牌不在手牌或删牌失败时返回 None 并向玩家发送提示（不删其它牌、不广播切牌）。
    """
    player = gamestate.player_list[player_index]
    hand = player.hand_tiles
    tile_id = action_data.get("TileId")
    cut_index = action_data.get("cutIndex")
    draw_slot = has_draw_slot(player)

    if not hand_contains_tile(hand, tile_id):
        logger.error(
            "切牌拒绝：tile_id=%s 不在玩家 %s 手牌中",
            tile_id,
            player_index,
        )
        log_critical_gamestate(
            gamestate,
            "cut_rejected_tile_not_in_hand",
            f"切牌拒绝：tile_id={tile_id} 不在玩家 {player_index} 手牌中",
            player_index=player_index,
            extra={
                "action_data": action_data,
                "tile_id": tile_id,
                "draw_slot": draw_slot,
                "hand_before": list(hand),
            },
        )
        await send_game_tip(gamestate, player_index, TILE_NOT_IN_HAND_TIP_MESSAGE)
        return None

    is_moqie, corrected = resolve_cut_class(
        hand, tile_id, action_data.get("cutClass"), cut_index, draw_slot=draw_slot
    )
    if corrected:
        # 静默校正：不向玩家弹提示（正常对局不应打扰），仅留服务端日志与快照供排查
        logger.warning(
            "切型校正：player=%s tile_id=%s 客户端声称=%s 服务端=%s draw_slot=%s hand=%s",
            player_index,
            tile_id,
            action_data.get("cutClass"),
            is_moqie,
            draw_slot,
            list(hand),
        )
        log_critical_gamestate(
            gamestate,
            "cut_class_corrected",
            f"切型校正：player={player_index} tile_id={tile_id} "
            f"客户端声称moqie={action_data.get('cutClass')} 服务端moqie={is_moqie}",
            player_index=player_index,
            extra={
                "action_data": action_data,
                "tile_id": tile_id,
                "server_is_moqie": is_moqie,
                "draw_slot": draw_slot,
                "hand_before": list(hand),
            },
        )

    removed = remove_cut_tile(hand, tile_id, is_moqie, draw_slot=draw_slot)
    if removed is None:
        logger.error(
            "切牌拒绝：remove_cut_tile 失败 player=%s tile_id=%s is_moqie=%s",
            player_index,
            tile_id,
            is_moqie,
        )
        log_critical_gamestate(
            gamestate,
            "cut_rejected_remove_failed",
            f"切牌拒绝：remove_cut_tile 失败 player={player_index} tile_id={tile_id}",
            player_index=player_index,
            extra={
                "action_data": action_data,
                "tile_id": tile_id,
                "is_moqie": is_moqie,
                "cut_class_corrected": corrected,
                "draw_slot": draw_slot,
                "hand_before": list(hand),
            },
        )
        await send_game_tip(gamestate, player_index, TILE_NOT_IN_HAND_TIP_MESSAGE)
        return None

    clear_draw_slot(player)
    return removed, is_moqie, cut_index
