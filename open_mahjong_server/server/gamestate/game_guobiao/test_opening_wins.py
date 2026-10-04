"""Optional 8-fan opening wins in standard Guobiao and blood battle."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock

import pytest

from .GuobiaoGameState import GuobiaoGameState
from .action_check import (
    check_action_after_cut, check_action_hand_action, check_hepai,
    opening_win_fan, refresh_waiting_tiles,
)
from ...game_calculation.game_calculation_service import GameCalculationService
from ...room.room_router import handle_create_GB_room
from ...room.test_guobiao_flowers import room_manager
from ...response import Response
from ..public.game_record_manager import build_game_title_data


HAND = [11, 12, 13, 24, 25, 26, 36, 37, 38, 45, 45, 45, 28, 28]
OPENING_FANS = {"天和", "地和", "人和"}
SUPPORTED = ["guobiao/standard", "guobiao/blood_battle"]


def make_state(sub_rule="guobiao/standard", enabled=True, dealer=0):
    state = GuobiaoGameState(SimpleNamespace(user_id_to_connection={}), {
        "player_list": [0, 2, 3, 4], "room_id": "opening-test",
        "round_timer": 10, "step_timer": 1, "game_round": 1, "tips": False,
        "room_rule": "guobiao", "room_type": "custom", "sub_rule": sub_rule,
        "tian_di_ren_he": enabled,
    }, GameCalculationService(), Mock(), "opening-test")
    state.dealer_index = state.current_player_index = dealer
    state.current_round = 1
    state.tiles_list = [19, 29, 39]
    for index, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = index
        player.hand_tiles = HAND[:-1].copy()
    return state


def prepare_win(state, kind, winner):
    if kind == "地和":
        state.player_list[state.dealer_index].discard_tiles = [28]
    else:
        state.current_player_index = winner
        state.player_list[winner].hand_tiles = HAND.copy()
        if kind == "人和":
            index = state.dealer_index
            while index != winner:
                state.player_list[index].discard_tiles = [19]
                index = (index + 1) % 4
        refresh_waiting_tiles(state, winner, is_first_action=True)


@pytest.mark.parametrize("sub_rule", SUPPORTED)
@pytest.mark.parametrize("dealer", range(4))
@pytest.mark.parametrize("kind,offset", [("天和", 0), ("地和", 1), ("地和", 2), ("地和", 3),
                                         ("人和", 1), ("人和", 2), ("人和", 3)])
def test_opening_win_enables_below_limit_hand_and_adds_exactly_eight(sub_rule, dealer, kind, offset):
    state = make_state(sub_rule, dealer=dealer)
    winner = (dealer + offset) % 4
    prepare_win(state, kind, winner)
    if kind == "地和":
        actions = check_action_after_cut(state, 28)
    else:
        actions = check_action_hand_action(state, winner, is_first_action=kind == "天和")
    hu_action = next(action for action in actions[winner] if action.startswith("hu_"))
    score, fans = state.result_dict[hu_action]
    assert OPENING_FANS.intersection(fans) == {kind}
    assert fans.count(kind) == 1

    state.tian_di_ren_he = False
    state.hepai_limit = 1
    state.result_dict = {}
    baseline_actions = {i: [] for i in range(4)}
    check_hepai(state, baseline_actions, 28, winner, "dianhe" if kind == "地和" else "handgot", kind == "天和")
    baseline_score, baseline_fans = state.result_dict[hu_action]
    assert baseline_score < 8
    assert not OPENING_FANS.intersection(baseline_fans)
    assert score == baseline_score + 8

    state.hepai_limit = 8
    state.result_dict = {}
    closed_actions = {i: [] for i in range(4)}
    check_hepai(state, closed_actions, 28, winner, "dianhe" if kind == "地和" else "handgot", kind == "天和")
    assert not closed_actions[winner]


@pytest.mark.parametrize("kind,winner", [("天和", 0), ("地和", 1), ("人和", 1)])
@pytest.mark.parametrize("caller", range(4))
@pytest.mark.parametrize("meld", ["s12", "k11", "g11", "G11"])
def test_any_players_chow_pung_or_kong_interrupts_opening_wins(kind, winner, caller, meld):
    state = make_state()
    prepare_win(state, kind, winner)
    state.player_list[caller].combination_tiles = [meld]
    assert opening_win_fan(state, winner, "dianhe" if kind == "地和" else "handgot") is None


@pytest.mark.parametrize("kind,winner", [("天和", 0), ("地和", 1), ("人和", 1)])
def test_flower_replacements_do_not_interrupt_opening_wins(kind, winner):
    state = make_state()
    prepare_win(state, kind, winner)
    for player in state.player_list:
        player.huapai_list = [51]
    assert opening_win_fan(state, winner, "dianhe" if kind == "地和" else "handgot") == kind


def test_later_draws_discards_and_robbing_kong_do_not_count():
    state = make_state()
    state.player_list[0].discard_tiles = [19, 28]
    assert opening_win_fan(state, 1, "dianhe") is None
    state.player_list[0].discard_tiles = [19]
    state.current_player_index = 1
    state.player_list[1].discard_tiles = [28]
    assert opening_win_fan(state, 2, "dianhe") is None
    assert opening_win_fan(state, 1, "handgot") is None
    state.current_player_index = 0
    assert opening_win_fan(state, 0, "handgot") is None
    assert opening_win_fan(state, 1, "qianggang") is None
    state.current_player_index = 2
    assert opening_win_fan(state, 2, "handgot", is_get_gang_tile=True) is None


@pytest.mark.parametrize("fan", sorted(OPENING_FANS))
def test_invalid_hand_cannot_become_a_win_from_opening_bonus(fan):
    service = GameCalculationService()
    score, fans = service.GB_hepai_check([11] * 13 + [28], [], ["自摸", fan], 28)
    assert (score, fans) == (0, [])


@pytest.mark.parametrize("sub_rule", ["guobiao/xiaolin", "guobiao/kshen", "guobiao/lanshi"])
def test_other_subrules_ignore_requested_opening_bonus(sub_rule):
    state = make_state(sub_rule)
    assert not state.tian_di_ren_he
    assert opening_win_fan(state, 0, "handgot") is None


@pytest.mark.parametrize("sub_rule", SUPPORTED + ["guobiao/xiaolin", "guobiao/kshen", "guobiao/lanshi"])
@pytest.mark.parametrize("enabled", [False, True])
@pytest.mark.parametrize("event", [False, True])
def test_room_creation_preserves_setting_only_for_supported_rules(sub_rule, enabled, event):
    manager = room_manager()
    if event:
        response = asyncio.run(manager.create_empty_event_room("event1", "guobiao", {
            "sub_rule": sub_rule, "tian_di_ren_he": enabled,
        }))
    else:
        response = asyncio.run(manager.create_GB_room("connection", "天地人和", 1, "", 20, 5, False,
                                                      sub_rule=sub_rule, tian_di_ren_he=enabled))
    assert response.success, response.message
    assert response.room_info["tian_di_ren_he"] is (enabled and sub_rule in SUPPORTED)


@pytest.mark.parametrize("enabled", [None, False, True])
def test_route_accepts_new_setting_and_old_clients_default_off(enabled):
    server = SimpleNamespace(players={}, create_GB_room=AsyncMock(return_value=Response(type="tips", success=True, message="创建成功")))
    socket = SimpleNamespace(send_json=AsyncMock())
    payload = dict(roomname="天地人和", gameround=1, password="", roundTimerValue=20, stepTimerValue=5,
                   tips=False, open_cuohe=False)
    if enabled is not None:
        payload["tian_di_ren_he"] = enabled
    asyncio.run(handle_create_GB_room(server, "connection", payload, socket))
    assert server.create_GB_room.call_args.kwargs["tian_di_ren_he"] is bool(enabled)


def test_record_title_preserves_opening_rule():
    state = make_state()
    assert build_game_title_data(state)["tian_di_ren_he"] is True
