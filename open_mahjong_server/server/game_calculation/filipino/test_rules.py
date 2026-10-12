from dataclasses import replace
import pytest
from .rules import Rules, PHYSICAL, TILES, FLOWERS, shapes, score, payments, waits

NORMAL = [11, 12, 13, 14, 15, 16, 21, 22, 23, 31, 32, 33, 35, 35, 35, 29, 29]
PAIRS = [11, 11, 12, 12, 14, 14, 16, 16, 21, 21, 25, 25, 31, 31, 37, 38, 39]


def test_physical_deck_and_honors_are_flowers():
    assert len(PHYSICAL) == 144
    assert sum(t in TILES for t in PHYSICAL) == 108
    assert sum(t in FLOWERS for t in PHYSICAL) == 36
    assert not shapes(NORMAL[:-1] + [41])


def test_five_melds_pair_and_declared_kong():
    assert shapes(NORMAL)
    assert not shapes(NORMAL[:-1])
    assert shapes(NORMAL[3:], ["s12"])
    assert shapes([14, 15, 16, 21, 22, 23, 31, 32, 33, 35, 35, 35, 29, 29], ["G11"])
    assert not shapes([11] * 5 + NORMAL[5:])
    assert not shapes(NORMAL[3:], ["s19"])


def test_seven_unique_pairs_plus_chow_and_option():
    assert any(s.kind == "seven_pairs" for s in shapes(PAIRS))
    assert any(s.kind == "seven_pairs" for s in shapes(PAIRS[:-3], ["s38"]))
    assert not shapes(PAIRS, rules=Rules(seven_pairs=False))
    assert 39 in waits(PAIRS[:-1])


@pytest.mark.parametrize("config", [{"base_points": True}, {"ambitions": 1}, {"base_points": -1},
    {"self_draw_multiplier": 3}, {"discard_payment": "foo"}, {"dealer_mode": "foo"},
    {"kong_points": 101}, {"unimplemented": True}, {"rule_version": "future"}])
def test_rejects_invalid_config(config):
    with pytest.raises(ValueError):
        Rules.from_dict(config)


def test_config_round_trips_and_scoring_switches():
    rules = Rules(ambitions=False)
    assert Rules.from_dict(rules.snapshot()) == rules
    assert score(NORMAL, winning_tile=29, rules=rules).points == 4
    assert score(NORMAL, winning_tile=29, rules=Rules()).points > 4
    assert score(NORMAL, winning_tile=19) is None


@pytest.mark.parametrize("winner", range(4))
@pytest.mark.parametrize("mode", ["all", "discarder"])
def test_conservative_payments(winner, mode):
    payer = (winner + 1) % 4
    changes = payments(winner, 4, payer=payer, rules=Rules(discard_payment=mode))
    assert sum(changes) == 0
    assert changes[winner] == (12 if mode == "all" else 4)
    changes = payments(winner, 4, self_draw=True)
    assert changes[winner] == 24 and sum(changes) == 0
    changes = payments(winner, 4, self_draw=True, rules=Rules(self_draw_multiplier=1), multiplier=2)
    assert changes[winner] == 24
