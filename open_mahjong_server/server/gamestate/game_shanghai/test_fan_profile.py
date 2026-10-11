"""清三混二碰二：独立牌例、真实建房协议及结算/牌谱番值。"""
import asyncio
from types import MethodType, SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from ...game_calculation.shanghai import qiaoma
from ...game_calculation.shanghai.test_qiaoma import OPEN_HAND, OPEN_MELDS
from ...room.room_manager import RoomManager
from ...room.room_router import handle_room_message
from ...room.room_validators import ShanghaiRoomValidator
from ..game_taiwan.boardcast import broadcast_game_start, send_reconnect_game_state, send_realtime_spectator_snapshot
from .test_shanghai_state import make_state, lock


PATTERNS = [
    ("清一色", [11,12,13,12,13,14,15,16,17,19,19], ["s18"], 2, 3, 8, 16),
    ("混一色", [11,12,13,12,13,14,15,16,17,41,41], ["s18"], 1, 2, 4, 8),
    ("碰碰和", [21,21,21,31,31,31,14,14,14,22,22], ["k11"], 1, 2, 4, 8),
]


@pytest.mark.parametrize("enabled", [False, True])
@pytest.mark.parametrize("name,hand,melds,old_fan,new_fan,old_points,new_points", PATTERNS)
def test_configured_pattern_fan_and_independent_score(name, hand, melds, old_fan, new_fan, old_points, new_points, enabled):
    result = qiaoma.score(hand, melds, [51], declared_ready=True, qing3_hun2_peng2=enabled)
    assert result["fan"] == (new_fan if enabled else old_fan)
    assert result["base_score"] == (new_points if enabled else old_points)
    assert name in result["fan_names"]  # 统计用番名保持兼容。
    assert hand[-1] in qiaoma.waits(hand[:-1], melds)
    if not enabled:
        assert result == qiaoma.score(hand, melds, [51], declared_ready=True)


def test_stacking_cap_and_other_fans_keep_existing_semantics():
    hand = [11,11,11,12,12,12,13,13,13,41,41]
    old = qiaoma.score(hand, ["k14"], [51], declared_ready=True)
    new = qiaoma.score(hand, ["k14"], [51], declared_ready=True, qing3_hun2_peng2=True)
    assert (old["fan"], old["base_score"]) == (2, 8)
    assert (new["fan"], new["capped_fan"], new["base_score"]) == (4, 3, 16)
    assert "三番封顶" in new["fan_names"]
    concealed = [11,11,11,13,13,13,15,15,15,17,17,17,19,19]
    result = qiaoma.score(concealed, declared_ready=True, self_draw=True, replacement=True, qing3_hun2_peng2=True)
    assert result["fan"] == 7 and result["base_score"] == 88
    assert {"门清", "杠开", "清一色", "碰碰和"}.issubset(result["fan_names"])


@pytest.mark.parametrize("enabled", [False, True])
@pytest.mark.parametrize("minimum", [0, 1])
def test_profile_does_not_create_plain_fan_or_remove_flower_gate(enabled, minimum):
    enough = qiaoma.score(OPEN_HAND, OPEN_MELDS, [51,52,53], declared_ready=True,
                         min_fan=minimum, qing3_hun2_peng2=enabled)
    assert (enough["base_score"] if enough else None) == (None if minimum else 4)
    assert qiaoma.score(OPEN_HAND, OPEN_MELDS, [51], declared_ready=True, self_draw=True,
                       replacement=True, qing3_hun2_peng2=enabled) is None
    assert qiaoma.score(PATTERNS[0][1], PATTERNS[0][2], [51], qing3_hun2_peng2=enabled) is None


@pytest.mark.parametrize("enabled", [False, True])
@pytest.mark.parametrize("name,hand,melds,old_fan,new_fan,old_points,new_points", PATTERNS)
def test_real_state_scores_self_draw_discard_rob_kong_and_resets(name, hand, melds, old_fan, new_fan, old_points, new_points, enabled):
    state = make_state(hepai_limit=1, detailed_config={qiaoma.QING3_HUN2_PENG2: enabled})
    player = state.player_list[1]
    lock(player, hand[:-1], melds)
    player.huapai_list = [51]
    tile = hand[-1]
    expected = new_points if enabled else old_points
    assert state.score_candidate(1, "discard", tile)["base_score"] == expected
    assert any(action.startswith("hu_") for action in state.check_discard_actions(tile)[1])
    assert state.score_candidate(1, "robbing_kong", tile)["base_score"] == min(expected * 2, 16)
    player.hand_tiles.append(tile)
    player.last_drawn_tile = tile
    player.has_draw_slot = True
    state.current_player_index = 1
    assert state.score_candidate(1, "self_draw")["base_score"] == expected
    assert state.check_hand_actions(1)[1] == ["hu_self"]
    state._reset_hand_runtime()
    assert state.qing3_hun2_peng2 is enabled


@pytest.mark.parametrize("configured", [None, False, True])
def test_route_room_start_reconnect_spectator_and_record_preserve_profile(configured):
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
        request = dict(type="room/create_Shanghai_room", roomname="清三混二碰二测试", gameround=1,
                       password="", roundTimerValue=20, stepTimerValue=5, tips=True, hepai_limit=1)
        if configured is not None:
            request["detailed_config"] = {qiaoma.QING3_HUN2_PENG2: configured}
        socket = AsyncMock()
        await handle_room_message(server, "client", request, socket)
        response = socket.send_json.await_args.args[0]
        assert response["success"], response
        room = response["room_info"]
        expected = configured is True
        assert room["detailed_config"][qiaoma.QING3_HUN2_PENG2] is expected
        assert server.room_manager.rooms[room["room_id"]]["detailed_config"] == room["detailed_config"]
        assert (room["round_timer"], room["step_timer"], room["hepai_limit"]) == (20, 5, 1)
        state = make_state(**{**room, "player_list": [101,102,103,104]})
        assert state.qing3_hun2_peng2 is expected
        # 与 game_loop_chinese 初始化相同，规则专属标题由正式钩子补入。
        state.game_record["game_title"].update(state.build_record_title_fields())
        assert state.game_record["game_title"]["detailed_config"][qiaoma.QING3_HUN2_PENG2] is expected
        state.game_server.user_id_to_connection[101] = SimpleNamespace(websocket=socket)
        await broadcast_game_start(state)
        assert socket.send_json.await_args.args[0]["game_info"]["detailed_config"][qiaoma.QING3_HUN2_PENG2] is expected
        await send_reconnect_game_state(state, state.player_list[0])
        assert socket.send_json.await_args.args[0]["game_info"]["detailed_config"][qiaoma.QING3_HUN2_PENG2] is expected
        state.game_server.user_id_to_connection[999] = SimpleNamespace(websocket=socket)
        await send_realtime_spectator_snapshot(state, 999, 0)
        assert socket.send_json.await_args.args[0]["game_info"]["detailed_config"][qiaoma.QING3_HUN2_PENG2] is expected
    asyncio.run(run())


@pytest.mark.parametrize("invalid", [1, 0, "true", None, [], {"other": True}])
def test_invalid_config_rejected_at_room_boundary(invalid):
    config = invalid if isinstance(invalid, (list, dict)) else {qiaoma.QING3_HUN2_PENG2: invalid}
    with pytest.raises(ValueError):
        ShanghaiRoomValidator(room_name="上海", game_round=1, round_timer=20, step_timer=5, detailed_config=config)


@pytest.mark.parametrize("sub_rule", ["shanghai/qinghunpeng", "shanghai/lajihe"])
def test_other_shanghai_rules_cannot_enable_profile(sub_rule):
    args = dict(room_name="上海", game_round=1, round_timer=20, step_timer=5, sub_rule=sub_rule)
    assert ShanghaiRoomValidator(**args).detailed_config is None
    with pytest.raises(ValueError, match="仅适用于上海敲麻"):
        ShanghaiRoomValidator(**args, detailed_config={qiaoma.QING3_HUN2_PENG2: True})


@pytest.mark.parametrize("enabled", [False, True])
def test_settlement_record_values_and_statistic_names(enabled):
    async def run():
        state = make_state(detailed_config={qiaoma.QING3_HUN2_PENG2: enabled})
        state.current_round = state.max_round * 4  # 不进入局间等待。
        name, hand, melds, _, _, old_points, new_points = PATTERNS[0]
        player = state.player_list[1]
        lock(player, hand[:-1], melds)
        player.huapai_list = [51]
        detail = state.score_candidate(1, "discard", hand[-1])
        state.pending_winners = [{"index": 1, "payer": 0, "detail": detail,
                                  "source": "discard", "tile": hand[-1], "hu_class": "shanghai/qiaoma"}]
        before = {p.original_player_index: p.score for p in state.player_list}
        with patch("server.gamestate.game_shanghai.ShanghaiGameState.broadcast_result", new=AsyncMock()) as output:
            assert await state._settle_hand(before)
        payload = output.await_args.kwargs
        points = new_points if enabled else old_points
        assert payload["hu_score"] == points
        assert payload["score_changes"] == {0: -points, 1: points, 2: 0, 3: 0}
        assert (f"{name}（3番）" if enabled else name) in payload["hu_fan"]
        assert name in player.record_counter.recorded_fans[-1]
        assert "3番" not in " ".join(player.record_counter.recorded_fans[-1])
        assert any(payload["hu_fan"] == value for value in _lists(state.game_record))
    asyncio.run(run())


def _lists(value):
    if isinstance(value, dict):
        for item in value.values():
            yield from _lists(item)
    elif isinstance(value, list):
        yield value
        for item in value:
            yield from _lists(item)
