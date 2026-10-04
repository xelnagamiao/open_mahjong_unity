import pytest

from .rules import score, waits, is_complete, payments, kong_payments, TILES


PLAIN = [11,12,13,14,15,16,21,22,23,34,35,36,28,28]
PUNGS = [11]*3+[22]*3+[33]*3+[44]*3+[47]*2
KNITTED = [11,14,17,22,25,28,33,36,41,42,43,44,45,46]


@pytest.mark.parametrize("name,expected_fan,hand,melds,options", [
    ("门清", 2, PLAIN, [], {}),
    ("平和", 2, PLAIN, [], {}),
    ("断幺", 2, [22,23,24,25,26,27,32,33,34,35,36,37,28,28], [], {}),
    ("报听", 2, PLAIN, [], {"declared_ready":True}),
    ("碰碰和", 6, PUNGS, [], {"winning_tile":44}),
    ("全带幺", 6, [11,12,13,17,18,19,21,22,23,41,41,41,47,47], [], {}),
    ("大吊车", 6, [28,28], ["s12","s15","s22","s35"], {}),
    ("混一色", 6, [11,12,13,14,15,16,17,18,19,41,41,41,47,47], [], {}),
    ("抢杠", 8, PLAIN, [], {"rob_kong":True}),
    ("海底", 8, PLAIN, [], {"last_discard":True}),
    ("杠上开花", 8, PLAIN, [], {"replacement":True,"self_draw":True}),
    ("全不靠", 8, KNITTED, [], {}),
    ("清龙", 8, list(range(11,20))+[21]*3+[32]*2, [], {}),
    ("天听", 16, PLAIN, [], {"declared_ready":True,"heavenly_ready":True}),
    ("七对", 16, [11,22,33,44,45,46,47]*2, [], {}),
    ("清一色", 16, [11,12,13,14,15,16,17,18,19,12,13,14,18,18], [], {}),
    ("豪华七对", 24, [11]*4+[22,33,44,45,46]*2, [], {}),
    ("天和", 32, PLAIN, [], {"first_draw":True,"self_draw":True}),
    ("十三幺", 32, [11,19,21,29,31,39,41,42,43,44,45,46,47,47], [], {}),
    ("大三元", 32, [45]*3+[46]*3+[47]*3+[11,12,13,22,22], [], {}),
    ("字一色", 32, [41,42,43,44,45,46,47]*2, [], {}),
    ("四暗刻", 32, PUNGS, [], {"winning_tile":47}),
    ("大四喜", 32, [41]*3+[42]*3+[43]*3+[44]*3+[47]*2, [], {}),
])
def test_every_rulebook_fan(name, expected_fan, hand, melds, options):
    result = score(hand, melds, **options)
    assert result and name in result["fan_names"]
    # Literal expectations transcribed from MIL 2024 pp8-9, independent of FANS.
    assert result["fan_values"][name] == expected_fan


def test_open_zero_fan_hand_does_not_require_ready():
    detail = score([14,15,16,21,22,23,34,35,36,41,41], ["s12"], winning_tile=41)
    assert detail["fan"] == 0
    assert detail["base_score"] == 2
    assert payments(1,0,0) == {0:-4,1:4,2:0,3:0}


def test_seven_pairs_quad_and_inclusion():
    result = score([11]*4+[22,33,44,45,46]*2)
    assert result["fan_names"] == ["豪华七对"]
    assert score([11,22,33,44,45,46,47]*2)["fan_names"] == ["七对"]


def test_knitted_is_any_fourteen_from_sixteen_not_hk_remix():
    assert is_complete(KNITTED)
    assert score(KNITTED)["fan_names"] == ["全不靠"]
    assert 46 in waits(KNITTED[:-1])
    assert not is_complete(KNITTED[:-1]+[11])
    assert not is_complete(KNITTED[:-1]+[12])


def test_four_concealed_pungs_ron_must_complete_pair():
    assert "四暗刻" in score(PUNGS, winning_tile=47)["fan_names"]
    assert "四暗刻" not in score(PUNGS, winning_tile=44)["fan_names"]
    assert "四暗刻" in score(PUNGS, winning_tile=44, self_draw=True)["fan_names"]
    assert not {"门清","碰碰和"} & set(score(PUNGS, winning_tile=47)["fan_names"])


def test_dark_kong_is_concealed_but_open_kong_is_not():
    hand = [22]*3+[33]*3+[44]*3+[47]*2
    assert "四暗刻" in score(hand,["G11"],winning_tile=47)["fan_names"]
    assert "四暗刻" not in score(hand,["g11"],winning_tile=47)["fan_names"]


def test_context_fans_do_not_leak_to_wrong_win_source():
    assert "海底" not in score(PLAIN,last_discard=True,self_draw=True)["fan_names"]
    assert "海底" not in score(PLAIN,last_discard=True,rob_kong=True)["fan_names"]
    assert "杠上开花" not in score(PLAIN,replacement=True)["fan_names"]
    assert "天和" not in score(PLAIN,first_draw=True)["fan_names"]
    assert "报听" not in score(PLAIN,declared_ready=True,heavenly_ready=True)["fan_names"]


@pytest.mark.parametrize("hand,melds", [
    (PLAIN[:-1], []), (PLAIN+[11], []), ([True]+PLAIN[1:], []),
    ([51]+PLAIN[1:], []), ([11]*5+[22]*3+[33]*3+[44]*3, []),
    ([11]*2, ["g11","k22","k33","k44"]),
    ([11]*2,["s10","k22","k33","k44"]),
    ([11]*2,["bogus","k22","k33","k44"]),
])
def test_invalid_hands_are_rejected(hand, melds):
    assert score(hand,melds) is None


def test_cap_payment_and_immutable_inputs():
    hand=list(PLAIN)
    detail=score(hand,first_draw=True,self_draw=True,declared_ready=True,heavenly_ready=True)
    assert detail["raw_fan"] > 32 and detail["fan"] == 32
    assert detail["base_score"] == 34
    assert payments(0,detail["fan"]) == {0:102,1:-34,2:-34,3:-34}
    assert payments(0,99,3) == {0:68,1:0,2:0,3:-68}
    assert hand == PLAIN


@pytest.mark.parametrize("kind,payer,drawn,expected", [
    ("direct",2,True,{0:2,1:0,2:-2,3:0}),
    ("added",None,True,{0:3,1:-1,2:-1,3:-1}),
    ("added",None,False,{0:0,1:0,2:0,3:0}),
    ("concealed",None,True,{0:6,1:-2,2:-2,3:-2}),
])
def test_kong_book_payments(kind,payer,drawn,expected):
    assert kong_payments(0,kind,payer=payer,drawn=drawn) == expected


@pytest.mark.parametrize("winner,fan,payer", [(4,2,None),(True,2,None),(0,-1,None),(0,0,0),(0,0,5)])
def test_reject_invalid_settlement(winner,fan,payer):
    with pytest.raises(ValueError): payments(winner,fan,payer)


def test_waits_use_physical_four_copy_limit():
    assert 11 not in waits([11], ["G11","k22","k33","k44"])
    assert waits([47],["k11","k22","k33","k44"]) == {47}
    assert set(waits(PLAIN[:-1])) <= set(TILES)
