"""MCR with three public glass copies and one opaque copy of each regular tile.

Only visibility and physical call choices change; the ordinary MCR wall,
flower replacements, fan table and payments are retained.
"""
from itertools import product

from ...game_calculation.riichi.washizu_tiles import OFFSET, normalize
from ..game_riichi.washizu import public_hand, call_candidates, valid_call_choice
from ..public.logic_common import get_index_relative_position

SUB_RULE = "guobiao/transparent"
REALTIME_SUB_RULE = "guobiao/transparent_realtime"
SUB_RULES = (SUB_RULE, REALTIME_SUB_RULE)
CHOICE_ACTIONS = ("chi_left", "chi_mid", "chi_right", "peng")


def enabled(game):
    return getattr(game, "sub_rule", None) in SUB_RULES


def public_hands(game):
    if not enabled(game):
        return None
    if getattr(game, 'sub_rule', None) == REALTIME_SUB_RULE:
        from ..game_riichi.realtime_hand import layout_for_viewer
        return {p.player_index: layout_for_viewer(game, p, -1)['tiles'] for p in game.player_list}
    return {p.player_index: public_hand(p.hand_tiles) for p in game.player_list}


def chow_candidates(hand, called, action):
    face = normalize(called)
    offsets = {"chi_left": (-2, -1), "chi_mid": (-1, 1), "chi_right": (1, 2)}[action]
    needs = [face + offset for offset in offsets]
    if not 11 <= face <= 39 or face % 10 == 0 or any(n // 10 != face // 10 or not 1 <= n % 10 <= 9 for n in needs):
        return []
    copies = [sorted({t for t in hand if normalize(t) == n}, reverse=True) for n in needs]
    return [list(pair) for pair in product(*copies)]


def prepare_choices(game, called, actions):
    """Cache physical choices in the same order sent with the current ask tick."""
    for player in game.player_list:
        player.chi_candidates = {}
        for action in actions[player.player_index]:
            if action.startswith("chi_"):
                player.chi_candidates[action] = chow_candidates(player.hand_tiles, called, action)
            elif action == "peng":
                player.chi_candidates[action] = call_candidates(player.hand_tiles, called)


def consume_meld(game, player_index, action, called, choice=0):
    """Remove only selected physical copies and build the actual visible mask."""
    player = game.player_list[player_index]
    if action in CHOICE_ACTIONS:
        if not valid_call_choice(player, action, choice):
            return None
        removed = list(player.chi_candidates[action][choice])
    else:
        removed = [t for t in player.hand_tiles if normalize(t) == normalize(called)]
        if action != "gang" or len(removed) != 3:
            return None
    face = normalize(called)
    if action.startswith("chi_"):
        target = f"s{face + {'chi_left': -1, 'chi_mid': 0, 'chi_right': 1}[action]}"
        pairs = [(1, called)] + [(0, t) for t in sorted(removed, key=normalize)]
    else:
        target = f"{'g' if action == 'gang' else 'k'}{face}"
        pairs = [(0, t) for t in removed]
        relative = get_index_relative_position(player_index, game.current_player_index, len(game.player_list))
        pairs.insert({"left": 0, "top": 1, "right": len(pairs)}[relative], (1, called))
    for tile in removed:
        player.hand_tiles.remove(tile)
    player.combination_tiles.append(target)
    return target, [value for pair in pairs for value in pair]
