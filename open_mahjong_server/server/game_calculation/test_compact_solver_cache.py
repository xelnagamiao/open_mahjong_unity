from collections import Counter

import pytest

from .compact_counter import pack_counter, unpack_counter
from .hongkong import solver as hk
from .hongkong.models import HongKongRules, SUPPORTED_PROFILES
from .taiwan import solver as tw
from .taiwan.rules import TaiwanRules


def test_counter_key_is_canonical_and_distinguishes_counts():
    first = Counter({47: 4, 11: 1, 22: 2, 35: 3, 41: 0})
    reordered = Counter({35: 3, 22: 2, 11: 1, 47: 4})
    key = pack_counter(first)
    assert isinstance(key, bytes)
    assert key == pack_counter(reordered)
    assert unpack_counter(key) == +first
    assert pack_counter(Counter({11: 2, 22: 1, 35: 3, 47: 4})) != key
    assert unpack_counter(pack_counter(Counter())) == Counter()


def _standard_hand(meld_count, exposed):
    # Four distinct melds plus a pair; prepend the fifth meld for 16-tile rules.
    hand = [11, 12, 13, 14, 15, 16, 21, 22, 23, 37, 38, 39, 45, 45]
    if exposed:
        hand = hand[3:]
    if meld_count == 5:
        hand += [41, 41, 41]
    return hand


@pytest.mark.parametrize("profile", SUPPORTED_PROFILES)
@pytest.mark.parametrize("melds", [(), ("s12",), ("G47",)])
def test_hongkong_profiles_closed_chow_and_kong_keep_wait_and_win(profile, melds):
    rules = HongKongRules(profile)
    hand = _standard_hand(rules.structure.meld_count, bool(melds))
    shapes = hk.decompositions(hand, melds, rules, winning_tile=45)
    assert any(shape.kind == "standard" for shape in shapes)
    before = list(hand)
    before.remove(45)
    assert 45 in hk.structural_waits(before, melds, rules)
    assert hk.decompositions(hand[::-1], melds, rules, winning_tile=45) == shapes
    if melds == ("G47",):
        invalid = [47 if tile == 45 else tile for tile in hand]
        assert hk.decompositions(invalid, melds, rules) == ()
        assert hk.structural_waits(invalid[:-1], melds, rules) == set()


@pytest.mark.parametrize("melds", [(), ("s12",), ("G47",)])
def test_taiwan_closed_chow_and_kong_keep_wait_and_win(melds):
    hand = _standard_hand(5, bool(melds))
    shapes = tw.enumerate_decompositions(hand, melds, winning_tile=45)
    assert shapes
    before = list(hand)
    before.remove(45)
    assert 45 in tw.structural_waits(before, melds)
    assert tw.enumerate_decompositions(hand[::-1], melds, winning_tile=45) == shapes
    if melds == ("G47",):
        invalid = [47 if tile == 45 else tile for tile in hand]
        assert tw.enumerate_decompositions(invalid, melds) == []
        assert tw.structural_waits(invalid[:-1], melds) == set()


def test_taiwan_special_rule_remains_part_of_wait_cache_key():
    hand = [11] * 3 + [tile for tile in (19, 21, 29, 31, 39, 41, 47) for _ in range(2)]
    hand.remove(47)
    assert 47 in tw.structural_waits(hand, (), TaiwanRules(eight_and_a_half_pairs_enabled=True))
    assert 47 not in tw.structural_waits(hand, (), TaiwanRules(eight_and_a_half_pairs_enabled=False))


def test_repeated_chows_keep_all_decompositions():
    hand = [11, 12, 13] * 2 + [27, 28, 29] * 2 + [45, 45]
    shapes = hk.decompositions(hand, winning_tile=13)
    assert shapes
    assert all(shape.kind == "standard" and len(shape.melds) == 4 for shape in shapes)
    hand += [41] * 3
    assert tw.enumerate_decompositions(hand, (), winning_tile=13)


def test_compacting_keys_retains_original_entry_capacities():
    for module, capacities in (
        (tw, {"_meld_partitions": 32768, "_can_form_melds": 131072,
              "_has_standard_shape": 65536, "_structural_waits_cached": 32768}),
        (hk, {"_partitions": 65536, "_decompositions": 32768, "_waits": 32768}),
    ):
        for name, capacity in capacities.items():
            assert getattr(module, name).cache_parameters()["maxsize"] == capacity
