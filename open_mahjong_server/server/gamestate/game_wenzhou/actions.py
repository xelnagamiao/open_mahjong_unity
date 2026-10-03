"""Offers are computed from physical ownership and the fixed white mapping.

The wildcard participates in win calculation only. Its natural-face option is
not permission to use it in chi/pung/kong. Exposed tiles retain physical IDs.
"""

from collections import Counter

from ...game_calculation.wenzhou.rules import waiting_tiles

CHOWS = ("chi_left", "chi_mid", "chi_right")
NUMBERS = frozenset(suit * 10 + number for suit in (1, 2, 3) for number in range(1, 10))


def empty_actions():
    return {i: [] for i in range(4)}


def natural(tile, caishen):
    return caishen if tile == 46 and caishen != 46 else tile


def needed_tiles(action, logical):
    if action in ("peng", "gang"):
        return [logical] * (3 if action == "gang" else 2)
    offsets = {"chi_left": (-2, -1), "chi_mid": (-1, 1), "chi_right": (1, 2)}[action]
    return [logical + n for n in offsets]


def physical_for(hand, logical_tiles, caishen):
    """Deterministic natural mapping, never a wildcard substitution in a call."""
    remaining = sorted(tile for tile in hand if tile != caishen)
    selected = []
    for logical in logical_tiles:
        tile = next((tile for tile in remaining if natural(tile, caishen) == logical), None)
        if tile is None:
            return None
        selected.append(tile)
        remaining.remove(tile)
    return selected


def kong_tiles(state, index, kind):
    p = state.player_list[index]
    if not p.has_draw_slot or not state.can_draw():
        return set()
    if kind == "angang":
        counts = Counter(natural(t, state.caishen) for t in p.hand_tiles if t != state.caishen)
        return {t for t in p.hand_tiles if t != state.caishen and counts[natural(t, state.caishen)] == 4}
    if kind == "jiagang":
        return {t for t in p.hand_tiles if t != state.caishen and f"k{natural(t, state.caishen)}" in p.combination_tiles}
    raise ValueError("未知杠牌类型")


def turn_actions(state, index, discard_only=False):
    p = state.player_list[index]
    result = []
    if not discard_only:
        if state.can_win(index, "self_draw", p.hand_tiles[-1]):
            result.append("hu_self")
        result.extend(kind for kind in ("angang", "jiagang") if kong_tiles(state, index, kind))
    if p.hand_tiles:
        result.append("cut")
    return {**empty_actions(), index: result}


def claim_actions(state, actor, tile, *, rob=False):
    actions = empty_actions()
    logical = natural(tile, state.caishen)
    for index, p in enumerate(state.player_list):
        if index == actor:
            continue
        if state.can_win(index, "rob_kong" if rob else "discard", tile, payer=actor):
            actions[index].append("hu")
        # At the reserve, only winning is allowed; a call must not bypass 荒庄.
        if state.can_draw() and tile != state.caishen:
            for kind in ("peng", "gang"):
                if physical_for(p.hand_tiles, needed_tiles(kind, logical), state.caishen):
                    actions[index].append(kind)
            if not rob and index == (actor + 1) % 4 and logical in NUMBERS:
                for kind in CHOWS:
                    needed = needed_tiles(kind, logical)
                    if all(t in NUMBERS and t // 10 == logical // 10 for t in needed) and physical_for(p.hand_tiles, needed, state.caishen):
                        actions[index].append(kind)
        if actions[index]:
            actions[index].append("pass")
    return actions


class ActionPolicy:
    def refresh_waiting_tiles(self, state, index, *, exclude_last_tile=False):
        p = state.player_list[index]
        hand = p.hand_tiles[:-1] if exclude_last_tile and p.has_draw_slot else p.hand_tiles
        p.waiting_tiles = waiting_tiles(hand, p.meld_records, caishen=state.caishen)
        return p.waiting_tiles
