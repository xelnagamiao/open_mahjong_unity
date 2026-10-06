"""Small canonical cache keys for validated ordinary-mahjong tile counters."""
from collections import Counter
from typing import Mapping


def pack_counter(counter: Mapping[int, int]) -> bytes:
    """Sorted (tile, count) byte pairs; omit zero counts left by subtraction.

    Internal callers validate tile IDs and the four-copy limit before encoding.
    Unlike nested tuples, this representation retains no per-tile tuple objects.
    """
    return bytes(value for pair in sorted(
        (tile, count) for tile, count in counter.items() if count
    ) for value in pair)


def unpack_counter(key: bytes) -> Counter[int]:
    return Counter(dict(zip(key[::2], key[1::2])))
