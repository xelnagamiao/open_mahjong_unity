"""
手牌槽位工具：摸切/手切判定、按槽位删牌、暗杠摸杠/手杠判定。一切以 tileId 为核心。
服务端 hand_tiles 按摸入/发牌顺序 append，不做排序；
has_draw_slot=True 时末张 hand[-1] 为摸牌区；吃碰杠后无摸牌区。
cutIndex 不参与服务端任何判定与删牌，仅由 apply_player_cut 原样回传客户端。

摸切/手切判定规则（与客户端 currentGetTile 标记对齐）：
- 客户端声称摸切且摸牌区正是该 tileId → 摸切；
- 客户端声称手切且手牌区存在该 tileId → 手切（同 id 多张时信任客户端声称）；
- 声称与该 tileId 实际所在区冲突时，以服务端槽位为准静默校正（由调用方记录日志，不打断对局）。

删牌规则：先按切型在对应区删，再跨区兜底；区内精确 tileId 优先、赤五归一化兜底；
找不到则返回 None（不误删其它牌）。
"""
from __future__ import annotations

import logging
from typing import List, Optional, Set, Tuple

logger = logging.getLogger(__name__)

TILE_NOT_IN_HAND_TIP_MESSAGE = "此牌不在您的手牌中"

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
    return _find_index(hand, tile_id, 0, len(hand) - 1) >= 0


def infer_bot_cut_class(
    hand: List[int],
    tile_id: int,
    cut_index: Optional[int] = None,
    *,
    draw_slot: bool = True,
) -> bool:
    """
    机器人提交切牌时推断 cutClass（True=摸切），与服务端 resolve_cut_class 结论对齐。
    优先根据 cut_index 是否指向末张摸牌槽位；无 cut_index 时按 tileId 所在区推断。
    """
    if not draw_slot or not hand:
        return False

    if cut_index is not None and 0 <= cut_index < len(hand):
        return cut_index == len(hand) - 1 and tiles_equal(hand[cut_index], tile_id)

    in_draw = is_last_slot_tile(hand, tile_id)
    in_hand = has_tile_in_hand_zone(hand, tile_id)
    if in_hand:
        return False
    return in_draw


def resolve_cut_class(
    hand: List[int],
    tile_id: int,
    client_is_moqie: Optional[bool],
    cut_index: Optional[int] = None,
    *,
    draw_slot: bool = True,
) -> Tuple[bool, bool]:
    """
    以 tileId 判定摸切/手切（cutIndex 不参与判定，仅原样回传客户端）。
    同 tileId 多张时信任客户端声称的切型，不强行校正。
    返回 (server_is_moqie, was_corrected)；was_corrected=True 表示声称与槽位冲突、已按服务端校正。
    """
    _ = cut_index  # 不参与服务端判定
    client_moqie = bool(client_is_moqie)
    if not hand:
        return client_moqie, False

    if not draw_slot:
        # 吃碰后无摸牌区：只能手切
        return False, client_moqie

    in_draw = is_last_slot_tile(hand, tile_id)
    in_hand = has_tile_in_hand_zone(hand, tile_id)

    if client_moqie:
        # 声称摸切：摸牌区确为该牌则采纳；否则只可能是手切
        return (True, False) if in_draw else (False, True)
    # 声称手切：手牌区有该牌则采纳；仅摸牌区有则校正为摸切
    if in_hand:
        return False, False
    return (True, True) if in_draw else (False, False)


def _find_index(hand: List[int], tile_id: int, lo: int, hi: int) -> int:
    """在 hand[lo:hi] 内从后往前找 tileId：精确匹配优先，赤五归一化兜底。返回 -1 表示未找到。"""
    if lo >= hi:
        return -1
    for i in range(hi - 1, lo - 1, -1):
        if hand[i] == tile_id:
            return i
    for i in range(hi - 1, lo - 1, -1):
        if tiles_equal(hand[i], tile_id):
            return i
    return -1


def _remove_from_draw_zone(hand: List[int], tile_id: int) -> Optional[int]:
    if hand and tiles_equal(hand[-1], tile_id):
        return hand.pop()
    return None


def _remove_from_hand_zone(hand: List[int], tile_id: int) -> Optional[int]:
    """从手牌区（非末张槽位）移除一张，精确 tileId 优先。"""
    idx = _find_index(hand, tile_id, 0, len(hand) - 1)
    return hand.pop(idx) if idx >= 0 else None


def _remove_one_by_value(hand: List[int], tile_id: int) -> Optional[int]:
    """按牌值移除一张（不区分区），精确 tileId 优先。"""
    idx = _find_index(hand, tile_id, 0, len(hand))
    return hand.pop(idx) if idx >= 0 else None


def remove_cut_tile(
    hand: List[int],
    tile_id: int,
    is_moqie: bool,
    *,
    draw_slot: bool = True,
) -> Optional[int]:
    """
    从 hand 移除切牌，与客户端 TryRemoveCutHandCard 一致：
    先按切型在本区删，找不到则跨区兜底；仍找不到则返回 None（不删除其它牌）。
    """
    if not hand:
        logger.error("remove_cut_tile: 空 hand, tile_id=%s", tile_id)
        return None

    if not draw_slot:
        removed = _remove_one_by_value(hand, tile_id)
        if removed is None:
            logger.error(
                "remove_cut_tile: 无摸牌区且 hand 中未找到 tile_id=%s, hand=%s",
                tile_id,
                hand,
            )
        return removed

    if is_moqie:
        removed = _remove_from_draw_zone(hand, tile_id)
        if removed is not None:
            return removed
        removed = _remove_from_hand_zone(hand, tile_id)
        if removed is not None:
            logger.warning("摸切跨区兜底(手牌区): tile_id=%s hand=%s", tile_id, hand)
            return removed
    else:
        removed = _remove_from_hand_zone(hand, tile_id)
        if removed is not None:
            return removed
        removed = _remove_from_draw_zone(hand, tile_id)
        if removed is not None:
            logger.warning("手切跨区兜底(摸牌区): tile_id=%s hand=%s", tile_id, hand)
            return removed

    removed = _remove_one_by_value(hand, tile_id)
    if removed is None:
        logger.error(
            "remove_cut_tile: hand 中未找到 tile_id=%s, hand=%s, is_moqie=%s, draw_slot=%s",
            tile_id,
            hand,
            is_moqie,
            draw_slot,
        )
    return removed


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
