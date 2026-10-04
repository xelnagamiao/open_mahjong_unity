"""敲麻一番和：牌例、合法动作，以及建房到重连/观战/牌谱的配置传递。"""
import asyncio
from types import MethodType, SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .test_shanghai_state import make_state, lock
from ...game_calculation.shanghai.test_qiaoma import PLAIN, OPEN_HAND, OPEN_MELDS, scored
from ...room.room_validators import ShanghaiRoomValidator
from ...room.room_manager import RoomManager
from ...room.room_router import handle_room_message
from ..game_taiwan.boardcast import (
    broadcast_game_start, send_reconnect_game_state, send_realtime_spectator_snapshot,
)


@pytest.mark.parametrize("flowers,self_draw,points", [
    ([], False, 11), ([], True, 11),
    ([51, 52], True, 3), ([51, 52, 53], False, 4),
    ([51, 52, 53, 54, 55, 56, 57, 58], False, 9),
])
def test_one_fan_blocks_plain_hand_even_with_enough_flowers(flowers, self_draw, points):
    assert scored(OPEN_HAND, OPEN_MELDS, flowers, self_draw=self_draw)["base_score"] == points
    assert scored(OPEN_HAND, OPEN_MELDS, flowers, self_draw=self_draw, min_fan=1) is None


@pytest.mark.parametrize("self_draw", [False, True])
def test_one_fan_accepts_concealed_hand_without_changing_score(self_draw):
    result = scored(PLAIN, min_fan=1, self_draw=self_draw)
    assert result["fan"] == 1 and result["base_score"] == 22


@pytest.mark.parametrize("flowers,kwargs,points", [
    ([51, 52], {"self_draw": True, "replacement": True}, 6),
    ([51, 52, 53], {"rob_kong": True}, 8),
    ([51], {"self_draw": True, "replacement": True}, None),
    ([51, 52], {"rob_kong": True}, None),
])
def test_incidental_fan_counts_but_flower_threshold_remains(flowers, kwargs, points):
    result = scored(OPEN_HAND, OPEN_MELDS, flowers, min_fan=1, **kwargs)
    assert (result["base_score"] if result else None) == points


@pytest.mark.parametrize("minimum", [0, 1])
def test_action_checks_enforce_minimum_for_self_draw_discard_and_rob_kong(minimum):
    state = make_state(hepai_limit=minimum)
    player = state.player_list[1]
    lock(player, OPEN_HAND[:-1], OPEN_MELDS)
    player.huapai_list = [51, 52, 53]
    assert any(a.startswith("hu_") for a in state.check_discard_actions(44)[1]) == (minimum == 0)
    assert any(a.startswith("hu_") for a in state.check_added_kong_actions(44)[1])
    state.current_player_index = 1
    player.hand_tiles.append(44)
    player.last_drawn_tile = 44
    player.has_draw_slot = True
    assert ("hu_self" in state.check_hand_actions(1)[1]) == (minimum == 0)
    state.last_draw_after_kong = True
    assert state.check_hand_actions(1)[1] == ["hu_self"]
    # 无论开关如何，门清一番均可和牌。
    state.last_draw_after_kong = False
    player.hand_tiles = list(PLAIN)
    player.combination_tiles = []
    assert state.check_hand_actions(1)[1] == ["hu_self"]


def test_one_fan_does_not_block_structural_ready_declaration():
    state = make_state(hepai_limit=1)
    player = state.player_list[0]
    player.combination_tiles = list(OPEN_MELDS)
    player.hand_tiles = OPEN_HAND[:-1] + [11]
    player.huapai_list = [51, 52, 53]
    assert state.ready_candidate_cuts(0)[11] == [44]
    assert "riichi_cut" in state.after_claim_actions()[0]


@pytest.mark.parametrize("minimum", [None, 0, 1])
def test_route_room_start_reconnect_spectator_and_record_preserve_one_fan(minimum):
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
        request = dict(type="room/create_Shanghai_room", roomname="一番和测试", gameround=1,
                       password="", roundTimerValue=30, stepTimerValue=3, tips=True)
        if minimum is not None:
            request["hepai_limit"] = minimum
        socket = AsyncMock()
        await handle_room_message(server, "client", request, socket)
        response = socket.send_json.await_args.args[0]
        assert response["success"], response
        room = response["room_info"]
        expected = minimum or 0
        assert room["hepai_limit"] == expected
        assert server.room_manager.rooms[room["room_id"]]["hepai_limit"] == expected
        state = make_state(**{**room, "player_list": [101, 102, 103, 104]})
        assert state.hepai_limit == expected
        assert state.game_record["game_title"]["hepai_limit"] == expected
        state.game_server.user_id_to_connection[101] = SimpleNamespace(websocket=socket)
        await broadcast_game_start(state)
        assert socket.send_json.await_args.args[0]["game_info"]["hepai_limit"] == expected
        await send_reconnect_game_state(state, state.player_list[0])
        assert socket.send_json.await_args.args[0]["game_info"]["hepai_limit"] == expected
        state.game_server.user_id_to_connection[999] = SimpleNamespace(websocket=socket)
        await send_realtime_spectator_snapshot(state, 999, 0)
        assert socket.send_json.await_args.args[0]["game_info"]["hepai_limit"] == expected
    asyncio.run(run())


@pytest.mark.parametrize("value", [-1, 2, 1.5, "1", None, True])
def test_invalid_one_fan_value_is_rejected(value):
    with pytest.raises(ValueError, match="一番和配置"):
        ShanghaiRoomValidator(room_name="上海", game_round=1, round_timer=30,
                              step_timer=3, hepai_limit=value)


def test_qinghunpeng_does_not_accept_qiaoma_one_fan_option():
    args = dict(room_name="清混碰", game_round=1, round_timer=30,
                step_timer=3, sub_rule="shanghai/qinghunpeng")
    assert ShanghaiRoomValidator(**args).hepai_limit == 0
    with pytest.raises(ValueError, match="仅适用于上海敲麻"):
        ShanghaiRoomValidator(**args, hepai_limit=1)
