from typing import Any
from pydantic import Field, StrictBool, StrictInt, field_validator
from .room_validators import GBRoomValidator
from .standard_room import create_standard_room, handle_create_standard_room
from ..game_calculation.fuzhou.rules import SUB_RULE, VERSION, normalize_config


class FuzhouRoomValidator(GBRoomValidator):
    sub_rule: str = SUB_RULE
    game_round: StrictInt = 1
    round_timer: StrictInt
    step_timer: StrictInt
    tips: StrictBool = True
    tourist_limit: StrictBool = False
    allow_spectator: StrictBool = True
    count_tips: StrictBool = False
    pointer_tips: StrictBool = True
    use_flowers: StrictBool = True
    claim_protection: StrictBool = False
    open_cuohe: StrictBool = False
    tactical_call: StrictBool = False
    tian_di_ren_he: StrictBool = False
    detailed_config: dict[str,Any] = Field(default_factory=normalize_config, validate_default=True)

    @field_validator("game_round")
    @classmethod
    def valid_duration(cls,value):
        if value not in (1,2,4):
            raise ValueError("福州时长只能为一圈、半庄或一雀")
        return value

    @field_validator("sub_rule")
    @classmethod
    def valid_profile(cls,value):
        if value!=SUB_RULE:
            raise ValueError("未开放的福州子规则")
        return value

    @field_validator("detailed_config", mode="before")
    @classmethod
    def valid_config(cls,value):
        return normalize_config(value)

    @field_validator("claim_protection","open_cuohe","tactical_call","tian_di_ren_he")
    @classmethod
    def fixed_false(cls,value):
        if value:
            raise ValueError("福州麻将不支持此选项")
        return False

    @field_validator("use_flowers")
    @classmethod
    def fixed_flowers(cls,value):
        if not value:
            raise ValueError("福州固定使用全部字牌及八花")
        return True


async def create_fuzhou_room(manager, player_id, *, gameround=1, sub_rule=SUB_RULE, **config):
    return await create_standard_room(
        manager, player_id, FuzhouRoomValidator, 'fuzhou', gameround=gameround, sub_rule=sub_rule, extra_room_fields={"hepai_limit": 0}, **config
    )


async def handle_create_fuzhou_room(server, connection_id, message, websocket):
    await handle_create_standard_room(server, connection_id, message, websocket, create_fuzhou_room, SUB_RULE, default_gameround=1)
