"""Shared Zhongyong wire must preserve each rule's concealed-kong contract."""
import pytest

from ..game_hangzhou.HangzhouGameState import HangzhouGameState
from ..game_hongkong.HongKongGameState import HongKongGameState
from ..game_wenzhou.WenzhouGameState import WenzhouGameState
from ..game_yixing.YixingGameState import YixingGameState
from ..game_zhongyong import NanqueGameState, ZhongyongGameState


@pytest.mark.parametrize("state_type,mask,private", [
    (ZhongyongGameState, [2, 13] * 4, False),
    (NanqueGameState, [2, 13] * 4, True),
    (HangzhouGameState, [2, 13] * 4, True),
    (WenzhouGameState, [2, 13] * 4, True),
    (YixingGameState, [2, 13, 0, 13, 2, 13, 2, 13], False),
    (HongKongGameState, [0, 13] * 4, False),
])
@pytest.mark.parametrize("viewer", [0, 1, 2, 3])
def test_snapshot_and_action_follow_rule_disclosure_contract(state_type, mask, private, viewer):
    state = state_type(calculation_service=object())
    state.tips = False
    state.initialize_round()
    owner = state.player_list[0]
    owner.combination_tiles = ["G13"]
    owner.combination_mask = [list(mask)]
    hidden = private and viewer != 0
    expected_code = "G0" if hidden else "G13"
    expected_mask = [2, 0] * 4 if hidden else mask

    snapshot = state.build_game_start_payload(viewer)["game_info"]["players_info"][0]
    assert snapshot["combination_tiles"] == [expected_code]
    assert snapshot["combination_mask"] == [expected_mask]

    event = dict(action="angang", player=0, tile=13, meld_code="G13", combination_mask=list(mask))
    packets = state.emit_visible_action_payloads(event)
    packet = next(packet for packet in packets if packet["player_index"] == viewer)
    assert packet["tile"] in (0, None) if hidden else packet["tile"] == 13
    assert packet["meld_code"] == expected_code
    assert packet["do_action_info"]["combination_target"] == expected_code
    assert packet["do_action_info"]["combination_mask"] == expected_mask
    assert owner.combination_tiles == ["G13"] and owner.combination_mask == [mask]
