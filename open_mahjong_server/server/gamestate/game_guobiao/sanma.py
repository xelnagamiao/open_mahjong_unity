"""Three-player standard Guobiao: only the player count and 2–8m change."""

SUB_RULE = "guobiao/sanma"


def filter_tiles(tiles, sub_rule):
    if sub_rule == SUB_RULE:
        return [tile for tile in tiles if not 12 <= tile <= 18]
    return tiles


def standard_score_changes(player_count, winner, fan, discarder=None):
    """Each opponent pays 8; fan is paid by the discarder or every opponent."""
    changes = [0] * player_count
    for seat in range(player_count):
        if seat == winner:
            continue
        payment = 8 + (fan if discarder is None or seat == discarder else 0)
        changes[seat] -= payment
        changes[winner] += payment
    return changes


def cuohe_payments(player_count, cuohe_type):
    return (40, 0) if cuohe_type == 1 else (10 * (player_count - 1), 10)


def round_wind(round_number, player_count):
    return "东南西北"[(round_number - 1) // player_count]


def ron_action(winner, source, player_count):
    distance = (winner - source) % player_count
    return ("hu_self", "hu_first", "hu_second", "hu_third")[distance]
