"""All legal offers and post-claim restrictions are server derived."""

from collections import Counter

from ...game_calculation.yixing.rules import FLOWERS, NUMBERS, waiting_tiles

CHOWS = ("chi_left", "chi_mid", "chi_right")


def empty_actions():
    return {i: [] for i in range(4)}


def claim_tiles(action, tile):
    if action in ("peng", "gang"):
        return [tile] * (3 if action == "gang" else 2)
    offsets = {"chi_left": (-2, -1), "chi_mid": (-1, 1), "chi_right": (1, 2)}[action]
    return [tile + n for n in offsets]


def forbidden_after_chow(action, tile, hand_size):
    if action not in CHOWS or hand_size == 4:
        return set()  # The rulebook explicitly permits 过水 into a big single.
    result = {tile}
    other = tile - 3 if action == "chi_left" else tile + 3 if action == "chi_right" else tile
    if other in NUMBERS and other // 10 == tile // 10:
        result.add(other)
    return result


def legal_cuts(state, index):
    p = state.player_list[index]
    if any(t in FLOWERS for t in p.hand_tiles):
        return set()
    if p.last_draw_last_wall:
        return {p.hand_tiles[-1]} if p.has_draw_slot else set()
    return set(p.hand_tiles) - p.forbidden_discards


def kong_tiles(state, index, kind):
    p = state.player_list[index]
    if not p.has_draw_slot or not state.tiles_list or any(t in FLOWERS for t in p.hand_tiles):
        return set()
    if kind == "angang":
        return {tile for tile, count in Counter(p.hand_tiles).items() if count == 4}
    return {tile for tile in p.hand_tiles if f"k{tile}" in p.combination_tiles}


def turn_actions(state, index, discard_only=False):
    p = state.player_list[index]
    choices = []
    if not discard_only:
        if state.can_win(index, "self_draw", p.hand_tiles[-1]):
            choices.append("hu_self")
        choices += [kind for kind in ("angang", "jiagang") if kong_tiles(state, index, kind)]
    if legal_cuts(state, index):
        choices.append("cut")
    return {**empty_actions(), index: choices}


def claim_actions(state, actor, tile, rob=False):
    actions = empty_actions()
    for index, p in enumerate(state.player_list):
        if index == actor:
            continue
        if state.can_win(index, "rob_kong" if rob else "discard", tile, payer=actor):
            actions[index].append("hu")
        if not rob and state.tiles_list:
            count = p.hand_tiles.count(tile)
            if tile not in p.passed_pungs:
                if count >= 2:
                    actions[index].append("peng")
                if count == 3:
                    actions[index].append("gang")
            if index == (actor + 1) % 4 and tile in NUMBERS:
                for action in CHOWS:
                    needed = claim_tiles(action, tile)
                    if not all(t in NUMBERS and t // 10 == tile // 10 and t in p.hand_tiles for t in needed):
                        continue
                    remaining = list(p.hand_tiles)
                    for t in needed:
                        remaining.remove(t)
                    # Held flowers are replaced in the claimant's own action
                    # window before discarding, so they must not prevent a chow.
                    if set(remaining) - forbidden_after_chow(action, tile, len(p.hand_tiles)):
                        actions[index].append(action)
        if actions[index]:
            actions[index].append("pass")
    return actions


class ActionPolicy:
    def refresh_waiting_tiles(self, state, index, *, exclude_last_tile=False):
        p = state.player_list[index]
        hand = p.hand_tiles[:-1] if exclude_last_tile and p.has_draw_slot else p.hand_tiles
        p.waiting_tiles = waiting_tiles(hand, p.combination_tiles, **state.detailed_config)
        return p.waiting_tiles
