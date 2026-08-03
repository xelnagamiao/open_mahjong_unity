"""Precomputed Hongque group index and bit-mask hand analysis.

Hongque has 126 unique tiles, but only a few thousand legal groups.  Building
that group universe once is substantially cheaper than enumerating every
subset of every hand for each draw, discard, claim prompt, and tips snapshot.
"""
from __future__ import annotations

from functools import lru_cache
from itertools import combinations
from typing import Iterable, Iterator

from .tile import HongqueTile, full_deck


DECK: tuple[str, ...] = tuple(full_deck())
TILE_INDEX = {code: index for index, code in enumerate(DECK)}
FULL_DECK_MASK = (1 << len(DECK)) - 1


def _tile_mask(colour: int, number: int) -> int:
    return 1 << TILE_INDEX[HongqueTile(colour, number).code]


def _generate_group_masks() -> tuple[int, ...]:
    """Generate every shape accepted by ``rules.classify_meld``.

    Triplets use a cyclic colour step of one or two.  Sequences use a signed
    number step from one to four and a colour step of zero, one, or two in the
    same ordered direction.  Duplicates arise from cyclic starting points and
    are removed by the set.
    """
    groups: set[int] = set()

    for number in range(1, 10):
        for colour_step, max_length in ((1, 14), (2, 7)):
            for length in range(3, max_length + 1):
                for start_colour in range(14):
                    mask = 0
                    for offset in range(length):
                        mask |= _tile_mask(
                            (start_colour + offset * colour_step) % 14,
                            number,
                        )
                    groups.add(mask)

    for number_step in (-4, -3, -2, -1, 1, 2, 3, 4):
        for start_number in range(1, 10):
            numbers: list[int] = []
            number = start_number
            while 1 <= number <= 9:
                numbers.append(number)
                number += number_step
            for length in range(3, len(numbers) + 1):
                for colour_step in (0, 1, 2):
                    for start_colour in range(14):
                        mask = 0
                        for offset, sequence_number in enumerate(numbers[:length]):
                            mask |= _tile_mask(
                                (start_colour + offset * colour_step) % 14,
                                sequence_number,
                            )
                        groups.add(mask)

    return tuple(sorted(groups, key=lambda mask: (mask.bit_count(), mask)))


GROUP_MASKS = _generate_group_masks()
_groups_by_tile: list[list[int]] = [[] for _ in DECK]
for _group_mask in GROUP_MASKS:
    _remaining = _group_mask
    while _remaining:
        _bit = _remaining & -_remaining
        _groups_by_tile[_bit.bit_length() - 1].append(_group_mask)
        _remaining ^= _bit
GROUPS_BY_TILE: tuple[tuple[int, ...], ...] = tuple(
    tuple(groups) for groups in _groups_by_tile
)
NUMBER_MASKS: tuple[int, ...] = tuple(
    sum(_tile_mask(colour, number) for colour in range(14))
    for number in range(1, 10)
)


def mask_from_codes(codes: Iterable[str]) -> int:
    mask = 0
    for source in codes:
        code = HongqueTile.parse(source).code
        bit = 1 << TILE_INDEX[code]
        if mask & bit:
            raise ValueError("Hongque tiles are unique")
        mask |= bit
    return mask


def codes_from_mask(mask: int) -> tuple[str, ...]:
    return tuple(code for index, code in enumerate(DECK) if mask & (1 << index))


def group_masks_containing(code: str) -> tuple[int, ...]:
    normalized = HongqueTile.parse(code).code
    return GROUPS_BY_TILE[TILE_INDEX[normalized]]


@lru_cache(maxsize=32768)
def can_partition_mask(mask: int) -> bool:
    if mask == 0:
        return True
    anchor = mask & -mask
    for group_mask in GROUPS_BY_TILE[anchor.bit_length() - 1]:
        if group_mask & mask == group_mask and can_partition_mask(mask ^ group_mask):
            return True
    return False


@lru_cache(maxsize=4096)
def partition_masks(mask: int) -> tuple[tuple[int, ...], ...]:
    if mask == 0:
        return ((),)
    anchor = mask & -mask
    results: list[tuple[int, ...]] = []
    for group_mask in GROUPS_BY_TILE[anchor.bit_length() - 1]:
        if group_mask & mask != group_mask:
            continue
        for tail in partition_masks(mask ^ group_mask):
            results.append((group_mask,) + tail)
    return tuple(results)


def partitions_from_codes(codes: Iterable[str]) -> list[list[list[str]]]:
    try:
        mask = mask_from_codes(codes)
    except ValueError:
        return []
    return [
        [list(codes_from_mask(group_mask)) for group_mask in partition]
        for partition in partition_masks(mask)
    ]


def _same_number_pair_masks(mask: int) -> Iterator[int]:
    for number_mask in NUMBER_MASKS:
        number_tiles = mask & number_mask
        bits: list[int] = []
        while number_tiles:
            bit = number_tiles & -number_tiles
            bits.append(bit)
            number_tiles ^= bit
        for left, right in combinations(bits, 2):
            yield left | right


def is_winning_mask(mask: int, *, has_open_group: bool) -> bool:
    if can_partition_mask(mask) and (mask != 0 or has_open_group):
        return True
    for pair_mask in _same_number_pair_masks(mask):
        remainder = mask ^ pair_mask
        if can_partition_mask(remainder) and (remainder != 0 or has_open_group):
            return True
    return False


def waiting_mask(hand_mask: int, *, used_mask: int, has_open_group: bool) -> int:
    """Return exact tiles which complete ``hand_mask`` in one draw.

    The completed tile either belongs to one indexed legal group or is the
    second tile of the optional same-number head used by this prototype.
    """
    available_mask = FULL_DECK_MASK & ~used_mask

    @lru_cache(maxsize=None)
    def accepts_group_remainder(mask: int) -> bool:
        if can_partition_mask(mask):
            return True
        return any(can_partition_mask(mask ^ pair) for pair in _same_number_pair_masks(mask))

    waits = 0
    for group_mask in GROUP_MASKS:
        overlap = group_mask & hand_mask
        if overlap.bit_count() != group_mask.bit_count() - 1:
            continue
        if overlap.bit_count() < 2:
            continue
        if accepts_group_remainder(hand_mask ^ overlap):
            waits |= group_mask ^ overlap

    # The drawn tile may instead complete the optional same-number head.  A
    # pair by itself is not a winning hand unless an exposed group already
    # exists, matching win_check's prototype extension.
    remaining = hand_mask
    while remaining:
        bit = remaining & -remaining
        remainder = hand_mask ^ bit
        if can_partition_mask(remainder) and (remainder != 0 or has_open_group):
            number = HongqueTile.parse(DECK[bit.bit_length() - 1]).number
            waits |= NUMBER_MASKS[number - 1]
        remaining ^= bit

    return waits & available_mask

