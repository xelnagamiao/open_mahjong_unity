import pytest

from .rules import score_hand, waiting_tiles, maximum_ready_score, TILES


PLAIN = [11,12,13,14,15,16,21,22,23,34,35,36,28,28]
PUNGS = [11,11,11,22,22,22,33,33,33,38,38,38,29,29]
PAIRS = [11,11,13,13,22,22,24,24,31,31,35,35,39,39]
DRAGON = [11,11,11,11,22,22,24,24,31,31,35,35,39,39]


@pytest.mark.parametrize("hand,melds,expected,ids", [
    (PLAIN, [], 3, ("plain",)),
    (PUNGS, [], 8, ("all_pungs",)),
    (PAIRS, [], 13, ("seven_pairs",)),
    (DRAGON, [], 26, ("dragon_pairs",)),
    ([11,12,13,14,15,16,17,18,19,17,18,19,15,15], [], 13, ("pure",)),
    ([11,11,11,12,12,12,13,13,13,14,14,14,15,15], [], 21, ("pure","all_pungs")),
    ([29,29], ["k11","G22","k33","g38"], 13, ("single",)),
    ([15,15], ["k11","G12","k13","g14"], 26, ("pure","single")),
])
def test_rulebook_hand_values(hand, melds, expected, ids):
    result = score_hand(hand, melds)
    assert (result.points, result.fan_ids) == (expected, ids)


@pytest.mark.parametrize("kind,expected", [("soft_ready",13),("hard_ready",26)])
def test_opening_ready_replaces_plain_base(kind, expected):
    assert score_hand(PLAIN, ready=kind).points == expected
    assert score_hand(PUNGS, ready=kind).points == min(39,expected+8)


@pytest.mark.parametrize("context,points", [({},3),({"kong_draw":True},4),({"hot_discard":True},4),({"rob_kong":True},3)])
def test_plain_context_and_passport(context, points):
    result = score_hand(PLAIN, **context)
    assert result.points == points
    assert result.requires_passport == (not context)


def test_cap_after_situational_bonus_and_nonstacking():
    result = score_hand(DRAGON, ready="hard_ready", kong_draw=True)
    assert result.points == 39 and result.raw_points == 53
    assert "seven_pairs" not in result.fan_ids
    assert score_hand(PLAIN, heavenly=True).points == 26


@pytest.mark.parametrize("hand,melds", [
    (PLAIN[:-1], []), (PLAIN+[11], []), ([41]*14, []), ([11]*14, []),
    ([11,11], ["k11","k12","k13","k14"]),
    ([29,29], ["s12","k22","k33","k38"]),
    (PLAIN, ["bad"]),
])
def test_invalid_physical_hands(hand, melds):
    assert score_hand(hand, melds) is None


def test_rulebook_page_13_dead_wait_is_ready_but_cannot_physically_win():
    # Example 1: the fourth 9m is held, the other three are an exposed pung.
    hand, melds = [21,21,21,19], ["k18","k19","k24"]
    assert 19 in waiting_tiles(hand,melds)
    assert 19 not in waiting_tiles(hand,melds,theoretical=False)
    assert maximum_ready_score(hand,melds) == 8
    assert score_hand(hand+[19],melds) is None
    assert score_hand(hand+[19],melds,theoretical=True).points == 8


def test_rulebook_page_13_four_copies_in_concealed_hand_are_not_ready():
    assert not waiting_tiles([19]*4,["k18","k21","k24"])
    assert maximum_ready_score([19]*4,["k18","k21","k24"]) == 0


def test_physical_waits_no_honors_and_ready_no_wall_visibility_dependency():
    waits = waiting_tiles(PLAIN[:-1],theoretical=False)
    assert 28 in waits and waits <= set(TILES)
    assert maximum_ready_score(PLAIN[:-1], ready="hard_ready") == 26


def test_invalid_ready_name_rejected():
    with pytest.raises(ValueError):
        score_hand(PLAIN, ready="ordinary")
