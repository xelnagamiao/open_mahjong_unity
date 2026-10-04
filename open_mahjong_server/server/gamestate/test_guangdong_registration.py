"""Real constructor/manager selection with an intentionally parked game loop."""

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch

import pytest

from .game_guangdong import GuangdongGameState
from .game_tuidao import TuidaoGameState
from .test_room_lifecycle import make_room, make_server, parked_loop, USERS
from ..game_calculation.guangdong.config import DEFAULT_CONFIG, SUB_RULE
from ..game_calculation.tuidao.rules import SUB_RULE as TUIDAO_SUB_RULE
from ..room.tuidao_room import CONFIG as TUIDAO_CONFIG


@pytest.mark.parametrize("sub_rule", [None, TUIDAO_SUB_RULE, SUB_RULE])
@pytest.mark.parametrize("require_minimum", [False, True])
def test_start_game_selects_real_class_and_preserves_family_indexes_and_snapshot(sub_rule, require_minimum):
    async def run():
        server = make_server(USERS)
        room = make_room(USERS, "guangdong")
        expected_class = GuangdongGameState if sub_rule == SUB_RULE else TuidaoGameState
        expected_config = {**DEFAULT_CONFIG, "require_minimum_score": require_minimum} if sub_rule == SUB_RULE else dict(TUIDAO_CONFIG)
        if sub_rule is not None:
            room["sub_rule"] = sub_rule
        room["detailed_config"] = dict(expected_config)
        server.room_manager.rooms["1"] = room
        with patch.object(expected_class, "run_game_loop", parked_loop):
            result = await server.gamestate_manager.start_game(str(USERS[0]), "1")
            assert result is None, result
            state = server.gamestate_manager.get_game_state_by_room_id("1")
            try:
                assert type(state) is expected_class
                assert state.room_rule == "guangdong"
                assert state.sub_rule == (sub_rule or TUIDAO_SUB_RULE)
                assert state.rules_dict == expected_config
                assert server.gamestate_manager.room_id_to_TuidaoGameState["1"] is state
                assert all(server.gamestate_manager.get_game_state_by_user_id(uid) is state for uid in USERS)
                assert room["is_game_running"] and room["active_gamestate_id"] == state.gamestate_id
                assert server.room_manager.active_game_room_ids[state.gamestate_id] == "1"
                room["detailed_config"]["edition"] = "lobby-edited-after-start"
                assert state.rules_dict["edition"] == expected_config["edition"]
                assert state.flower_tiles == ()
                assert state.hepai_limit == (2 if sub_rule == SUB_RULE else 0)
                await asyncio.sleep(0)
                assert not state.game_task.done()
            finally:
                if state is not None:
                    await server.gamestate_manager.cleanup_game_state_complete(gamestate_id=state.gamestate_id)
            assert server.gamestate_manager.get_game_state_by_room_id("1") is None
            assert all(not server.gamestate_manager.is_user_in_active_game(uid) for uid in USERS)
            assert not server.room_manager.active_game_room_ids

    asyncio.run(run())


@pytest.mark.parametrize("sub_rule,config", [
    ("guangdong/unknown", None), (SUB_RULE, {"edition": "unknown"}),
    (SUB_RULE, TUIDAO_CONFIG), (TUIDAO_SUB_RULE, DEFAULT_CONFIG),
])
def test_start_game_rejects_wrong_version_without_reserving_any_player(sub_rule, config):
    async def run():
        server = make_server(USERS)
        room = make_room(USERS, "guangdong")
        room.update(sub_rule=sub_rule, detailed_config=config)
        server.room_manager.rooms["1"] = room
        response = await server.gamestate_manager.start_game(str(USERS[0]), "1")
        assert response is not None and not response.success
        assert not room["is_game_running"]
        assert not server.gamestate_manager.gamestate_id_to_game_state
        assert not server.gamestate_manager.user_id_to_game_state
        assert not server.room_manager.active_game_room_ids

    asyncio.run(run())


@pytest.mark.parametrize("sub_rule", [SUB_RULE, TUIDAO_SUB_RULE])
@pytest.mark.parametrize("suffix", ["cut_tile", "send_action"])
def test_guangdong_action_routes_forward_current_protocol_without_changing_payload(sub_rule, suffix):
    from .gamestate_router import handle_gamestate_message
    state = SimpleNamespace(room_rule="guangdong", sub_rule=sub_rule, abandoned_users=set())
    server = SimpleNamespace(
        gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=Mock(return_value=state)),
        players={"conn": SimpleNamespace(user_id=101)},
    )
    message = {"type": f"gamestate/guangdong/{suffix}", "gamestate_id": "unit-gd", "action_tick": 7,
               "action": "angang", "targetTile": 11, "cutClass": False, "TileId": 55, "cutIndex": 6}
    socket = SimpleNamespace(send_json=AsyncMock())
    with patch("server.gamestate.gamestate_router.get_action", new=AsyncMock()) as queue:
        asyncio.run(handle_gamestate_message(server, "conn", message, socket))
    assert queue.await_count == 1
    assert queue.await_args.args[:3] == (state, "conn", "cut" if suffix == "cut_tile" else "angang")
    assert queue.await_args.kwargs["action_tick"] == 7
    if suffix == "cut_tile":
        assert queue.await_args.args[3:5] == (False, 55)
        assert queue.await_args.kwargs["cutIndex"] == 6
    else:
        assert queue.await_args.kwargs["target_tile"] == 11


@pytest.mark.parametrize("kind", ["missing_id", "missing_state", "wrong_rule", "removed_during_dispatch", "abandoned"])
def test_guangdong_action_routes_reject_cross_rule_missing_and_abandoned_games(kind):
    from .gamestate_router import handle_gamestate_message
    state = SimpleNamespace(room_rule="taiwan" if kind == "wrong_rule" else "guangdong",
                            abandoned_users={101} if kind == "abandoned" else set())
    lookup = Mock(return_value=None if kind in ("missing_id", "missing_state") else state)
    if kind == "removed_during_dispatch":
        lookup.side_effect = [state, None]
    server = SimpleNamespace(gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lookup),
                             players={"conn": SimpleNamespace(user_id=101)})
    message = {"type": "gamestate/guangdong/send_action", "action": "hu_self"}
    if kind != "missing_id":
        message["gamestate_id"] = "unit-gd"
    with patch("server.gamestate.gamestate_router.get_action", new=AsyncMock()) as queue:
        asyncio.run(handle_gamestate_message(server, "conn", message, SimpleNamespace(send_json=AsyncMock())))
    queue.assert_not_awaited()


def test_guangdong_old_gb_action_channel_remains_compatible():
    from .gamestate_router import handle_gamestate_message
    state = SimpleNamespace(room_rule="guangdong", abandoned_users=set())
    server = SimpleNamespace(gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=Mock(return_value=state)),
                             players={"conn": SimpleNamespace(user_id=101)})
    with patch("server.gamestate.gamestate_router.get_action", new=AsyncMock()) as queue:
        asyncio.run(handle_gamestate_message(server, "conn", {
            "type": "gamestate/GB/send_action", "gamestate_id": "unit-gd", "action": "pass",
        }, SimpleNamespace(send_json=AsyncMock())))
    assert queue.await_args.args[:3] == (state, "conn", "pass")


def test_guangdong_route_does_not_intercept_existing_later_rule_routes():
    from .gamestate_router import handle_gamestate_message
    state = SimpleNamespace(room_rule="free", abandoned_users=set(), handle_command=AsyncMock())
    server = SimpleNamespace(gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=Mock(return_value=state)),
                             players={"conn": SimpleNamespace(user_id=101)})
    asyncio.run(handle_gamestate_message(server, "conn", {
        "type": "gamestate/free/cut_tile", "gamestate_id": "unit-free", "TileId": 11,
    }, SimpleNamespace(send_json=AsyncMock())))
    state.handle_command.assert_awaited_once_with(101, {"action": "cut", "TileId": 11})
