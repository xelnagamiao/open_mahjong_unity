"""Hongque ready-hand and wait enumeration."""
from __future__ import annotations

from typing import Iterable, Sequence

from .group_index import (
    DECK,
    FULL_DECK_MASK,
    GROUPS_BY_TILE,
    TILE_INDEX,
    can_partition_mask,
    mask_from_codes,
    waiting_mask,
)
from .tile import HongqueTile


def waiting_tiles(hand: Iterable[str], open_melds: Sequence[dict] = ()) -> list[str]:
    codes = [HongqueTile.parse(code).code for code in hand]
    if len(set(codes)) != len(codes):
        return []
    used_codes = list(codes)
    for meld in open_melds:
        used_codes.extend(
            HongqueTile.parse(code).code for code in meld.get("tiles", ())
        )
    try:
        hand_mask = mask_from_codes(codes)
        used_mask = mask_from_codes(used_codes)
    except ValueError:
        return []
    waits = waiting_mask(
        hand_mask,
        used_mask=used_mask,
        has_open_group=bool(open_melds),
    )
    return [code for index, code in enumerate(DECK) if waits & (1 << index)]


def is_tenpai(hand: Iterable[str], open_melds: Sequence[dict] = ()) -> bool:
    return bool(waiting_tiles(hand, open_melds))


def kong_win_waiting_tiles(hand: Iterable[str], open_melds: Sequence[dict] = ()) -> list[str]:
    """杠和听牌：摸到该张后，可把若干手牌并入明牌（杠）并立即和牌。

    这类和法必须自摸才能成立——明牌加牌只能在自己回合用手牌进行，
    其它家弃牌只能用于吃/碰/虹或捉和，不能并入自身明牌。因此这些听牌
    全部是“仅自摸和”。

    枚举思路与 ``group_index.waiting_mask`` 一致：对每副明牌，扫描包含它的
    合法组超集，若超集比明牌多的牌中恰好只有一张不在手牌里，且杠完
    剩余手牌可构成和牌型，则该缺失张就是一张杠和听牌。
    """
    codes = [HongqueTile.parse(code).code for code in hand]
    if len(set(codes)) != len(codes):
        return []
    if not open_melds:
        return []
    try:
        hand_mask = mask_from_codes(codes)
        used_codes = list(codes)
        for meld in open_melds:
            used_codes.extend(
                HongqueTile.parse(code).code for code in meld.get("tiles", ())
            )
        used_mask = mask_from_codes(used_codes)
    except ValueError:
        return []
    available_mask = FULL_DECK_MASK & ~used_mask

    waits: list[str] = []
    seen: set[str] = set()
    for meld in open_melds:
        meld_codes = [HongqueTile.parse(code).code for code in meld.get("tiles", ())]
        if len(meld_codes) < 3:
            continue
        try:
            meld_mask = mask_from_codes(meld_codes)
        except ValueError:
            continue
        anchor_index = TILE_INDEX[min(meld_codes, key=lambda code: TILE_INDEX[code])]
        for group_mask in GROUPS_BY_TILE[anchor_index]:
            if group_mask & meld_mask != meld_mask:
                continue
            extra = group_mask ^ meld_mask
            if extra == 0:
                continue
            missing = extra & ~hand_mask
            if missing.bit_count() != 1:
                continue
            if missing & available_mask == 0:
                continue
            rest = hand_mask ^ (extra & hand_mask)
            if rest != 0 and not can_partition_mask(rest):
                continue
            tile = DECK[missing.bit_length() - 1]
            if tile not in seen:
                seen.add(tile)
                waits.append(tile)
    return waits
