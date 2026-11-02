from collections import Counter
from types import SimpleNamespace

import pytest

from . import w_rules as rules
from .w_scoring import score_hand, score_partition, FAN_VALUES
from .profile import heavenly, score_for_state
from .action_check import check_action_hand_action, check_action_after_cut
from ..game_hongque.HongqueGameState import HongqueGameState
from ...room.hongque_room import normalize_hongque_config


def codes(text):
    return text.split()


def fan_map(result):
    assert result is not None
    return {fan["name"]: fan["total"] for fan in result["fans"]}


@pytest.mark.parametrize("white,size", [(True, 144), (False, 126)])
def test_deck(white, size):
    deck = rules.full_deck(white)
    assert len(deck) == size
    assert set(Counter(deck).values()) == {2}
    assert any(tile[0] == "H" for tile in deck) == white


@pytest.mark.parametrize("hand,kind,base", [
    ("HX2 HX2", "pair", "pair"), ("AX5 CX5 GX5", "triplet", "triplet"),
    ("HX1 HX3 HX5 HX7 HX9", "sequence", "sequence"),
    ("AX2 AX2 AX4 AX4 AX6 AX6", "double", "sequence"),
    ("AX3 AX3 DX3 DX3 GX3 GX3", "double", "triplet"),
    ("AX5 BX5 CX5 DX5 EX5 FX5 GX5", "rainbow", "triplet"),
    ("EX2 FX3 CX4 DX5 AX6 GX7 BX8", "rainbow", "sequence"),
])
def test_group_shapes(hand, kind, base):
    shape = rules.classify_meld(codes(hand))
    assert shape and (shape.kind, shape.base_kind) == (kind, base)
    assert tuple(sorted(codes(hand))) in rules.available_groups(tuple(sorted(codes(hand))))


@pytest.mark.parametrize("hand", ["AX1 BX1 HX1", "AX1 AX1 BX1 BX1 HX1 HX1", "AX1 AX1 AX1",
                                       "AX1 AX2 AX4", "AX1 BX2 CX3", "AY1 AY2 AY3", "HX1 AX2 BX3 CX4 DX5 EX6 FX7"])
def test_invalid_groups(hand):
    assert rules.classify_meld(codes(hand)) is None


def test_pair_and_all_pairs_paths_and_inventory():
    assert rules.is_winning_hand(codes("AX1 AX2 AX3 HX5 HX5"))
    assert rules.is_winning_hand(codes("AX1 AX1 BX5 BX5 HX9 HX9"))
    assert not rules.is_winning_hand(codes("AX1 AX2 AX3 HX5 HX5 GX9 GX9"))
    assert not rules.is_winning_hand(codes("AX1 AX1 AX1 AX2 AX3"))
    assert not rules.is_winning_hand([])


def test_claim_types_and_priority():
    assert not rules.call_candidates(codes("AX3 CX3"), "GX3")
    double = rules.call_candidates(codes("AX3 AX3 CX3 CX3 GX3"), "GX3", claimant_index=1, discarder_index=0)[0]
    peng = rules.call_candidates([f"{c}X3" for c in "ABCDEF"], "GX3", claimant_index=1, discarder_index=0)[0]
    hong = rules.call_candidates(codes("EX2 FX3 CX4 DX5 AX6 GX7"), "BX8", claimant_index=1, discarder_index=0)[0]
    chi = rules.call_candidates(codes("HX1 HX3"), "HX5", claimant_index=1, discarder_index=0)[0]
    assert [item["action_type"] for item in (double, peng, hong, chi)] == ["double_first", "peng_first", "hong_first", "chi_first"]
    assert double["priority"] > peng["priority"] > hong["priority"] > chi["priority"]
    assert Counter(double["tiles"]) == Counter(double["hand_tiles"]) + Counter(["GX3"])


def test_multitile_kong_preserves_external_tile():
    melds = [{"tiles": codes("HX1 HX3 HX5"), "claimed_tile": "HX3"}]
    options = rules.kong_candidates(codes("HX1 HX3 HX5 GX8"), melds)
    option = next(item for item in options if len(item["hand_tiles"]) == 3)
    assert option["tiles"] == codes("HX1 HX1 HX3 HX3 HX5 HX5")
    assert option["meld_index"] == 0
    assert not rules.kong_candidates(codes("AX3 BX3"), [{"tiles": codes("CX3 DX3 HX3")}])


@pytest.mark.parametrize("group,name", [("AX1 AX1 CX1 CX1 GX1 GX1", "双同刻"),
                                       ("HX1 HX1 HX3 HX3 HX5 HX5", "双同顺")])
def test_double_expands_according_to_base_kind(group, name):
    result = score_partition([codes(group)])
    assert fan_map(result)[name] == 1
    assert result["base"] == 6
    assert fan_map(result)["金龙"] == 6
    assert ("双同顺" if name == "双同刻" else "双同刻") not in fan_map(result)


def test_combination_family_no_double_count_and_repetition():
    group = [codes(f"{c}X1 {c}X2 {c}X3") for c in "ABCD"]
    fans = fan_map(score_partition(group))
    assert fans["四同顺"] == 6
    assert not {"三同顺", "双同顺"} & fans.keys()
    repeated = [codes(f"{c}X{n} {c}X{n+1} {c}X{n+2}") for n in (1, 7) for c in "AB"]
    result = score_partition(repeated)
    assert next(f for f in result["fans"] if f["name"] == "双同顺")["count"] == 2


@pytest.mark.parametrize("groups,yes,no", [
    (["AX1 AX2 AX3", "BX4 BX5 BX6", "CX7 CX8 CX9"], "花龙", "清龙"),
    (["AX1 AX2 AX3", "AX4 AX5 AX6", "AX7 AX8 AX9"], "清龙", "花龙"),
    (["HX1 HX2 HX3", "AX4 AX5 AX6"], "混一色", "清双色"),
    (["HX1 HX2 HX3", "AX4 AX5 AX6", "BX7 BX8 BX9"], "混双色", "三色"),
    (["AX1 AX2 AX3", "BX4 BX5 BX6"], "清双色", "混一色"),
    (["AX1 AX2 AX3", "BX4 BX5 BX6", "CX7 CX8 CX9"], "三色", "混双色"),
    (["AX1 BX1 CX1"], "清一数", "碰碰和"),
    (["AX1 BX1 CX1", "AX5 BX5 CX5"], "二数", "碰碰和"),
    (["AX1 AX3 AX5"], "三数", "四数"),
    (["AX1 AX3 AX5 AX7"], "四数", "三数"),
])
def test_categories(groups, yes, no):
    fans = fan_map(score_partition([codes(group) for group in groups]))
    assert yes in fans and no not in fans


def test_heavenly_requires_whole_table_closed_and_own_first_discard():
    players = [SimpleNamespace(discards=[], melds=[]) for _ in range(4)]
    state = SimpleNamespace(sub_rule=rules.SUB_RULE, players=players)
    assert heavenly(state, players[1])
    players[0].discards = ["AX1"]
    assert heavenly(state, players[1])
    players[0].melds = [{"tiles": codes("AX1 AX2 AX3")}]
    assert not heavenly(state, players[1])
    fans = fan_map(score_hand(codes("HX1 HX2 HX3"), self_draw=True, heavenly=True, last_tile=True))
    assert fans["天和"] == 18 and fans["海底"] == 2 and "门清" not in fans


def test_exposed_double_allpairs_and_no_combinations():
    result = score_hand(codes("FX6 FX6 EX8 EX8 HX8 HX8"), [{"tiles": codes("BX3 BX3 CX3 CX3 DX3 DX3")}])
    assert "对对和" in fan_map(result)
    assert not {"双同刻", "双同顺", "平和", "碰碰和"} & fan_map(result).keys()


def test_zero_fan_and_no_concealed_base_bonus():
    groups = [codes(text) for text in ("AX2 AX3 AX4", "BX5 CX5 DX5", "EX1 EX3 EX5", "FX2 GX2 AX2")]
    result = score_partition([], [{"tiles": group} for group in groups])
    assert (result["fan_total"], result["points"], result["base"]) == (0, 1, 3)
    closed = score_partition(groups)
    assert closed["base"] == 3 and closed["fan_total"] == 1


def test_room_normalization():
    assert normalize_hongque_config({"sub_rule": rules.SUB_RULE})["hongque_colourless"]
    assert not normalize_hongque_config({"sub_rule": rules.SUB_RULE, "hongque_colourless": False})["hongque_colourless"]
    for options in ({"sub_rule": "hongque/nope"}, {"hongque_min_fan": -1}, {"hongque_colourless": "false"}):
        with pytest.raises(ValueError):
            normalize_hongque_config(options)


def test_supplement_budget_is_per_player_and_minimum_is_authoritative():
    players = [SimpleNamespace(index=i, hand=codes("AX1 AX2 AX3"), melds=[], discards=[], supplements=0) for i in range(4)]
    state = SimpleNamespace(sub_rule=rules.SUB_RULE, phase="turn", game_status="waiting_hand_action", players=players,
                            current_player_index=0, wall=["HX1"], hongque_min_fan=100, hongque_min_points=0)
    players[0].supplements = 2
    assert "supplement" not in check_action_hand_action(state, players[0])[0]
    assert "win" not in check_action_hand_action(state, players[0])[0]
    state.current_player_index = 1
    assert "supplement" in check_action_hand_action(state, players[1])[0]
    state.hongque_min_fan = 0
    assert "win" in check_action_hand_action(state, players[1])[0]
    state.game_status = "onlycut_after_action"
    assert "win" not in check_action_hand_action(state, players[1])[0]


def test_catalog_complete():
    assert len(FAN_VALUES) == 30


def test_win_after_extending_two_separate_melds():
    hand = codes("AX2 AX4 BX2 BX4 CX7 CX7")
    melds = [{"tiles": codes(group)} for group in ("AX1 AX3 AX5", "BX1 BX3 BX5")]
    assert score_hand(hand, melds) is None
    result = score_hand(hand, melds, self_draw=True, allow_kong_win=True)
    assert (result["base"], result["fan_total"], result["points"]) == (7, 5, 35)
    assert len(result["kong_path"]) == 2


def test_minimum_selects_highest_eligible_decomposition():
    hand = [f"AX{rank}" for rank in range(1, 7) for _ in range(2)]
    assert score_hand(hand)["points"] == 348
    assert score_hand(hand, minimum_fan=30)["points"] == 93
    assert score_hand(hand, minimum_fan=30, minimum_points=100) is None
