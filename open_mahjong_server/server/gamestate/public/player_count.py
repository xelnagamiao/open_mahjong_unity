"""Seat count for subrules shared by rooms, game factories and record readers."""


def player_count_for_sub_rule(sub_rule):
    return 3 if sub_rule in ("riichi/sanma", "guobiao/sanma") else 4


def game_player_count(state):
    """Live tables use their seats; legacy protocol-only callers default to four."""
    players = getattr(state, "player_list", None)
    return len(players) if players is not None else 4
