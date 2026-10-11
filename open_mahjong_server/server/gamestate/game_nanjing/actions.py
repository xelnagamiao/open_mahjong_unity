from collections import Counter

from ...game_calculation.nanjing.rules import FLOWERS, TILES, waiting_tiles


def empty_actions():
    return {i: [] for i in range(4)}


def legal_cuts(state, index):
    hand = state.player_list[index].hand_tiles
    return set() if any(t in FLOWERS for t in hand) else set(hand)


def kong_tiles(state, index, kind):
    p = state.player_list[index]
    if not p.has_draw_slot or not state.tiles_list or any(t in FLOWERS for t in p.hand_tiles) or "peida" in p.tag_list:
        return set()
    if kind == "angang":
        return {t for t, c in Counter(p.hand_tiles).items() if c == 4 and t in TILES}
    return {t for t in p.hand_tiles if f"k{t}" in p.combination_tiles}


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
    if tile not in TILES:
        return actions
    for index, p in enumerate(state.player_list):
        if index == actor or "peida" in p.tag_list:
            continue
        if state.can_win(index, "rob_kong" if rob else "discard", tile, payer=actor):
            actions[index].append("hu")
        count = p.hand_tiles.count(tile)
        if not rob and tile not in p.passed_pungs:
            if count >= 2:
                actions[index].append("peng")
            if count == 3 and state.tiles_list:
                actions[index].append("gang")
        if actions[index]:
            actions[index].append("pass")
    return actions


class ActionPolicy:
    def refresh_waiting_tiles(self, state, index, *, exclude_last_tile=False):
        p = state.player_list[index]
        hand = p.hand_tiles[:-1] if exclude_last_tile and p.has_draw_slot else p.hand_tiles
        p.waiting_tiles = waiting_tiles(hand, p.combination_tiles)
        return p.waiting_tiles
