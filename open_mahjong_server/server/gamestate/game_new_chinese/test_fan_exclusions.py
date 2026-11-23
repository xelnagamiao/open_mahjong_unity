import pytest

from ...database.subrule_fans import collect_subrule_fans
from ...game_calculation.new_chinese import rules
from .state_machine import Phase
from .test_new_chinese import table


DARK_PUNG_FANS = {"双暗刻", "三暗刻", "四暗刻", "四暗刻单骑救主"}
PAIR_RUN_FANS = {"连四对", "连五对", "连六对", "连七对"}
TILE_HOG_FANS = {"四归一", "八归", "十二归"}
KONG_FANS = {"杠", "双杠", "三杠", "十八罗汉", "大四喜之十八罗汉"}
KONG_CHAIN_FANS = {"杠上杠", "三连杠", "连四杠"}


@pytest.mark.parametrize(("before", "win", "special"), [
    ([11,11,14,14,22,22,25,25,33,33,36,36,41], 41, None),
    ([41,41,42,42,43,43,44,44,45,45,46,46,47], 47, "七福星"),
    (sorted(rules.ORPHANS), 41, "国士无双十三面"),
    (sorted(rules.ORPHANS - {41}) + [45], 41, "十三幺"),
    ([11,11,11,12,13,14,15,16,17,18,19,19,19], 15, "九宝莲灯之天衣无缝"),
    ([11,11,11,12,12,14,15,16,17,18,19,19,19], 13, "九宝莲灯之准灯高挂"),
    ([12,13,14,14,14,14,15,16,16,16,16,17,18], 11, "准听九"),
])
def test_naturally_closed_shapes_do_not_award_closed_hand(before, win, special):
    fans = rules.score(before, (), win, "self_draw")["fans"]
    assert "门清" not in fans
    if special is None:
        assert fans == {}
    else:
        assert special in fans


@pytest.mark.parametrize(("before", "win", "source", "tier"), [
    ([11,11,11,22,22,22,33,33,33,45,45,41,41], 45, "self_draw", "四暗刻"),
    ([11,11,11,22,22,22,33,33,33,45,45,45,41], 41, "discard", "四暗刻单骑救主"),
    ([11,11,11,22,22,22,33,33,33,45,45,41,41], 45, "discard", "三暗刻"),
    ([11,11,11,22,22,22,13,14,15,35,36,37,41], 41, "discard", "双暗刻"),
])
def test_concealed_pung_tier_uses_winning_component(before, win, source, tier):
    fans = rules.score(before, (), win, source)["fans"]
    assert DARK_PUNG_FANS.intersection(fans) == {tier}
    assert ("门清" in fans) == (tier in ("三暗刻", "双暗刻"))


@pytest.mark.parametrize(("hand", "melds", "tier"), [
    ([11,12,13,14,15,16,17,18,19,41,41,41,45], (), "暗中一条龙"),
    ([11,12,13,14,15,16,17,18,19,45], ("k41",), "暗中一条龙"),
    ([11,12,13,14,15,16,17,18,19,45], ("G41",), "暗中一条龙"),
])
def test_concealed_straight_only_requires_its_own_three_sequences(hand, melds, tier):
    fans = rules.score(hand, melds, 45, "self_draw")["fans"]
    assert tier in fans
    assert ("门清" in fans) == ("k41" not in melds)


def test_closed_pure_suit_preserves_both_scores_for_an_ordinary_shape():
    hand = [11,11,11,12,13,14,15,16,17,18,18,18,19]
    fans = rules.score(hand, (), 19, "self_draw")["fans"]
    assert fans["清一色"] == 80 and fans["门清"] == 5


@pytest.mark.parametrize(("faces", "tier"), [
    ([11,12,13,14,22,35,41], "连四对"),
    ([11,12,13,14,15,22,35], "连五对"),
    ([11,12,13,14,15,16,22], "连六对"),
    ([11,12,13,14,15,16,17], "连七对"),
])
def test_seven_pairs_run_only_pays_its_longest_tier(faces, tier):
    hand = [t for t in faces for _ in range(2)]
    detail = rules.score(hand[:-1], (), hand[-1], "self_draw")
    assert detail["shape"] == "seven_pairs"
    assert PAIR_RUN_FANS.intersection(detail["fans"]) == {tier}
    assert "门清" not in detail["fans"]


@pytest.mark.parametrize(("quads", "others", "win", "tier"), [
    ([11], [24,27,35,42,45], 45, "龙七对"),
    ([11,22], [35,42,45], 11, "双龙七对"),
    ([11,22], [35,42,45], 45, "双龙七对单骑救主"),
    ([11,22,33], [45], 11, "三龙七对"),
    ([11,22,33], [45], 45, "三龙七对单骑救主"),
])
def test_dragon_pairs_exclude_tile_hogs(quads, others, win, tier):
    hand = [t for t in quads for _ in range(4)] + [t for t in others for _ in range(2)]
    hand.remove(win)
    fans = rules.score(hand, (), win, "self_draw")["fans"]
    assert tier in fans
    assert "门清" not in fans and TILE_HOG_FANS.isdisjoint(fans)


@pytest.mark.parametrize(("ranks", "meld", "win", "tier"), [
    ("1112223334", "k41", 11, "四归一"),
    ("1111222333", "k41", 12, "八归"),
    ("1112223339", "s12", 19, "十二归"),
])
def test_tile_hogs_still_score_in_standard_hands(ranks, meld, win, tier):
    before = [10+int(rank) for rank in ranks]
    fans = rules.score(before, (meld,), win, "discard")["fans"]
    assert TILE_HOG_FANS.intersection(fans) == {tier}


@pytest.mark.parametrize(("ranks", "win", "tier"), [
    ("1111222333", 12, "听三张"),
    ("1112233344", 12, "听四张"),
    ("1112223334", 11, "听五张"),
    ("1112223456", 11, "听六张"),
    ("1112345666", 11, "听七张"),
    ("2223456777", 11, "听八张"),
])
def test_multiway_waits_only_pay_one_tier_and_allow_open_melds(ranks, win, tier):
    before = [10+int(rank) for rank in ranks]
    fans = rules.score(before, ("k41",), win, "discard")["fans"]
    assert (set(rules.WAIT_FANS.values()) | {"准听九"}).intersection(fans) == {tier}


@pytest.mark.parametrize(("faces", "kong_tier", "chain_tier"), [
    ([12], "杠", None),
    ([12,23], "双杠", "杠上杠"),
    ([12,23,34], "三杠", "三连杠"),
    ([12,23,34,46], "十八罗汉", "连四杠"),
    ([41,42,43,44], "大四喜之十八罗汉", "连四杠"),
])
def test_kong_count_and_chain_are_independent_tiers(faces, kong_tier, chain_tier):
    remaining_pungs = [12,23,34,46][len(faces):]
    before = [t for t in remaining_pungs for _ in range(3)] + [45]
    fans = rules.score(before, tuple(f"g{t}" for t in faces), 45, "self_draw", chain=len(faces))["fans"]
    assert KONG_FANS.intersection(fans) == {kong_tier}
    assert KONG_CHAIN_FANS.intersection(fans) == ({chain_tier} if chain_tier else set())
    if chain_tier == "三连杠":
        assert fans[chain_tier] + fans[kong_tier] == 280
    if kong_tier == "大四喜之十八罗汉":
        assert fans[kong_tier] == 1800 and "大四喜字一色" in fans


def test_only_awarded_fans_reach_settlement_record_and_player_statistics():
    state = table()
    winner = state.player_list[1]
    winner.hand_tiles = [11]*4 + [22]*4 + [35,35,42,42,45]
    winner.declared = True
    state.ready_order = [1]
    state.start_game_recording()
    state.start_round_recording()
    state.machine.transition(Phase.TURN)
    state.machine.transition(Phase.RESPONSE)
    state.player_list[0].discard_tiles = [45]
    state.player_list[0].riichi_flags = [False]
    state.settle(1, 45, "discard", 0)
    state.finalize_round_recording()
    entry, = state.deferred_hu_settlements
    detail = entry["fan_details"]
    assert detail["shape"] == "seven_pairs"
    assert "门清" not in detail["fans"] and TILE_HOG_FANS.isdisjoint(detail["fans"])
    assert detail["fan"] == sum(detail["fans"].values())
    assert entry["fan_ids"] == rules.settlement_labels(detail)
    assert entry["fan_names"] == list(detail["fans"])
    assert entry["score_changes"] == rules.payments(detail, 1, 0)
    assert collect_subrule_fans(state.game_record, winner.original_player_index, rules.SUB_RULE) == {
        name: 1 for name in detail["fans"]
    }
