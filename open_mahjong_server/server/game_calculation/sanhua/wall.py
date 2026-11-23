"""Physical wall in dealing order, starting with the upper tile of the opening stack."""
import random

from ..jokers import TILES

RED, WHITE, GREEN = 45, 46, 47


def special_tiles(indicator):
    if indicator not in TILES:
        raise ValueError("无效的翻明牌")
    if indicator < 40:
        return indicator, indicator // 10 * 10 + indicator % 10 % 9 + 1
    if indicator <= 44:
        return indicator, 41 + (indicator - 40) % 4
    return (GREEN, WHITE) if indicator == GREEN else (WHITE, GREEN)


def deal(seed, dealer, *, wall=None, dice=None):
    from collections import Counter

    rng = random.Random(seed)
    deck = [t for t in TILES for _ in range(4)]
    if wall is None:
        rng.shuffle(deck)
    elif Counter(deck) == Counter(wall):
        deck = list(wall)
    else:
        raise ValueError("散花天才麻将需要136张无花标准牌")
    dice = tuple(dice) if dice is not None else (rng.randint(1, 6), rng.randint(1, 6))
    if len(dice) != 2 or any(type(d) is not int or not 1 <= d <= 6 for d in dice):
        raise ValueError("需要两枚六面骰")
    side = (dealer + sum(dice) - 1) % 4
    first = 34 * side
    start = (first + 2 * min(dice)) % 136
    deck = deck[start:] + deck[:start]
    marker = (first - start) % 136
    hands = [[] for _ in range(4)]
    for packet in range(12):
        hands[(dealer + packet) % 4].extend(deck[packet * 4:packet * 4 + 4])
    for offset in range(4):
        hands[(dealer + offset) % 4].append(deck[48 + offset])
    for hand in hands:
        hand.sort()
    hands[dealer].append(deck[52])
    return hands, deck[53:], marker - 53, deck[marker], dice
