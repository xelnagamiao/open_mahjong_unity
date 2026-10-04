"""Small exhaustive substitution oracle, deliberately not production logic.

Only 0--2 jokers are exhaustively expanded here.  Production must never use
this approach: the common DP handles 0--4 jokers and physical assignments.
"""

from collections import Counter
from functools import lru_cache
from itertools import combinations_with_replacement
import random

from .rules import JOKER, TILES, WinContext, can_baotou, evaluate_win


@lru_cache(maxsize=32768)
def _literal_melds(tiles):
    if not tiles:
        return True
    counts = Counter(tiles)
    first = min(tiles)
    if counts[first] >= 3:
        remaining = list(tiles)
        for _ in range(3):
            remaining.remove(first)
        if _literal_melds(tuple(remaining)):
            return True
    if first < 40 and first % 10 <= 7 and counts[first + 1] and counts[first + 2]:
        remaining = list(tiles)
        for tile in (first, first + 1, first + 2):
            remaining.remove(tile)
        if _literal_melds(tuple(remaining)):
            return True
    return False


def _literal_standard(tiles):
    for tile, count in Counter(tiles).items():
        if count >= 2:
            remaining = list(tiles)
            remaining.remove(tile)
            remaining.remove(tile)
            if _literal_melds(tuple(remaining)):
                return True
    return False


def _oracle_shapes(hand, *, pairless=False):
    count = hand.count(JOKER)
    assert count <= 2
    literal = [tile for tile in hand if tile != JOKER]
    standard = pairs = False
    for assigned in combinations_with_replacement(TILES, count):
        logical = tuple(sorted(literal + list(assigned)))
        standard |= _literal_melds(logical) if pairless else _literal_standard(logical)
        pairs |= all(n % 2 == 0 for n in Counter(logical).values())
        if standard and pairs:
            break
    return standard, pairs


def _fixtures():
    rng = random.Random(2026100207)
    natural = [tile for tile in TILES if tile != JOKER]
    generated = 0
    while generated < 150:
        mode = (generated // 3) % 3
        if mode == 0:
            hand = rng.sample([tile for tile in natural for _ in range(4)], 14)
        elif mode == 1:
            hand = []
            for _ in range(4):
                if rng.randrange(2):
                    tile = rng.choice(natural)
                    hand.extend([tile] * 3)
                else:
                    first = rng.choice((11, 12, 13, 14, 15, 16, 17,
                                        21, 22, 23, 24, 25, 26, 27,
                                        31, 32, 33, 34, 35, 36, 37))
                    hand.extend([first, first + 1, first + 2])
            hand.extend([rng.choice(natural)] * 2)
        else:
            hand = [tile for tile in rng.sample(natural, 7) for _ in range(2)]
        for index in rng.sample(range(14), generated % 3):
            hand[index] = JOKER
        if max(Counter(hand).values()) > 4:
            continue
        rng.shuffle(hand)
        generated += 1
        yield hand, generated % 5


def test_150_fixed_seed_hands_match_exhaustive_low_joker_scoring_oracle():
    saw_win = saw_loss = saw_baotou = saw_pairs = False
    for hand, piao in _fixtures():
        standard, pairs = _oracle_shapes(hand)
        context = WinContext(cai_piao_count=piao, pre_draw_tiles=hand[:-1])
        score = evaluate_win(hand, win_tile=hand[-1], context=context)
        assert (score is not None) == (standard or pairs), hand
        before = list(hand[:-1])
        expected_baotou = False
        if JOKER in before:
            before.remove(JOKER)
            expected_baotou = any(_oracle_shapes(before, pairless=True))
        assert can_baotou(hand[:-1]) == expected_baotou, hand
        saw_baotou |= expected_baotou
        if score is None:
            saw_loss = True
            continue
        saw_win = True
        saw_pairs |= pairs
        expected = 0
        if pairs:
            counts = Counter(hand)
            luxury = any(n == 4 for tile, n in counts.items() if tile != JOKER)
            expected += 4 if luxury else 2
        expected += int(JOKER not in hand)
        if expected_baotou:
            expected += 1 + piao
        assert score.raw_fan == expected, hand
        assert score.fan_total == min(4, expected)
        assert score.shape == ("seven_pairs" if pairs else "standard")
    assert saw_win and saw_loss and saw_pairs and saw_baotou
