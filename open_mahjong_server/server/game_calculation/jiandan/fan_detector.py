from __future__ import annotations

from collections import Counter, defaultdict
from dataclasses import dataclass
from typing import Iterable

from .decompose import Decomposition, Meld, is_seven_pairs, is_thirteen_orphans
from .fan_definitions import FANS
from .tiles import (
    DRAGONS,
    HONORS,
    ORPHANS,
    TERMINALS,
    is_dragon,
    is_honor,
    is_simple,
    is_suited,
    is_terminal,
    is_wind,
    number,
    suit,
)


@dataclass(frozen=True)
class FanOccurrence:
    fan_id: str
    count: int = 1

    @property
    def value(self) -> int:
        return FANS[self.fan_id].value * self.count


@dataclass(frozen=True)
class DetectionContext:
    final_tiles: tuple[int, ...]
    concealed_tiles: tuple[int, ...]
    decomposition: Decomposition | None
    melds: tuple[Meld, ...]
    winning_tile: int | None = None
    win_source: str = "self_draw"  # self_draw, discard, rob_kong
    pre_win_tiles: tuple[int, ...] | None = None
    is_dealer: bool = False
    heavenly_win: bool = False
    earthly_win: bool = False
    haitei: bool = False
    houtei: bool = False
    rinshan: bool = False
    chankan: bool = False
    include_special_shapes: bool = True

    @property
    def is_closed(self) -> bool:
        return all((not meld.declared) or (meld.kind == "kong" and meld.concealed) for meld in self.melds)

    @property
    def finalized_kong_count(self) -> int:
        return sum(1 for meld in self.melds if meld.kind == "kong" and meld.declared and not meld.robbed)


def detect_fans(ctx: DetectionContext) -> list[FanOccurrence]:
    occurrences: list[FanOccurrence] = []
    if ctx.include_special_shapes:
        occurrences.extend(_detect_special_shape(ctx))
    occurrences.extend(_detect_composition(ctx))
    occurrences.extend(_detect_context_fans(ctx))

    if ctx.decomposition is not None:
        occurrences.extend(_detect_standard_shape(ctx))
        occurrences.extend(_detect_winds(ctx.decomposition))
        occurrences.extend(_detect_dragons(ctx.decomposition))
        occurrences.extend(_detect_terminal_honor_standard(ctx.decomposition, ctx.final_tiles))
        occurrences.extend(_detect_three_suit_number(ctx.decomposition))
        occurrences.extend(_detect_concealed_triplets(ctx))
        occurrences.extend(_detect_consecutive_triplets(ctx.decomposition))
        occurrences.extend(_detect_identical_sequences(ctx.decomposition))

    occurrences.extend(_detect_kong_count(ctx))
    return occurrences


def _detect_special_shape(ctx: DetectionContext) -> list[FanOccurrence]:
    occ: list[FanOccurrence] = []
    if is_seven_pairs(ctx.final_tiles):
        occ.append(FanOccurrence("seven_pairs"))
    if is_thirteen_orphans(ctx.final_tiles):
        occ.append(FanOccurrence("thirteen_orphans"))
    if _is_nine_gates(ctx):
        occ.append(FanOccurrence("nine_gates"))
    return occ


def _detect_standard_shape(ctx: DetectionContext) -> list[FanOccurrence]:
    melds = ctx.decomposition.melds
    if all(meld.kind == "sequence" for meld in melds):
        return [FanOccurrence("pinfu")]
    if all(meld.is_triplet_like for meld in melds):
        return [FanOccurrence("all_triplets")]
    return []


def _detect_composition(ctx: DetectionContext) -> list[FanOccurrence]:
    tiles = ctx.final_tiles
    occ: list[FanOccurrence] = []

    if all(is_simple(tile) for tile in tiles):
        occ.append(FanOccurrence("all_simples"))

    all_terminal_or_honor = all(is_terminal(tile) or is_honor(tile) for tile in tiles)
    has_terminal = any(is_terminal(tile) for tile in tiles)
    has_honor = any(is_honor(tile) for tile in tiles)
    if all_terminal_or_honor and has_terminal and has_honor and not is_thirteen_orphans(tiles):
        occ.append(FanOccurrence("mixed_terminals"))
    if all(tile in TERMINALS for tile in tiles):
        occ.append(FanOccurrence("pure_terminals"))

    suited_suits = {suit(tile) for tile in tiles if is_suited(tile)}
    if len(suited_suits) == 1 and has_honor:
        occ.append(FanOccurrence("half_flush"))
    if len(suited_suits) == 1 and not has_honor:
        occ.append(FanOccurrence("full_flush"))
    if all(is_honor(tile) for tile in tiles):
        occ.append(FanOccurrence("all_honors"))

    return occ


def _detect_context_fans(ctx: DetectionContext) -> list[FanOccurrence]:
    occ: list[FanOccurrence] = []
    if ctx.is_closed:
        occ.append(FanOccurrence("closed_hand"))
    if ctx.heavenly_win:
        occ.append(FanOccurrence("heavenly_win"))
    if ctx.earthly_win:
        occ.append(FanOccurrence("earthly_win"))
    if ctx.haitei:
        occ.append(FanOccurrence("haitei"))
    if ctx.houtei:
        occ.append(FanOccurrence("houtei"))
    if ctx.rinshan:
        occ.append(FanOccurrence("rinshan"))
    if ctx.chankan:
        occ.append(FanOccurrence("chankan"))
    return occ


def _detect_winds(decomp: Decomposition) -> list[FanOccurrence]:
    wind_sets = {meld.head for meld in decomp.melds if meld.is_triplet_like and is_wind(meld.head)}
    wind_pair = decomp.pair.head if is_wind(decomp.pair.head) else None
    if len(wind_sets) == 4:
        return [FanOccurrence("big_four_winds")]
    if len(wind_sets) == 3 and wind_pair and wind_pair not in wind_sets:
        return [FanOccurrence("small_four_winds")]
    if len(wind_sets) == 3:
        return [FanOccurrence("big_three_winds")]
    if len(wind_sets) == 2 and wind_pair and wind_pair not in wind_sets:
        return [FanOccurrence("small_three_winds")]
    if len(wind_sets) == 2:
        return [FanOccurrence("two_wind_triplets")]
    return []


def _detect_dragons(decomp: Decomposition) -> list[FanOccurrence]:
    dragon_sets = {meld.head for meld in decomp.melds if meld.is_triplet_like and is_dragon(meld.head)}
    dragon_pair = decomp.pair.head if is_dragon(decomp.pair.head) else None
    if len(dragon_sets) == 3:
        return [FanOccurrence("big_three_dragons")]
    if len(dragon_sets) == 2 and dragon_pair and dragon_pair not in dragon_sets:
        return [FanOccurrence("small_three_dragons")]
    if len(dragon_sets) == 2:
        return [FanOccurrence("two_dragon_triplets")]
    if len(dragon_sets) == 1:
        tile = next(iter(dragon_sets))
        return [FanOccurrence({45: "red_dragon", 46: "white_dragon", 47: "green_dragon"}[tile])]
    return []


def _detect_terminal_honor_standard(decomp: Decomposition, final_tiles: tuple[int, ...]) -> list[FanOccurrence]:
    occ: list[FanOccurrence] = []
    melds = decomp.melds

    for s in (1, 2, 3):
        seqs = {meld.tiles for meld in melds if meld.kind == "sequence" and suit(meld.head) == s}
        if {(s * 10 + 1, s * 10 + 2, s * 10 + 3), (s * 10 + 4, s * 10 + 5, s * 10 + 6), (s * 10 + 7, s * 10 + 8, s * 10 + 9)} <= seqs:
            occ.append(FanOccurrence("full_straight"))

    def unit_has_terminal_or_honor(unit: Meld) -> bool:
        return any(is_terminal(tile) or is_honor(tile) for tile in unit.tiles)

    if all(unit_has_terminal_or_honor(unit) for unit in decomp.all_units):
        has_sequence = any(meld.kind == "sequence" for meld in melds)
        has_honor = any(is_honor(tile) for tile in final_tiles)
        if has_sequence and has_honor:
            occ.append(FanOccurrence("mixed_outside_hand"))
        if has_sequence and not has_honor:
            occ.append(FanOccurrence("pure_outside_hand"))

    return occ


def _detect_three_suit_number(decomp: Decomposition) -> list[FanOccurrence]:
    occ: list[FanOccurrence] = []
    triplet_numbers: dict[int, set[int]] = defaultdict(set)
    pair_numbers: dict[int, set[int]] = defaultdict(set)
    sequence_starts: dict[int, set[int]] = defaultdict(set)

    for meld in decomp.melds:
        if not is_suited(meld.head):
            continue
        if meld.is_triplet_like:
            triplet_numbers[number(meld.head)].add(suit(meld.head))
        if meld.kind == "sequence":
            sequence_starts[number(meld.head)].add(suit(meld.head))
    if is_suited(decomp.pair.head):
        pair_numbers[number(decomp.pair.head)].add(suit(decomp.pair.head))

    for num, suits in triplet_numbers.items():
        if len(suits) == 3:
            occ.append(FanOccurrence("three_suit_triplets"))
        elif len(suits) == 2:
            missing = ({1, 2, 3} - suits).pop()
            if missing in pair_numbers.get(num, set()):
                occ.append(FanOccurrence("small_three_suit_triplets"))
            else:
                occ.append(FanOccurrence("two_suit_triplets"))

    for suits in sequence_starts.values():
        if len(suits) == 3:
            occ.append(FanOccurrence("mixed_triple_chow"))

    return occ


def _detect_concealed_triplets(ctx: DetectionContext) -> list[FanOccurrence]:
    assert ctx.decomposition is not None
    count = 0
    for meld in ctx.decomposition.melds:
        if not meld.is_triplet_like or not meld.concealed:
            continue
        if ctx.win_source in {"discard", "rob_kong"} and ctx.winning_tile in meld.tiles:
            # This conservative first version treats a discard/robbed tile that
            # belongs to a triplet as completing that triplet.
            continue
        count += 1
    if count >= 4:
        return [FanOccurrence("four_concealed_triplets")]
    if count == 3:
        return [FanOccurrence("three_concealed_triplets")]
    if count == 2:
        return [FanOccurrence("two_concealed_triplets")]
    return []


def _detect_consecutive_triplets(decomp: Decomposition) -> list[FanOccurrence]:
    occ: list[FanOccurrence] = []
    by_suit: dict[int, list[int]] = defaultdict(list)
    for meld in decomp.melds:
        if meld.is_triplet_like and is_suited(meld.head):
            by_suit[suit(meld.head)].append(number(meld.head))

    for nums in by_suit.values():
        nums = sorted(set(nums))
        chains: list[list[int]] = []
        current: list[int] = []
        for num in nums:
            if not current or num == current[-1] + 1:
                current.append(num)
            else:
                chains.append(current)
                current = [num]
        if current:
            chains.append(current)

        for chain in chains:
            if len(chain) >= 4:
                occ.append(FanOccurrence("four_consecutive_triplets"))
            elif len(chain) == 3:
                occ.append(FanOccurrence("three_consecutive_triplets"))
            elif len(chain) == 2:
                occ.append(FanOccurrence("two_consecutive_triplets"))

    return occ


def _detect_identical_sequences(decomp: Decomposition) -> list[FanOccurrence]:
    groups = Counter((suit(meld.head), number(meld.head)) for meld in decomp.melds if meld.kind == "sequence")
    if any(count >= 4 for count in groups.values()):
        return [FanOccurrence("pure_quadruple_chow")]
    if any(count == 3 for count in groups.values()):
        return [FanOccurrence("pure_triple_chow")]
    pair_groups = [key for key, count in groups.items() if count >= 2]
    if len(pair_groups) >= 2:
        return [FanOccurrence("twice_pure_double_chow")]
    if len(pair_groups) == 1:
        return [FanOccurrence("pure_double_chow")]
    return []


def _detect_kong_count(ctx: DetectionContext) -> list[FanOccurrence]:
    count = ctx.finalized_kong_count
    if count >= 4:
        return [FanOccurrence("four_kongs")]
    if count == 3:
        return [FanOccurrence("three_kongs")]
    if count == 2:
        return [FanOccurrence("two_kongs")]
    if count == 1:
        return [FanOccurrence("one_kong")]
    return []


def _is_nine_gates(ctx: DetectionContext) -> bool:
    if not ctx.pre_win_tiles or not ctx.winning_tile or not ctx.is_closed:
        return False
    if len(ctx.pre_win_tiles) != 13:
        return False
    if not all(is_suited(tile) for tile in ctx.pre_win_tiles + (ctx.winning_tile,)):
        return False
    s = suit(ctx.winning_tile)
    if any(suit(tile) != s for tile in ctx.pre_win_tiles):
        return False
    counts = Counter(number(tile) for tile in ctx.pre_win_tiles)
    return [counts[i] for i in range(1, 10)] == [3, 1, 1, 1, 1, 1, 1, 1, 3]
