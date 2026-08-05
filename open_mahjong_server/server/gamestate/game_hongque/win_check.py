"""Authoritative Hongque winning-hand decomposition.

Rulebook 5.1.1: the winning shape requires every tile to be used in one and
only one legal group (three or more tiles).  There is no pair/head; a hand
that leaves a same-number pair outside every group is not a win.
"""
from __future__ import annotations

from typing import Iterable, Sequence

from .group_index import (
    can_partition_mask,
    mask_from_codes,
    partitions_from_codes,
)
from .tile import HongqueTile


def winning_decompositions(
    hand: Iterable[str],
    open_melds: Sequence[dict] = (),
) -> list[dict]:
    codes = [HongqueTile.parse(code).code for code in hand]
    if len(set(codes)) != len(codes):
        return []

    results: list[dict] = []
    seen: set[tuple] = set()

    def append(groups: Sequence[Sequence[str]]) -> None:
        key = (
            tuple(tuple(group) for group in groups),
            (),
        )
        if key in seen:
            return
        seen.add(key)
        results.append({
            "groups": [list(group) for group in groups],
            "pair": [],
        })

    for partition in partitions_from_codes(codes):
        if partition or open_melds:
            append(partition)

    return results


def is_winning_hand(hand: Iterable[str], open_melds: Sequence[dict] = ()) -> bool:
    codes = [HongqueTile.parse(code).code for code in hand]
    if len(set(codes)) != len(codes):
        return False
    try:
        mask = mask_from_codes(codes)
    except ValueError:
        return False
    if can_partition_mask(mask) and (mask != 0 or bool(open_melds)):
        return True
    return False
