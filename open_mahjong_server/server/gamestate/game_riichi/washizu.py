"""Washizu's public hand projection and shared shuffled draw pool."""
from collections import Counter
from itertools import combinations
from ...game_calculation.riichi.washizu_tiles import SUB_RULES, REALTIME_SUB_RULE, is_transparent, normalize

def enabled(game):
    return getattr(game, 'sub_rule', None) in SUB_RULES

def public_hand(tiles):
    visible = sorted((t for t in tiles if is_transparent(t)), key=normalize)
    return visible + [0] * (len(tiles) - len(visible))

def public_hands(game):
    if not enabled(game):
        return None
    if getattr(game, 'sub_rule', None) == REALTIME_SUB_RULE:
        from .realtime_hand import layout_for_viewer
        return {p.player_index: layout_for_viewer(game, p, -1)['tiles'] for p in game.player_list}
    # Sort the public projection independently of the owner's private hand order.
    return {p.player_index: public_hand(p.hand_tiles) for p in game.player_list}

def call_candidates(hand, called, count=2):
    matching = [t for t in hand if normalize(t) == normalize(called)]
    return [list(pair) for pair in sorted(set(tuple(sorted(c)) for c in combinations(matching, count)),
        key=lambda pair: (-sum(is_transparent(t) for t in pair), pair))]

def valid_call_choice(player, action, index):
    options = (getattr(player, 'chi_candidates', None) or {}).get(action, [])
    if not isinstance(index, int) or isinstance(index, bool) or not 0 <= index < len(options):
        return False
    available = Counter(player.hand_tiles)
    return not (Counter(options[index]) - available)

def init_pool_after_deal(game):
    if getattr(game, 'sub_rule', None) == REALTIME_SUB_RULE:
        from .realtime_hand import initialize_hand
        for player in game.player_list:
            initialize_hand(game, player)
    game.dora_indicators = [game.tiles_list.pop()]
    game.kan_dora_indicators = []
    game.ura_dora_indicators = []
    game.ura_kan_dora_indicators = []
    game.rinshan_count = 0
    # This is a remaining-draw budget offset, NOT a dead wall. Normal and
    # replacement draws both consume one of the 70 draws, including dealer's first.
    game.dead_wall_count = len(game.tiles_list) - 70
    game._dora_slots = []
    game._ura_slots = []

def draw_indicator(game):
    tile = game.tiles_list.pop()
    game.dead_wall_count -= 1  # indicators do not consume a player's draw
    return tile

def prepare_ura(game):
    # Peek the next draws from the same randomized pool only at a win query.
    # A declined ron must not remove tiles or alter later draws.
    count = 1 + len(game.kan_dora_indicators)
    ura = list(reversed(game.tiles_list[-count:]))
    game.ura_dora_indicators = ura[:1]
    game.ura_kan_dora_indicators = ura[1:]
