from __future__ import annotations

from collections import Counter
from typing import Any

from .decompose import parse_melds
from .scoring import HandContext, score_hand
from .tiles import ALL_TILES, validate_tiles


def tingpai_check(
    hand_tiles: list[int] | tuple[int, ...],
    meld_codes: list[str] | tuple[str, ...] | None = None,
    context: dict[str, Any] | None = None,
) -> set[int]:
    """Return tiles that would complete the current Jiandan hand.

    This intentionally uses the scoring engine as the source of truth. It is a
    small brute-force checker over the 34 tile ids, which is fast enough for
    backend smoke tests and keeps edge-case behavior aligned with `score_hand`.
    """
    meld_codes = meld_codes or []
    context = context or {}
    concealed = list(hand_tiles)
    validate_tiles(concealed)

    melds = parse_melds(meld_codes)
    known_counts = Counter(concealed)
    for meld in melds:
        known_counts.update(meld.tiles)

    waits: set[int] = set()
    for tile in ALL_TILES:
        if known_counts[tile] >= 4:
            continue
        candidate_hand = concealed + [tile]
        try:
            result = score_hand(
                HandContext(
                    hand_tiles=candidate_hand,
                    meld_codes=list(meld_codes),
                    winning_tile=tile,
                    win_source=context.get("win_source", "self_draw"),
                    pre_win_tiles=context.get("pre_win_tiles"),
                    heavenly_win=bool(context.get("heavenly_win")),
                    earthly_win=bool(context.get("earthly_win")),
                    haitei=bool(context.get("haitei")),
                    houtei=bool(context.get("houtei")),
                    rinshan=bool(context.get("rinshan")),
                    chankan=bool(context.get("chankan")),
                )
            )
        except ValueError:
            continue
        if result.is_win:
            waits.add(tile)
    return waits
