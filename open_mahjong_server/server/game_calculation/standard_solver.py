"""Suit-independent, canonical-order solver for non-wildcard standard mahjong hands.

Design goals:
  1. Zero meld-order permutation duplicates (canonical lowest-tile-first extraction).
  2. Four-suit independence: each numbered suit (1..9) is packed into a single
     27-bit Python int (3 bits per rank, supporting counts 0..7) and cached via
     bounded ``@lru_cache``. Honors (41..47) are checked directly in O(7).
  3. O(1) mod-3 suit pruning + ±1 neighbor pruning for waiting-tile checks
     (and 14-tile batch discard-wait checks), avoiding brute-force 34-tile loops.
  4. Bounded memory footprint (~3.7 MB max per interpreter across all non-wildcard
     rules sharing this module).
"""
from __future__ import annotations

from functools import lru_cache
from itertools import product
from typing import Dict, Iterable, List, Optional, Sequence, Set, Tuple

_SUIT_BASES: Tuple[int, int, int] = (11, 21, 31)
_HONOR_BASE = 41
_RANK_MASK = 0x7

# Precomputed bit shifts and 3-tile sequence masks for a 9-rank suit (3 bits/rank).
_SHIFTS: Tuple[int, ...] = tuple(3 * r for r in range(9))
_INC: Tuple[int, ...] = tuple(1 << (3 * r) for r in range(9))
_PAIR_SUB: Tuple[int, ...] = tuple(2 << (3 * r) for r in range(9))
_TRIPLET_SUB: Tuple[int, ...] = tuple(3 << (3 * r) for r in range(9))
_SEQ_SUB: Tuple[int, ...] = tuple(
    (1 << (3 * r)) | (1 << (3 * (r + 1))) | (1 << (3 * (r + 2)))
    for r in range(7)
)


@lru_cache(maxsize=16384)
def _suit_can_meld(key: int) -> bool:
    """Return True iff the packed 9-rank suit can be partitioned solely into melds."""
    if key == 0:
        return True
    # Find lowest non-zero rank via lowest set bit.
    r = ((key & -key).bit_length() - 1) // 3
    c = (key >> _SHIFTS[r]) & _RANK_MASK
    if c >= 3 and _suit_can_meld(key - _TRIPLET_SUB[r]):
        return True
    if r <= 6:
        c1 = (key >> _SHIFTS[r + 1]) & _RANK_MASK
        c2 = (key >> _SHIFTS[r + 2]) & _RANK_MASK
        # If c in (1, 2), all c copies MUST be consumed by sequences starting at r.
        if c <= 2:
            if c1 >= c and c2 >= c:
                return _suit_can_meld(key - c * _SEQ_SUB[r])
            return False
        # c >= 3 (triplet already failed above): must use at least (c - 3) or c sequences.
        if c1 >= 1 and c2 >= 1 and _suit_can_meld(key - _SEQ_SUB[r]):
            return True
    return False


@lru_cache(maxsize=16384)
def _suit_can_meld_with_pair(key: int, pair_mask: int = 0x1FF) -> bool:
    """Return True iff the packed 9-rank suit forms 1 pair (in pair_mask) + melds."""
    for r in range(9):
        if not ((pair_mask >> r) & 1):
            continue
        if ((key >> _SHIFTS[r]) & _RANK_MASK) >= 2:
            if _suit_can_meld(key - _PAIR_SUB[r]):
                return True
    return False


@lru_cache(maxsize=8192)
def _suit_meld_decompositions(key: int) -> Tuple[Tuple[Tuple[int, int], ...], ...]:
    """Return all unique canonical meld partitions for a 9-rank suit.

    Each meld is encoded as ``(kind, rank)`` where:
      - ``kind == 0``: triplet of rank ``rank`` (0..8)
      - ``kind == 1``: sequence ``(rank, rank+1, rank+2)`` (rank 0..6)
    """
    if key == 0:
        return ((),)
    r = ((key & -key).bit_length() - 1) // 3
    c = (key >> _SHIFTS[r]) & _RANK_MASK
    results: List[Tuple[Tuple[int, int], ...]] = []
    if c >= 3:
        sub = _suit_meld_decompositions(key - _TRIPLET_SUB[r])
        if sub:
            head = ((0, r),)
            for tail in sub:
                results.append(head + tail)
    if r <= 6:
        c1 = (key >> _SHIFTS[r + 1]) & _RANK_MASK
        c2 = (key >> _SHIFTS[r + 2]) & _RANK_MASK
        if c1 >= 1 and c2 >= 1:
            sub = _suit_meld_decompositions(key - _SEQ_SUB[r])
            if sub:
                head = ((1, r),)
                for tail in sub:
                    results.append(head + tail)
    return tuple(results)


@lru_cache(maxsize=8192)
def _suit_pair_decompositions(
    key: int,
    pair_mask: int = 0x1FF,
) -> Tuple[Tuple[int, Tuple[Tuple[int, int], ...]], ...]:
    """Return all unique ``(pair_rank, melds)`` decompositions for a 9-rank suit."""
    results: List[Tuple[int, Tuple[Tuple[int, int], ...]]] = []
    for r in range(9):
        if not ((pair_mask >> r) & 1):
            continue
        if ((key >> _SHIFTS[r]) & _RANK_MASK) >= 2:
            for melds in _suit_meld_decompositions(key - _PAIR_SUB[r]):
                results.append((r, melds))
    return tuple(results)


def _honors_can_meld(hcounts: Sequence[int]) -> bool:
    for c in hcounts:
        if c != 0 and c != 3:
            return False
    return True


def _honors_can_meld_with_pair(hcounts: Sequence[int], pair_mask: int = 0x7F) -> bool:
    pair_found = False
    for r, c in enumerate(hcounts):
        if c == 0 or c == 3:
            continue
        if c in (2, 5) and not pair_found and ((pair_mask >> r) & 1):
            pair_found = True
        else:
            return False
    return pair_found


def _honors_meld_tuple(hcounts: Sequence[int]) -> Optional[Tuple[Tuple[int, int], ...]]:
    melds: List[Tuple[int, int]] = []
    for r, c in enumerate(hcounts):
        if c == 0:
            continue
        if c == 3:
            melds.append((0, _HONOR_BASE + r))
        else:
            return None
    return tuple(melds)


def _honors_pair_decomposition(
    hcounts: Sequence[int],
    pair_mask: int = 0x7F,
) -> Optional[Tuple[int, Tuple[Tuple[int, int], ...]]]:
    pair_tile = -1
    melds: List[Tuple[int, int]] = []
    for r, c in enumerate(hcounts):
        if c == 0:
            continue
        if c == 3:
            melds.append((0, _HONOR_BASE + r))
        elif c == 5 and pair_tile < 0 and ((pair_mask >> r) & 1):
            pair_tile = _HONOR_BASE + r
            melds.append((0, _HONOR_BASE + r))
        elif c == 2 and pair_tile < 0 and ((pair_mask >> r) & 1):
            pair_tile = _HONOR_BASE + r
        else:
            return None
    if pair_tile < 0:
        return None
    return pair_tile, tuple(melds)


def _parse_suits(
    hand_tiles: Iterable[int],
    *,
    allow_honors: bool = True,
) -> Optional[Tuple[List[int], List[int], List[int], int]]:
    """Split tiles into 3 packed 27-bit suit keys, 7 honor counts, 4 suit totals, and total count.

    Returns ``None`` if any tile is outside valid range or exceeds 7 copies.
    """
    skeys = [0, 0, 0]
    hcounts = [0, 0, 0, 0, 0, 0, 0]
    totals = [0, 0, 0, 0]
    n = 0
    for t in hand_tiles:
        n += 1
        if 11 <= t <= 19:
            r = t - 11
            if ((skeys[0] >> _SHIFTS[r]) & _RANK_MASK) >= 6:
                return None
            skeys[0] += _INC[r]
            totals[0] += 1
        elif 21 <= t <= 29:
            r = t - 21
            if ((skeys[1] >> _SHIFTS[r]) & _RANK_MASK) >= 6:
                return None
            skeys[1] += _INC[r]
            totals[1] += 1
        elif 31 <= t <= 39:
            r = t - 31
            if ((skeys[2] >> _SHIFTS[r]) & _RANK_MASK) >= 6:
                return None
            skeys[2] += _INC[r]
            totals[2] += 1
        elif allow_honors and 41 <= t <= 47:
            r = t - 41
            if hcounts[r] >= 6:
                return None
            hcounts[r] += 1
            totals[3] += 1
        else:
            return None
    return skeys, hcounts, totals, n


def _pair_masks(pair_tiles: Optional[Iterable[int]]) -> Tuple[int, int, int, int]:
    if pair_tiles is None:
        return 0x1FF, 0x1FF, 0x1FF, 0x7F
    m0 = m1 = m2 = m3 = 0
    for t in pair_tiles:
        if 11 <= t <= 19:
            m0 |= 1 << (t - 11)
        elif 21 <= t <= 29:
            m1 |= 1 << (t - 21)
        elif 31 <= t <= 39:
            m2 |= 1 << (t - 31)
        elif 41 <= t <= 47:
            m3 |= 1 << (t - 41)
    return m0, m1, m2, m3


def can_standard_win(
    hand_tiles: Iterable[int],
    *,
    allow_honors: bool = True,
    pair_tiles: Optional[Iterable[int]] = None,
) -> bool:
    """Return True iff concealed ``hand_tiles`` form ``k`` melds + 1 pair (for any k >= 0)."""
    parsed = _parse_suits(hand_tiles, allow_honors=allow_honors)
    if parsed is None:
        return False
    skeys, hcounts, totals, n = parsed
    if n < 2 or n % 3 != 2:
        return False

    pair_suit = -1
    for s in range(4):
        rem = totals[s] % 3
        if rem == 1:
            return False
        if rem == 2:
            if pair_suit >= 0:
                return False
            pair_suit = s
    if pair_suit < 0:
        return False

    pmasks = _pair_masks(pair_tiles)
    for s in range(3):
        if s == pair_suit:
            if not _suit_can_meld_with_pair(skeys[s], pmasks[s]):
                return False
        elif skeys[s] and not _suit_can_meld(skeys[s]):
            return False

    if pair_suit == 3:
        return _honors_can_meld_with_pair(hcounts, pmasks[3])
    if totals[3] and not _honors_can_meld(hcounts):
        return False
    return True


def _suit_candidate_ranks(key: int, max_existing: int) -> List[int]:
    """Return ranks r (0..8) with count <= max_existing that have a tile at r-1, r, or r+1."""
    if key == 0:
        return []
    counts = [(key >> _SHIFTS[r]) & _RANK_MASK for r in range(9)]
    cands: List[int] = []
    for r in range(9):
        if counts[r] > max_existing:
            continue
        if counts[r] > 0 or (r > 0 and counts[r - 1] > 0) or (r < 8 and counts[r + 1] > 0):
            cands.append(r)
    return cands


def _waits_from_parsed(
    skeys: List[int],
    hcounts: List[int],
    totals: List[int],
    pmasks: Tuple[int, int, int, int],
    *,
    max_existing: int,
) -> Set[int]:
    """Compute waiting tiles from pre-parsed 4-suit representation."""
    mod1: List[int] = []
    mod2: List[int] = []
    for s in range(4):
        rem = totals[s] % 3
        if rem == 1:
            mod1.append(s)
        elif rem == 2:
            mod2.append(s)

    waits: Set[int] = set()

    # Case 1: exactly one suit has mod 3 == 1, other three have mod 3 == 0.
    # Adding 1 tile to that suit makes it mod 3 == 2 (must hold the pair).
    if len(mod1) == 1 and not mod2:
        target = mod1[0]
        for s in range(3):
            if s != target and skeys[s] and not _suit_can_meld(skeys[s]):
                return waits
        if target != 3 and totals[3] and not _honors_can_meld(hcounts):
            return waits

        if target == 3:
            for r in range(7):
                if hcounts[r] > 0 and hcounts[r] <= max_existing:
                    hcounts[r] += 1
                    if _honors_can_meld_with_pair(hcounts, pmasks[3]):
                        waits.add(_HONOR_BASE + r)
                    hcounts[r] -= 1
        else:
            key = skeys[target]
            base = _SUIT_BASES[target]
            pmask = pmasks[target]
            for r in _suit_candidate_ranks(key, max_existing):
                if _suit_can_meld_with_pair(key + _INC[r], pmask):
                    waits.add(base + r)
        return waits

    # Case 2: exactly two suits have mod 3 == 2, other two have mod 3 == 0.
    # Adding 1 tile to suit A makes A mod 3 == 0 (all melds), while suit B holds the pair (and vice versa).
    if not mod1 and len(mod2) == 2:
        s_a, s_b = mod2[0], mod2[1]
        for s in range(3):
            if s != s_a and s != s_b and skeys[s] and not _suit_can_meld(skeys[s]):
                return waits
        if s_a != 3 and s_b != 3 and totals[3] and not _honors_can_meld(hcounts):
            return waits

        a_has_pair = (
            _honors_can_meld_with_pair(hcounts, pmasks[3])
            if s_a == 3
            else _suit_can_meld_with_pair(skeys[s_a], pmasks[s_a])
        )
        b_has_pair = (
            _honors_can_meld_with_pair(hcounts, pmasks[3])
            if s_b == 3
            else _suit_can_meld_with_pair(skeys[s_b], pmasks[s_b])
        )

        # Try completing s_a into all-melds when s_b provides the pair.
        if b_has_pair:
            if s_a == 3:
                for r in range(7):
                    if hcounts[r] > 0 and hcounts[r] <= max_existing:
                        hcounts[r] += 1
                        if _honors_can_meld(hcounts):
                            waits.add(_HONOR_BASE + r)
                        hcounts[r] -= 1
            else:
                key = skeys[s_a]
                base = _SUIT_BASES[s_a]
                for r in _suit_candidate_ranks(key, max_existing):
                    if _suit_can_meld(key + _INC[r]):
                        waits.add(base + r)

        # Try completing s_b into all-melds when s_a provides the pair.
        if a_has_pair:
            if s_b == 3:
                for r in range(7):
                    if hcounts[r] > 0 and hcounts[r] <= max_existing:
                        hcounts[r] += 1
                        if _honors_can_meld(hcounts):
                            waits.add(_HONOR_BASE + r)
                        hcounts[r] -= 1
            else:
                key = skeys[s_b]
                base = _SUIT_BASES[s_b]
                for r in _suit_candidate_ranks(key, max_existing):
                    if _suit_can_meld(key + _INC[r]):
                        waits.add(base + r)

    return waits


def standard_waiting_tiles(
    hand_tiles: Iterable[int],
    *,
    allow_honors: bool = True,
    pair_tiles: Optional[Iterable[int]] = None,
    allow_fifth_copy: bool = False,
) -> Set[int]:
    """Return all tiles that complete concealed ``hand_tiles`` into ``k`` melds + 1 pair.

    When ``allow_fifth_copy=False`` (default), tiles already held 4 times in ``hand_tiles``
    are excluded. Pass ``allow_fifth_copy=True`` to match pure-shape checkers that filter
    physical copies externally (e.g. legacy Guobiao tingpai).
    """
    parsed = _parse_suits(hand_tiles, allow_honors=allow_honors)
    if parsed is None:
        return set()
    skeys, hcounts, totals, n = parsed
    if n < 1 or n % 3 != 1:
        return set()
    pmasks = _pair_masks(pair_tiles)
    return _waits_from_parsed(
        skeys,
        hcounts,
        totals,
        pmasks,
        max_existing=4 if allow_fifth_copy else 3,
    )


def standard_decompositions(
    hand_tiles: Iterable[int],
    *,
    allow_honors: bool = True,
    pair_tiles: Optional[Iterable[int]] = None,
) -> List[Tuple[int, Tuple[Tuple[int, int], ...]]]:
    """Return all unique canonical ``(pair_tile, melds)`` decompositions for concealed ``hand_tiles``.

    Each meld in ``melds`` is ``(kind, tile_id)``:
      - ``kind == 0``: triplet of ``tile_id``
      - ``kind == 1``: sequence centered at ``tile_id`` (i.e. ``tile_id-1, tile_id, tile_id+1``)
    """
    parsed = _parse_suits(hand_tiles, allow_honors=allow_honors)
    if parsed is None:
        return []
    skeys, hcounts, totals, n = parsed
    if n < 2 or n % 3 != 2:
        return []

    pair_suit = -1
    for s in range(4):
        rem = totals[s] % 3
        if rem == 1:
            return []
        if rem == 2:
            if pair_suit >= 0:
                return []
            pair_suit = s
    if pair_suit < 0:
        return []

    pmasks = _pair_masks(pair_tiles)

    # Decompose the 3 non-pair suits first; short-circuit immediately if any fails.
    suit_meld_lists: List[Sequence[Tuple[Tuple[int, int], ...]]] = [(), (), (), ()]
    for s in range(3):
        if s == pair_suit:
            continue
        if skeys[s] == 0:
            suit_meld_lists[s] = ((),)
        else:
            raw = _suit_meld_decompositions(skeys[s])
            if not raw:
                return []
            base = _SUIT_BASES[s]
            suit_meld_lists[s] = tuple(
                tuple((kind, base + r + kind) for kind, r in part)
                for part in raw
            )

    if pair_suit != 3:
        if totals[3] == 0:
            suit_meld_lists[3] = ((),)
        else:
            hm = _honors_meld_tuple(hcounts)
            if hm is None:
                return []
            suit_meld_lists[3] = (hm,)

    # Decompose the pair suit.
    pair_options: List[Tuple[int, Tuple[Tuple[int, int], ...]]] = []
    if pair_suit == 3:
        hp = _honors_pair_decomposition(hcounts, pmasks[3])
        if hp is None:
            return []
        pair_options.append(hp)
    else:
        raw_pairs = _suit_pair_decompositions(skeys[pair_suit], pmasks[pair_suit])
        if not raw_pairs:
            return []
        base = _SUIT_BASES[pair_suit]
        for pr, part in raw_pairs:
            pair_options.append(
                (base + pr, tuple((kind, base + r + kind) for kind, r in part))
            )

    other_suits = [suit_meld_lists[s] for s in range(4) if s != pair_suit]
    out: List[Tuple[int, Tuple[Tuple[int, int], ...]]] = []
    for pair_tile, pair_melds in pair_options:
        for m0, m1, m2 in product(other_suits[0], other_suits[1], other_suits[2]):
            out.append((pair_tile, pair_melds + m0 + m1 + m2))
    return out


def standard_combination_codes(
    hand_tiles: Iterable[int],
    base_combinations: Sequence[str] = (),
    *,
    allow_honors: bool = True,
    kezi_prefix: str = "K",
    shunzi_prefix: str = "S",
    quetou_prefix: str = "q",
) -> List[List[str]]:
    """Return sorted, deduplicated combination code lists (e.g. ``['K11', 'S12', 'q41']``)."""
    decomps = standard_decompositions(hand_tiles, allow_honors=allow_honors)
    if not decomps:
        return []
    base = list(base_combinations)
    results: List[List[str]] = []
    for pair_tile, melds in decomps:
        comb = base.copy()
        comb.append(f"{quetou_prefix}{pair_tile}")
        for kind, tile_id in melds:
            comb.append(f"{shunzi_prefix if kind else kezi_prefix}{tile_id}")
        comb.sort()
        results.append(comb)
    return results


def discard_waiting_tiles_map(
    hand_tiles: Iterable[int],
    *,
    allow_honors: bool = True,
    pair_tiles: Optional[Iterable[int]] = None,
    allow_fifth_copy: bool = False,
) -> Dict[int, Set[int]]:
    """For a 3m+2 hand, compute ``{discard_tile: waiting_tiles}`` in one pass.

    Uses 4-suit mod-3 pruning to skip impossible discards without repeated parsing.
    """
    parsed = _parse_suits(hand_tiles, allow_honors=allow_honors)
    if parsed is None:
        return {}
    skeys, hcounts, totals, n = parsed
    if n < 2 or n % 3 != 2:
        return {}

    pmasks = _pair_masks(pair_tiles)
    max_existing = 4 if allow_fifth_copy else 3
    result: Dict[int, Set[int]] = {}

    for s in range(3):
        key = skeys[s]
        if key == 0:
            continue
        base = _SUIT_BASES[s]
        for r in range(9):
            if ((key >> _SHIFTS[r]) & _RANK_MASK) == 0:
                continue
            skeys[s] = key - _INC[r]
            totals[s] -= 1
            waits = _waits_from_parsed(skeys, hcounts, totals, pmasks, max_existing=max_existing)
            if waits:
                result[base + r] = waits
            totals[s] += 1
            skeys[s] = key

    if allow_honors and totals[3] > 0:
        for r in range(7):
            if hcounts[r] == 0:
                continue
            hcounts[r] -= 1
            totals[3] -= 1
            waits = _waits_from_parsed(skeys, hcounts, totals, pmasks, max_existing=max_existing)
            if waits:
                result[_HONOR_BASE + r] = waits
            totals[3] += 1
            hcounts[r] += 1

    return result


def clear_caches() -> None:
    _suit_can_meld.cache_clear()
    _suit_can_meld_with_pair.cache_clear()
    _suit_meld_decompositions.cache_clear()
    _suit_pair_decompositions.cache_clear()


def cache_info() -> Dict[str, Dict[str, int]]:
    return {
        "suit_can_meld": _suit_can_meld.cache_info()._asdict(),
        "suit_can_meld_with_pair": _suit_can_meld_with_pair.cache_info()._asdict(),
        "suit_meld_decompositions": _suit_meld_decompositions.cache_info()._asdict(),
        "suit_pair_decompositions": _suit_pair_decompositions.cache_info()._asdict(),
    }
