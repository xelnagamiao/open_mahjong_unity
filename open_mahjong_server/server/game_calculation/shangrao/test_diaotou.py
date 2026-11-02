import pytest

from . import rules as r


@pytest.mark.parametrize("version", [r.RULE_VERSION, r.LEGACY_VERSION])
def test_reported_two_bao_hand_cannot_use_draw_to_complete_groups(version):
    hand = [23, 23, 13, 15, 16, 24, 24, 14]
    assert r.score_hand(hand, ["k14", "g17"], indicator=22, winning_tile=14,
                        config={"rule_version": version}) is None


@pytest.mark.parametrize("triplets", [False, True])
def test_held_bao_waits_on_every_physical_tile_after_groups_are_complete(triplets):
    hand = [23, 23, 13, 13, 13, 24, 24] if triplets else [23, 23, 13, 14, 15, 24, 24]
    melds = ["k11", "g19"]
    for draw in r.TILES:
        result = r.score_hand(hand + [draw], melds, indicator=22, winning_tile=draw)
        if draw == 19:
            assert result is None
        else:
            assert result and result["shape"] == ("triplets" if triplets else "standard")


def test_triplets_cannot_use_draw_to_finish_a_triplet_instead_of_bao_pair():
    hand = [23, 23, 13, 13, 24, 24, 43, 13]
    result = r.score_hand(hand, ["k11", "g19"], indicator=22, winning_tile=13)
    assert result is None


def test_bao_restore_multiplier_requires_a_legal_diaotou_first():
    hand = [23, 23, 13, 14, 15, 24, 24, 24]
    result = r.score_hand(hand, ["k11", "g19"], indicator=22, winning_tile=24)
    assert result["multipliers"]["宝还原"] == 2


@pytest.mark.parametrize("win", [26, 16])
def test_reported_natural_bao_sequence_can_win_including_first_drawn_bao(win):
    hand = [16, 14, 14, 17, 17, 18, 18, 19, 24, 25, 26, 27, 28, 26]
    result = r.score_hand(hand, indicator=15, winning_tile=win)
    assert result["pattern"] == "平和"
    assert result["points"] == 2
    assert result["multipliers"] == {"门清": 2}


def test_natural_bao_cannot_substitute_for_another_rank_without_diaotou():
    hand = [16, 14, 14, 17, 17, 18, 18, 19, 24, 25, 26, 27, 28, 26]
    hand[1] = 15
    assert r.score_hand(hand, indicator=15, winning_tile=26) is None


def test_natural_hand_can_also_choose_a_legal_wildcard_pure_suit_decomposition():
    hand = [23, 23, 23, 11, 12, 13, 14, 15, 16, 17, 18, 19, 19, 19]
    result = r.score_hand(hand, indicator=22, winning_tile=13, config={"qingyise": True})
    assert result["pattern"] == "清一色"
    assert result["points"] == 8
    assert result["multipliers"] == {"门清": 2, "宝还原": 2}
