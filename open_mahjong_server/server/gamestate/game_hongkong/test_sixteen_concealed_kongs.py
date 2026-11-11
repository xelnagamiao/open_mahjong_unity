"""Sixteen-tile menqing follows final melds: every kong, including added, is allowed."""

import pytest

from .test_branches import table, act
from .client_context import private_context
from .action_check import ready_discards
from ...game_calculation.hongkong import HandContext, HongKongRules, score_hand

PROFILE = "hongkong/new16"
# Screenshot: 123m 567m 789m 234p + pair 5p, with a kong of 7s.
HAND = (11, 12, 13, 15, 16, 17, 17, 18, 19, 22, 23, 24, 25, 25)


@pytest.mark.parametrize("kong", ["g37", "G37"])
def test_direct_or_concealed_kong_keeps_menqing_and_menmo(kong):
    quote = score_hand(HandContext(HAND, (kong,), 25, True, replacement="kong"), HongKongRules(PROFILE))
    assert quote.is_win
    assert {"concealed", "concealed_self_draw", "replacement"} <= set(quote.fan_ids)
    assert quote.fan == 33


def test_pung_upgraded_to_kong_restores_menqing():
    state, window = table(PROFILE, hands={1: [*HAND[:-1], 37]}, melds={1: ["k37"]}, actor=1, tail=25)
    window = act(state, window, **{"1": dict(action_type="jiagang", target_tile=37)})
    act(state, window)
    assert state.player_list[1].combination_tiles == ["g37"]
    quote = state.score_win(1, "self_draw", 25)
    assert quote.is_win and "replacement" in quote.fan_ids
    assert {"concealed", "concealed_self_draw"} <= set(quote.fan_ids)
    assert "closed_ready" not in quote.fan_ids
    state.tips = True
    context = private_context(state, 1)["__local__"][0]["local_request"]
    assert context["context"]["called_chow_or_pung"] is True


def test_added_kong_counts_as_concealed_pung_and_restores_menqing():
    # One actual暗刻 plus an added kong: both两暗刻 and门清/门摸 apply.
    hand = (11, 11, 11, 22, 23, 24, 26, 27, 28, 32, 33, 34, 45, 45)
    state, window = table(PROFILE, hands={1: [*hand[:-1], 37]},
                          melds={1: ["k37"]}, actor=1, tail=45)
    before = state.score_win(1, "self_draw", 37)
    assert not before.is_win
    window = act(state, window, **{"1": dict(action_type="jiagang", target_tile=37)})
    window = act(state, window)
    quote = state.score_win(1, "self_draw", 45)
    fans = {fan.id: fan for fan in quote.fans}
    assert quote.is_win
    assert fans["concealed_pungs"].name == "2暗刻"
    assert fans["concealed_pungs"].value == 5
    assert {"concealed", "concealed_self_draw"} <= fans.keys()
    act(state, window, **{"1": "hu_self"})
    win = state.deferred_hu_settlements[0]
    assert "HK|concealed_pungs|5|2暗刻" in win["fan_ids"]
    assert {"HK|concealed|5|门清", "HK|concealed_self_draw|5|门摸"} <= set(win["fan_ids"])


def test_multiple_added_kongs_restore_menqing_only_after_last_pung_is_upgraded():
    hand = (11, 12, 13, 15, 16, 17, 22, 23, 24, 25, 25)
    state, window = table(PROFILE, hands={1: [*hand[:-1], 37]},
                          melds={1: ["k37", "k41"]}, actor=1, tail=25)
    state.tiles_list.remove(41)
    state.tiles_list.append(41)
    window = act(state, window, **{"1": dict(action_type="jiagang", target_tile=37)})
    window = act(state, window)
    assert state.player_list[1].combination_tiles == ["g37", "k41"]
    remaining_pung = score_hand(HandContext(hand, ("g37", "k41"), 25, True,
                                           called_chow_or_pung=True), HongKongRules(PROFILE))
    assert remaining_pung.is_win and "concealed" not in remaining_pung.fan_ids
    window = act(state, window, **{"1": dict(action_type="jiagang", target_tile=41)})
    window = act(state, window)
    quote = state.score_win(1, "self_draw", 25)
    assert {"concealed", "concealed_self_draw", "concealed_pungs"} <= set(quote.fan_ids)
    assert state.player_list[1].called_chow_or_pung is True


def test_added_kong_allows_real_closed_ready_declaration_and_win():
    state, window = table(PROFILE, hands={1: [*HAND[:-1], 37]},
                          melds={1: ["k37"]}, actor=1, tail=25)
    # The original pung belongs to an earlier turn, after its required discard.
    state.player_list[1].discard_count = 1
    window = act(state, window, **{"1": dict(action_type="jiagang", target_tile=37)})
    window = act(state, window)
    assert 25 in ready_discards(state, 1)
    window = act(state, window, **{"1": "riichi"})
    act(state, window, **{"1": dict(action_type="cut", TileId=25)})
    assert state.player_list[1].declared_ready
    quote = state.score_win(1, "discard", 25, payer=0)
    assert quote.is_win
    assert {f.id: f.value for f in quote.fans}["closed_ready"] == 15
    assert not {"concealed", "ready", "concealed_self_draw"} & set(quote.fan_ids)
