from __future__ import annotations

from dataclasses import dataclass, field

from .decompose import (
    Decomposition,
    Meld,
    find_standard_decompositions,
    is_seven_pairs,
    is_thirteen_orphans,
    parse_melds,
)
from .fan_definitions import FANS, fan_name
from .fan_detector import DetectionContext, FanOccurrence, detect_fans
from .tiles import sorted_tiles, validate_tiles


@dataclass(frozen=True)
class HandContext:
    hand_tiles: tuple[int, ...] | list[int]
    meld_codes: tuple[str, ...] | list[str] = field(default_factory=tuple)
    winning_tile: int | None = None
    win_source: str = "self_draw"
    pre_win_tiles: tuple[int, ...] | list[int] | None = None
    heavenly_win: bool = False
    earthly_win: bool = False
    haitei: bool = False
    houtei: bool = False
    rinshan: bool = False
    chankan: bool = False


@dataclass(frozen=True)
class ScoreResult:
    is_win: bool
    points: int
    fan_ids: tuple[str, ...]
    fan_names: tuple[str, ...]
    raw_points: int
    decomposition: Decomposition | None


def score_hand(ctx: HandContext) -> ScoreResult:
    concealed_tiles = tuple(sorted_tiles(ctx.hand_tiles))
    validate_tiles(concealed_tiles)
    melds = parse_melds(ctx.meld_codes)
    meld_tiles = tuple(tile for meld in melds for tile in meld.tiles)
    final_tiles = tuple(sorted_tiles(concealed_tiles + meld_tiles))

    candidates: list[tuple[list[FanOccurrence], Decomposition | None]] = []

    base_ctx = DetectionContext(
        final_tiles=final_tiles,
        concealed_tiles=concealed_tiles,
        decomposition=None,
        melds=melds,
        winning_tile=ctx.winning_tile,
        win_source=ctx.win_source,
        pre_win_tiles=tuple(ctx.pre_win_tiles) if ctx.pre_win_tiles else None,
        heavenly_win=ctx.heavenly_win,
        earthly_win=ctx.earthly_win,
        haitei=ctx.haitei,
        houtei=ctx.houtei,
        rinshan=ctx.rinshan,
        chankan=ctx.chankan,
    )
    special_occ = detect_fans(base_ctx)
    special_shape_ids = {"seven_pairs", "thirteen_orphans", "nine_gates"}
    if any(occ.fan_id in special_shape_ids for occ in special_occ):
        candidates.append((special_occ, None))

    for decomp in find_standard_decompositions(concealed_tiles, melds):
        dctx = DetectionContext(
            final_tiles=final_tiles,
            concealed_tiles=concealed_tiles,
            decomposition=decomp,
            melds=melds + tuple(m for m in decomp.melds if not m.declared),
            winning_tile=ctx.winning_tile,
            win_source=ctx.win_source,
            pre_win_tiles=tuple(ctx.pre_win_tiles) if ctx.pre_win_tiles else None,
            heavenly_win=ctx.heavenly_win,
            earthly_win=ctx.earthly_win,
            haitei=ctx.haitei,
            houtei=ctx.houtei,
            rinshan=ctx.rinshan,
            chankan=ctx.chankan,
            include_special_shapes=False,
        )
        candidates.append((detect_fans(dctx), decomp))

    if not candidates and (is_seven_pairs(final_tiles) or is_thirteen_orphans(final_tiles)):
        candidates.append((special_occ, None))

    if not candidates:
        return ScoreResult(False, 0, (), (), 0, None)

    best_occ, best_decomp = max(candidates, key=lambda item: _total_points(item[0]))
    kept = _apply_row_rules(best_occ)
    raw = sum(occ.value for occ in kept)
    capped = min(raw, 20)
    fan_ids = tuple(_expand_ids(kept))
    return ScoreResult(
        is_win=True,
        points=capped,
        fan_ids=fan_ids,
        fan_names=tuple(fan_name(fan_id) for fan_id in fan_ids),
        raw_points=raw,
        decomposition=best_decomp,
    )


def _total_points(occurrences: list[FanOccurrence]) -> int:
    return sum(occ.value for occ in _apply_row_rules(occurrences))


def _apply_row_rules(occurrences: list[FanOccurrence]) -> list[FanOccurrence]:
    by_row: dict[str, list[FanOccurrence]] = {}
    for occ in occurrences:
        definition = FANS[occ.fan_id]
        by_row.setdefault(definition.row, []).append(occ)

    kept: list[FanOccurrence] = []
    for row, row_occ in by_row.items():
        # The three-suit-number row is evaluated from resolved meld units.
        # Its detector emits no overlapping low/high occurrence for the same
        # number, so occurrences that remain here consume disjoint units and
        # may score together (for example 小三色同刻 at 2 plus 二色同刻 at 3).
        if row == "three_suit_number":
            kept.extend(row_occ)
            continue
        non_repeatable = [occ for occ in row_occ if not FANS[occ.fan_id].repeatable]
        if non_repeatable:
            kept.append(max(non_repeatable, key=lambda occ: (FANS[occ.fan_id].value, FANS[occ.fan_id].name)))
        else:
            kept.extend(row_occ)
    return kept


def _expand_ids(occurrences: list[FanOccurrence]) -> list[str]:
    ids: list[str] = []
    for occ in sorted(occurrences, key=lambda item: (-FANS[item.fan_id].value, FANS[item.fan_id].row, item.fan_id)):
        ids.extend([occ.fan_id] * occ.count)
    return ids
