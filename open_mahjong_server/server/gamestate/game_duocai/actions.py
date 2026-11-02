from collections import Counter
from functools import lru_cache

from ...game_calculation.duocai import tiles as t
from ...game_calculation.duocai.actions import call_choices, turn_choices
from ...game_calculation.duocai.furiten import logical_waits
from ...game_calculation.duocai.solver import Meld

KINDS = {'minggang': 'gang', 'chi': 'chi_left'}


def empty_actions():
    return {i: [] for i in range(4)}


def legal_cuts(state, index):
    p = state.player_list[index]
    return [p.hand_tiles[-1]] if p.riichi and p.has_draw_slot else sorted(set(p.hand_tiles))


@lru_cache(maxsize=1024)
def riichi_cuts(hand, melds):
    result = []
    for tile in sorted(set(hand)):
        left = list(hand)
        left.remove(tile)
        if logical_waits(tuple(left), melds):
            result.append(tile)
    return tuple(result)


def choices(state, index, *, called=None, actor=None):
    p = state.player_list[index]
    fourth = bool(state.wall.live) and (state.variant != 'riichi' or state.wall.rinshan_used < 4)
    if called is not None:
        if p.riichi or not state.wall.live:
            return ()
        return call_choices(p.hand_tiles, called, next_player=index == (actor + 1) % 4, can_kong=fourth)
    result = turn_choices(p.hand_tiles, p.melds, can_fourth=fourth, can_extend=bool(state.wall.live))
    if not p.riichi:
        return result
    before = logical_waits(tuple(p.hand_tiles[:-1]), tuple(p.melds))
    allowed = []
    for choice in result:
        if not p.has_draw_slot or p.hand_tiles[-1] not in choice.take:
            continue
        if choice.meld_index >= 0 and not p.melds[choice.meld_index].concealed:
            continue
        left, melds = list(p.hand_tiles), list(p.melds)
        for tile in choice.take:
            left.remove(tile)
        if choice.kind == 'angang':
            melds.append(Meld(choice.take, True, 'angang'))
        else:
            old = melds[choice.meld_index]
            melds[choice.meld_index] = Meld(old.physical + choice.take, True, old.declared, old.source)
        if logical_waits(tuple(left), tuple(melds)) == before:
            allowed.append(choice)
    return tuple(allowed)


def turn_actions(state, index, after_meld=False):
    p, result = state.player_list[index], []
    if not after_meld and state.can_win(index, 'self_draw', p.hand_tiles[-1]):
        result.append('hu_self')
    options = choices(state, index)
    state.meld_choices = {index: options}
    result.extend(dict.fromkeys(KINDS.get(c.kind, c.kind) for c in options))
    if state.variant == 'riichi' and not after_meld and not p.riichi and p.score >= 1000 and all(m.concealed for m in p.melds):
        if riichi_cuts(tuple(p.hand_tiles), tuple(p.melds)):
            result.append('riichi_cut')
    if state.variant == 'riichi' and not after_meld and not state.opening_flow_interrupted and not p.discard_origin_tiles:
        if len({t.face(v) for v in p.hand_tiles if t.face(v) in t.ORPHANS}) >= 9:
            result.append('jiuzhongjiupai')
    if legal_cuts(state, index):
        result.append('cut')
    return {**empty_actions(), index: result}


def claim_actions(state, actor, tile, rob=False):
    result, state.meld_choices = empty_actions(), {}
    for index, p in enumerate(state.player_list):
        if index == actor:
            continue
        if state.can_win(index, 'rob_kong' if rob else 'discard', tile, payer=actor):
            result[index].append('hu')
        options = () if rob else choices(state, index, called=tile, actor=actor)
        state.meld_choices[index] = options
        result[index].extend(dict.fromkeys(KINDS.get(c.kind, c.kind) for c in options))
        if result[index]:
            result[index].append('pass')
    return result


class ActionPolicy:
    def refresh_waiting_tiles(self, state, index, *, exclude_last_tile=False):
        p = state.player_list[index]
        p.waiting_tiles = set(logical_waits(tuple(p.hand_tiles[:-1] if exclude_last_tile else p.hand_tiles), tuple(p.melds), state.variant))
        return p.waiting_tiles
