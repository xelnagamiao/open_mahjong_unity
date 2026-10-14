"""MCR four-joker house rule: exact legal replacements, bounded caches.

59 is an additional physical red flower, never a replacement flower (51..58).
No 34**j substitution search: ordinary shapes use the shared suit DP; knitted
shapes fill six fixed patterns. Only complete legal structures reach scoring.
"""
from collections import Counter
from dataclasses import replace
from functools import lru_cache
from itertools import combinations, permutations

from .guobiao_hepai_check import Chinese_Hepai_Check, PlayerTiles
from .gb_tingpai_check import Chinese_Tingpai_Check
from .jokers import JokerPolicy, TILES, can_win as joker_can_win, iter_winning_shapes
from .jokers.solver import _prepare

SUB_RULE = "guobiao/four_jokers"
JOKER = 59
PHYSICAL_TILES = TILES + (JOKER,)
POLICY = JokerPolicy(joker_tiles=(JOKER,), physical_tiles=PHYSICAL_TILES,
                     physical_limits=((JOKER, 4),), closed_cap=4,
                     shapes=("standard", "seven_pairs", "thirteen_orphans"))
KNITTED = tuple(tuple(sorted(suit * 10 + rank for suit, start in zip((1, 2, 3), order)
                             for rank in range(start, 10, 3)))
                for order in permutations((1, 2, 3)))
HONORS = tuple(range(41, 48))


def enabled(state):
    return getattr(state, "sub_rule", "") in (SUB_RULE, "guobiao/huahun")


def for_state(state):
    if getattr(state, "sub_rule", "") == "guobiao/huahun":
        from . import guobiao_huahun
        return guobiao_huahun
    import sys
    return sys.modules[__name__]


def is_joker(state, tile):
    return (tile in (51, 52, 53, 54) if getattr(state, "sub_rule", "") == "guobiao/huahun"
            else enabled(state) and tile == JOKER)


def is_flower(state, tile):
    return (55 <= tile <= 58 if getattr(state, "sub_rule", "") == "guobiao/huahun"
            else 51 <= tile <= 58)


@lru_cache(maxsize=256)
def policy_for(melds):
    prepared = _prepare((), melds, POLICY)
    if prepared is None:
        return None
    exposed = Counter(t for group in prepared[1] for t in group.logical)
    return replace(POLICY, closed_limits=tuple(sorted((t, 4 - n) for t, n in exposed.items())))


def _knitted_remainders(hand, melds, policy):
    """Use each present knitted tile, or spend a joker and retain that tile.

    Spending a joker on a present tile is necessary: the retained natural tile
    may complete the remaining pung/pair. Enumerate at most nine binary slots.
    """
    if len(melds) > 1:
        return
    natural = Counter(t for t in hand if t != JOKER)
    ghosts = hand.count(JOKER)
    for pattern in KNITTED:
        missing = tuple(t for t in pattern if not natural[t])
        if len(missing) > ghosts:
            continue
        present = tuple(t for t in pattern if natural[t])
        for spend in range(min(ghosts - len(missing), len(present)) + 1):
            for retained in combinations(present, spend):
                remaining = natural.copy()
                for t in present:
                    if t not in retained:
                        remaining[t] -= 1
                targets = missing + retained
                rest = tuple(sorted(remaining.elements())) + (JOKER,) * (ghosts - len(targets))
                caps = dict(policy.closed_limits)
                for t in pattern:
                    caps[t] = caps.get(t, 4) - 1
                if any(n < 0 for n in caps.values()):
                    continue
                rest_policy = replace(policy, meld_count=1, shapes=("standard",),
                                      closed_limits=tuple(sorted(caps.items())))
                yield pattern, targets, rest, rest_policy


def _unconnected_assignments(hand):
    natural = [t for t in hand if t != JOKER]
    if len(hand) != 14 or len(set(natural)) != len(natural):
        return
    for pattern in KNITTED:
        domain = set(pattern + HONORS)
        if not set(natural) <= domain:
            continue
        for targets in combinations(sorted(domain - set(natural)), hand.count(JOKER)):
            yield tuple(sorted(natural + list(targets))), targets


@lru_cache(maxsize=8192)
def _can_win(hand, melds):
    policy = policy_for(melds)
    if policy is None or len(hand) + 3 * len(melds) != 14 or _prepare(hand, melds, policy) is None:
        return False
    if joker_can_win(hand, melds, policy):
        return True
    if not melds and next(_unconnected_assignments(hand), None) is not None:
        return True
    return any(joker_can_win(rest, melds, rest_policy)
               for _, _, rest, rest_policy in _knitted_remainders(hand, melds, policy))


def can_win(hand, melds=()):
    return _can_win(tuple(sorted(hand)), tuple(melds))


@lru_cache(maxsize=4096)
def _waiting(hand, melds):
    if len(hand) + 3 * len(melds) != 13:
        return frozenset()
    policy = policy_for(melds)
    prepared = _prepare(hand, melds, policy) if policy else None
    if prepared is None:
        return frozenset()
    return frozenset(t for t in PHYSICAL_TILES if prepared[4][t] < 4
                     and _can_win(tuple(sorted(hand + (t,))), melds))


def waiting_tiles(hand, melds=()):
    return set(_waiting(tuple(sorted(hand)), tuple(melds)))


def _code(group):
    return ("S" + str(group.logical[1]) if group.kind == "sequence"
            else ("q" if group.kind == "pair" else "K") + str(group.logical[0]))


@lru_cache(maxsize=8192)
def _resolved_single_wait(before, melds, winning_tile):
    """One ordinary wait decomposition per resolved pre-win hand, not 34 scores.

    Red flowers have already acquired fixed identities. Count all legal waits
    of this hand (across decompositions), excluding an impossible fifth copy.
    """
    waits = Chinese_Tingpai_Check().tingpai_check(list(before), list(melds))
    caps = dict(policy_for(melds).closed_limits)
    counts = Counter(before)
    return {t for t in waits if counts[t] < caps.get(t, 4)} == {winning_tile}


@lru_cache(maxsize=1024)
def _score(hand, melds, way, winning_tile, joker_multi_wait, allow_single_wait):
    if winning_tile not in hand or not _can_win(hand, melds):
        return 0, ()
    if winning_tile == JOKER and not any(x in way for x in ("自摸", "杠上开花", "last_deal", "妙手回春")):
        return 0, ()
    checker = Chinese_Hepai_Check()

    # Physical draw candidates still include 59, even when no joker is held.
    # They cannot determine the wait fan of a resolved ordinary hand. Ignore
    # caller hints and derive that fact here for settlement and hint requests.
    way = tuple(x for x in way if x != "和单张" and not (winning_tile == JOKER and x == "和绝张"))
    single_wait_allowed = allow_single_wait and not (joker_multi_wait and JOKER in hand)

    def resolved_way(logical, logical_win):
        if single_wait_allowed:
            before = list(logical)
            before.remove(logical_win)
            if _resolved_single_wait(tuple(before), melds, logical_win):
                return list(way) + ["和单张"]
        return list(way)

    if JOKER not in hand:
        score, fans = checker.hepai_check(list(hand), list(melds), resolved_way(hand, winning_tile), winning_tile)
        return score, tuple(fans)

    best = (0, ())
    seen = set()

    def accept(player, logical, logical_win):
        nonlocal best
        # A wait fan adds at most one point (or replaces chicken hand's eight).
        # Score the shape first; only competitive candidates need the ordinary
        # wait decomposition. Keep a pristine copy because MCR scoring mutates
        # concealed pungs and fan lists. Never prune a possible chicken hand
        # before resolving its mandatory wait fan.
        original = player.__deepcopy__({}) if single_wait_allowed else None
        score, fans = checker.fan_count(player, logical_win, list(way))
        if single_wait_allowed and score + 1 > best[0]:
            fixed_way = resolved_way(logical, logical_win)
            if len(fixed_way) > len(way):
                score, fans = checker.fan_count(original, logical_win, fixed_way)
        candidate = (score, tuple(fans))
        if candidate[0] > best[0]:
            best = candidate

    # MCR scores the resolved logical tiles/melds/winning identity, with no
    # soft/hard bonus or per-group joker bonus. This is precisely the shared
    # solver's logical_structure equivalence; physical permutations are free.
    policy = policy_for(melds)
    for shape in iter_winning_shapes(hand, melds, policy, winning_tile,
                                     equivalence="logical_structure"):
        logical_win = shape.assignment[shape.winning_index]
        logical = tuple(sorted(shape.assignment))
        closed = tuple(_code(g) for g in shape.groups if not g.external)
        key = (shape.kind, closed, logical_win)
        if key in seen:
            continue
        seen.add(key)
        if shape.kind == "standard":
            player = PlayerTiles([], list(melds) + list(closed), 14)
            player.initial_combination_count = len(melds)
            accept(player, logical, logical_win)
        else:
            player = PlayerTiles(logical, [], 0)
            candidates = []
            (checker.QD_check if shape.kind == "seven_pairs" else checker.GS_check)(player, candidates)
            for candidate in candidates:
                accept(candidate, logical, logical_win)

    # Rare knitted forms: enumerate only fixed-pattern completions and invoke
    # the existing MCR scorer. De-duplicate resolved hands across the patterns.
    logical_hands = []
    if not melds:
        logical_hands.extend(_unconnected_assignments(hand))
    for pattern, targets, rest, rest_policy in _knitted_remainders(hand, melds, policy):
        for shape in iter_winning_shapes(rest, melds, rest_policy, equivalence="logical_structure"):
            logical = tuple(sorted(pattern + shape.assignment))
            all_targets = targets + tuple(use.logical for use in shape.joker_uses)
            logical_hands.append((logical, all_targets))
    checked = set()
    for logical, targets in logical_hands:
        for logical_win in sorted(set(targets)) if winning_tile == JOKER else (winning_tile,):
            key = (logical, logical_win)
            if key in checked:
                continue
            checked.add(key)
            score, fans = checker.hepai_check(list(logical), list(melds), resolved_way(logical, logical_win), logical_win)
            if score > best[0]:
                best = score, tuple(fans)
    return best


def hepai_check(hand, melds, way, winning_tile, joker_multi_wait=False, allow_single_wait=True):
    score, fans = _score(tuple(sorted(hand)), tuple(melds), tuple(sorted(way)), winning_tile,
                         joker_multi_wait, allow_single_wait)
    return score, list(fans)


def hint_snapshot(hand, melds):
    """Structural hints only; never multiply exact scoring by 14*35 draws."""
    hand, melds = tuple(hand), tuple(melds)
    result = dict(hand=list(hand), melds=list(melds), waits=[], discard_waits={})
    if len(hand) + 3 * len(melds) == 13:
        result['waits'] = sorted(waiting_tiles(hand, melds))
    elif len(hand) + 3 * len(melds) == 14:
        for cut in sorted(set(hand)):
            remaining = list(hand); remaining.remove(cut)
            result['discard_waits'][cut] = sorted(waiting_tiles(remaining, melds))
    return result


def calculate_hint(hand, melds, ron_way, self_way, tile, limit, joker_multi_wait=False, allow_single_wait=True):
    """Called on demand by an open hint row, in a calculation worker."""
    flowers = ron_way.count('花牌')
    ron, _ = hepai_check(hand, melds, ron_way, tile, joker_multi_wait, allow_single_wait)
    if ron > 0 and ron - flowers >= limit:
        return dict(kind='dianhe', label=f'{ron}番', score=ron)
    own, _ = hepai_check(hand, melds, self_way, tile, joker_multi_wait, allow_single_wait)
    if own > 0 and own - self_way.count('花牌') >= limit:
        return dict(kind='zimo', label=f'自摸{own}番', score=own)
    return dict(kind='wuyi', label='未起和', score=max(ron, own))
