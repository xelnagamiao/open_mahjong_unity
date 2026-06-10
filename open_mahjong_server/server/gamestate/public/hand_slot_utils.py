"""
手牌槽位工具：摸切/手切校验、按槽位删牌、暗杠摸杠/手杠判定。
服务端 hand_tiles 按摸入/发牌顺序 append，不做排序。
has_draw_slot=True 时末张 hand[-1] 为摸牌区；吃碰杠后无摸牌区，仅按牌值删牌。
删牌与客户端 TryRemoveCutHandCard 对齐：先按切型在本区找，再跨区兜底，始终能出牌。
"""
from __future__ import annotations

import logging
from typing import List, Optional, Set, Tuple

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


def hand_contains_tile(hand: List[int], tile_id: int) -> bool:
    """手牌中是否存在该牌（含赤宝牌归一化匹配）。"""
    return any(tiles_equal(t, tile_id) for t in hand)


def has_draw_slot(player) -> bool:
    return bool(getattr(player, "has_draw_slot", False))


def mark_draw_slot(player) -> None:
    player.has_draw_slot = True


def clear_draw_slot(player) -> None:
    player.has_draw_slot = False


def is_last_slot_tile(hand: List[int], tile_id: int) -> bool:
    if not hand:
        return False
    return tiles_equal(hand[-1], tile_id)


def has_tile_in_hand_zone(hand: List[int], tile_id: int) -> bool:
    """手牌区（非末张槽位）是否存在该牌。"""
    if len(hand) <= 1:
        return False
    for i in range(len(hand) - 1):
        if tiles_equal(hand[i], tile_id):
            return True
    return False


def resolve_cut_class(
    hand: List[int],
    tile_id: int,
    client_is_moqie: Optional[bool],
    cut_index: Optional[int] = None,
    *,
    draw_slot: bool = True,
) -> Tuple[bool, bool]:
    """
    校正摸切/手切类型（cutIndex 不参与判定，仅回传客户端）。
    同牌多张时：客户端手切且手牌区有该牌则信任手切，不因末张也有同牌而改摸切。
    仅当客户端声称的切型在对应区找不到牌时才校正，并触发 game_tip。
    返回 (server_is_moqie, was_corrected)。
    """
    _ = cut_index  # 不参与服务端判定
    client_moqie = bool(client_is_moqie)
    if not hand:
        return client_moqie, False

    if not draw_slot:
        return False, client_moqie

    in_draw = is_last_slot_tile(hand, tile_id)
    in_hand = has_tile_in_hand_zone(hand, tile_id)

    if client_moqie:
        if in_draw:
            return True, False
        return False, True

    if in_hand:
        return False, False
    if in_draw:
        return True, True
    return False, False


def _remove_from_draw_zone(hand: List[int], tile_id: int) -> Optional[int]:
    if hand and tiles_equal(hand[-1], tile_id):
        return hand.pop()
    return None


def _remove_from_hand_zone(hand: List[int], tile_id: int) -> Optional[int]:
    """从非末张槽位移除一张（手牌区），从后往前。"""
    for i in range(len(hand) - 2, -1, -1):
        if tiles_equal(hand[i], tile_id):
            return hand.pop(i)
    return None


def _remove_one_by_value(hand: List[int], tile_id: int) -> int:
    """按牌值移除一张（不区分区），从后往前。"""
    for i in range(len(hand) - 1, -1, -1):
        if tiles_equal(hand[i], tile_id):
            return hand.pop(i)
    for i, t in enumerate(hand):
        if tiles_equal(t, tile_id):
            return hand.pop(i)
    logger.error("_remove_one_by_value: hand 中未找到 tile_id=%s, hand=%s", tile_id, hand)
    return hand.pop() if hand else tile_id


def remove_cut_tile(
    hand: List[int],
    tile_id: int,
    is_moqie: bool,
    *,
    draw_slot: bool = True,
) -> int:
    """
    从 hand 移除切牌，与客户端 TryRemoveCutHandCard 一致：
    先按切型在本区删，找不到则跨区兜底，最后按 tileId 任意删。始终出牌。
    """
    if not hand:
        logger.error("remove_cut_tile: 空 hand, tile_id=%s", tile_id)
        return tile_id

    if not draw_slot:
        return _remove_one_by_value(hand, tile_id)

    if is_moqie:
        removed = _remove_from_draw_zone(hand, tile_id)
        if removed is not None:
            return removed
        removed = _remove_from_hand_zone(hand, tile_id)
        if removed is not None:
            logger.debug("摸切跨区兜底(手牌区): tile_id=%s", tile_id)
            return removed
    else:
        removed = _remove_from_hand_zone(hand, tile_id)
        if removed is not None:
            return removed
        removed = _remove_from_draw_zone(hand, tile_id)
        if removed is not None:
            logger.debug("手切跨区兜底(摸牌区): tile_id=%s", tile_id)
            return removed

    return _remove_one_by_value(hand, tile_id)


def pick_timeout_discard_tile(
    hand: List[int],
    forbidden_norms: Optional[Set[int]] = None,
) -> int:
    """超时自动出牌：优先从后往前选非禁切牌。"""
    forbidden_norms = forbidden_norms or set()
    for tile_id in reversed(hand):
        if normalize_tile(tile_id) not in forbidden_norms:
            return tile_id
    return hand[-1]


def resolve_is_mo_gang(hand: List[int], kan_tile: int, *, draw_slot: bool = True) -> bool:
    """摸杠：有摸牌区且杠牌与 hand[-1] 相同；吃碰杠后无摸牌区则为手杠。"""
    if not draw_slot or not hand:
        return False
    return tiles_equal(hand[-1], kan_tile)


def remove_angang_tiles(hand: List[int], normal_tile: int, *, draw_slot: bool = True) -> List[int]:
    """
    从 hand 移除暗杠 4 张（归一化匹配），返回 4 张真实 ID（供 combination_mask）。
    有摸牌区且末张匹配时优先移除末张。
    """
    removed: List[int] = []
    target_norm = normalize_tile(normal_tile)

    if draw_slot and hand and tiles_equal(hand[-1], normal_tile):
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
