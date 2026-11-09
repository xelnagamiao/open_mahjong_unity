"""HKMA sixteen-tile allows kongs, but a previous pung still breaks menqing."""

import pytest

from .test_branches import table, act
from .client_context import private_context
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


def test_pung_upgraded_to_kong_does_not_restore_menqing():
    state, window = table(PROFILE, hands={1: [*HAND[:-1], 37]}, melds={1: ["k37"]}, actor=1, tail=25)
    window = act(state, window, **{"1": dict(action_type="jiagang", target_tile=37)})
    act(state, window)
    assert state.player_list[1].combination_tiles == ["g37"]
    quote = state.score_win(1, "self_draw", 25)
    assert quote.is_win and "replacement" in quote.fan_ids
    assert not {"concealed", "concealed_self_draw", "closed_ready"} & set(quote.fan_ids)
    state.tips = True
    context = private_context(state, 1)["__local__"][0]["local_request"]
    assert context["context"]["called_chow_or_pung"] is True
