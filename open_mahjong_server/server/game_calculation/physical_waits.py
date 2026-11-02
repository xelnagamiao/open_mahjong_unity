"""Remove shape waits that require a fifth owned physical tile."""
from collections import Counter


def exclude_fifth_copy_waits(hand, melds, waits):
    counts = Counter(hand)
    for meld in melds:
        kind, tile = meld[0].lower(), int(meld[1:])
        if kind == "s":
            counts.update((tile - 1, tile, tile + 1))
        elif kind in ("k", "g"):
            counts[tile] += 4 if kind == "g" else 3
    if any(count > 4 for count in counts.values()):
        return set()
    return {tile for tile in waits if counts[tile] < 4}
