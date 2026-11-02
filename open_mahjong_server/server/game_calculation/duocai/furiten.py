from functools import lru_cache

from . import tiles as t
from .evaluation import evaluate, iter_scores
from .scoring import Context, conditions
from .solver import Meld, structural_win


@lru_cache(maxsize=2048)
def logical_waits(hand, melds=(), variant='riichi'):
    result = set()
    for value in t.PHYSICAL:
        if not t.joker(value) and t.face(value) not in result and structural_win(hand + (value,), melds, variant):
            result.add(t.face(value))
    return frozenset(result)


def discard_furiten(hand, melds, discards, winning_tile, ctx=Context()):
    hand, melds = tuple(hand), tuple(Meld.read(m) for m in melds)
    discarded = set()
    conditional = set()
    for value in discards:
        if value in (t.CHOW, t.PUNG):
            continue
        if value in (t.ODD, t.EVEN, t.MANGAN):
            conditional.add(value)
        else:
            discarded.update(t.DOMAIN[value])
    if discarded and logical_waits(hand, melds) & discarded:
        return True
    if conditional:
        effective = [result for result in iter_scores(hand + (winning_tile,), melds, 'riichi', ctx) if result.valid]
        return bool(effective) and any(all(conditions((tile,), result.visible, result.fu, 'riichi')
                                          for result in effective) for tile in conditional)
    return False


def legal_ron(hand, melds, discards, winning_tile, ctx=Context(), *, temporary=False, permanent=False):
    if temporary or permanent:
        return False
    result = evaluate((*hand, winning_tile), melds, 'riichi', ctx)
    return bool(result and result.valid and not discard_furiten(hand, melds, discards, winning_tile, ctx))
