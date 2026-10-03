"""Guobiao flower configuration reaches custom/event rooms and key overrides."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .room_manager import RoomManager
from .room_router import handle_create_GB_room
from ..response import Response
from ..database.duplicate_walls import duplicate_room_context
from ..gamestate.public.test_duplicate_wall import wall_tiles


def room_manager():
    player = SimpleNamespace(user_id=10000001, username="host", current_room_id=None)
    server = SimpleNamespace(players={"connection": player}, match_manager=None,
        db_manager=SimpleNamespace(get_user_settings=lambda _: {"username": "host"}, get_event=lambda _: {"status": "active"}),
        gamestate_manager=SimpleNamespace(is_user_in_active_game=lambda _: False))
    manager = RoomManager(server)
    manager._broadcast_room_info = AsyncMock()
    return manager


@pytest.mark.parametrize("sub_rule", ["guobiao/standard", "guobiao/lanshi"])
@pytest.mark.parametrize("use_flowers", [False, True])
@pytest.mark.parametrize("event", [False, True])
def test_custom_and_event_configuration_propagates_flowers(sub_rule, use_flowers, event):
    manager = room_manager()
    if event:
        response = asyncio.run(manager.create_empty_event_room("event1", "guobiao", dict(sub_rule=sub_rule, use_flowers=use_flowers)))
    else:
        response = asyncio.run(manager.create_GB_room("connection", "花牌测试", 1, "", 20, 5, False, sub_rule=sub_rule, use_flowers=use_flowers))
    assert response.success, response.message
    assert response.room_info["use_flowers"] == (use_flowers and sub_rule != "guobiao/lanshi")


@pytest.mark.parametrize("use_flowers", [False, True])
def test_wall_configuration_overrides_client_default(use_flowers):
    manager = room_manager()
    wall = dict(key="DUP_test01234567", rule="guobiao", wall_type="key", use_flowers=use_flowers,
                tiles=wall_tiles("guobiao" if use_flowers else "guobiao/lanshi"))
    token = duplicate_room_context.set(wall)
    try:
        response = asyncio.run(manager.create_GB_room("connection", "复式花牌", 1, "", 20, 5, False, use_flowers=not use_flowers))
    finally:
        duplicate_room_context.reset(token)
    assert response.success, response.message
    assert response.room_info["use_flowers"] is use_flowers


@pytest.mark.parametrize("use_flowers", [None, False, True])
def test_room_route_preserves_explicit_false_and_legacy_default(use_flowers):
    server = SimpleNamespace(players={}, create_GB_room=AsyncMock(return_value=Response(type="tips", success=True, message="创建成功")))
    socket = SimpleNamespace(send_json=AsyncMock())
    payload = dict(roomname="花牌测试", gameround=1, password="", roundTimerValue=20, stepTimerValue=5, tips=False, open_cuohe=False)
    if use_flowers is not None:
        payload["use_flowers"] = use_flowers
    asyncio.run(handle_create_GB_room(server, "connection", payload, socket))
    assert server.create_GB_room.call_args.kwargs["use_flowers"] is (True if use_flowers is None else use_flowers)
