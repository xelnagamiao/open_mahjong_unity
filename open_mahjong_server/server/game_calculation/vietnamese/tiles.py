"""Stable physical IDs. Jokers keep their identity when assigned a face."""
from collections import Counter
from functools import lru_cache
from itertools import combinations

TILES = tuple(s * 10 + n for s in (1, 2, 3) for n in range(1, 10)) + tuple(range(41, 48))
FLOWERS = tuple(range(51, 67))
ANY, DOT, BAMBOO, CHARACTER, BIG_FLOWER, WIND, DRAGON, NUMBER = range(71, 79)
JOKERS = (ANY, DOT, BAMBOO, CHARACTER, WIND, DRAGON, NUMBER)
BONUS = FLOWERS + (BIG_FLOWER,)
BLUE = (ANY, DOT, BAMBOO, CHARACTER, NUMBER)  # Number counts as blue only for discarding.
PHYSICAL = tuple(t for t in TILES for _ in range(4)) + FLOWERS + tuple(range(71, 79))
ORPHANS = (11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47)
GROUPS = tuple(("k", (t, t, t)) for t in TILES) + tuple(("s", (s*10+n, s*10+n+1, s*10+n+2)) for s in (1,2,3) for n in range(1,8))


def represents(physical, target, *, discard_bonus=False):
    if physical == target or physical == ANY:
        return True
    if physical == DOT:
        return 21 <= target <= 29  # project IDs: characters=1, dots=2, bamboo=3
    if physical == BAMBOO:
        return 31 <= target <= 39
    if physical == CHARACTER:
        return 11 <= target <= 19
    if physical == WIND:
        return 41 <= target <= 44
    if physical == DRAGON:
        return 45 <= target <= 47
    if physical == NUMBER:
        return target < 40
    return discard_bonus and physical in BONUS


@lru_cache(maxsize=65536)
def consume(hand, targets, discard_bonus=False):
    """All distinct physical assignments for an intended group; never mutate hand."""
    if not targets:
        return ((hand, ()),)
    target, rest = targets[0], targets[1:]
    result = set()
    for physical in set(hand):
        if not represents(physical, target, discard_bonus=discard_bonus):
            continue
        remaining = list(hand)
        remaining.remove(physical)
        for left, used in consume(tuple(remaining), rest, discard_bonus):
            result.add((left, (physical,) + used))
    return tuple(sorted(result))


def fits(hand, targets, discard_bonus=False):
    """Perfect scoped-joker matching; sorted tight domains avoid factorial search."""
    if len(hand) != len(targets):
        return False
    domains = sorted((tuple(t for t in set(targets) if represents(p, t, discard_bonus=discard_bonus)) for p in hand), key=len)
    counts = Counter(targets)
    def visit(at):
        if at == len(domains):
            return True
        for t in domains[at]:
            if counts[t]:
                counts[t] -= 1
                if visit(at+1):
                    counts[t] += 1
                    return True
                counts[t] += 1
        return False
    return visit(0)


@lru_cache(maxsize=65536)
def groups(hand, left, discard_bonus=False):
    if not hand:
        return ((),) if left == 0 else ()
    if len(hand) != left*3 or left <= 0:
        return ()
    anchor = next((t for t in hand if t in TILES), None)
    result = set()
    for kind, targets in GROUPS:
        if anchor is not None and anchor not in targets:
            continue
        for remaining, used in consume(hand, targets, discard_bonus):
            if anchor is not None and anchor not in used:
                continue
            for tail in groups(remaining, left-1, discard_bonus):
                result.add(tuple(sorted(((kind, targets[1]),) + tail)))
    return tuple(sorted(result))


@lru_cache(maxsize=32768)
def standard(hand, meld_count=0, discard_bonus=False):
    if len(hand) != (4-meld_count)*3+2:
        return ()
    result = set()
    for tile in TILES:
        for rest, used in consume(hand, (tile, tile), discard_bonus):
            for sets in groups(rest, 4-meld_count, discard_bonus):
                result.add((tile, sets))
    return tuple(sorted(result))


@lru_cache(maxsize=32768)
def seven_pairs(hand, discard_bonus=False):
    if not hand:
        return True
    anchor = next((t for t in hand if t in TILES), None)
    for tile in (anchor,) if anchor is not None else TILES:
        for rest, used in consume(hand, (tile, tile), discard_bonus):
            if (anchor is None or anchor in used) and seven_pairs(rest, discard_bonus):
                return True
    return False


@lru_cache(maxsize=8192)
def pair_faces(hand):
    if not hand:
        return ((),)
    result=set()
    anchor=next((t for t in hand if t in TILES),None)
    for tile in (anchor,) if anchor is not None else TILES:
        for rest,used in consume(hand,(tile,tile)):
            if anchor is None or anchor in used:
                result.update(tuple(sorted((tile,)+tail)) for tail in pair_faces(rest))
    return tuple(sorted(result))


def shape(hand, melds=(), *, pairs=False, self_draw=False, discard_bonus=False):
    hand = tuple(sorted(hand))
    if len(hand) != (4-len(melds))*3+2 or any(t not in TILES+JOKERS+(BONUS if discard_bonus else ()) for t in hand):
        return False
    if standard(hand, len(melds), discard_bonus):
        return True
    if melds:
        return False
    return any(fits(hand, ORPHANS+(t,), discard_bonus) for t in ORPHANS) or (pairs and self_draw and seven_pairs(hand, discard_bonus))


@lru_cache(maxsize=8192)
def natural_waits(hand, meld_count=0):
    return tuple(t for t in TILES if hand.count(t)<4 and standard(tuple(sorted(hand+(t,))), meld_count))


def meld_tiles(code):
    if not isinstance(code,str) or len(code)<3 or code[0] not in 'skgG':
        raise ValueError('invalid meld')
    tile=int(code[1:])
    if tile not in TILES or code[0]=='s' and (tile>=40 or tile%10 not in range(2,9)):
        raise ValueError('invalid meld face')
    return (tile-1,tile,tile+1) if code[0]=='s' else (tile,)*(3 if code[0]=='k' else 4)
