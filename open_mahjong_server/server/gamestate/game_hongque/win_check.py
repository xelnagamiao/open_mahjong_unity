"""Authoritative Hongque winning-hand decomposition.

The rulebook win shape (all concealed tiles belong to legal groups) remains
valid.  The playable prototype additionally accepts one same-number pair as a
mahjong-style head, so a player who has exposed the rest of the hand can win
on a single-number wait.
"""
from __future__ import annotations

from itertools import combinations
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

    def append(groups: Sequence[Sequence[str]], pair: Sequence[str] = ()) -> None:
        # A pair by itself is not a complete hand.  It is the head for one or
        # more concealed/exposed legal groups.
        if pair and not groups and not open_melds:
            return
        key = (
            tuple(tuple(group) for group in groups),
            tuple(sorted(pair)),
        )
        if key in seen:
            return
        seen.add(key)
        results.append({
            "groups": [list(group) for group in groups],
            "pair": list(pair),
        })

    for partition in partitions_from_codes(codes):
        if partition or open_melds:
            append(partition)

    # The rulebook all-groups shape is already complete and always keeps at
    # least as many long-group base points as removing two tiles for a head.
    # Only search pair extensions when the original shape is not a win; this
    # avoids O(n²) full partition searches on large rainbow/long-group hands.
    if results:
        return results

    for left, right in combinations(range(len(codes)), 2):
        first = HongqueTile.parse(codes[left])
        second = HongqueTile.parse(codes[right])
        if first.number != second.number:
            continue
        remainder = [
            code for index, code in enumerate(codes)
            if index not in (left, right)
        ]
        for partition in partitions_from_codes(remainder):
            append(partition, (codes[left], codes[right]))

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
    for left, right in combinations(range(len(codes)), 2):
        if HongqueTile.parse(codes[left]).number != HongqueTile.parse(codes[right]).number:
            continue
        pair_mask = mask_from_codes((codes[left], codes[right]))
        remainder = mask ^ pair_mask
        if can_partition_mask(remainder) and (remainder != 0 or bool(open_melds)):
            return True
    return False
