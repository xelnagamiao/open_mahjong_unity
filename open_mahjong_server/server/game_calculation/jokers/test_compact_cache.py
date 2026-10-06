"""Compatibility checks for physical identities and cached policy domains."""
from dataclasses import replace

import pytest

from . import JokerPolicy, can_win, clear_caches, structural_waits
from .solver import _group_profile, _minimum_group_jokers, _standard, _pairs, _waits


class Tile(int):
    pass


@pytest.mark.parametrize("bad", [Tile(11), 11.0, True, -1, 256])
def test_packing_does_not_coerce_invalid_tile_identities(bad):
    hand = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41, 42]
    assert 42 in structural_waits(hand)
    hand[0] = bad
    assert structural_waits(hand) == frozenset()
    assert not can_win(hand + [42])


def test_cached_masks_keep_joker_and_pair_domains_separate():
    clear_caches()
    hand = [11, 12, 46, 21, 22, 23, 31, 32, 33, 41, 41, 41, 42, 42]
    policy = JokerPolicy(joker_tiles=(46,), joker_targets=(13,), pair_tiles=(42,))
    policies = [policy, replace(policy, joker_targets=(14,)),
                replace(policy, pair_tiles=(41,)),
                replace(policy, logical_tiles=tuple(reversed(policy.logical_tiles)))]
    # Run twice and reverse the policy order; no warmed key may reuse a mask
    # from a policy with different targets or pair identities.
    for order in (policies, list(reversed(policies))):
        for current in order:
            expected = current.joker_targets == (13,) and current.pair_tiles == (42,)
            assert can_win(hand, policy=current) is expected
            assert (42 in structural_waits(hand[:-1], policy=current)) is expected


def test_structural_cache_capacities_are_retained():
    for function, capacity in ((_group_profile, 65536), (_minimum_group_jokers, 32768),
                               (_standard, 32768), (_pairs, 32768), (_waits, 16384)):
        assert function.cache_info().maxsize == capacity
