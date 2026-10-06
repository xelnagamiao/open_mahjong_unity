"""Three-player riichi constants and payment policy; table authority stays on the server."""

SANMA = 'riichi/sanma'
REMOVED_TILES = frozenset(range(12, 19)) | {105}


def starting_score(sub_rule):
    return 35000 if sub_rule == SANMA else 50000 if sub_rule == 'riichi/langyong' else 25000


def player_count(sub_rule):
    return 3 if sub_rule == SANMA else 4


def replacement_index(count, sanma=False):
    # Keep all five dora/ura pairs out of either draw stream. After the first
    # four replacements the ten indicator tiles occupy the end of the wall.
    return -11 if sanma and count >= 4 else -1


def dora_indicator(tile, sanma=False):
    # The library cycles all nine manzu. 8m's successor is 9m, so translate 1m.
    return 18 if sanma and tile == 11 else tile


def tsumo_payments(cost, winner, count=4, mode='loss'):
    """Per-payer base points, without honba/deposits. Split rounds each share up to 100."""
    payments = {i: int(cost.get('main' if winner == 0 or i == 0 else 'additional', 0))
                for i in range(count) if i != winner}
    if count == 3 and mode == 'split':
        missing = int(cost.get('main' if winner == 0 else 'additional', 0))
        share = ((missing + 199) // 200) * 100
        payments = {i: amount + share for i, amount in payments.items()}
    return payments
