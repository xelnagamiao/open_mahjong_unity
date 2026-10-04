"""Rule-specific legal choices; network clients never supply qualification."""

from collections import Counter

from ...game_calculation.guizhou.rules import NORMAL_CHICKENS, waiting_tiles


def empty_actions():
    return {i: [] for i in range(4)}


def ready_cuts(state, index):
    p = state.player_list[index]
    if p.declared_ready or p.discard_count or p.combination_tiles or p.draw_count > (0 if index == 0 else 1):
        return set()
    if not p.has_draw_slot:
        return set()
    result = set()
    for tile in set(p.hand_tiles):
        rest = list(p.hand_tiles)
        rest.remove(tile)
        if waiting_tiles(rest):
            result.add(tile)
    return result


def legal_cuts(state, index):
    p = state.player_list[index]
    if p.declared_ready:
        return {p.hand_tiles[-1]} if p.has_draw_slot else set()
    return ready_cuts(state, index) if p.ready_pending else set(p.hand_tiles)


def kong_tiles(state, index, kind):
    p = state.player_list[index]
    if p.declared_ready or not p.has_draw_slot or not state.tiles_list:
        return set()
    if kind == "angang":
        return {tile for tile, count in Counter(p.hand_tiles).items() if count == 4}
    return {tile for tile in p.hand_tiles if f"k{tile}" in p.combination_tiles}


def turn_actions(state, index, discard_only=False):
    choices = []
    p = state.player_list[index]
    if not discard_only and state.can_win(index, "self_draw", p.hand_tiles[-1]):
        choices.append("hu_self")
    if p.ready_pending:
        return {**empty_actions(), index: ["cut", "guizhou_ready_cancel"]}
    if not discard_only:
        choices += [kind for kind in ("angang", "jiagang") if kong_tiles(state, index, kind)]
        if ready_cuts(state, index):
            choices.append("guizhou_ready")
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
            if not p.declared_ready and (rob or state.hot_discard):
                continue  # VIII.6: mandatory win, even after passing a lower hand.
        if not rob and not p.declared_ready:
            count = p.hand_tiles.count(tile)
            passed = tile in p.passed_pungs
            own_pass = tile in p.discarded_since_action and tile not in NORMAL_CHICKENS
            if count >= 2 and not passed and not own_pass:
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
