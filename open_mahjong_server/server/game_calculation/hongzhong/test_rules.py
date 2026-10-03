"""MIL 原书牌例、实体/逻辑边界，以及独立无癞回溯的随机差分。"""
from collections import Counter
from functools import lru_cache
from itertools import combinations_with_replacement
import random

import pytest

from . import rules as book
from ..jokers import iter_winning_shapes


@pytest.mark.parametrize("hand,melds,win,fans", [
    ([28,28,19,45], ["k15","k16","k27"], 19, {"大对子"}),
    ([28,28,19,45], ["k15","k16","k27"], 28, {"大对子"}),
    ([11,12,13,14,15,16,17,19,27,45], ["k32"], 18, {"一条龙"}),
    ([11,12,13,14,15,16,17,19,27,45], ["k32"], 27, {"一条龙"}),
    ([24,24,27,28,28,29,45,16,16,38,38,39,39], [], 27, {"七对子"}),
    ([24,24,27,28,28,29,45,16,16,38,38,39,39], [], 29, {"七对子"}),
    ([12,13,14,16,17,18,18], ["k11","k15"], 15, {"清一色","无红中"}),
    ([28,28,45,29], ["k21","k22","k24"], 29, {"清一色","大对子"}),
    ([28,28,45,29], ["k21","k22","k24"], 28, {"清一色","大对子"}),
    ([31,31,32,32,34,34,35,35,37,38,38,39,39], [], 37, {"清一色","七对子","无红中"}),
    ([24,24,24,28,28,29,45,16,16,38,38,39,39], [], 24, {"龙七对"}),
])
def test_pdf_page9_images(hand, melds, win, fans):
    # 图片中的目标番之外，仍叠加“无红中”等满足定义的其他番。
    detail = book.score(hand + [win], melds, winning_tile=win)
    assert detail is not None and set(detail["fan_names"]) == fans
    assert detail["raw_fan"] == sum(book.FANS[f] for f in fans)
    assert detail["fan"] == min(4, detail["raw_fan"])
    assert Counter(detail["logical_hand"]).most_common(1)[0][1] <= 4


def test_book_closed_cap_excludes_external_melds_but_physical_supply_does_not():
    hand = [11,12,13,14,14,14,14,15,15,16,16,17,17]
    assert book.waits(hand) == {11,17,45}
    assert not book.is_complete(hand + [14])
    shapes = list(iter_winning_shapes(hand + [45], (), book.policy(), winning_tile=45))
    assert {s.winning_logical_tile for s in shapes} == {11,17}
    opened = [11,12,13,14,15,15,16,16,17,17]
    # A real14 would be a physical fifth copy; a red logically used as14 is legal.
    assert book.waits(opened, ["k14"]) == {11,17,45}
    shapes = list(iter_winning_shapes(opened + [45], ["k14"], book.policy(), winning_tile=45))
    assert {s.winning_logical_tile for s in shapes} == {11,14,17}
    konged = [11,12,15,15,16,16,17,17,19,19]
    assert book.waits(konged, ["G13"]) == {45}
    result = book.score(konged + [45], ["G13"], winning_tile=45)
    assert result["winning_logical_tile"] == 13
    assert book.score(konged + [13], ["G13"], winning_tile=13) is None


def test_long_pairs_win_must_be_fourth_and_red_may_be_fourth():
    closed = [24]*4 + [28]*2 + [29] + [16]*2 + [38]*2 + [39]*2
    ordinary = book.score(closed + [29], winning_tile=29)
    assert ordinary["fan_names"] == ["无红中", "七对子"]
    red_hand = [24]*3 + [28]*2 + [29]*2 + [16]*2 + [38]*2 + [39]*2
    red = book.score(red_hand + [45], winning_tile=45)
    assert red["fan_names"] == ["龙七对"] and red["winning_logical_tile"] == 24


@pytest.mark.parametrize("red_count", range(5))
def test_zero_to_four_red_preserve_identity_and_use_highest_fan(red_count):
    hand = [11,11,12,12,13,13,14,14,15,15,16,16,17,17]
    if red_count:
        hand[-red_count:] = [45]*red_count
    detail = book.score(hand, winning_tile=hand[-1])
    assert detail and book.is_complete(hand)
    shapes = list(iter_winning_shapes(hand, (), book.policy(), winning_tile=hand[-1]))
    candidate_fans = [book.score_interpretation(hand, (), book.Interpretation(s.kind,
        s.closed_logical_tiles, tuple(g.logical for g in s.melds), s.winning_logical_tile))["raw_fan"] for s in shapes]
    assert detail["raw_fan"] == max(candidate_fans)
    assert len(detail["joker_substitutions"]) == red_count
    assignment = dict((use[0],use[2]) for use in detail["joker_substitutions"])
    assert Counter(assignment.get(i,t) for i,t in enumerate(hand)) == Counter(detail["logical_hand"])
    assert ("无红中" in detail["fan_names"]) == (red_count == 0)
    detail["logical_hand"].clear()
    assert book.score(hand)["logical_hand"]  # 返回对象不能污染缓存。


def test_pure_joker_pair_and_meld_and_plain_zero_fan():
    hand = [11,12,13,22,23,24,34,35,36,27,27,27,45,45]
    assert book.score(hand)["fan_names"] == ["平和"]
    all_ghost_meld = [11,12,13,22,23,24,34,35,36,28,28,45,45,45]
    assert book.is_complete(all_ghost_meld)
    assert any(all(t == 45 for t in g.physical) and len(g.physical)==3
               for s in iter_winning_shapes(all_ghost_meld, (), book.policy(), winning_tile=45)
               for g in s.melds)


def test_supplement_adds_exact_one_and_physical_win_tile_validation():
    hand = [11,12,13,22,23,24,34,35,36,27,27,27,28,45]
    assert book.score(hand, replacement=True)["fan_names"] == ["杠上花"]
    assert book.score(hand, winning_tile=29) is None
    assert book.score(hand, winning_tile=True) is None
    assert book.score(hand, replacement=1) is None
    assert book.score(None) is None
    assert not book.waits([11]*14)
    assert not book.is_complete([11]*14)
    # 合法牌数但结构无解，不能让四红中的便利扩散为随意成和。
    bad = [11,12,14,16,18,21,23,25,27,29,31,33,35,37]
    assert book.score(bad) is None and not book.is_complete(bad)


@lru_cache(maxsize=32768)
def natural_win(tiles):
    """独立的去将/递归三张检查，不调用共用核心。"""
    c = Counter(tiles)
    if any(n > 4 for n in c.values()):
        return False
    if len(tiles) == 14 and all(n % 2 == 0 for n in c.values()):
        return True
    def meldable(rest):
        if not rest:
            return True
        t = min(rest)
        if rest[t] >= 3:
            next_counts = rest.copy(); next_counts[t] -= 3
            if not next_counts[t]: del next_counts[t]
            if meldable(next_counts): return True
        if t % 10 <= 7 and rest[t+1] and rest[t+2]:
            next_counts = rest.copy()
            for value in (t,t+1,t+2):
                next_counts[value] -= 1
                if not next_counts[value]: del next_counts[value]
            if meldable(next_counts): return True
        return False
    for t in c:
        if c[t] >= 2:
            rest = c.copy(); rest[t] -= 2
            if not rest[t]: del rest[t]
            if meldable(rest): return True
    return False


def brute_win(hand):
    natural = [t for t in hand if t != 45]
    return any(natural_win(tuple(sorted(natural + list(assignment))))
               for assignment in combinations_with_replacement(book.NUMBERS, hand.count(45)))


def test_seeded_random_differential_zero_to_two_jokers():
    randomizer = random.Random(20241002)
    wall = [t for t in book.NUMBERS for _ in range(4)]
    for count in range(3):
        for _ in range(30):
            hand = randomizer.sample(wall, 14-count) + [45]*count
            expected = brute_win(hand)
            assert book.is_complete(hand) == expected, hand
            assert bool(book.score(hand, winning_tile=hand[-1])) == expected, hand


def test_score_upper_bound_pruning_matches_complete_enumeration():
    randomizer = random.Random(20241003)
    for red_count in range(5):
        for case in range(12):
            while True:
                if case % 2:
                    natural = [t for t in randomizer.sample(book.NUMBERS, 7) for _ in range(2)]
                else:
                    natural = []
                    for _ in range(4):
                        suit, rank = randomizer.choice((1,2,3)), randomizer.randint(1,7)
                        natural.extend(suit*10+rank+offset for offset in range(3))
                    natural.extend([randomizer.choice(book.NUMBERS)]*2)
                if max(Counter(natural).values()) <= 4:
                    break
            randomizer.shuffle(natural)
            if red_count:
                natural[-red_count:] = [45]*red_count
            result = book.score(natural)
            assert result is not None
            all_scores = [book.score_interpretation(natural, (), book.Interpretation(shape.kind,
                shape.closed_logical_tiles, tuple(group.logical for group in shape.melds),
                shape.winning_logical_tile))["raw_fan"]
                for shape in iter_winning_shapes(natural, (), book.policy(), winning_tile=natural[-1])]
            assert result["raw_fan"] == max(all_scores), (natural, result, all_scores)


def test_four_melds_with_two_red_pair_ignore_exposed_logical_cap():
    melds = ["k11", "k21", "G31", "g19"]
    result = book.score([45,45], melds, winning_tile=45)
    assert result["fan_names"] == ["大对子"]
    assert len(result["joker_substitutions"]) == 2
    # 已杠出的19、31没有第五张实体牌；红中仍能逻辑替代这两个牌面。
    assert book.waits([45], melds) == set(book.TILES) - {19,31}
