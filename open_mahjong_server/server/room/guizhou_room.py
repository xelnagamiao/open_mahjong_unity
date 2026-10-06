"""Fixed MIL 2023 Guizhou profile and existing room lifecycle integration."""

import uuid
from typing import Any, Dict

from pydantic import Field, StrictBool, StrictInt, validator

from .room_validators import GBRoomValidator
from ..game_calculation.guizhou.rules import RULE_VERSION, SUB_RULE, normalize_config
from ..response import Response


class GuizhouRoomValidator(GBRoomValidator):
    sub_rule: str = SUB_RULE
    game_round: StrictInt
    round_timer: StrictInt
    step_timer: StrictInt
    tips: StrictBool = True
    tourist_limit: StrictBool = False
    allow_spectator: StrictBool = True
    count_tips: StrictBool = False
    pointer_tips: StrictBool = True
    use_flowers: StrictBool = False
    claim_protection: StrictBool = False
    open_cuohe: StrictBool = False
    tactical_call: StrictBool = True
    tian_di_ren_he: StrictBool = False
    detailed_config: Dict[str, Any] = Field(default_factory=lambda: {"rule_version": RULE_VERSION})

    @validator("sub_rule")
    def valid_profile(cls, value):
        if value != SUB_RULE:
            raise ValueError("贵州麻将仅支持 MIL 2023 标准规")
        return value

    @validator("detailed_config", pre=True, always=True)
    def valid_config(cls, value):
        return normalize_config(value)

    @validator("use_flowers", "claim_protection", "open_cuohe", "tian_di_ren_he")
    def fixed_false(cls, value):
        if value:
            raise ValueError("贵州 MIL 标准规不支持此选项")
        return False


def apply_bot_tactical_policy(room):
    """Only actual seated bots close the switch; never reopen saved false."""
    if room.get("room_rule") == "guizhou" and any(type(user_id) is int and 0 <= user_id <= 10
                                                 for user_id in room.get("player_list", [])):
        room["tactical_call"] = False


async def create_guizhou_room(manager, player_id, *, room_name, gameround=1, password="",
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
        config = GuizhouRoomValidator(room_name=room_name, game_round=gameround,
            round_timer=roundTimerValue, step_timer=stepTimerValue, tips=tips,
            random_seed=random_seed, sub_rule=sub_rule, detailed_config=detailed_config,
            tourist_limit=tourist_limit, allow_spectator=allow_spectator,
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
    room.update(room_id=room_id, instance_id=str(uuid.uuid4()), room_type="custom", room_rule="guizhou",
                hepai_limit=0, tourist_limit=bool(tourist_limit), allow_spectator=bool(allow_spectator),
                max_player=4, player_list=[connection.user_id], player_settings={connection.user_id: profile},
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


async def handle_create_guizhou_room(server, connection_id, message, websocket):
    response = await server.room_manager.create_Guizhou_room(connection_id,
        room_name=message.get("roomname", ""), gameround=message.get("gameround", 1),
        password=message.get("password", ""), roundTimerValue=message.get("roundTimerValue", 20),
        stepTimerValue=message.get("stepTimerValue", 5), tips=message.get("tips", True),
        random_seed=message.get("random_seed", 0), sub_rule=message.get("sub_rule", SUB_RULE),
        detailed_config=message.get("detailed_config"), tourist_limit=message.get("tourist_limit", False),
        allow_spectator=message.get("allow_spectator", True), event_id=message.get("event_id"),
        count_tips=message.get("count_tips", False), pointer_tips=message.get("pointer_tips", True),
        **{key: message[key] for key in ("use_flowers", "claim_protection", "open_cuohe", "tactical_call", "tian_di_ren_he") if key in message})
    await websocket.send_json(response.model_dump(exclude_none=True))
