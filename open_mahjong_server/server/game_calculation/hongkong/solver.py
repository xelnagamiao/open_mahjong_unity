"""Exhaustive structural solver for four/five melds and profile-specific shapes.

The physical four-copy limit includes exposed kongs. A discarded winning tile
is assigned to every possible concealed component before scoring; this avoids
silently awarding concealed-pung points on an exposed winning pung.
"""

from collections import Counter
from functools import lru_cache
from itertools import permutations

from ..compact_counter import pack_counter as _key, unpack_counter
from .models import HongKongRules, Meld, Shape, TILES, NUMBERS, HONORS, ORPHANS


def parse_meld(code: str) -> Meld:
    if not isinstance(code, str) or len(code) < 3 or code[0] not in "skgG":
        raise ValueError(f"Invalid meld: {code!r}")
    try:
        tile = int(code[1:])
    except ValueError as exc:
        raise ValueError(f"Invalid meld: {code!r}") from exc
    if code[0] == "s":
        if tile not in NUMBERS or not 2 <= tile % 10 <= 8:
            raise ValueError(f"Invalid chow: {code!r}")
        return Meld("sequence", tile, True, False)
    if tile not in TILES:
        raise ValueError(f"Invalid meld tile: {code!r}")
    return Meld("triplet" if code[0] == "k" else "kong", tile, True, code[0] == "G")


@lru_cache(maxsize=65536)
def _partitions(key: bytes, count: int):
    tiles = unpack_counter(key)
    if not tiles:
        return ((),) if count == 0 else ()
    if count <= 0 or sum(tiles.values()) != count * 3:
        return ()
    tile = min(tiles)
    results = []
    choices = []
    if tiles[tile] >= 3:
        choices.append(Meld("triplet", tile))
    if tile in NUMBERS and tile % 10 <= 7 and tiles[tile + 1] and tiles[tile + 2]:
        choices.append(Meld("sequence", tile + 1))
    for meld in choices:
        rest = tiles.copy()
        rest.subtract(meld.tiles)
        results.extend((meld,) + tail for tail in _partitions(_key(rest), count - 1))
    return tuple(results)


def _valid(hand, codes, rules, complete):
    try:
        melds = tuple(parse_meld(code) for code in codes)
    except (ValueError, TypeError):
        return None
    if len(melds) > rules.structure.meld_count:
        return None
    if len(hand) != rules.structure.concealed_tile_count(len(melds), complete=complete):
        return None
    if any(type(t) is not int or t not in TILES for t in hand):
        return None
    counter = Counter(hand)
    counter.update(t for meld in melds for t in meld.tiles)
    if any(c > 4 for c in counter.values()):
        return None
    return melds


def _orphans(counter):
    return (sum(counter.values()) == 14 and set(counter) == ORPHANS
            and sorted(counter.values()) == [1] * 12 + [2])


def _special_shapes(hand, external, rules):
    counter = Counter(hand)
    if rules.is_sixteen:
        # The extra seventeenth-tile meld may be a declared kong: the book's
        # concealed-hand definition allows kongs, but not chows or pungs.
        if len(external) <= 1 and all(m.kind == "kong" for m in external):
            if external and _orphans(counter):
                yield Shape("orphans", melds=external)
            elif not external:
                possibilities = [Meld("triplet", t) for t, c in counter.items() if c >= 3]
                possibilities += [Meld("sequence", t + 1) for t in NUMBERS
                                  if t % 10 <= 7 and all(counter[x] for x in (t, t + 1, t + 2))]
                for meld in possibilities:
                    rest = counter.copy()
                    rest.subtract(meld.tiles)
                    rest = +rest
                    if _orphans(rest):
                        yield Shape("orphans", melds=(meld,))
        if not external:
            if sorted(c % 2 for c in counter.values()).count(1) == 1 and 3 in counter.values():
                yield Shape("likugu")
            if len(counter) == 16 and all(counter[t] for t in HONORS):
                suits = [sorted(t % 10 for t in counter if t // 10 == s) for s in (1, 2, 3)]
                if all(len(s) == 3 and s[1] - s[0] >= 3 and s[2] - s[1] >= 3 for s in suits):
                    yield Shape("sixteen_unconnected")
    elif not external:
        if _orphans(counter):
            yield Shape("orphans")
        if (rules.is_gametower or rules.is_remix) and all(c % 2 == 0 for c in counter.values()):
            yield Shape("seven_pairs")
        if rules.is_remix and len(counter) == 14:
            for ranks in permutations(((1,4,7),(2,5,8),(3,6,9))):
                knitted = {s*10+n for s, numbers in enumerate(ranks,1) for n in numbers}
                if set(counter) <= knitted | set(HONORS) and (knitted <= set(counter) or set(HONORS) <= set(counter)):
                    yield Shape("knitted"); break


@lru_cache(maxsize=32768)
def _decompositions(hand, codes, rules, winning_tile):
    external = _valid(hand, codes, rules, True)
    if external is None or (winning_tile and winning_tile not in hand):
        return ()
    result = list(_special_shapes(hand, external, rules))
    counts = Counter(hand)
    for pair in sorted(t for t, c in counts.items() if c >= 2):
        rest = counts.copy()
        rest[pair] -= 2
        for partition in _partitions(_key(rest), rules.structure.meld_count - len(external)):
            melds = external + partition
            if not winning_tile or pair == winning_tile:
                result.append(Shape("standard", pair, melds, -1))
            if winning_tile:
                for i, meld in enumerate(melds):
                    if not meld.external and winning_tile in meld.tiles:
                        result.append(Shape("standard", pair, melds, i))
    return tuple(dict.fromkeys(result))


def decompositions(hand, codes=(), rules=None, winning_tile=0):
    return _decompositions(tuple(sorted(hand)), tuple(codes), rules or HongKongRules(), winning_tile)


@lru_cache(maxsize=32768)
def _waits(hand, codes, rules):
    external = _valid(hand, codes, rules, False)
    if external is None:
        return frozenset()
    counts = Counter(hand)
    counts.update(t for m in external for t in m.tiles)
    return frozenset(t for t in TILES if counts[t] < 4 and decompositions(hand + (t,), codes, rules, t))


def structural_waits(hand, codes=(), rules=None):
    return set(_waits(tuple(sorted(hand)), tuple(codes), rules or HongKongRules()))
