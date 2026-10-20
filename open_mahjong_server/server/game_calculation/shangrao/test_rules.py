import pytest
from . import rules as r

NORMAL = [11, 12, 13, 14, 15, 16, 22, 23, 24, 31, 32, 33, 41, 41]
LAN = [11, 14, 17, 21, 24, 27, 31, 34, 41, 42, 43, 44, 45, 46]


@pytest.mark.parametrize("indicator,expected", [(11, 12), (19, 11), (29, 21), (39, 31),
    (41, 42), (42, 43), (43, 44), (44, 41), (45, 47), (47, 46), (46, 45)])
def test_bao_cycles(indicator, expected):
    assert r.bao_tile(indicator) == expected


@pytest.mark.parametrize("hand,indicator,win,pattern,points", [
    (NORMAL, 45, 41, "平和", 2),
    (NORMAL[:-1] + [47], 45, 41, "钓头", 2),
    ([11,11,19,19,21,21,29,29,31,31,39,41,41,45], 46, 45, "九幺", 2),
    ([11,11,19,21,29,31,39,41,42,43,44,45,46,47], 18, 47, "七星九幺", 4),
    (LAN, 18, 46, "十三烂", 2),
    ([t for t in LAN if t != 34] + [47], 18, 47, "七星十三烂", 4),
    ([11,11,13,13,15,15,21,21,23,23,25,25,31,47], 45, 31, "七对", 2),
    ([11,11,13,13,15,15,21,21,23,23,25,25,31,31], 45, 31, "硬七对", 4),
    ([11]*4 + [13,13,21,21,23,23,25,25,31,47], 45, 31, "豪华七对", 4),
    ([11]*4 + [13,13,21,21,23,23,25,25,31,31], 45, 31, "豪华硬七对", 8),
    ([11]*3 + [22]*3 + [33]*3 + [44]*3 + [41,47], 45, 41, "碰碰和", 4),
    ([11]*3 + [22]*3 + [33]*3 + [44]*3 + [41,41], 45, 41, "清碰", 8),
    ([11,11,19,19,21,21,29,29,31,31,39,39,41,41], 45, 41, "九幺硬七", 8),
    ([11]*3 + [19]*3 + [21]*3 + [29]*3 + [41,41], 45, 41, "九幺清碰", 16),
])
def test_all_fourteen_base_patterns(hand, indicator, win, pattern, points):
    result = r.score_hand(hand, indicator=indicator, winning_tile=win)
    assert result and (result["pattern"], result["points"]) == (pattern, points)


def test_first_bao_cannot_complete_pair_but_next_non_bao_can():
    hand = NORMAL[:-1] + [47]
    assert r.standard_pairs(hand, indicator=45)
    assert r.score_hand(hand, indicator=45, winning_tile=47) is None
    assert r.score_hand(hand, indicator=45, winning_tile=41)["points"] == 2
    assert r.score_hand(hand, indicator=45, winning_tile=47, opening=True)["points"] == 4
    assert r.score_hand([t for t in LAN if t != 34] + [47], indicator=45, winning_tile=47)["pattern"] == "七星十三烂"


def test_one_bao_cannot_be_used_in_group_with_natural_pair():
    hand = [11,12,47,21,22,23,31,32,33,44,44,44,41,41]
    assert not r.standard_pairs(hand, indicator=45)
    assert r.score_hand(hand, indicator=45, winning_tile=41) is None
    hand[-1] = 47
    assert r.standard_pairs(hand, indicator=45)


def test_seven_pairs_reserves_held_bao_for_the_actual_winning_tile():
    six_pairs = [11,11,13,13,15,15,21,21,23,23,25,25]
    assert r.seven_pairs(six_pairs + [47,47], indicator=45)
    assert r.seven_pairs([11,11,13,13,15,15,21,21,23,23,25,47,47,47], indicator=45, winning_tile=25)
    assert r.seven_pairs([11,11,13,13,15,15,21,21,23,23,25,31,47,47], indicator=45, winning_tile=31)
    assert r.score_hand(six_pairs + [31,47], indicator=45, winning_tile=47) is None


@pytest.mark.parametrize("before,win,allowed", [
    ([11,11,12,12,13,13,14,14,15,15,16,16,17], 47, False),
    ([11,11,12,12,13,13,14,14,15,15,16,47,17], 16, False),
    ([11,11,12,12,13,13,14,14,15,15,16,47,17], 17, False),
    ([11,11,12,12,13,13,14,14,15,15,16,16,47], 29, True),
    ([11,11,12,12,13,13,14,14,15,15,16,16,47], 47, True),
    ([11,11,13,13,15,15,21,21,23,23,25,47,47], 31, True),
    ([11,11,13,13,15,15,21,21,23,25,47,47,47], 31, True),
    ([11,11,13,13,15,15,21,23,25,47,47,47,47], 31, True),
    ([11,11,13,13,15,15,21,21,23,25,31,47,47], 23, False),
])
def test_updated_seven_pairs_examples(before, win, allowed):
    assert r.seven_pairs(before + [win], indicator=45, winning_tile=win) == allowed


def test_seven_pairs_change_does_not_restrict_standard_bao_groups():
    hand = [11,11,12,12,13,13,14,14,15,15,16,47,17,16]
    assert not r.seven_pairs(hand, indicator=45, winning_tile=16)
    assert r.score_hand(hand, indicator=45, winning_tile=16)["shape"] == "standard"


@pytest.mark.parametrize("hand,win,restored", [
    ([11,11,13,13,15,15,21,21,23,25,47,47,47,31], 31, False),
    ([11,11,13,13,15,15,21,21,23,23,47,47,47,31], 31, True),
    ([11,11,13,13,15,15,21,23,25,47,47,47,47,31], 31, False),
    ([11,11,13,13,15,15,21,21,25,47,47,47,47,31], 31, True),
])
def test_multi_bao_seven_pairs_restoration_requires_two_free_bao(hand, win, restored):
    result = r.score_hand(hand, indicator=45, winning_tile=win)
    assert result["pattern"] == "七对"
    assert ("宝还原" in result["multipliers"]) == restored
    assert result["points"] == (4 if restored else 2)


def test_multipliers_stack_and_kongs_break_concealment():
    hand, melds = [41,47], ["k11", "k22", "g33", "G44"]
    result = r.score_hand(hand, melds, indicator=45, winning_tile=41, after_kong=True, flying_bao=2)
    assert result["points"] == 2 * 2 * 2 * 4
    assert result["multipliers"] == {"单钓": 2, "杠上开花": 2, "飞宝": 4}
    assert r.score_hand([47,47], melds, indicator=45, winning_tile=47)["points"] == 8
    assert r.score_hand(NORMAL[:-2] + [47,47], indicator=45, winning_tile=33)["multipliers"]["宝还原"] == 2


def test_qingyise_multiplies_seven_pairs_and_rejects_off_suit_draw():
    hand = [11,11,13,13,15,15,17,17,18,18,19,19,16,16]
    q = r.score_hand(hand, indicator=45, winning_tile=16, config={"qingyise": True, "rule_version": r.LEGACY_VERSION})
    assert q["points"] == 8 and q["multipliers"] == {"清一色": 2}
    single = [11,12,13,14,15,16,17,18,19,12,12,12,47,47]
    assert "清一色" not in r.score_hand(single, indicator=45, winning_tile=47, config={"qingyise": True})["multipliers"]
    assert r.score_hand(single, indicator=45, winning_tile=12, config={"qingyise": True})["pattern"] == "清一色"
    assert "清一色" not in r.score_hand([11,12,13,14,15,16,19,47], ["k47", "s12"], indicator=45, winning_tile=19, config={"qingyise": True})["multipliers"]


def test_ziyise_and_special_bao_interaction():
    hand = [41,41,42,42,43,43,44,44,45,45,47,47,12,12]
    q = r.score_hand(hand, indicator=11, winning_tile=41,
                     config={"allow_special_bao": True, "ziyise": True, "rule_version": r.LEGACY_VERSION})
    assert q["points"] == 8 and q["multipliers"]["字一色"] == 2
    assert "字一色" not in r.score_hand(hand, indicator=11, winning_tile=41,
                                       config={"ziyise": True})["multipliers"]


def test_lan_bao_is_natural_unless_enabled_and_still_needs_space():
    hand = [11,14,17,21,24,27,31,34,41,42,43,44,45,12]
    assert not r.thirteen_lan(hand, indicator=11)
    assert r.thirteen_lan(hand, indicator=11, allow_bao=True)
    assert not r.thirteen_lan([11,13,17,21,24,27,31,34,41,42,43,44,45,12], indicator=11, allow_bao=True)
    assert not r.thirteen_lan([11,11,17,21,24,27,31,34,41,42,43,44,45,12], indicator=11, allow_bao=True)


def test_flying_seven_stars_requires_history_and_switch():
    hand = [11,14,17,21,24,27,31,34,41,42,43,44,45,46]
    assert r.score_hand(hand, indicator=45, winning_tile=46)["pattern"] == "十三烂"
    assert r.score_hand(hand, indicator=45, winning_tile=46, flying_seven_stars=True,
                        config={"flying_seven_stars": True})["pattern"] == "七星十三烂"
    assert r.score_hand(hand, indicator=45, winning_tile=46,
                        config={"flying_seven_stars": True})["pattern"] == "十三烂"


@pytest.mark.parametrize("config", [{"other": True}, {"qingyise": 1}, {"four_bao_reward": "yes"}, {"rule_version": "future"}, []])
def test_reject_invalid_options(config):
    with pytest.raises(ValueError):
        r.normalize_config(config)


@pytest.mark.parametrize("hand,melds", [(NORMAL + [11], []), (NORMAL[:-1], []),
    ([11]*4 + [22]*3 + [33]*3 + [41]*4, []), (NORMAL[3:], ["s19"]), (NORMAL[3:], ["s42"])])
def test_reject_invalid_physical_hand(hand, melds):
    assert not r.valid_hand(hand, melds, indicator=41)


@pytest.mark.parametrize("seat", range(4))
def test_each_opponent_pays_full_points(seat):
    changes = r.payments(seat, 16)
    assert changes[seat] == 48 and sum(changes) == 0
    assert all(v == -16 for i, v in enumerate(changes) if i != seat)
