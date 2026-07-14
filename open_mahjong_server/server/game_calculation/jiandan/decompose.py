from __future__ import annotations

from collections import Counter
from dataclasses import dataclass
from typing import Iterable, Literal

from .tiles import is_honor, is_suited, sorted_tiles, validate_tiles

MeldKind = Literal["sequence", "triplet", "kong", "pair"]


@dataclass(frozen=True)
class Meld:
    kind: MeldKind
    tiles: tuple[int, ...]
    concealed: bool = True
    declared: bool = False
    added: bool = False
    robbed: bool = False

    @property
    def head(self) -> int:
        return self.tiles[0]

    @property
    def is_set(self) -> bool:
        return self.kind in {"sequence", "triplet", "kong"}

    @property
    def is_triplet_like(self) -> bool:
        return self.kind in {"triplet", "kong"}


@dataclass(frozen=True)
class Decomposition:
    melds: tuple[Meld, ...]
    pair: Meld
    special: str | None = None

    @property
    def all_units(self) -> tuple[Meld, ...]:
        return self.melds + (self.pair,)


def parse_meld(code: str) -> Meld:
    marker = code[0]
    tile = int(code[1:])
    if marker in {"s", "S"}:
        return Meld("sequence", (tile, tile + 1, tile + 2), concealed=marker.isupper(), declared=True)
    if marker in {"k", "K"}:
        return Meld("triplet", (tile, tile, tile), concealed=marker.isupper(), declared=True)
    if marker in {"g", "G"}:
        return Meld("kong", (tile, tile, tile, tile), concealed=marker.isupper(), declared=True)
    if marker == "q":
        return Meld("pair", (tile, tile), concealed=True, declared=False)
    raise ValueError(f"Unknown meld code: {code}")


def parse_melds(codes: Iterable[str]) -> tuple[Meld, ...]:
    return tuple(parse_meld(code) for code in codes)


def is_seven_pairs(tiles: Iterable[int]) -> bool:
    tiles = list(tiles)
    if len(tiles) != 14:
        return False
    validate_tiles(tiles)
    return sum(count // 2 for count in Counter(tiles).values()) == 7


def is_thirteen_orphans(tiles: Iterable[int]) -> bool:
    from .tiles import ORPHANS

    tiles = list(tiles)
    if len(tiles) != 14:
        return False
    validate_tiles(tiles)
    counts = Counter(tiles)
    return set(counts) == set(ORPHANS) and sorted(counts.values()) == [1] * 12 + [2]


def find_standard_decompositions(
    concealed_tiles: Iterable[int],
    exposed_melds: Iterable[Meld] = (),
) -> list[Decomposition]:
    concealed = sorted_tiles(concealed_tiles)
    validate_tiles(concealed)
    exposed = tuple(exposed_melds)
    needed_melds = 4 - sum(1 for meld in exposed if meld.is_set)
    if needed_melds < 0:
        return []
    if len(concealed) != needed_melds * 3 + 2:
        return []

    counts = Counter(concealed)
    results: list[Decomposition] = []

    for pair_tile in sorted(counts):
        if counts[pair_tile] < 2:
            continue
        if counts[pair_tile] == 4:
            # Undeclared four identical concealed tiles cannot be used as the
            # standard-hand pair under this rule set.
            continue
        counts[pair_tile] -= 2
        pair = Meld("pair", (pair_tile, pair_tile), concealed=True)
        for melds in _find_melds(counts, needed_melds):
            results.append(Decomposition(tuple(exposed + tuple(melds)), pair))
        counts[pair_tile] += 2

    return results


def _find_melds(counts: Counter[int], target_count: int) -> list[list[Meld]]:
    if target_count == 0:
        return [[]] if all(count == 0 for count in counts.values()) else []

    first = next((tile for tile in sorted(counts) if counts[tile] > 0), None)
    if first is None:
        return []

    results: list[list[Meld]] = []

    if counts[first] >= 3 and counts[first] != 4:
        counts[first] -= 3
        for tail in _find_melds(counts, target_count - 1):
            results.append([Meld("triplet", (first, first, first), concealed=True)] + tail)
        counts[first] += 3

    if is_suited(first):
        second = first + 1
        third = first + 2
        if (
            not is_honor(first)
            and first % 10 <= 7
            and counts[second] > 0
            and counts[third] > 0
        ):
            counts[first] -= 1
            counts[second] -= 1
            counts[third] -= 1
            for tail in _find_melds(counts, target_count - 1):
                results.append([Meld("sequence", (first, second, third), concealed=True)] + tail)
            counts[first] += 1
            counts[second] += 1
            counts[third] += 1

    return results
