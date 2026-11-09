"""Invitations must never join a later occupant of the same lobby number."""
import asyncio
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock
import uuid

import pytest

from .room_manager import RoomManager
from .room_invites import EXPIRED_MESSAGE, INVALID_MESSAGE, handle_invite_message
from .room_router import handle_room_message


def setup_room():
    server = SimpleNamespace(players={}, user_id_to_connection={}, match_manager=None,
                             db_manager=MagicMock(), gamestate_manager=MagicMock())
    server.gamestate_manager.is_user_in_active_game.return_value = False
    server.db_manager.get_user_settings.return_value = {}
    manager = server.room_manager = RoomManager(server)
    for uid in (10001, 10002):
        player = SimpleNamespace(user_id=uid, username=f"player-{uid}", current_room_id=None,
                                 is_tourist=False, websocket=SimpleNamespace(send_json=AsyncMock()))
        server.players[str(uid)] = server.user_id_to_connection[uid] = player
    room = dict(room_id=manager._generate_room_id(), instance_id=str(uuid.uuid4()),
                room_name="邀请测试", room_rule="guobiao", sub_rule="guobiao/standard",
                room_type="custom", host_user_id=10001, host_name="房主", max_player=4,
                player_list=[10001], player_settings={}, has_password=False, is_game_running=False,
                game_round=1, ready_list=[], random_seed=987654, duplicate_key="private-wall")
    manager.rooms[room["room_id"]] = room
    server.players["10001"].current_room_id = room["room_id"]
    manager._broadcast_room_info = AsyncMock()
    return server, manager, room


def invitation(room, kind="resolve_invite", **kwargs):
    return dict(type=f"room/{kind}", room_id=room["room_id"],
                room_instance_id=room["instance_id"], invite_request_id="invite-1", **kwargs)


def test_resolution_is_read_only_and_only_exposes_password_requirement():
    async def run():
        server, manager, room = setup_room()
        before = deepcopy(room)
        response = await handle_invite_message(manager, "10002", invitation(room))
        assert response.success and response.invite_request_id == "invite-1"
        assert response.invite_info == dict(room_id=room["room_id"], instance_id=room["instance_id"], has_password=False)
        assert room == before and server.players["10002"].current_room_id is None
        manager._broadcast_room_info.assert_not_awaited()
    asyncio.run(run())


def test_destroy_reuse_and_restart_never_redirect_an_old_invitation():
    async def run():
        server, manager, room = setup_room()
        old = invitation(room, "join_invite")
        assert (await handle_invite_message(manager, "10002", invitation(room))).success
        await manager.destroy_room(room["room_id"])
        assert manager._generate_room_id() == old["room_id"]
        replacement = dict(room, instance_id=str(uuid.uuid4()), player_list=[10001])
        manager.rooms[old["room_id"]] = replacement
        for kind in ("room/resolve_invite", "room/join_invite"):
            response = await handle_invite_message(manager, "10002", dict(old, type=kind))
            assert not response.success and response.message == EXPIRED_MESSAGE
        assert replacement["player_list"] == [10001]
        assert server.players["10002"].current_room_id is None
        manager.rooms.clear()
        assert (await handle_invite_message(manager, "10002", old)).message == EXPIRED_MESSAGE
    asyncio.run(run())


@pytest.mark.parametrize("bad", [None, "", "abcd", 12, [], {}, str(uuid.uuid1())])
def test_missing_or_malformed_instance_never_falls_back_to_room_number(bad):
    async def run():
        server, manager, room = setup_room()
        msg = invitation(room, "join_invite")
        msg["room_instance_id"] = bad
        result = await handle_invite_message(manager, "10002", msg)
        assert not result.success and result.message == INVALID_MESSAGE
        assert room["player_list"] == [10001]
    asyncio.run(run())


def test_password_is_checked_again_at_join_and_retry_succeeds_once():
    async def run():
        server, manager, room = setup_room()
        room["has_password"] = True
        manager.room_passwords[room["room_id"]] = "correct"
        assert (await handle_invite_message(manager, "10002", invitation(room))).invite_info["has_password"]
        wrong = await handle_invite_message(manager, "10002", invitation(room, "join_invite", password="wrong"))
        assert not wrong.success and wrong.message == "密码错误"
        request = invitation(room, "join_invite", password="correct")
        assert (await handle_invite_message(manager, "10002", request)).success
        assert (await handle_invite_message(manager, "10002", request)).success
        assert room["player_list"].count(10002) == 1
        assert server.players["10002"].current_room_id == room["room_id"]
    asyncio.run(run())


@pytest.mark.parametrize("condition,expected", [
    ("full", "房间已满"), ("running", "游戏正在进行中"), ("tourist", "不允许游客"),
    ("closing", "正在关闭"), ("game", "正在对局"), ("queue", "匹配队列"),
    ("another_room", "其他房间"), ("venue", "赛事资格不足"),
])
def test_join_rechecks_all_room_permissions_after_preview(condition, expected):
    async def run():
        server, manager, room = setup_room()
        assert (await handle_invite_message(manager, "10002", invitation(room))).success
        if condition == "full": room["player_list"] = [10001, 10003, 10004, 10005]
        if condition == "running": room["is_game_running"] = True
        if condition == "tourist":
            room["tourist_limit"] = True
            server.players["10002"].is_tourist = True
        if condition == "closing": room["admin_closing"] = True
        if condition == "game": server.gamestate_manager.is_user_in_active_game.return_value = True
        if condition == "queue":
            server.match_manager = MagicMock()
            server.match_manager.is_user_in_queue.return_value = True
        if condition == "another_room": server.players["10002"].current_room_id = "2"
        if condition == "venue": manager._can_join_venue_room = MagicMock(return_value="赛事资格不足")
        result = await handle_invite_message(manager, "10002", invitation(room, "join_invite"))
        assert not result.success and expected in result.message
        assert 10002 not in room["player_list"]
    asyncio.run(run())


def test_full_and_running_are_temporary_not_revoked_and_host_change_keeps_link():
    async def run():
        server, manager, room = setup_room()
        request = invitation(room, "join_invite")
        room["is_game_running"] = True
        assert not (await handle_invite_message(manager, "10002", request)).success
        room.update(is_game_running=False, host_name="新房主")
        assert (await handle_invite_message(manager, "10002", request)).success
    asyncio.run(run())


def test_router_delivers_correlated_invitation_response_without_legacy_join():
    async def run():
        server, manager, room = setup_room()
        server.join_room = AsyncMock()
        socket = server.players["10002"].websocket
        await handle_room_message(server, "10002", invitation(room, "join_invite"), socket)
        payload = socket.send_json.call_args.args[0]
        assert payload["type"] == "room/join_invite" and payload["success"]
        assert payload["room_instance_id"] == room["instance_id"]
        server.join_room.assert_not_awaited()
    asyncio.run(run())


def test_unauthed_user_cannot_resolve_or_join():
    async def run():
        server, manager, room = setup_room()
        server.players["10002"].user_id = None
        for kind in ("resolve_invite", "join_invite"):
            result = await handle_invite_message(manager, "10002", invitation(room, kind))
            assert not result.success and result.message == "请先登录"
    asyncio.run(run())


def test_replacement_during_broadcast_cannot_be_returned_as_joined_room():
    async def run():
        server, manager, room = setup_room()
        async def replace(_):
            await manager.destroy_room(room["room_id"])
            manager.rooms[room["room_id"]] = dict(room, instance_id=str(uuid.uuid4()), player_list=[10001])
        manager._broadcast_room_info = replace
        response = await handle_invite_message(manager, "10002", invitation(room, "join_invite"))
        assert not response.success and response.message == EXPIRED_MESSAGE and response.room_info is None
        assert server.players["10002"].current_room_id is None
        assert manager.rooms[room["room_id"]]["player_list"] == [10001]
    asyncio.run(run())


def test_existing_member_cannot_bypass_the_instance_check():
    async def run():
        server, manager, room = setup_room()
        stale = str(uuid.uuid4())
        response = await manager.join_room("10001", room["room_id"], "", expected_instance_id=stale)
        assert not response.success and response.message == EXPIRED_MESSAGE
    asyncio.run(run())
