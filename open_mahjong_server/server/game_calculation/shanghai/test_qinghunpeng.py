import pytest

from . import qinghunpeng as rule


@pytest.mark.parametrize("hand,melds,flowers,expected", [
    ([11,12,13,14,15,16,17,18,19,11,12,13,41,41], [], [], 1),
    ([11,12,13,14,15,16,17,18,19,45,45,45,41,41], [], [], 3),
    ([11,12,13,14,15,16,17,18,19,42,42,42,41,41], [], [51,52], 4),
    ([11,11,11,22,22,22,33,33,33,41,41,41,45,45], [], [], 2),
    ([11,12,13,14,15,16,17,18,19,11,12,13,15,15], [], [51,52], 10),
    ([11,11,11,12,12,12,13,13,13,41,41,41,45,45], [], [51], 10),
    ([11,11,11,12,12,12,13,13,13,14,14,14,15,15], [], [51], 20),
    ([41,41,42,42,43,43,44,44,45,45,46,46,47,47], [], [], 20),
    ([41,41,41,42,42,42,45,45,45,46,46,46,47,47], [], [], 40),
    ([11,12,13,14,15,16,17,18,19,41,41], ["G45"], [51,52,53,54,55,56,57,58], 10),
    ([45,45], ["s12","s15","s18","k41"], [], 10),
    ([41,41,42,42,43,44,45,46,47,47,45,46,44,43], [], [], 20),
])
def test_rulebook_scores(hand, melds, flowers, expected):
    assert rule.score(hand, melds, flowers, self_draw=True)["base_score"] == expected


def test_one_flower_cannot_ron_but_extra_can_supply_minimum():
    hand = [11,12,13,14,15,16,17,18,19,11,12,13,41,41]
    assert rule.score(hand) is None
    assert rule.score(hand, self_draw=True)["base_score"] == 1
    assert rule.score(hand, flowers=[51])["base_score"] == 2
    assert rule.score(hand, rob_kong=True)["base_score"] == 10
    assert rule.score(hand, last_tile=True)["base_score"] == 10


def test_extras_do_not_stack_or_create_starting_fan():
    hand = [11,12,13,14,15,16,17,18,19,45,45,45,41,41]
    assert rule.score(hand, self_draw=True, replacement=True, last_tile=True)["base_score"] == 10
    plain = [11,12,13,21,22,23,31,32,33,17,18,19,41,41]
    assert rule.score(plain, self_draw=True, replacement=True, last_tile=True) is None


@pytest.mark.parametrize("tile,pung,open_kong,closed_kong", [(11,0,1,2),(41,1,2,3),(45,2,3,4)])
def test_triplet_flower_table(tile, pung, open_kong, closed_kong):
    for kind, expected in [("k",pung),("g",open_kong),("G",closed_kong)]:
        assert rule.physical_flower_count([], [f"{kind}{tile}"]) == expected


def test_no_seven_pairs_flowers_fifth_copy_or_malformed_meld():
    assert rule.score([11,11,14,14,17,17,21,21,24,24,41,41,45,45], self_draw=True) is None
    assert rule.score([41]*5 + [42]*3 + [45]*3 + [47]*3, self_draw=True) is None
    assert rule.score([51,41,42,42,43,43,44,44,45,45,46,46,47,47], self_draw=True) is None
    assert rule.score([45,45], ["s19","s15","s18","k41"], self_draw=True) is None


def test_waits_include_unstructured_honors_and_dragons_but_not_flowers():
    hand = [41,41,42,42,43,43,44,44,45,45,46,46,47]
    assert rule.waits(hand) == set(rule.HONORS)
    assert not rule.waits([11,12,13,21,22,23,31,32,33,17,18,19,41])


@pytest.mark.parametrize("melds,allowed", [(["s12","k21"],False),(["k11","k21","s12"],False),
    (["s12","k45"],True),(["G21","s12"],False),(["k11","k22","G33"],True)])
def test_melds_must_retain_a_starting_pattern(melds, allowed):
    assert rule.melds_allow_starting_pattern(melds) is allowed


@pytest.mark.parametrize("discarder,partners,rob,expected", [
    (1,[],False,[10,-10,0,0]), (None,[],False,[30,-10,-10,-10]),
    (1,[2],False,[20,-10,-10,0]), (1,[1],False,[20,-20,0,0]),
    (None,[1],False,[50,-30,-10,-10]), (None,[1,2,1],False,[70,-30,-30,-10]),
    (1,[],True,[30,-30,0,0]), (1,[1],True,[60,-60,0,0]), (1,[2],True,[60,-30,-30,0]),
    (1,[1,2,1],True,[90,-60,-30,0]), (1,[2,3],True,[90,-30,-30,-30]),
])
def test_zero_sum_payments(discarder, partners, rob, expected):
    changes = rule.payments(0, 10, discarder=discarder, partners=partners, rob_kong=rob)
    assert list(changes.values()) == expected
    assert sum(changes.values()) == 0
