"""Use real disconnect/session methods and managers, without live DB/network IO."""
import ast
import asyncio
import logging
from pathlib import Path
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from ..gamestate.test_room_lifecycle import make_server, make_room, parked_loop, USERS, module
from ..gamestate.public.lifecycle import start_owned_task
from .room_seats import get_seats


def server_methods():
    path = Path(__file__).parents[1] / "server.py"
    tree = ast.parse(path.read_text(encoding="utf-8-sig"))
    owner = next(n for n in tree.body if isinstance(n, ast.ClassDef) and n.name == "GameServer")
    nodes = [n for n in owner.body if getattr(n, "name", "") in ("disconnect", "store_player_session", "leave_room")]
    namespace = {"logging": logging}
    exec(compile(ast.Module(body=nodes, type_ignores=[]), str(path), "exec"), namespace)
    return namespace


def make_session_server(users):
    server = make_server(users)
    server.match_manager = NS(player_disconnect=MagicMock(), is_user_committed=lambda _: False, is_user_in_queue=lambda _: False)
    server.friend_manager = NS(on_player_disconnect=AsyncMock(), on_game_end=AsyncMock())
    return server


@pytest.mark.parametrize("sub_rule", ["sichuan/doubles", "sichuan/xueliu_doubles"])
@pytest.mark.parametrize("users", [USERS, [USERS[0], 0, 0, 0]])
@pytest.mark.parametrize("persistent", [False, True])
def test_disconnect_every_human_cleans_seats_and_preserves_only_managed_empty_rooms(sub_rule, users, persistent):
    async def run():
        server = make_session_server(users)
        room = make_room(users, "sichuan", "events" if persistent else "custom")
        room.update(sub_rule=sub_rule, team_by_seat=[0, 1, 0, 1])
        get_seats(room)
        server.room_manager.rooms["1"] = room
        game_type = module.XueliuGameState if "xueliu" in sub_rule else module.SichuanGameState
        with patch.object(game_type, "run_game_loop", parked_loop):
            assert await server.gamestate_manager.start_game(str(users[0]), "1", roster_revision=room["roster_revision"]) is None
            state = server.gamestate_manager.get_game_state_by_room_id("1")
            await asyncio.sleep(0)
            for uid in users:
                if uid > 10:
                    await server_methods()["disconnect"](server, str(uid))
            assert not server.players and not server.user_id_to_connection
            assert state.lifecycle_state == "closed"
            assert not server.gamestate_manager.gamestate_id_to_game_state
            if persistent:
                assert server.room_manager.rooms["1"] is room
                assert room["player_list"] == [] and room["seat_list"] == [-1] * 4
                assert room["host_user_id"] == 0 and room["ready_list"] == []
            else:
                assert "1" not in server.room_manager.rooms
    asyncio.run(run())


@pytest.mark.parametrize("room_rule, sub_rule, game_type", [
    ("new_chinese", "new_chinese/standard", module.NewChineseGameState),
    ("sichuan", "sichuan/doubles", module.SichuanGameState),
])
@pytest.mark.parametrize("reconnect_to_game", [False, True])
def test_lobby_relogin_during_socket_close_only_preserves_a_reattached_game_seat(
        room_rule, sub_rule, game_type, reconnect_to_game):
    async def run():
        uid = USERS[0]
        users = [uid, 0, 0, 0]
        server = make_session_server(users)
        gm = server.gamestate_manager
        rm = server.room_manager
        room = make_room(users, room_rule)
        room["sub_rule"] = sub_rule
        if sub_rule.endswith("/doubles"):
            room["team_by_seat"] = [0, 1, 0, 1]
        get_seats(room)
        rm.rooms["1"] = room
        leave_start, resume_leave = asyncio.Event(), asyncio.Event()
        original_leave_room = rm.leave_room

        async def pause_leave(*args):
            response = await original_leave_room(*args)
            leave_start.set()
            await resume_leave.wait()
            return response

        with patch.object(game_type, "run_game_loop", parked_loop):
            assert await gm.start_game(str(uid), "1", roster_revision=room.get("roster_revision")) is None
            state = gm.get_game_state_by_room_id("1")
            bot_task = start_owned_task(state, asyncio.Event().wait())
            with patch.object(rm, "leave_room", pause_leave):
                disconnecting = asyncio.create_task(server_methods()["disconnect"](server, str(uid)))
                await asyncio.wait_for(leave_start.wait(), timeout=1)
                fresh = NS(Connect_id="fresh", current_room_id=None, websocket=NS(send_json=AsyncMock()))
                server.players["fresh"] = fresh
                server_methods()["store_player_session"](server, "fresh", uid, "owner")
                if reconnect_to_game:
                    assert await gm.player_reconnect(uid, connection=fresh)
                resume_leave.set()
                await asyncio.wait_for(disconnecting, timeout=2)
            if reconnect_to_game:
                assert state.lifecycle_state == "running"
                assert not bot_task.done()
                await server_methods()["disconnect"](server, "fresh")
            else:
                assert server.user_id_to_connection[uid] is fresh
                assert fresh.current_room_id is None
            assert state.lifecycle_state == "closed"
            assert state.game_task.done() and bot_task.done()
            assert not gm.gamestate_id_to_game_state and not gm.user_id_to_game_state
            assert not rm.rooms and not rm.active_game_room_ids
    asyncio.run(run())


@pytest.mark.parametrize("persistent", [False, True])
def test_all_online_abandoners_are_removed_from_locked_team_lobby(persistent):
    async def run():
        server = make_session_server(USERS)
        gm, rm = server.gamestate_manager, server.room_manager
        room = make_room(USERS, "sichuan", "events" if persistent else "custom")
        room.update(sub_rule="sichuan/doubles", team_by_seat=[0, 1, 0, 1])
        get_seats(room)
        rm.rooms["1"] = room
        with patch.object(module.SichuanGameState, "run_game_loop", parked_loop):
            assert await gm.start_game(str(USERS[0]), "1", roster_revision=room["roster_revision"]) is None
            state = gm.get_game_state_by_room_id("1")
            for uid in USERS:
                await gm.abandon_player_game(str(uid), uid)
            assert state.lifecycle_state == "closed"
            assert all(conn.current_room_id is None for conn in server.user_id_to_connection.values())
            if persistent:
                assert rm.rooms["1"] is room and room["player_list"] == []
            else:
                assert not rm.rooms
    asyncio.run(run())


def test_last_player_leaving_room_via_socket_stops_game_while_still_logged_in():
    async def run():
        server = make_session_server(USERS)
        gm, rm = server.gamestate_manager, server.room_manager
        rm.rooms["1"] = make_room(USERS, "guobiao")
        with patch.object(module.GuobiaoGameState, "run_game_loop", parked_loop):
            assert await gm.start_game(str(USERS[0]), "1") is None
            state = gm.get_game_state_by_room_id("1")
            leave = server_methods()["leave_room"]
            for uid in USERS[:-1]:
                await leave(server, str(uid), "1")
                assert state.lifecycle_state == "running"
            await leave(server, str(USERS[-1]), "1")
            assert set(server.user_id_to_connection) == set(USERS)
            assert state.lifecycle_state == "closed" and state.game_task.done()
            assert not rm.rooms and not rm.active_game_room_ids
    asyncio.run(run())


def test_full_room_owner_cold_login_and_repeat_join_restore_same_seat_without_password():
    async def run():
        server = make_session_server(USERS)
        room = make_room(USERS, "sichuan")
        room.update(sub_rule="sichuan/doubles", team_by_seat=[0, 1, 0, 1], has_password=True)
        get_seats(room)
        server.room_manager.rooms["1"] = room
        original = list(room["seat_list"])
        fresh = NS(Connect_id="fresh", user_id=None, current_room_id=None, websocket=NS(send_json=AsyncMock()))
        server.players["fresh"] = fresh
        server_methods()["store_player_session"](server, "fresh", USERS[0], "owner")
        assert fresh.current_room_id == "1"
        for _ in range(2):
            response = await server.room_manager.join_room("fresh", "1", "")
            assert response.success and response.type == "room/refresh_room_info"
            assert room["seat_list"] == original and room["player_list"] == USERS
        assert room["host_user_id"] == USERS[0]
        # A delayed close from the old socket must not remove the replacement's seat.
        await server_methods()["disconnect"](server, str(USERS[0]))
        assert room["seat_list"] == original and fresh.current_room_id == "1"
    asyncio.run(run())


def test_missing_cached_room_does_not_leave_orphan_membership_on_disconnect():
    async def run():
        server = make_session_server([USERS[0], 0, 0, 0])
        server.room_manager.rooms["1"] = make_room([USERS[0], 0, 0, 0], "sichuan")
        server.players[str(USERS[0])].current_room_id = None
        await server_methods()["disconnect"](server, str(USERS[0]))
        assert not server.room_manager.rooms
    asyncio.run(run())


def test_finished_room_removes_offline_owner_but_retains_online_players_and_allows_rejoin():
    async def run():
        server = make_session_server(USERS)
        room = make_room(USERS, "sichuan")
        room.update(sub_rule="sichuan/doubles", team_by_seat=[0, 1, 0, 1])
        get_seats(room)
        server.room_manager.rooms["1"] = room
        with patch.object(module.SichuanGameState, "run_game_loop", parked_loop):
            assert await server.gamestate_manager.start_game(str(USERS[0]), "1", roster_revision=room["roster_revision"]) is None
            state = server.gamestate_manager.get_game_state_by_room_id("1")
            await server_methods()["disconnect"](server, str(USERS[0]))
            assert USERS[0] in room["player_list"]  # Active roster remains locked.
            await server.gamestate_manager.cleanup_game_state_complete(gamestate_id=state.gamestate_id)
            assert room["player_list"] == USERS[1:] and room["host_user_id"] == USERS[1]
            fresh = NS(Connect_id="fresh", user_id=None, current_room_id=None, websocket=NS(send_json=AsyncMock()))
            server.players["fresh"] = fresh
            server_methods()["store_player_session"](server, "fresh", USERS[0], "owner")
            assert (await server.room_manager.join_room("fresh", "1", "")).success
            assert len(room["player_list"]) == 4 and room["seat_list"][0] == USERS[0]
    asyncio.run(run())
