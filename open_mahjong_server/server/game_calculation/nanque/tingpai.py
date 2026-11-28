from __future__ import annotations

from collections import Counter
from typing import Any

from ..standard_solver import standard_waiting_tiles
from .decompose import find_standard_decompositions, is_seven_pairs, is_thirteen_orphans, parse_melds
from .tiles import ALL_TILES, validate_tiles

_ORPHANS: frozenset[int] = frozenset(
    (11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47)
)


def tingpai_check(
    hand_tiles: list[int] | tuple[int, ...],
    meld_codes: list[str] | tuple[str, ...] | None = None,
    context: dict[str, Any] | None = None,
) -> set[int]:
    """Return tiles that would complete the current Nanque hand.

    In Nanque, ``score_hand`` marks a hand as winning iff it matches a standard
    4-meld+1-pair shape, seven pairs, or thirteen orphans (with physical 4-copy
    limits). Computing structural waits directly avoids 34 full scoring passes.
    """
    meld_codes = meld_codes or []
    concealed = list(hand_tiles)
    validate_tiles(concealed)

    melds = parse_melds(meld_codes)
    if len(melds) > 4 or len(concealed) != (4 - len(melds)) * 3 + 1:
        return set()

    known_counts = Counter(concealed)
    for meld in melds:
        known_counts.update(meld.tiles)
    if any(c > 4 for c in known_counts.values()):
        return set()

    waits: set[int] = standard_waiting_tiles(concealed, allow_fifth_copy=False)

    if not melds and len(concealed) == 13:
        # Seven pairs (quad counts as 2 pairs in Nanque's is_seven_pairs).
        odds = [t for t, c in known_counts.items() if c % 2 == 1]
        if len(odds) == 1:
            waits.add(odds[0])
        # Thirteen orphans.
        if all(t in _ORPHANS for t in known_counts):
            if len(known_counts) == 13:
                waits.update(_ORPHANS)
            elif len(known_counts) == 12:
                waits.update(_ORPHANS - set(known_counts))

    out: set[int] = set()
    concealed_counts = Counter(concealed)
    has_existing_quad = any(c == 4 for c in concealed_counts.values())
    for t in waits:
        if known_counts[t] >= 4:
            continue
        if has_existing_quad or concealed_counts[t] == 3:
            cand = tuple(sorted(concealed + [t]))
            if (not melds and (is_seven_pairs(cand) or is_thirteen_orphans(cand))) or find_standard_decompositions(cand, melds):
                out.add(t)
        else:
            out.add(t)
    return out
