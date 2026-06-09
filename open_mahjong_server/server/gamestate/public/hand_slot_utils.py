"""
手牌槽位工具：摸切/手切校验、按槽位删牌、暗杠摸杠/手杠判定。
服务端 hand_tiles 按摸入/发牌顺序 append，不做排序；末张 hand[-1] 为最近入手的牌。
"""
from __future__ import annotations

import logging
from typing import List, Optional, Tuple

logger = logging.getLogger(__name__)

CUT_CLASS_TIP_MESSAGE = "检测到客户端和服务器手摸切验证出现问题"

# 赤 5 → 归一化 5
_AKA_OFFSET = 100


def normalize_tile(tile: int) -> int:
    if tile in (105, 205, 305):
        return tile - _AKA_OFFSET
    return tile


def tiles_equal(a: int, b: int) -> bool:
    return normalize_tile(a) == normalize_tile(b)


def is_last_slot_tile(hand: List[int], tile_id: int) -> bool:
    if not hand:
        return False
    return tiles_equal(hand[-1], tile_id)


def resolve_cut_class(
    hand: List[int],
    tile_id: int,
    client_is_moqie: Optional[bool],
    cut_index: Optional[int] = None,
) -> Tuple[bool, bool]:
    """
    根据手牌槽位校正摸切/手切。
    cut_index 仅用于判定（如客户端声称手切但 index 指向末张则校正为摸切），不参与删牌。
    返回 (server_is_moqie, was_corrected)。
    """
    client_moqie = bool(client_is_moqie)
    if not hand:
        return client_moqie, False

    last_is_target = is_last_slot_tile(hand, tile_id)

    if client_moqie:
        # 摸切：必须切末张
        if not last_is_target:
            return False, True
        return True, False

    # 手切：不能切末张（末张视为刚摸入）
    if last_is_target:
        return True, True

    # cut_index 若指向末张槽位，同样校正为摸切
    if cut_index is not None and cut_index == len(hand) - 1:
        return True, True

    return False, False


def remove_cut_tile(hand: List[int], tile_id: int, is_moqie: bool) -> int:
    """
    从 hand 移除切牌，返回被移除的真实牌 ID。
    不使用客户端 cutIndex：服务端只关心牌值与摸切/手切槽位规则；
    cutIndex 仅由 apply_player_cut 原样回传客户端，供 2D 同牌多张时删对位置。
    """
    if not hand:
        logger.error("remove_cut_tile: 空 hand, tile_id=%s", tile_id)
        return tile_id

    if is_moqie:
        if not tiles_equal(hand[-1], tile_id):
            logger.warning(
                "摸切校正删牌：末张 id=%s 与请求 tile_id=%s 不一致，仍删末张",
                hand[-1],
                tile_id,
            )
        return hand.pop()

    # 手切：优先从非末张槽位删除（末张视为刚摸入区），同牌多张时任删一张即可
    for i in range(len(hand) - 2, -1, -1):
        if tiles_equal(hand[i], tile_id):
            return hand.pop(i)

    for i, t in enumerate(hand):
        if tiles_equal(t, tile_id):
            return hand.pop(i)

    logger.error("remove_cut_tile: hand 中未找到 tile_id=%s, hand=%s", tile_id, hand)
    return hand.pop() if hand else tile_id


def resolve_is_mo_gang(hand: List[int], kan_tile: int) -> bool:
    """摸杠：杠牌归一化值与 hand[-1] 相同；否则为手杠。适用于暗杠与加杠。"""
    if not hand:
        return False
    return tiles_equal(hand[-1], kan_tile)


def remove_angang_tiles(hand: List[int], normal_tile: int) -> List[int]:
    """
    从 hand 移除暗杠 4 张（归一化匹配），返回 4 张真实 ID（供 combination_mask）。
    摸杠时优先移除末张，再移除其余同牌。
    """
    removed: List[int] = []
    target_norm = normalize_tile(normal_tile)

    if hand and tiles_equal(hand[-1], normal_tile):
        removed.append(hand.pop())

    while len(removed) < 4:
        found_idx = -1
        for i, t in enumerate(hand):
            if normalize_tile(t) == target_norm:
                found_idx = i
                break
        if found_idx < 0:
            logger.error(
                "remove_angang_tiles: 不足 4 张 normal=%s, hand=%s, removed=%s",
                normal_tile,
                hand,
                removed,
            )
            removed.append(normal_tile)
            continue
        removed.append(hand.pop(found_idx))

    return removed
