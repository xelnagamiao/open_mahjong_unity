"""Structural shanten for any supported meld count (including 16-tile hands)."""
from collections import Counter
from functools import lru_cache
from ....game_calculation.compact_counter import pack_counter

NUMBERS = frozenset(s * 10 + n for s in (1, 2, 3) for n in range(1, 10))


@lru_cache(maxsize=65536)
def _partials(key):
    if not key:
        return ((0,0,0),)
    # Sorted tile/count bytes keep the same 65,536 states without retaining
    # a separate tuple object for every tile in every cached sub-hand.
    counts = dict(zip(key[::2], key[1::2]))
    tile,count = key[:2]
    def rest(used):
        next_counts = dict(counts)
        for t in used:
            next_counts[t] -= 1
        # Subtraction preserves the canonical order inherited from key.
        return bytes(value for t,c in next_counts.items() if c for value in (t,c))
    result = set(_partials(rest((tile,))))
    if count>=3:
        result.update((m+1,t,p) for m,t,p in _partials(rest((tile,)*3)))
    if tile in NUMBERS and tile%10<=7 and counts.get(tile+1) and counts.get(tile+2):
        result.update((m+1,t,p) for m,t,p in _partials(rest((tile,tile+1,tile+2))))
    if count>=2:
        for m,t,p in _partials(rest((tile,tile))):
            result.add((m,t+1,p))
            if not p:
                result.add((m,t,1))
    if tile in NUMBERS:
        for step in (1,2):
            if tile%10+step<=9 and counts.get(tile+step):
                result.update((m,t+1,p) for m,t,p in _partials(rest((tile,tile+step))))
    return tuple(sorted(a for a in result if not any(a!=b and all(x<=y for x,y in zip(a,b)) for b in result)))


def distance(hand,meld_count,total_melds):
    needed = total_melds-meld_count
    return min(2*needed-2*m-min(t,max(0,needed-m))-p
               for m,t,p in _partials(pack_counter(Counter(hand))))
