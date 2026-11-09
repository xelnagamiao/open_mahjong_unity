from __future__ import annotations

from collections import Counter
from typing import Iterable


MAN = range(11, 20)
PIN = range(21, 30)
SOU = range(31, 40)
WINDS = (41, 42, 43, 44)
DRAGONS = (45, 46, 47)
HONORS = WINDS + DRAGONS
TERMINALS = (11, 19, 21, 29, 31, 39)
ORPHANS = TERMINALS + HONORS
ALL_TILES = tuple(MAN) + tuple(PIN) + tuple(SOU) + HONORS


def suit(tile: int) -> int:
    return tile // 10


def number(tile: int) -> int:
    return tile % 10


def is_suited(tile: int) -> bool:
    return 1 <= suit(tile) <= 3 and 1 <= number(tile) <= 9


def is_honor(tile: int) -> bool:
    return tile in HONORS


def is_wind(tile: int) -> bool:
    return tile in WINDS


def is_dragon(tile: int) -> bool:
    return tile in DRAGONS


def is_terminal(tile: int) -> bool:
    return tile in TERMINALS


def is_simple(tile: int) -> bool:
    return is_suited(tile) and not is_terminal(tile)


def tile_sort_key(tile: int) -> tuple[int, int]:
    return (suit(tile), number(tile))


def sorted_tiles(tiles: Iterable[int]) -> list[int]:
    return sorted(tiles, key=tile_sort_key)


def validate_tiles(tiles: Iterable[int]) -> None:
    counts = Counter(tiles)
    unknown = [tile for tile in counts if tile not in ALL_TILES]
    if unknown:
        raise ValueError(f"Unknown tile id(s): {unknown}")
    too_many = {tile: count for tile, count in counts.items() if count > 4}
    if too_many:
        raise ValueError(f"Tile count exceeds four copies: {too_many}")
