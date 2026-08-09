"""Pure CPU decisions for the Hongque tile-efficiency bot (user_id=2).

This module deliberately does not import or mutate the shared mahjong bot
state machine.  Hongque has 126 unique tiles and variable-length groups, so it
uses the rule-local group index instead of the ordinary 34-type shanten code.
"""
from __future__ import annotations

from dataclasses import dataclass
from functools import lru_cache
from typing import Iterable, Optional, Sequence

from .group_index import (
    GROUP_MASKS,
    TILE_INDEX,
    mask_from_codes,
    waiting_mask,
    waiting_masks_after_discards,
)
from .win_check import is_winning_hand


@dataclass(frozen=True)
class EfficiencyValue:
    """A comparable Hongque hand value; a larger rank is better."""

    distance: int
    live_waits: int
    flexibility: int

    @property
    def rank(self) -> tuple[int, int, int]:
        return (-self.distance, self.live_waits, self.flexibility)


def _bit_for_code(code: str) -> int:
    return 1 << TILE_INDEX[code]


@lru_cache(maxsize=32768)
def _structural_value(hand_mask: int) -> tuple[int, int]:
    """Return (replacement distance, shape flexibility).

    Distance is the minimum number of current tiles which must be replaced so
    that one subsequent draw can make the concealed tiles a union of legal
    groups.  It is the Hongque counterpart of shanten and works with arbitrary
    concealed hand sizes after calls and supplements.

    The DP packs ``(kept tile count, completed group length)`` reachability
    into one Python integer.  Bit shifts therefore advance every reachable
    state at once, instead of recursively constructing and comparing hundreds
    of thousands of tuples.  The primary distance is exactly equivalent to
    the previous tuple DP; flexibility is a lightweight tie-break based on
    the number and strength of overlapping legal shapes.
    """
    hand_size = hand_mask.bit_count()
    if hand_size == 0:
        return 2, 0
    target_size = hand_size + 1

    variant_counts: dict[tuple[int, int], int] = {}
    for group_mask in GROUP_MASKS:
        group_size = group_mask.bit_count()
        if group_size > target_size:
            break
        overlap = group_mask & hand_mask
        # A one-tile overlap needs at least two entirely new cards and gives no
        # actionable shape information.  Treat it as a replaceable singleton;
        # waiting_mask only reports tiles that complete a legal group, since
        # same-number pair heads are not a winning shape.
        if overlap.bit_count() >= 2:
            key = (overlap, group_size)
            variant_counts[key] = variant_counts.get(key, 0) + 1

    sizes_by_overlap: dict[int, int] = {}
    flexibility = 0
    for (overlap, group_size), count in variant_counts.items():
        overlap_size = overlap.bit_count()
        missing = group_size - overlap_size
        flexibility += (
            min(count, 32) * overlap_size * overlap_size * 8 // (missing + 1)
        )
        sizes_by_overlap[overlap] = (
            sizes_by_overlap.get(overlap, 0) | (1 << group_size)
        )

    by_anchor: dict[int, list[tuple[int, int, tuple[int, ...]]]] = {}
    for overlap, size_bits in sizes_by_overlap.items():
        overlap_size = overlap.bit_count()
        group_sizes = tuple(
            size for size in range(3, target_size + 1)
            if size_bits & (1 << size)
        )
        remaining = overlap
        while remaining:
            bit = remaining & -remaining
            by_anchor.setdefault(bit, []).append(
                (overlap, overlap_size, group_sizes)
            )
            remaining ^= bit

    # Each row encodes group-length costs 0..target_size for one kept count.
    # Masking before a shift prevents a cost overflow from spilling into the
    # next kept-count row.
    stride = target_size + 1
    cost_caps = [0] * (target_size + 1)
    for group_size in range(3, target_size + 1):
        row = (1 << (target_size - group_size + 1)) - 1
        cap = 0
        for kept in range(hand_size + 1):
            cap |= row << (kept * stride)
        cost_caps[group_size] = cap

    @lru_cache(maxsize=None)
    def reachable(mask: int) -> int:
        if mask == 0:
            return 1  # kept=0, group cost=0

        anchor = mask & -mask
        states = reachable(mask ^ anchor)  # replace this tile
        for overlap, overlap_size, group_sizes in by_anchor.get(anchor, ()):
            if overlap & mask != overlap:
                continue
            tail = reachable(mask ^ overlap)
            kept_shift = overlap_size * stride
            for group_size in group_sizes:
                states |= (
                    (tail & cost_caps[group_size])
                    << (kept_shift + group_size)
                )
        return states

    states = reachable(hand_mask)
    # Unfilled target length is either zero or at least one complete group
    # (three tiles).  Residual lengths one and two are not legal groups.
    valid_costs = 1 << target_size
    if target_size >= 3:
        valid_costs |= (1 << (target_size - 2)) - 1
    for kept in range(hand_size, -1, -1):
        row = states >> (kept * stride)
        if row & valid_costs:
            return hand_size - kept, max(0, flexibility)
    return hand_size, 0


def _visible_mask(codes: Iterable[str]) -> int:
    # Visible snapshots may contain the same claimed tile both in the old river
    # and the new meld during a transition.  A set keeps the bit-mask builder
    # strict while accepting that harmless wire-level duplication.
    return mask_from_codes(tuple(sorted(set(codes))))


def evaluate_hand(
    hand: Sequence[str],
    open_melds: Sequence[dict],
    visible_codes: Iterable[str],
) -> EfficiencyValue:
    hand_mask = mask_from_codes(hand)
    visible_mask = _visible_mask(visible_codes)
    waits = waiting_mask(
        hand_mask,
        used_mask=visible_mask,
        has_open_group=bool(open_melds),
    )
    distance, flexibility = _structural_value(hand_mask)
    live_waits = waits.bit_count()
    # The overlap distance ignores rare future-tile collisions.  Exact waits
    # are authoritative at distance zero, so a collision cannot create a fake
    # ready hand.
    if live_waits:
        distance = 0
    elif distance == 0:
        distance = 1
    return EfficiencyValue(distance, live_waits, flexibility)


def choose_discard(
    hand: Sequence[str],
    open_melds: Sequence[dict],
    visible_codes: Iterable[str],
    drawn_tile: Optional[str] = None,
) -> tuple[Optional[str], EfficiencyValue]:
    if not hand:
        return None, EfficiencyValue(99, 0, 0)

    hand_mask = mask_from_codes(hand)
    visible_mask = _visible_mask(visible_codes) | hand_mask
    has_open_group = bool(open_melds)
    hand_code_by_index = {TILE_INDEX[tile]: tile for tile in hand}

    # First scan exact waits.  This is much cheaper than the generalized shape
    # DP and lets every ready-hand discard participate, not just a heuristic
    # shortlist.
    waits_by_discard = waiting_masks_after_discards(
        hand_mask,
        used_mask=visible_mask,
        has_open_group=has_open_group,
    )
    ready: list[tuple[str, int]] = []
    for tile in hand:
        live_waits = waits_by_discard[_bit_for_code(tile)].bit_count()
        if live_waits:
            ready.append((tile, live_waits))

    connectivity = {tile: 0 for tile in hand}
    for group_mask in GROUP_MASKS:
        overlap = group_mask & hand_mask
        overlap_size = overlap.bit_count()
        if overlap_size < 2:
            continue
        missing = group_mask.bit_count() - overlap_size
        quality = overlap_size * overlap_size * 8 // (missing + 1)
        bits = overlap
        while bits:
            bit = bits & -bits
            code_index = bit.bit_length() - 1
            # All overlap bits belong to the hand and therefore exist in the map.
            code = hand_code_by_index[code_index]
            connectivity[code] += quality
            bits ^= bit

    if ready:
        best_live = max(item[1] for item in ready)
        finalists = [tile for tile, live in ready if live == best_live]
        finalists.sort(key=lambda tile: (connectivity[tile], tile))
        best_tile = drawn_tile if drawn_tile in finalists else finalists[0]
        return best_tile, EfficiencyValue(0, best_live, -connectivity[best_tile])

    # For non-ready hands, low-connectivity tiles are the only plausible cuts.
    # Running the more expensive generalized distance on six finalists keeps
    # the decision close to the ordinary bot's CPU envelope without turning a
    # draw broadcast into a full-hand combinatorial search.
    shortlist = sorted(hand, key=lambda tile: (connectivity[tile], tile))[:6]
    if drawn_tile in hand and drawn_tile not in shortlist:
        shortlist.append(drawn_tile)

    best_tile: Optional[str] = None
    best_value = EfficiencyValue(99, 0, 0)
    for tile in shortlist:
        remaining_mask = hand_mask ^ _bit_for_code(tile)
        distance, flexibility = _structural_value(remaining_mask)
        value = EfficiencyValue(max(1, distance), 0, flexibility)
        if value.rank > best_value.rank:
            best_tile, best_value = tile, value
            continue
        if value.rank != best_value.rank:
            continue
        # Prefer a true drawn-tile discard on a complete tie, then a stable code
        # order.  This avoids artificial hand rearrangement and random stalls.
        if tile == drawn_tile and best_tile != drawn_tile:
            best_tile = tile
        elif best_tile != drawn_tile and (best_tile is None or tile < best_tile):
            best_tile = tile
    return best_tile, best_value


def _remove_tiles(hand: Sequence[str], selected: Sequence[str]) -> Optional[list[str]]:
    result = list(hand)
    for tile in selected:
        if tile not in result:
            return None
        result.remove(tile)
    return result


def _meld_from_candidate(candidate: dict) -> dict:
    return {
        "kind": candidate.get("kind", "sequence"),
        "tiles": list(candidate.get("tiles", ())),
        "claimed_tile": candidate.get("tile"),
    }


def choose_turn_plan(
    hand: Sequence[str],
    open_melds: Sequence[dict],
    visible_codes: Sequence[str],
    kong_options: Sequence[dict],
    *,
    supplements: int,
    wall_count: int,
    drawn_tile: Optional[str],
    last_draw_was_supplement: bool = False,
) -> dict:
    """Choose win, supplement, kong extension, or an efficient discard."""
    # 杠和（自摸牌并入明牌即和）并入“和”：直接宣言和牌，不实际移动手牌。
    if is_winning_hand(hand, open_melds) or any(
        candidate.get("kind") == "kong_win" for candidate in kong_options
    ):
        return {"action": "win"}

    best_tile, baseline = choose_discard(hand, open_melds, visible_codes, drawn_tile)
    # Treat supplement as a limited redraw for an ineffective draw, not as an
    # unconditional extra card.  The bot spends one only when the tile it just
    # drew would otherwise be the best discard and the resulting hand is not
    # already ready.  A supplement draw must be followed by a discard, which
    # prevents both charges being consumed back-to-back at the opening.
    if (
        supplements < 2
        and wall_count > 0
        and drawn_tile is not None
        and best_tile == drawn_tile
        and baseline.distance > 0
        and baseline.live_waits == 0
        and not last_draw_was_supplement
    ):
        return {"action": "supplement"}

    best_kong: Optional[dict] = None
    best_kong_rank: Optional[tuple] = None
    for candidate in kong_options:
        after = _remove_tiles(hand, candidate.get("hand_tiles", ()))
        if after is None:
            continue
        melds_after = list(open_melds)
        # The authoritative state upgrades the indexed meld in place.  For
        # structural evaluation it is enough to know that an exposed group
        # remains present.
        if is_winning_hand(after, melds_after):
            rank = (
                1,
                0,
                0,
                1 if candidate.get("kind") == "kong_win" else 0,
                len(candidate.get("hand_tiles", ())),
            )
        else:
            _, value = choose_discard(after, melds_after, visible_codes, None)
            rank = (0,) + value.rank + (0, len(candidate.get("hand_tiles", ())))
        if best_kong_rank is None or rank > best_kong_rank:
            best_kong, best_kong_rank = candidate, rank

    baseline_rank = (0,) + baseline.rank + (0,)
    if best_kong is not None and best_kong_rank is not None and best_kong_rank >= baseline_rank:
        if best_kong_rank[0] == 1:
            return {"action": "win"}
        return {"action": "kong", "candidate_id": best_kong.get("id")}
    return {"action": "discard", "tile": best_tile}


def _claim_advances(before: EfficiencyValue, after: EfficiencyValue, priority: int) -> bool:
    if after.distance != before.distance:
        return after.distance < before.distance
    if after.live_waits != before.live_waits:
        return after.live_waits > before.live_waits
    if priority >= 6:  # 虹(6)：结构性中性但番值高，宽松放行；碰(5)/吃(2-4) 需补偿开副露
        return after.flexibility >= before.flexibility
    # Ordinary chi/peng must compensate for opening the hand.
    return after.flexibility > before.flexibility * 5 // 4


def choose_claim_plan(
    hand: Sequence[str],
    open_melds: Sequence[dict],
    candidates: Sequence[dict],
    visible_codes: Sequence[str],
) -> dict:
    """Choose an authoritative candidate id or pass after another discard."""
    for candidate in candidates:
        if candidate.get("kind") == "win":
            return {"action": "claim", "candidate_id": candidate.get("id")}

    before = evaluate_hand(hand, open_melds, visible_codes)
    best_candidate: Optional[dict] = None
    best_rank: Optional[tuple] = None

    for candidate in candidates:
        if candidate.get("kind") not in {"sequence", "triplet", "rainbow"}:
            continue
        after = _remove_tiles(hand, candidate.get("hand_tiles", ()))
        if after is None:
            continue
        melds_after = list(open_melds) + [_meld_from_candidate(candidate)]
        priority = int(candidate.get("priority", 0) or 0)
        if is_winning_hand(after, melds_after):
            value = EfficiencyValue(0, 126, 1_000_000)
        elif after:
            _, value = choose_discard(after, melds_after, visible_codes, None)
        else:
            continue
        if not _claim_advances(before, value, priority):
            continue
        rank = value.rank + (priority, len(candidate.get("tiles", ())))
        if best_rank is None or rank > best_rank:
            best_candidate, best_rank = candidate, rank

    if best_candidate is None:
        return {"action": "pass"}
    return {"action": "claim", "candidate_id": best_candidate.get("id")}
