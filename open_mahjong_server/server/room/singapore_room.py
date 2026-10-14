"""新加坡麻将独立建房入口，采用用户规则书的固定配置。"""
import uuid

from pydantic import Field, StrictBool, StrictInt, validator

from .room_validators import GBRoomValidator
from ..game_calculation.singapore.rules import SUB_RULE, CONFIG
from ..response import Response

DESCRIPTION = "新加坡四人麻将，SPGG 2024 平台定型；148张、1台起和、5台封顶，动物与花季婚配，详见平台补充约定。"


class SingaporeRoomValidator(GBRoomValidator):
    room_name: str = Field(max_length=128)
    sub_rule: str = SUB_RULE
    game_round: StrictInt = 4
    round_timer: StrictInt
    step_timer: StrictInt
    tips: StrictBool = True
    count_tips: StrictBool = False
    pointer_tips: StrictBool = True
    tourist_limit: StrictBool = False
    allow_spectator: StrictBool = True
    use_flowers: StrictBool = True
    hepai_limit: StrictInt = 1
    claim_protection: StrictBool = False
    open_cuohe: StrictBool = False
    tactical_call: StrictBool = False
    tian_di_ren_he: StrictBool = False
    detailed_config: dict = Field(default_factory=lambda: dict(CONFIG))

    @validator("sub_rule")
    def valid_sub_rule(cls, value):
        if value != SUB_RULE:
            raise ValueError("不支持的新加坡麻将子规则")
        return value

    @validator("detailed_config", pre=True, always=True)
    def fixed_config(cls, value):
        if value is None or value == {}:
            return dict(CONFIG)
        if not isinstance(value, dict) or value != CONFIG:
            raise ValueError("新加坡麻将使用固定规则书配置")
        return dict(CONFIG)

    @validator("claim_protection", "open_cuohe", "tactical_call", "tian_di_ren_he")
    def disabled_options(cls, value):
        if value:
            raise ValueError("新加坡麻将不支持此建房选项")
        return value

    @validator("use_flowers")
    def required_replacement(cls, value):
        if not value:
            raise ValueError("新加坡麻将固定使用花季和动物补牌")
        return value

    @validator("hepai_limit")
    def fixed_minimum(cls, value):
        if value != 1:
            raise ValueError("新加坡麻将固定1台起和")
        return value


async def create_singapore_room(manager, player_id, *, room_name, gameround=4, password="",
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
        config = SingaporeRoomValidator(room_name=room_name, game_round=gameround, round_timer=roundTimerValue,
            step_timer=stepTimerValue, tips=tips, random_seed=random_seed, sub_rule=sub_rule,
            detailed_config=detailed_config, tourist_limit=tourist_limit, allow_spectator=allow_spectator,
            count_tips=count_tips, pointer_tips=pointer_tips, **fixed_options)
    except (TypeError, ValueError) as error:
        return Response(type="tips", success=False, message=f"房间配置无效: {error}")
    settings = manager.game_server.db_manager.get_user_settings(connection.user_id)
    if not settings:
        return Response(type="tips", success=False, message="获取用户设置失败")
    room_id = manager._generate_room_id()
    profile = {key: settings.get(key, default) for key, default in (("username", connection.username),
        ("title_id",1), ("profile_image_id",1), ("character_id",1), ("voice_id",1), ("avatar_frame_id",0))}
    profile["user_id"] = connection.user_id
    room = config.model_dump()
    room.update(room_id=room_id, instance_id=str(uuid.uuid4()), room_type="custom", room_rule="singapore",
        max_player=4, player_list=[connection.user_id], player_settings={connection.user_id:profile},
        has_password=bool(password), host_user_id=connection.user_id, host_name=connection.username,
        is_game_running=False, is_player_set_random_seed=config.random_seed != 0)
    manager._apply_event_fields(room, event_id)
    manager.rooms[room_id] = room
    if password:
        manager.room_passwords[room_id] = password
    connection.current_room_id = room_id
    await manager._broadcast_room_info(room_id)
    return Response(type="room/create_room_done", success=True, message="房间创建成功", room_info=room)


async def handle_create_singapore_room(server, connection_id, message, websocket):
    response = await create_singapore_room(server.room_manager, connection_id,
        room_name=message.get("roomname",""), gameround=message.get("gameround",4), password=message.get("password",""),
        roundTimerValue=message.get("roundTimerValue",20), stepTimerValue=message.get("stepTimerValue",5),
        tips=message.get("tips",True), random_seed=message.get("random_seed",0),
        sub_rule=message.get("sub_rule",SUB_RULE), detailed_config=message.get("detailed_config"),
        tourist_limit=message.get("tourist_limit",False), allow_spectator=message.get("allow_spectator",True),
        event_id=message.get("event_id"), count_tips=message.get("count_tips",False), pointer_tips=message.get("pointer_tips",True),
        **{key:message[key] for key in ("use_flowers","claim_protection","open_cuohe","tactical_call","tian_di_ren_he","hepai_limit") if key in message})
    await websocket.send_json(response.model_dump(exclude_none=True))
