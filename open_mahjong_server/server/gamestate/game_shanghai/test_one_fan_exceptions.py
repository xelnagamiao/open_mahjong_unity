"""一番和附属配置：严格校验，以及建房至重连/观战/牌谱的传递。"""
import asyncio
from types import MethodType, SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from ...game_calculation.shanghai import qiaoma
from ...room.room_manager import RoomManager
from ...room.room_router import handle_room_message
from ...room.room_validators import ShanghaiRoomValidator
from ..game_taiwan.boardcast import (
    broadcast_game_start, send_reconnect_game_state, send_realtime_spectator_snapshot,
)
from .test_shanghai_state import make_state, lock
from ...game_calculation.shanghai.test_qiaoma import PLAIN, OPEN_HAND, OPEN_MELDS, scored


KEY = qiaoma.FORBID_LAJIHE_DADIAO
ROOM = dict(room_name="一番和附属选项", game_round=1, round_timer=20, step_timer=5)


@pytest.mark.parametrize("value", [1, 0, "true", "false", None, [], {}])
def test_exception_config_requires_strict_boolean(value):
    with pytest.raises(ValueError, match="禁止垃圾和/大吊配置必须为布尔值"):
        ShanghaiRoomValidator(**ROOM, hepai_limit=1, detailed_config={KEY: value})


def test_exception_config_requires_one_fan_and_defaults_off():
    assert ShanghaiRoomValidator(**ROOM).detailed_config[KEY] is False
    assert ShanghaiRoomValidator(**ROOM, hepai_limit=1).detailed_config[KEY] is False
    with pytest.raises(ValueError, match="仅在开启一番和时可用"):
        ShanghaiRoomValidator(**ROOM, detailed_config={KEY: True})
    assert ShanghaiRoomValidator(**ROOM, hepai_limit=1, detailed_config={KEY: True}).detailed_config[KEY]


@pytest.mark.parametrize("sub_rule", ["shanghai/qinghunpeng", "shanghai/lajihe"])
def test_other_shanghai_subrules_cannot_enable_exception(sub_rule):
    with pytest.raises(ValueError, match="禁止垃圾和/大吊仅适用于上海敲麻"):
        ShanghaiRoomValidator(**ROOM, sub_rule=sub_rule, detailed_config={KEY: True})


@pytest.mark.parametrize("minimum,configured,expected", [
    (None, None, False), (1, None, False), (1, False, False), (1, True, True), (0, False, False),
])
def test_config_survives_route_room_round_reset_reconnect_spectator_and_record(minimum, configured, expected):
    from ...server import GameServer

    async def run():
        server = SimpleNamespace(
            players={"client": SimpleNamespace(user_id=101, username="host", current_room_id=None)},
            db_manager=SimpleNamespace(get_user_settings=lambda _: {"username": "host"}),
            gamestate_manager=SimpleNamespace(is_user_in_active_game=lambda _: False), match_manager=None,
        )
        server.room_manager = RoomManager(server)
        server.room_manager._broadcast_room_info = AsyncMock()
        server.create_Shanghai_room = MethodType(GameServer.create_Shanghai_room, server)
        request = dict(type="room/create_Shanghai_room", roomname=ROOM["room_name"], gameround=1,
                       password="", roundTimerValue=20, stepTimerValue=5, tips=True)
        if minimum is not None:
            request["hepai_limit"] = minimum
        if configured is not None:
            request["detailed_config"] = {KEY: configured}
        socket = AsyncMock()
        await handle_room_message(server, "client", request, socket)
        response = socket.send_json.await_args.args[0]
        assert response["success"], response
        room = response["room_info"]
        assert room["detailed_config"][KEY] is expected
        assert server.room_manager.rooms[room["room_id"]]["detailed_config"][KEY] is expected
        assert (room["hepai_limit"], room["round_timer"], room["step_timer"]) == (minimum or 0, 20, 5)
        state = make_state(**{**room, "player_list": [101, 102, 103, 104]})
        assert state.forbid_lajihe_dadiao is expected
        state.game_record["game_title"].update(state.build_record_title_fields())
        assert state.game_record["game_title"]["detailed_config"][KEY] is expected
        state.game_server.user_id_to_connection[101] = SimpleNamespace(websocket=socket)
        await broadcast_game_start(state)
        assert socket.send_json.await_args.args[0]["game_info"]["detailed_config"][KEY] is expected
        await send_reconnect_game_state(state, state.player_list[0])
        assert socket.send_json.await_args.args[0]["game_info"]["detailed_config"][KEY] is expected
        state.game_server.user_id_to_connection[999] = SimpleNamespace(websocket=socket)
        await send_realtime_spectator_snapshot(state, 999, 0)
        assert socket.send_json.await_args.args[0]["game_info"]["detailed_config"][KEY] is expected
        state._reset_hand_runtime()
        assert state.forbid_lajihe_dadiao is expected
    asyncio.run(run())


DADIAO = [44, 44]
DADIAO_MELDS = ["s12", "s22", "s32", "s18"]


@pytest.mark.parametrize("enabled", [False, True])
@pytest.mark.parametrize("self_draw", [False, True])
def test_zero_fan_garbage_never_passes_one_fan_minimum(enabled, self_draw):
    assert scored(OPEN_HAND, OPEN_MELDS, [51, 52, 53], min_fan=1,
                  self_draw=self_draw, forbid_lajihe_dadiao=enabled) is None


@pytest.mark.parametrize("enabled", [False, True])
@pytest.mark.parametrize("kwargs,flowers,points", [
    ({"self_draw": True, "replacement": True}, [51, 52], 6),
    ({"rob_kong": True}, [51, 52, 53], 8),
    ({"self_draw": True, "replacement": True}, [], 22),
    ({"rob_kong": True}, [], 22),
    ({"self_draw": True, "replacement": True}, [51], None),
    ({"rob_kong": True}, [51, 52], None),
])
def test_incidental_fan_garbage_is_blocked_only_when_option_enabled(enabled, kwargs, flowers, points):
    result = scored(OPEN_HAND, OPEN_MELDS, flowers, min_fan=1, forbid_lajihe_dadiao=enabled, **kwargs)
    assert (result["base_score"] if result else None) == (None if enabled else points)
    if result:
        assert result["fan"] == 1
        assert result == scored(OPEN_HAND, OPEN_MELDS, flowers, min_fan=1, **kwargs)


@pytest.mark.parametrize("enabled", [False, True])
@pytest.mark.parametrize("self_draw", [False, True])
@pytest.mark.parametrize("melds,profile,fan,points", [
    (DADIAO_MELDS, False, 1, 4),
    (["s12", "s13", "s15", "s18"], False, 2, 8),
    (["s12", "s13", "s15", "s18"], True, 3, 16),
    (["k11", "k14", "k21", "k31"], False, 2, 8),
    (["k11", "k14", "k21", "k31"], True, 3, 16),
])
def test_all_dadiao_patterns_are_blocked_even_with_other_fan(enabled, self_draw, melds, profile, fan, points):
    result = scored(DADIAO, melds, [51], min_fan=1, self_draw=self_draw,
                    qing3_hun2_peng2=profile, forbid_lajihe_dadiao=enabled)
    if enabled:
        assert result is None
    else:
        assert result["fan"] == fan and result["base_score"] == points


@pytest.mark.parametrize("enabled", [False, True])
@pytest.mark.parametrize("tian", [False, True])
def test_ordinary_and_tianqiao_remain_eligible(enabled, tian):
    result = scored(PLAIN, flowers=[51], min_fan=1, tian_qiao=tian, forbid_lajihe_dadiao=enabled)
    assert result["fan"] == (2 if tian else 1)
    assert result["base_score"] == (8 if tian else 4)


@pytest.mark.parametrize("hand,melds", [(OPEN_HAND, OPEN_MELDS), (DADIAO, DADIAO_MELDS)])
def test_option_has_no_effect_without_parent_and_never_bypasses_ready_or_shape(hand, melds):
    assert scored(hand, melds, [51, 52, 53], forbid_lajihe_dadiao=True) == scored(hand, melds, [51, 52, 53])
    assert qiaoma.score(hand, melds, [51, 52, 53], min_fan=1, forbid_lajihe_dadiao=False) is None
    assert scored(hand + [44], melds, [51, 52, 53], min_fan=1) is None


@pytest.mark.parametrize("enabled", [False, True])
@pytest.mark.parametrize("hand,melds,ordinary", [(OPEN_HAND, OPEN_MELDS, False), (DADIAO, DADIAO_MELDS, True)])
def test_actual_action_eligibility_for_ordinary_and_incidental_wins(enabled, hand, melds, ordinary):
    state = make_state(hepai_limit=1, detailed_config={KEY: enabled})
    player = state.player_list[1]
    lock(player, hand[:-1], melds)
    player.huapai_list = [51, 52, 53]
    assert any(a.startswith("hu_") for a in state.check_discard_actions(44)[1]) == (ordinary and not enabled)
    assert any(a.startswith("hu_") for a in state.check_added_kong_actions(44)[1]) == (not enabled)
    state.current_player_index = 1
    player.hand_tiles.append(44)
    player.last_drawn_tile = 44
    player.has_draw_slot = True
    assert ("hu_self" in state.check_hand_actions(1)[1]) == (ordinary and not enabled)
    state.last_draw_after_kong = True
    assert ("hu_self" in state.check_hand_actions(1)[1]) == (not enabled)
    state.last_draw_after_kong = False
    player.hand_tiles = PLAIN.copy()
    player.combination_tiles = []
    assert "hu_self" in state.check_hand_actions(1)[1]


@pytest.mark.parametrize("hand,melds,source", [(OPEN_HAND, OPEN_MELDS, "robbing_kong"), (DADIAO, DADIAO_MELDS, "discard")])
def test_disabled_option_preserves_actual_settlement_and_enabled_option_rejects(hand, melds, source):
    async def run():
        state = make_state(hepai_limit=1, detailed_config={KEY: False})
        lock(state.player_list[0], hand[:-1], melds)
        state.player_list[0].huapai_list = [51, 52, 53]
        detail = state.score_candidate(0, source, 44)
        assert detail["fan"] == 1 and detail["base_score"] == 8
        state.forbid_lajihe_dadiao = True
        assert state.score_candidate(0, source, 44) is None
        state.forbid_lajihe_dadiao = False
        state.current_round = state.max_round * 4
        state.pending_winners = [{"index": 0, "payer": 1, "detail": detail, "source": source, "tile": 44, "hu_class": "hu_first"}]
        before = {p.original_player_index: p.score for p in state.player_list}
        with patch("server.gamestate.game_shanghai.ShanghaiGameState.broadcast_result", new=AsyncMock()):
            assert await state._settle_hand(before)
        assert state.player_list[0].score - before[0] == 8
        assert state.player_list[1].score - before[1] == -8
        assert sum(p.score for p in state.player_list) == sum(before.values())
    asyncio.run(run())
