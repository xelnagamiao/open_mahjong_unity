import asyncio
import json
import hashlib
import hmac
import time
from copy import deepcopy
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock, patch

import pytest
from fastapi import FastAPI

from ..gamestate.test_room_lifecycle import game, make_server, make_room, USERS, parked_loop, module
from ..webapi.admin_room import register_admin_room_routes
from .admin_rooms import close_admin_room, list_admin_rooms


async def request(app, method, path, body=None, host="127.0.0.1", origin=None, signed=True, stamp=None, signed_path=None, signed_payload=None):
    payload = json.dumps(body).encode() if body is not None else b""
    headers = [(b"content-type", b"application/json")]
    if origin:
        headers.append((b"origin", origin.encode()))
    if signed:
        stamp = str(int(time.time()) if stamp is None else stamp)
        digest = hashlib.sha256(payload if signed_payload is None else signed_payload).hexdigest()
        message = f"room-admin\n{stamp}\n{method}\n{signed_path or path}\n{digest}"
        signature = hmac.new(b"test-room-admin-secret", message.encode(), hashlib.sha256).hexdigest()
        headers.extend([(b"x-room-admin-time", stamp.encode()), (b"x-room-admin-signature", signature.encode())])
    scope = {"type": "http", "http_version": "1.1", "method": method, "scheme": "http",
             "path": path, "raw_path": path.encode(), "query_string": b"", "root_path": "",
             "headers": headers, "client": (host, 123), "server": ("test", 80)}
    sent = []
    async def receive():
        return {"type": "http.request", "body": payload, "more_body": False}
    async def send(message):
        sent.append(message)
    await app(scope, receive, send)
    status = next(message["status"] for message in sent if message["type"] == "http.response.start")
    data = json.loads(b"".join(message.get("body", b"") for message in sent if message["type"] == "http.response.body"))
    return status, data


def test_list_omits_private_room_data_and_distinguishes_offline_players():
    server = make_server(USERS)
    room = make_room(USERS)
    room.update(duplicate_key="secret", random_seed=123, password="secret")
    server.room_manager.rooms["1"] = room
    server.user_id_to_connection.pop(USERS[0])
    data = list_admin_rooms(server.room_manager)[0]
    assert data["online_count"] == 3 and not data["players"][0]["online"]
    assert data["instance_id"] and "duplicate_key" not in data and "random_seed" not in data and "password" not in data


@pytest.mark.parametrize("rule", ["guobiao", "sichuan", "xueliu", "riichi", "hongque"])
def test_admin_close_running_room_stops_game_and_clears_every_membership(rule):
    async def run():
        async with game(rule=rule) as (server, room, state):
            closed = await close_admin_room(server.room_manager, "1", room["instance_id"])
            assert closed == ["1"] and not server.room_manager.rooms
            assert state.lifecycle_state == "closed" and state.close_reason == "admin_room_closed"
            assert not server.gamestate_manager.user_id_to_game_state
            assert all(conn.current_room_id is None for conn in server.players.values())
    asyncio.run(run())


def test_stale_room_instance_and_seating_in_progress_cannot_be_closed():
    async def run():
        server = make_server(USERS)
        room = make_room(USERS)
        room.update(instance_id="replacement", event_seating_pending=True)
        server.room_manager.rooms["1"] = room
        for instance in ("old", "replacement"):
            with pytest.raises(ValueError):
                await close_admin_room(server.room_manager, "1", instance)
        assert server.room_manager.rooms["1"] is room
    asyncio.run(run())


def test_reused_room_created_during_game_cleanup_is_never_destroyed():
    async def run():
        server = make_server(USERS)
        room = make_room(USERS)
        room["instance_id"] = "old"
        replacement = {**deepcopy(room), "instance_id": "new"}
        server.room_manager.rooms["1"] = room
        gm = server.gamestate_manager
        gm.get_game_state_by_room_id = lambda _: NS(gamestate_id="old-game")
        async def cleanup(**_):
            server.room_manager.rooms["1"] = replacement
        gm.cleanup_game_state_complete = cleanup
        with pytest.raises(ValueError, match="发生变化"):
            await close_admin_room(server.room_manager, "1", "old")
        assert server.room_manager.rooms["1"] is replacement
        assert not replacement.get("admin_closing")
    asyncio.run(run())


def test_admin_close_pair_removes_both_rooms_and_cancels_pair():
    async def run():
        server = make_server([])
        first = make_room(USERS, "sichuan", "events")
        first.update(room_id="1", instance_id="open", duplicate_match_id="pair")
        second = {**deepcopy(first), "room_id": "2", "instance_id": "closed"}
        server.room_manager.rooms.update({"1": first, "2": second})
        from ..gamestate.public.sichuan_duplicate import PairedMatch, new_match
        from ..game_calculation.sichuan.duplicate_rules import make_board
        data = new_match("pair", "sichuan/doubles_duplicate", {"open": "1", "closed": "2"}, [make_board(42, 1)], 42)
        pair = PairedMatch(data, lambda *_: None)
        server.gamestate_manager.sichuan_duplicate_matches["pair"] = pair
        assert await close_admin_room(server.room_manager, "1", "open") == ["1", "2"]
        assert not server.room_manager.rooms and pair.data["rooms_deleted"]
        assert pair.data["status"] == "cancelled"
    asyncio.run(run())


def test_admin_close_running_pair_cancels_both_loops_and_all_eight_memberships():
    async def run():
        from ..gamestate.public.sichuan_duplicate import PairedMatch, new_match
        from ..game_calculation.sichuan.duplicate_rules import make_board
        second_users = [uid + 10 for uid in USERS]
        server = make_server(USERS + second_users)
        gm, manager = server.gamestate_manager, server.room_manager
        with patch.object(module.SichuanGameState, "run_game_loop", parked_loop):
            try:
                states = []
                for rid, users in (("1", USERS), ("2", second_users)):
                    room = make_room(users, "sichuan", "events")
                    room["room_id"] = rid
                    manager.rooms[rid] = room
                    for uid in users:
                        server.user_id_to_connection[uid].current_room_id = rid
                    assert await gm.start_game(str(users[0]), rid) is None
                    states.append(gm.get_game_state_by_room_id(rid))
                pair = PairedMatch(new_match("pair", "sichuan/doubles_duplicate", {"open": "1", "closed": "2"}, [make_board(42, 1)], 42), lambda *_: None)
                gm.sichuan_duplicate_matches["pair"] = pair
                for table, state in zip(("open", "closed"), states):
                    manager.rooms[state.room_id]["duplicate_match_id"] = "pair"
                    state.duplicate_match = pair
                    pair.states[table] = state
                await asyncio.sleep(0)
                instance = manager.rooms["1"]["instance_id"]
                assert await asyncio.wait_for(close_admin_room(manager, "1", instance), 3) == ["1", "2"]
                assert all(state.lifecycle_state == "closed" and state.game_task.done() for state in states)
                assert not gm.user_id_to_game_state and not gm.gamestate_id_to_game_state
                assert not manager.rooms and pair.data["rooms_deleted"]
                assert all(conn.current_room_id is None for conn in server.players.values())
            finally:
                for gid in list(gm.gamestate_id_to_game_state):
                    await gm.cleanup_game_state_complete(gamestate_id=gid)
    asyncio.run(run())


def test_room_http_create_list_close_and_local_proxy_boundary():
    async def run():
        server = make_server([])
        app = FastAPI()
        register_admin_room_routes(app, server, proxy_secret="test-room-admin-secret")
        assert (await request(app, "GET", "/admin/rooms", signed=False))[0] == 403
        assert (await request(app, "GET", "/admin/rooms", stamp=int(time.time()) - 60))[0] == 403
        assert (await request(app, "GET", "/admin/rooms", stamp="9" * 1000))[0] == 403
        assert (await request(app, "GET", "/admin/rooms", signed_path="/admin/rooms/create"))[0] == 403
        assert (await request(app, "GET", "/admin/rooms", signed_payload=b"tampered"))[0] == 403
        assert (await request(app, "GET", "/admin/rooms", origin="https://external.invalid"))[0] == 403
        status, created = await request(app, "POST", "/admin/rooms/create", {
            "room_rule": "sichuan", "room_config": {"sub_rule": "sichuan/doubles"}, "created_by": USERS[0]})
        assert status == 200, created
        data = (await request(app, "GET", "/admin/rooms"))[1]["items"]
        assert len(data) == 1 and data[0]["persist_empty"] and data[0]["players"] == []
        status, _ = await request(app, "POST", f'/admin/rooms/{data[0]["room_id"]}/close', {"instance_id": data[0]["instance_id"]})
        assert status == 200 and not server.room_manager.rooms
        assert (await request(app, "GET", "/admin/rooms", host="203.0.113.1"))[0] == 403
    asyncio.run(run())
