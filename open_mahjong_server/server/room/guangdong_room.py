"""Guangdong family rooms: preserve MIL Tuidao and select the MIL flower-joker profile."""

import uuid
from typing import Any

from pydantic import Field, StrictBool, StrictInt, validator

from .room_validators import GBRoomValidator
from .tuidao_room import TuidaoRoomValidator, create_tuidao_room
from ..game_calculation.guangdong.config import SUB_RULE, normalize_config
from ..game_calculation.tuidao.rules import SUB_RULE as TUIDAO_SUB_RULE
from ..response import Response


FIXED_OPTIONS = (
    "use_flowers", "claim_protection", "open_cuohe", "tian_di_ren_he",
)


def enforce_guangdong_tactical(room):
    """Only occupied bot seats disable MIL flower-ghost tactical calls; never enable."""
    from ..gamestate.public.claim_protection import has_bot_players
    if (room.get("room_rule") == "guangdong" and room.get("sub_rule") == SUB_RULE
            and (has_bot_players(room.get("player_list")) or has_bot_players(room.get("seat_list")))):
        room["tactical_call"] = False


class GuangdongMilRoomValidator(GBRoomValidator):
    sub_rule: str = SUB_RULE
    game_round: StrictInt = 4
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
    hepai_limit: StrictInt = 2
    detailed_config: dict[str, Any] = Field(default_factory=dict)

    @validator("sub_rule")
    def valid_profile(cls, value):
        if value != SUB_RULE:
            raise ValueError("不支持的广东花鬼子规则")
        return value

    @validator("detailed_config", pre=True, always=True)
    def valid_config(cls, value):
        return normalize_config(value)

    @validator(*FIXED_OPTIONS)
    def fixed_false(cls, value):
        if value:
            raise ValueError("MIL 广东花鬼不支持此选项；四张花鬼留在手中，不补花")
        return False

    @validator("hepai_limit")
    def fixed_minimum_fan(cls, value):
        if value != 2:
            raise ValueError("MIL 广东花鬼固定两番起和")
        return value


def GuangdongRoomValidator(**config):
    """Select before validating so a Tuidao config cannot become a joker room."""
    sub_rule = config.get("sub_rule", TUIDAO_SUB_RULE)
    if sub_rule == TUIDAO_SUB_RULE:
        return TuidaoRoomValidator(**config)
    if sub_rule == SUB_RULE:
        return GuangdongMilRoomValidator(**config)
    raise ValueError("不支持的广东麻将子规则")


async def create_guangdong_room(
    manager, player_id, *, room_name, gameround=4, password="",
    roundTimerValue=20, stepTimerValue=5, tips=True, random_seed=0,
    sub_rule=TUIDAO_SUB_RULE, detailed_config=None, tourist_limit=False,
    allow_spectator=True, event_id=None, count_tips=False, pointer_tips=True,
    **fixed_options,
):
    # Keep old calls without a sub-rule on the existing, unchanged Tuidao path.
    if sub_rule == TUIDAO_SUB_RULE:
        try:
            TuidaoRoomValidator(room_name=room_name, game_round=gameround,
                round_timer=roundTimerValue, step_timer=stepTimerValue, **fixed_options)
        except (TypeError, ValueError) as exc:
            return Response(type="tips", success=False, message=f"房间配置无效: {exc}")
        return await create_tuidao_room(
            manager, player_id, room_name=room_name, gameround=gameround, password=password,
            roundTimerValue=roundTimerValue, stepTimerValue=stepTimerValue, tips=tips,
            random_seed=random_seed, sub_rule=sub_rule, detailed_config=detailed_config,
            tourist_limit=tourist_limit, allow_spectator=allow_spectator, event_id=event_id,
            count_tips=count_tips, pointer_tips=pointer_tips,
            tactical_call=fixed_options.get("tactical_call", True),
        )
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
        config = GuangdongRoomValidator(
            room_name=room_name, game_round=gameround, round_timer=roundTimerValue,
            step_timer=stepTimerValue, tips=tips, random_seed=random_seed, sub_rule=sub_rule,
            detailed_config=detailed_config, tourist_limit=tourist_limit,
            allow_spectator=allow_spectator, count_tips=count_tips,
            pointer_tips=pointer_tips, **fixed_options,
        )
    except (TypeError, ValueError) as error:
        return Response(type="tips", success=False, message=f"房间配置无效: {error}")
    settings = manager.game_server.db_manager.get_user_settings(connection.user_id)
    if not settings:
        return Response(type="tips", success=False, message="获取用户设置失败")
    room_id = manager._generate_room_id()
    profile = {key: settings.get(key, default) for key, default in (
        ("username", connection.username), ("title_id", 1), ("profile_image_id", 1),
        ("character_id", 1), ("voice_id", 1), ("avatar_frame_id", 0),
    )}
    profile["user_id"] = connection.user_id
    room = config.model_dump()
    room.update(
        room_id=room_id, instance_id=str(uuid.uuid4()), room_type="custom", room_rule="guangdong",
        max_player=4, player_list=[connection.user_id], player_settings={connection.user_id: profile},
        has_password=bool(password), host_user_id=connection.user_id, host_name=connection.username,
        is_game_running=False, is_player_set_random_seed=config.random_seed != 0,
    )
    manager._apply_event_fields(room, event_id)
    manager.rooms[room_id] = room
    if password:
        manager.room_passwords[room_id] = password
    connection.current_room_id = room_id
    await manager._broadcast_room_info(room_id)
    return Response(type="room/create_room_done", success=True, message="房间创建成功", room_info=room)


async def handle_create_guangdong_room(server, connection_id, message, websocket):
    response = await create_guangdong_room(
        server.room_manager, connection_id, room_name=message.get("roomname", ""),
        gameround=message.get("gameround", 1), password=message.get("password", ""),
        roundTimerValue=message.get("roundTimerValue", 20), stepTimerValue=message.get("stepTimerValue", 5),
        tips=message.get("tips", True), random_seed=message.get("random_seed", 0),
        sub_rule=message.get("sub_rule", TUIDAO_SUB_RULE), detailed_config=message.get("detailed_config"),
        tourist_limit=message.get("tourist_limit", False), allow_spectator=message.get("allow_spectator", True),
        event_id=message.get("event_id"), count_tips=message.get("count_tips", False),
        pointer_tips=message.get("pointer_tips", True),
        **{key: message[key] for key in (*FIXED_OPTIONS, "tactical_call", "hepai_limit") if key in message},
    )
    await websocket.send_json(response.model_dump(exclude_none=True))
