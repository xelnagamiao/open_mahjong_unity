"""Choose legal overlapping meld patterns by score, not greedy recognition order."""

from functools import lru_cache

from .models import Fan


def best_local_patterns(candidates, exclusions=()):
    """HKMA single-use settlement: both shared fractions must be below 1/2.

    Whole-hand/event/flower patterns are outside this local-meld combinator.
    Explicit exclusions also apply to disjoint local masks (e.g. four steps
    excludes three steps). Four-return bonuses are expressly independently
    additive and are supplied outside this combinator.
    """
    candidates = sorted(set(candidates), key=lambda f: (-f.value, f.id, f.groups))
    exclusions = set(exclusions)
    conflicts = []
    for i, a in enumerate(candidates):
        mask = 1 << i
        for j, b in enumerate(candidates):
            shared = (a.groups & b.groups).bit_count()
            incompatible = shared * 2 >= min(a.groups.bit_count(), b.groups.bit_count())
            if incompatible or (a.id, b.id) in exclusions or (b.id, a.id) in exclusions:
                mask |= 1 << j
        conflicts.append(mask)

    @lru_cache(maxsize=None)
    def search(available):
        if not available:
            return (0, ())
        low = available & -available
        i = low.bit_length() - 1
        skipped = search(available ^ low)
        points, selected = search(available & ~conflicts[i])
        chosen = (points + candidates[i].value, (i,) + selected)
        return chosen if chosen[0] >= skipped[0] else skipped

    return tuple(candidates[i] for i in search((1 << len(candidates)) - 1)[1])


def mask_for(indices):
    return sum(1 << i for i in indices)
