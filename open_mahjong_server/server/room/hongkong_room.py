"""Hong Kong room configuration, independently versioned from Taiwan rooms."""

from typing import Any, Dict, Optional
import uuid

from pydantic import validator

from .room_validators import GBRoomValidator
from ..game_calculation.hongkong.models import HongKongRules, PROFILES, SUPPORTED_PROFILES
from ..response import Response


class HongKongRoomValidator(GBRoomValidator):
    tips: bool = True
    sub_rule: str = PROFILES[0]
    detailed_config: Optional[Dict[str, Any]] = None
    claim_protection: bool = False

    @validator('sub_rule')
    def validate_hongkong_profile(cls, value):
        if value not in SUPPORTED_PROFILES:
            raise ValueError('香港麻将子规则须为清章、新章十三张或新章十六张')
        return value

    @validator('detailed_config', pre=True, always=True)
    def validate_hongkong_config(cls, value, values):
        rules = HongKongRules.from_room(dict(sub_rule=values.get('sub_rule',PROFILES[0]),detailed_config=value))
        return rules.room_config()

    @validator('open_cuohe', 'tactical_call')
    def no_illegal_hand_variants(cls, value):
        if value:
            raise ValueError('本规则版本仅接受合法和牌及正常吃碰杠')
        return value


async def create_hongkong_room(manager, player_id, *, room_name, gameround, password='',
                               roundTimerValue=20, stepTimerValue=5, tips=True, random_seed=0,
                               sub_rule=PROFILES[0], detailed_config=None, tourist_limit=False,
                               allow_spectator=True, event_id=None, count_tips=False, pointer_tips=True):
    connection = manager.game_server.players.get(player_id)
    if connection is None or not connection.user_id:
        return Response(type='tips',success=False,message='请先登录')
    if connection.current_room_id:
        return Response(type='tips',success=False,message='请先退出当前房间')
    blocked = manager._reject_room_entry_conflicts(connection.user_id,'创建房间')
    if blocked:
        return blocked
    event_id = manager._normalize_event_id(event_id)
    blocked = manager._validate_event_for_room(event_id,connection.user_id)
    if blocked:
        return blocked
    try:
        config = HongKongRoomValidator(room_name=room_name,game_round=gameround,
                    round_timer=roundTimerValue,step_timer=stepTimerValue,tips=tips,
                    random_seed=random_seed,sub_rule=sub_rule,detailed_config=detailed_config)
    except (TypeError,ValueError) as error:
        return Response(type='tips',success=False,message=f'房间配置无效: {error}')
    settings = manager.game_server.db_manager.get_user_settings(connection.user_id)
    if not settings:
        return Response(type='tips',success=False,message='获取用户设置失败')
    room_id = manager._generate_room_id()
    profile = {key:settings.get(key,default) for key,default in
               (("username",connection.username),("title_id",1),("profile_image_id",1),
                ("character_id",1),("voice_id",1),("avatar_frame_id",0))}
    profile["user_id"] = connection.user_id
    room = config.model_dump()
    room.update(room_id=room_id,instance_id=str(uuid.uuid4()),room_type='custom',room_rule='hongkong',
                hepai_limit=HongKongRules.from_room(room).minimum_fan,tourist_limit=tourist_limit,
                allow_spectator=allow_spectator,max_player=4,player_list=[connection.user_id],
                player_settings={connection.user_id:profile},
                has_password=bool(password),host_user_id=connection.user_id,host_name=connection.username,
                is_game_running=False,is_player_set_random_seed=config.random_seed!=0,
                count_tips=bool(count_tips),pointer_tips=bool(pointer_tips))
    manager._apply_event_fields(room,event_id)
    manager.rooms[room_id] = room
    if password:
        manager.room_passwords[room_id] = password
    connection.current_room_id = room_id
    await manager._broadcast_room_info(room_id)
    return Response(type='room/create_room_done',success=True,message='房间创建成功',room_info=room)


async def handle_create_hongkong_room(server, connection_id, message, websocket):
    response = await server.room_manager.create_HongKong_room(connection_id,
        room_name=message.get('roomname',''),gameround=message.get('gameround',1),
        password=message.get('password',''),roundTimerValue=message.get('roundTimerValue',20),
        stepTimerValue=message.get('stepTimerValue',5),tips=message.get('tips',True),
        random_seed=message.get('random_seed',0),sub_rule=message.get('sub_rule',PROFILES[0]),
        detailed_config=message.get('detailed_config'),tourist_limit=message.get('tourist_limit',False),
        allow_spectator=message.get('allow_spectator',True),event_id=message.get('event_id'),
        count_tips=message.get('count_tips',False),pointer_tips=message.get('pointer_tips',True))
    await websocket.send_json(response.model_dump(exclude_none=True))
