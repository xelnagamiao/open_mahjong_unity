"""MIL 推倒和的固定版本配置和普通建房入口。"""

import uuid
from typing import Any, Dict, Optional
from pydantic import StrictBool, validator
from .room_validators import GBRoomValidator
from ..game_calculation.tuidao.rules import EDITION, SUB_RULE
from ..response import Response

CONFIG = {"edition": EDITION, "fan_cap": 32, "wildcards": False}


def normalize_tuidao_config(raw=None):
    if raw is None:
        return dict(CONFIG)
    if not isinstance(raw, dict):
        raise ValueError("推倒和配置必须为对象")
    if set(raw)-CONFIG.keys():
        raise ValueError("无癞子标准本不支持额外地方补则")
    for key, value in raw.items():
        if type(value) is not type(CONFIG[key]) or value != CONFIG[key]:
            raise ValueError("不支持的推倒和版本或规则覆盖")
    return dict(CONFIG)


class TuidaoRoomValidator(GBRoomValidator):
    tips: bool = True
    sub_rule: str = SUB_RULE
    detailed_config: Optional[Dict[str, Any]] = None
    use_flowers: StrictBool = False
    claim_protection: StrictBool = False

    @validator("sub_rule")
    def validate_profile(cls, value):
        if value != SUB_RULE:
            raise ValueError("不支持的推倒和子规则")
        return value

    @validator("detailed_config", pre=True, always=True)
    def validate_config(cls, value):
        return normalize_tuidao_config(value)

    @validator("open_cuohe", "tactical_call", "use_flowers", "tian_di_ren_he", "claim_protection")
    def fixed_standard(cls, value):
        if value:
            raise ValueError("MIL推倒和标准本不支持该规则覆盖")
        return value


async def create_tuidao_room(manager, player_id, *, room_name, gameround, password="",
                             roundTimerValue=20, stepTimerValue=5, tips=True, random_seed=0,
                             sub_rule=SUB_RULE, detailed_config=None, tourist_limit=False,
                             allow_spectator=True, event_id=None, count_tips=False, pointer_tips=True):
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
        config = TuidaoRoomValidator(room_name=room_name, game_round=gameround,
            round_timer=roundTimerValue, step_timer=stepTimerValue, tips=tips,
            random_seed=random_seed, sub_rule=sub_rule, detailed_config=detailed_config)
    except (TypeError, ValueError) as exc:
        return Response(type="tips", success=False, message=f"房间配置无效: {exc}")
    settings = manager.game_server.db_manager.get_user_settings(connection.user_id)
    if not settings:
        return Response(type="tips", success=False, message="获取用户设置失败")
    room_id = manager._generate_room_id()
    profile = {key:settings.get(key, default) for key, default in
        (("username", connection.username), ("title_id", 1), ("profile_image_id", 1),
         ("character_id", 1), ("voice_id", 1), ("avatar_frame_id", 0))}
    profile["user_id"] = connection.user_id
    room = config.model_dump()
    room.update(room_id=room_id, instance_id=str(uuid.uuid4()), room_type="custom", room_rule="guangdong",
        hepai_limit=0, tourist_limit=tourist_limit, allow_spectator=allow_spectator, max_player=4,
        player_list=[connection.user_id], player_settings={connection.user_id:profile},
        has_password=bool(password), host_user_id=connection.user_id, host_name=connection.username,
        is_game_running=False, is_player_set_random_seed=config.random_seed != 0,
        count_tips=bool(count_tips), pointer_tips=bool(pointer_tips))
    manager._apply_event_fields(room, event_id)
    manager.rooms[room_id] = room
    if password:
        manager.room_passwords[room_id] = password
    connection.current_room_id = room_id
    await manager._broadcast_room_info(room_id)
    return Response(type="room/create_room_done", success=True, message="房间创建成功", room_info=room)


async def handle_create_tuidao_room(server, connection_id, message, websocket):
    response = await create_tuidao_room(server.room_manager, connection_id,
        room_name=message.get("roomname", ""), gameround=message.get("gameround", 1),
        password=message.get("password", ""), roundTimerValue=message.get("roundTimerValue", 20),
        stepTimerValue=message.get("stepTimerValue", 5), tips=message.get("tips", True),
        random_seed=message.get("random_seed", 0), sub_rule=message.get("sub_rule", SUB_RULE),
        detailed_config=message.get("detailed_config"), tourist_limit=message.get("tourist_limit", False),
        allow_spectator=message.get("allow_spectator", True), event_id=message.get("event_id"),
        count_tips=message.get("count_tips", False), pointer_tips=message.get("pointer_tips", True))
    await websocket.send_json(response.model_dump(exclude_none=True))

