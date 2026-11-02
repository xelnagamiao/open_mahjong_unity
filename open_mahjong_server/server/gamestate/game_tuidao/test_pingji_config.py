"""平鸡自摸禁吃：配置往返、权威动作及零番和牌限制。"""
import asyncio
from types import SimpleNamespace

import pytest

from .test_state import make_state, hand, draw, HAND
from .test_adapters import room_manager, Socket
from ..game_taiwan.boardcast import send_reconnect_game_state
from ...room.tuidao_room import normalize_tuidao_config, PINGJI_ZIMO_NO_CHOW as KEY
from ...room.guangdong_room import create_guangdong_room, GuangdongRoomValidator


# 明碰一万，手内三顺子、东风将；按推倒和计番为零番鸡胡。
CHICKEN = [14, 15, 16, 21, 22, 23, 34, 35, 36, 41]


def test_default_and_legacy_configs_keep_option_off():
    for config in (None, {}, {"edition": "mil-tuidao-2024-om1", "fan_cap": 32, "wildcards": False}):
        assert normalize_tuidao_config(config)[KEY] is False
    assert make_state().pingji_zimo_no_chow is False


@pytest.mark.parametrize("value", [None, 0, 1, "false", "true", [], {}])
def test_option_rejects_non_boolean_values(value):
    with pytest.raises(ValueError, match="必须是布尔值"):
        normalize_tuidao_config({KEY: value})


@pytest.mark.parametrize("enabled", [False, True])
def test_room_and_record_reconnect_preserve_option(enabled):
    manager = room_manager()
    result = asyncio.run(create_guangdong_room(manager, "unit", room_name="平鸡测试",
        detailed_config={KEY: enabled}))
    assert result.success
    room = manager.rooms["123456"]
    assert room["detailed_config"][KEY] is enabled
    state = make_state(detailed_config=room["detailed_config"])
    assert state.rules_dict[KEY] is enabled
    assert state.build_record_round_fields()["detailed_config"][KEY] is enabled
    socket = Socket()
    player = state.player_list[0]
    state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=socket)
    asyncio.run(send_reconnect_game_state(state, player))
    assert socket.messages[0]["game_info"]["detailed_config"][KEY] is enabled


def test_option_does_not_leak_to_mil2023():
    with pytest.raises(ValueError):
        GuangdongRoomValidator(room_name="花鬼", game_round=1, round_timer=20, step_timer=5,
            sub_rule="guangdong/mil2023", detailed_config={KEY: True})


@pytest.mark.parametrize("enabled", [False, True])
@pytest.mark.parametrize("tile,tiles,action", [
    (11, [12, 13], "chi_right"), (12, [11, 13], "chi_mid"), (13, [11, 12], "chi_left"),
])
def test_all_chows_are_removed_without_empty_pass_windows(enabled, tile, tiles, action):
    state = make_state(detailed_config={KEY: enabled})
    hand(state, 1, tiles + [21, 21, 21, 22, 24, 26, 31, 35, 39, 41, 43])
    offered = state.check_discard_actions(tile)[1]
    if enabled:
        assert offered == []
    else:
        assert action in offered and "pass" in offered


def test_disabled_chow_cannot_be_executed_by_forged_claim():
    state = make_state(detailed_config={KEY: True})
    state.player_list[0].discard_tiles = [11]
    player = hand(state, 1, [12, 13, 21, 22, 23, 31, 32, 33, 34, 35, 36, 41, 41])
    before = list(player.hand_tiles)
    asyncio.run(state.execute_claim(1, "chi_right"))
    assert player.hand_tiles == before and player.combination_tiles == []
    assert state.player_list[0].discard_tiles == [11]


@pytest.mark.parametrize("enabled", [False, True])
def test_chicken_discard_win_requires_self_draw_only_when_enabled(enabled):
    state = make_state(detailed_config={KEY: enabled})
    player = hand(state, 1, CHICKEN, ["k11"])
    detail = state.score_candidate(1, "discard", 41, ignore_pass=True)
    assert (detail is None) is enabled
    assert ("hu_first" in state.check_discard_actions(41)[1]) is not enabled
    if detail:
        assert detail["fan"] == 0
    draw(player, 41)
    assert state.score_candidate(1, "self_draw")["fan"] == 0
    assert "hu_self" in state.check_hand_actions(1)[1]


def test_pungs_kongs_and_scoring_wins_remain_available():
    state = make_state(detailed_config={KEY: True})
    hand(state, 1, [11, 11, 11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41])
    offered = state.check_discard_actions(11)[1]
    assert "peng" in offered and "gang" in offered
    assert not any(action.startswith("chi_") for action in offered)
    hand(state, 1, HAND)
    assert "hu_first" in state.check_discard_actions(28)[1]
    player = hand(state, 1, CHICKEN, ["k11"], ready=True)
    assert "报听" in state.score_candidate(1, "discard", 41)["fan_names"]
    player.declared_ready = player.ready_locked = False
    assert "抢杠" in state.score_candidate(1, "robbing_kong", 41)["fan_names"]
    state.tiles_list = [19] * 13
    assert "海底" in state.score_candidate(1, "discard", 41)["fan_names"]
