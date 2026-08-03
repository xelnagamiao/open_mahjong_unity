"""Hongque ready-hand and wait enumeration."""
from __future__ import annotations

from typing import Iterable, Sequence

from .group_index import DECK, mask_from_codes, waiting_mask
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
