from . import rules, scoring, w_rules, w_scoring
from .tile import HongqueTile


def rules_for(state):
    return w_rules if w_rules.is_w(state) else rules


def tile_code(state, tile):
    return w_rules.normalize(tile) if w_rules.is_w(state) else HongqueTile.parse(tile or "").code


def heavenly(state, player):
    if w_rules.is_w(state):
        return not player.discards and not any(item.melds for item in state.players)
    return not any(item.discards for item in state.players)


def score_for_state(state, player, hand=None, *, self_draw=True, allow_kong_win=True):
    calculator = w_scoring if w_rules.is_w(state) else scoring
    limits = {"minimum_fan": getattr(state, "hongque_min_fan", 0),
              "minimum_points": getattr(state, "hongque_min_points", 0)} if w_rules.is_w(state) else {}
    result = calculator.best_win_result(
        player.hand if hand is None else hand, player.melds, self_draw=self_draw,
        before_first_discard=self_draw and heavenly(state, player), wall_empty=not state.wall,
        allow_kong_win=self_draw and allow_kong_win, **limits)
    if result is not None and w_rules.is_w(state):
        if result["fan_total"] < getattr(state, "hongque_min_fan", 0) or result["points"] < getattr(state, "hongque_min_points", 0):
            return None
    return result
