"""Offers use physical tiles: a white dragon is never a callable tile."""

from collections import Counter

from ...game_calculation.hangzhou.rules import JOKER, TILES, waiting_tiles

NUMBERS = frozenset(t for t in TILES if t < 40)
CHOWS = ("chi_left", "chi_mid", "chi_right")


def empty_actions():
    return {i: [] for i in range(4)}


def claim_tiles(action, tile):
    if action in ("peng", "gang"):
        return [tile] * (3 if action == "gang" else 2)
    return [tile + n for n in {"chi_left": (-2, -1), "chi_mid": (-1, 1), "chi_right": (1, 2)}[action]]


def legal_cuts(state, index):
    p = state.player_list[index]
    if state.is_cai_locked(index):
        return {p.hand_tiles[-1]} if p.has_draw_slot else set()
    return set(p.hand_tiles)


def kong_tiles(state, index, kind):
    p = state.player_list[index]
    # A kong consumes a lower tile and burns its upper partner. Do not cross
    # the reserved twenty-tile wall. Chi/peng may immediately precede a kong.
    if len(state.tiles_list) < 22:
        return set()
    if kind == "angang":
        return {t for t, n in Counter(p.hand_tiles).items() if n == 4 and t != JOKER}
    return {t for t in p.hand_tiles if t != JOKER and f"k{t}" in p.combination_tiles}


def turn_actions(state, index, discard_only=False):
    p = state.player_list[index]
    actions = []
    if not discard_only and state.can_win(index, "self_draw", p.hand_tiles[-1]):
        actions.append("hu_self")
    actions += [kind for kind in ("angang", "jiagang") if kong_tiles(state, index, kind)]
    if legal_cuts(state, index):
        actions.append("cut")
    return {**empty_actions(), index: actions}


def claim_actions(state, actor, tile):
    actions = empty_actions()
    if tile == JOKER or len(state.tiles_list) <= 20:
        return actions
    for index, p in enumerate(state.player_list):
        if index == actor or state.is_cai_locked(index):
            continue
        count = p.hand_tiles.count(tile)
        if count >= 2:
            actions[index].append("peng")
        if count == 3 and len(state.tiles_list) >= 22:
            actions[index].append("gang")
        if index == (actor + 1) % 4 and tile in NUMBERS:
            for action in CHOWS:
                needed = claim_tiles(action, tile)
                if all(t in NUMBERS and t // 10 == tile // 10 and t in p.hand_tiles for t in needed):
                    actions[index].append(action)
        if actions[index]:
            actions[index].append("pass")
            if state.sync_tactical_enabled():
                actions[index].append("force_pass")
    return actions


class ActionPolicy:
    def refresh_waiting_tiles(self, state, index, *, exclude_last_tile=False):
        p = state.player_list[index]
        hand = p.hand_tiles[:-1] if exclude_last_tile and p.has_draw_slot else p.hand_tiles
        p.waiting_tiles = waiting_tiles(hand, p.combination_tiles)
        return p.waiting_tiles
