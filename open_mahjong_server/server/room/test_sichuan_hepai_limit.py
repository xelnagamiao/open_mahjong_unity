"""川麻起和番在建房、赛事空房和排位默认值中的协议回归。"""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .room_manager import RoomManager
from .room_router import handle_create_Sichuan_room
from .room_validators import SichuanRoomValidator
from ..response import Response
from ..match.rank_calculator import queue_type_to_room_config
from ..match.rating_rules import QUEUES


BASE = dict(room_name="起和番测试", game_round=1, round_timer=20, step_timer=5)
SUB_RULES = ("sichuan/standard", "sichuan/xueliu", "sichuan/xueliu_exchange")


def manager():
    server = SimpleNamespace(
        players={"connection": SimpleNamespace(user_id=101, username="host", current_room_id=None)},
        db_manager=SimpleNamespace(get_user_settings=lambda _: {"username": "host"}),
        gamestate_manager=SimpleNamespace(is_user_in_active_game=lambda _: False),
        match_manager=None,
    )
    result = RoomManager(server)
    result._broadcast_room_info = AsyncMock()
    result._validate_event_for_room = lambda *_: None
    return result


@pytest.mark.parametrize("sub_rule", SUB_RULES)
def test_default_keeps_zero_fan_legal(sub_rule):
    assert SichuanRoomValidator(**BASE, sub_rule=sub_rule).hepai_limit == 0


@pytest.mark.parametrize("value", [-1, 65, True, False, 1.5, "2", None])
def test_invalid_limit_is_rejected(value):
    with pytest.raises(ValueError):
        SichuanRoomValidator(**BASE, hepai_limit=value)


@pytest.mark.parametrize("sub_rule", SUB_RULES)
@pytest.mark.parametrize("limit", [0, 1, 3, 64])
def test_custom_and_event_rooms_keep_limit(sub_rule, limit):
    rooms = manager()
    response = asyncio.run(rooms.create_Sichuan_room(
        "connection", BASE["room_name"], 1, "", 20, 5, True,
        sub_rule=sub_rule, hepai_limit=limit,
    ))
    assert response.success, response.message
    assert response.room_info["hepai_limit"] == limit
    assert rooms.rooms[response.room_info["room_id"]]["hepai_limit"] == limit
    event = asyncio.run(rooms.create_empty_event_room(
        "hepai-limit-event", "sichuan", dict(BASE, sub_rule=sub_rule, hepai_limit=limit),
        broadcast=False,
    ))
    assert event.success, event.message
    assert event.room_info["hepai_limit"] == limit


@pytest.mark.parametrize("explicit", [False, True])
def test_router_passes_limit_and_legacy_default(explicit):
    server = SimpleNamespace(players={}, create_Sichuan_room=AsyncMock(
        return_value=Response(type="room/create_room_done", success=True, message="ok")))
    socket = SimpleNamespace(send_json=AsyncMock())
    message = dict(roomname="测试", gameround=1, password="", roundTimerValue=20, stepTimerValue=5, tips=True)
    if explicit:
        message["hepai_limit"] = 3
    asyncio.run(handle_create_Sichuan_room(server, "connection", message, socket))
    assert server.create_Sichuan_room.await_args.kwargs["hepai_limit"] == (3 if explicit else 0)


def test_ranked_sichuan_keeps_zero_fan_default():
    configs = [queue_type_to_room_config(key) for key, spec in QUEUES.items() if spec.rule == "sichuan"]
    assert configs
    assert all(config["hepai_limit"] == 0 for config in configs)
