from collections import Counter

from ...game_calculation.fuzhou import rules as book

CHOWS = ("chi_left", "chi_mid", "chi_right")


def empty_actions():
    return {i: [] for i in range(4)}


def legal_cuts(state, index):
    p = state.player_list[index]
    if any(t in book.FLOWERS for t in p.hand_tiles) or not p.hand_tiles:
        return set()
    if p.golden_middle or p.youjin:
        return {p.hand_tiles[-1]} if p.has_draw_slot else set()
    return set(p.hand_tiles)


def kong_tiles(state, index, kind):
    p = state.player_list[index]
    # A kong must leave a replacement before the four final draws. Locked
    # declarations cannot use a kong to change their promised physical waits.
    reserve_increase = 2 if kind == "angang" else 1
    if not p.has_draw_slot or p.golden_middle or p.youjin or state.in_last_four or state.double_youjin_owner is not None:
        return set()
    if len(state.tiles_list) <= state.dead_wall_count + 4 + reserve_increase:
        return set()
    if kind == "angang":
        return {t for t, n in Counter(p.hand_tiles).items() if n == 4 and t != state.jin and t in book.NUMBERS}
    if kind == "jiagang":
        return {t for t in p.hand_tiles if t != state.jin and f"k{t}" in p.combination_tiles}
    raise ValueError("未知杠类型")


def declaration_cuts(state, index, action):
    p = state.player_list[index]
    if not p.has_draw_slot or state.in_last_four or p.golden_middle or state.double_youjin_owner is not None:
        return set()
    if action == "youjin":
        if p.youjin == 1:
            return {state.jin} if p.hand_tiles[-1] == state.jin and state.can_win(index, "self_draw", state.jin) else set()
        if p.youjin:
            return set()
        return set(book.can_youjin(p.hand_tiles, p.combination_tiles, jin=state.jin))
    if action == "tingjin" and not p.youjin:
        return {t for t in set(p.hand_tiles)
                if book.golden_middle_wait([*p.hand_tiles[:p.hand_tiles.index(t)], *p.hand_tiles[p.hand_tiles.index(t)+1:]],
                                          p.combination_tiles, jin=state.jin)}
    return set()


def turn_actions(state, index, discard_only=False):
    p = state.player_list[index]
    actions = []
    if not discard_only:
        if state.can_win(index, "self_draw", p.hand_tiles[-1]):
            actions.append("hu_self")
        if state.can_three_gold(index):
            actions.append("sanjin")
        actions.extend(kind for kind in ("angang", "jiagang") if kong_tiles(state, index, kind))
        actions.extend(kind for kind in ("youjin", "tingjin") if declaration_cuts(state, index, kind))
    if legal_cuts(state, index):
        actions.append("cut")
    return {**empty_actions(), index: actions}


def claim_actions(state, actor, tile, rob=False):
    actions = empty_actions()
    if tile == state.jin or tile not in book.NUMBERS:
        return actions
    for index, p in enumerate(state.player_list):
        if index == actor:
            continue
        if state.can_win(index, "rob_kong" if rob else "discard", tile, payer=actor):
            actions[index].append("hu")
        if not rob and not p.golden_middle and not p.youjin and not state.in_last_four and state.double_youjin_owner is None:
            n = p.hand_tiles.count(tile)
            if n >= 2:
                actions[index].append("peng")
            if n == 3 and len(state.tiles_list) > state.dead_wall_count + 5:
                actions[index].append("gang")
            if index == (actor + 1) % 4:
                for name, offsets in zip(CHOWS, ((-2, -1), (-1, 1), (1, 2))):
                    needed = [tile + off for off in offsets]
                    if all(t in book.NUMBERS and t // 10 == tile // 10 and t != state.jin
                           and t in p.hand_tiles for t in needed):
                        actions[index].append(name)
        if actions[index]:
            actions[index].append("pass")
    return actions


class ActionPolicy:
    def refresh_waiting_tiles(self, state, index, *, exclude_last_tile=False):
        p = state.player_list[index]
        hand = p.hand_tiles[:-1] if exclude_last_tile and p.has_draw_slot else p.hand_tiles
        p.waiting_tiles = set(book.waiting_tiles(hand, p.combination_tiles, jin=state.jin)) if state.jin else set()
        return p.waiting_tiles
