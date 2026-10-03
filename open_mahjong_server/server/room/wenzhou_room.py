"""MIL Wenzhou 2024 room validation, with the existing room lifecycle."""

import uuid
from typing import Any

from pydantic import Field, StrictBool, StrictInt, validator

from .room_validators import GBRoomValidator
from ..response import Response

SUB_RULE = "wenzhou/mil2024"
EDITION = "mil-wenzhou-2024-om1"


def normalize_wenzhou_config(value=None):
    if value is None:
        return {"edition": EDITION}
    if not isinstance(value, dict) or set(value) - {"edition"}:
        raise ValueError("温州 MIL 2024 不支持未公布的馆规设置")
    if value.get("edition", EDITION) != EDITION:
        raise ValueError("不支持的温州麻将规则版本")
    return {"edition": EDITION}


class WenzhouRoomValidator(GBRoomValidator):
    sub_rule: str = SUB_RULE
    game_round: StrictInt = 4
    round_timer: StrictInt = 20
    step_timer: StrictInt = 5
    tips: StrictBool = True
    count_tips: StrictBool = False
    pointer_tips: StrictBool = True
    tourist_limit: StrictBool = False
    allow_spectator: StrictBool = True
    use_flowers: StrictBool = False
    claim_protection: StrictBool = False
    open_cuohe: StrictBool = False
    tactical_call: StrictBool = False
    tian_di_ren_he: StrictBool = False
    detailed_config: dict[str, Any] = Field(default_factory=lambda: {"edition": EDITION})

    @validator("sub_rule")
    def valid_profile(cls, value):
        if value != SUB_RULE:
            raise ValueError("温州麻将仅支持 MIL 2024")
        return value

    @validator("detailed_config", pre=True, always=True)
    def valid_config(cls, value):
        return normalize_wenzhou_config(value)

    @validator("use_flowers", "claim_protection", "open_cuohe", "tactical_call", "tian_di_ren_he")
    def fixed_false(cls, value):
        if value:
            raise ValueError("温州 MIL 2024 不支持此选项")
        return False


async def create_wenzhou_room(manager, player_id, *, room_name, gameround=4, password="",
                             roundTimerValue=20, stepTimerValue=5, tips=True, random_seed=0,
                             sub_rule=SUB_RULE, detailed_config=None, tourist_limit=False,
                             allow_spectator=True, event_id=None, count_tips=False, pointer_tips=True,
                             **fixed_options):
    connection = manager.game_server.players.get(player_id)
    if connection is None or not connection.user_id:
        return Response(type="tips", success=False, message="请先登录")
    if connection.current_room_id:
        return Response(type="tips", success=False, message="请先退出当前房间")
    blocked = manager._reject_room_entry_conflicts(connection.user_id, "创建房间")
    if blocked:
        return blocked
    event_id = manager._normalize_event_id(event_id)
    blocked = manager._validate_event_for_room(event_id, connection.user_id)
    if blocked:
        return blocked
    try:
        config = WenzhouRoomValidator(room_name=room_name, game_round=gameround, round_timer=roundTimerValue,
            step_timer=stepTimerValue, tips=tips, random_seed=random_seed, sub_rule=sub_rule,
            detailed_config=detailed_config, tourist_limit=tourist_limit, allow_spectator=allow_spectator,
            count_tips=count_tips, pointer_tips=pointer_tips, **fixed_options)
    except (TypeError, ValueError) as error:
        return Response(type="tips", success=False, message=f"房间配置无效: {error}")
    settings = manager.game_server.db_manager.get_user_settings(connection.user_id)
    if not settings:
        return Response(type="tips", success=False, message="获取用户设置失败")
    room_id = manager._generate_room_id()
    profile = {key: settings.get(key, default) for key, default in (
        ("username", connection.username), ("title_id", 1), ("profile_image_id", 1),
        ("character_id", 1), ("voice_id", 1), ("avatar_frame_id", 0))}
    profile["user_id"] = connection.user_id
    room = config.model_dump()
    room.update(room_id=room_id, instance_id=str(uuid.uuid4()), room_type="custom", room_rule="wenzhou",
                hepai_limit=0, max_player=4, player_list=[connection.user_id], player_settings={connection.user_id: profile},
                has_password=bool(password), host_user_id=connection.user_id, host_name=connection.username,
                is_game_running=False, is_player_set_random_seed=config.random_seed != 0)
    manager._apply_event_fields(room, event_id)
    manager.rooms[room_id] = room
    if password:
        manager.room_passwords[room_id] = password
    connection.current_room_id = room_id
    await manager._broadcast_room_info(room_id)
    return Response(type="room/create_room_done", success=True, message="房间创建成功", room_info=room)


async def handle_create_wenzhou_room(server, connection_id, message, websocket):
    response = await create_wenzhou_room(server.room_manager, connection_id,
        room_name=message.get("roomname", ""), gameround=message.get("gameround", 4), password=message.get("password", ""),
        roundTimerValue=message.get("roundTimerValue", 20), stepTimerValue=message.get("stepTimerValue", 5),
        tips=message.get("tips", True), random_seed=message.get("random_seed", 0),
        sub_rule=message.get("sub_rule", SUB_RULE), detailed_config=message.get("detailed_config"),
        tourist_limit=message.get("tourist_limit", False), allow_spectator=message.get("allow_spectator", True),
        event_id=message.get("event_id"), count_tips=message.get("count_tips", False), pointer_tips=message.get("pointer_tips", True),
        **{key: message[key] for key in ("use_flowers", "claim_protection", "open_cuohe", "tactical_call", "tian_di_ren_he") if key in message})
    await websocket.send_json(response.model_dump(exclude_none=True))
