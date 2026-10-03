"""Authoritative legal choices. No client-provided scoring or wait sets."""

from collections import Counter

from ...game_calculation.hongkong import structural_waits
from ...game_calculation.hongkong.models import NUMBERS


def empty_actions():
    return {i:[] for i in range(4)}


def required_claim_tiles(action,tile):
    return {
        "chi_left": (tile-2,tile-1), "chi_mid": (tile-1,tile+1),
        "chi_right": (tile+1,tile+2), "peng": (tile,tile), "gang": (tile,tile,tile),
    }[action]


def forbidden_after_claim(action,tile):
    forbidden = {tile}
    if action == "chi_left" and tile%10 >= 4:
        forbidden.add(tile-3)
    elif action == "chi_right" and tile%10 <= 6:
        forbidden.add(tile+3)
    return forbidden


def ready_discards(state,index):
    p = state.player_list[index]
    if not state.rules.has_ready or p.declared_ready:
        return set()
    if not state.rules.is_sixteen and any(m[0] != "G" for m in p.combination_tiles):
        return set()
    choices = set()
    for tile in set(p.hand_tiles) - p.forbidden_discards:
        remaining = list(p.hand_tiles)
        remaining.remove(tile)
        if structural_waits(remaining,p.combination_tiles,state.rules):
            choices.add(tile)
    return choices


def legal_discards(state,index):
    p = state.player_list[index]
    if p.declared_ready:
        return {p.hand_tiles[-1]} if p.has_draw_slot else set()
    if p.ready_pending:
        return ready_discards(state,index)
    return set(p.hand_tiles) - p.forbidden_discards


def turn_actions(state,index,*,discard_only=False):
    result = empty_actions()
    p = state.player_list[index]
    actions = result[index]
    if not discard_only and p.has_draw_slot and state.can_win(index,"self_draw",p.hand_tiles[-1]):
        actions.append("hu_self")
    if p.ready_pending:
        actions.extend(("cut","riichi_cancel"))
        return result
    if not discard_only and p.has_draw_slot and not p.declared_ready and state.tiles_list:
        counts = Counter(p.hand_tiles)
        if any(c==4 for c in counts.values()):
            actions.append("angang")
        if any(f"k{t}" in p.combination_tiles for t in counts):
            actions.append("jiagang")
    if state.rules.has_ready and not p.declared_ready and ready_discards(state,index):
        actions.append("riichi")
    if legal_discards(state,index):
        actions.append("cut")
    return result


def claim_actions(state,discarder,tile):
    result = empty_actions()
    for i,p in enumerate(state.player_list):
        if i==discarder:
            continue
        actions = result[i]
        if state.can_win(i,"discard",tile,payer=discarder):
            actions.append("hu")
        if state.tiles_list and not p.declared_ready:
            count = p.hand_tiles.count(tile)
            possible = []
            if count>=2:
                possible.append("peng")
            if count>=3:
                possible.append("gang")
            if i == (discarder+1)%4 and tile in NUMBERS:
                if tile%10>=3:
                    possible.append("chi_left")
                if 2<=tile%10<=8:
                    possible.append("chi_mid")
                if tile%10<=7:
                    possible.append("chi_right")
            for action in possible:
                if state.rules.is_lianhuise and action == "peng" and tile in p.passed_claim_tiles:
                    continue
                required = Counter(required_claim_tiles(action,tile))
                hand = Counter(p.hand_tiles)
                if any(hand[t]<n for t,n in required.items()):
                    continue
                if state.rules.is_old and state.would_assume_liability(i,action,tile):
                    if tile in p.passed_claim_tiles or tile in p.discarded_since_draw:
                        continue
                remaining = hand-required
                forbidden = {tile} if state.rules.is_lianhuise else forbidden_after_claim(action,tile)
                if action != "gang" and not (set(remaining)-forbidden):
                    continue
                actions.append(action)
        if actions:
            actions.append("pass")
    return result


def rob_kong_actions(state,actor,tile,*,concealed=False):
    actions = empty_actions()
    if concealed and not state.rules.can_rob_concealed_kong:
        return actions
    for i in range(4):
        if i==actor:
            continue
        if state.can_win(i,"rob_kong",tile,payer=actor):
            quote = state.score_win(i,"rob_kong",tile,payer=actor)
            if not concealed or quote.shape=="orphans":
                actions[i] = ["hu","pass"]
    return actions


class HongKongActionPolicy:
    @staticmethod
    def refresh_waiting_tiles(state,index,*,exclude_last_tile=False):
        p = state.player_list[index]
        hand = p.hand_tiles[:-1] if exclude_last_tile else p.hand_tiles
        p.waiting_tiles = structural_waits(hand,p.combination_tiles,state.rules)
        return p.waiting_tiles
