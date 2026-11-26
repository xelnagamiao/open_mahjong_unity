"""MIL Hongzhong 2024 rooms with a fixed, versioned rules profile."""

from typing import Any

from pydantic import Field, StrictBool, StrictInt, field_validator

from .room_validators import GBRoomValidator
from .standard_room import create_standard_room, handle_create_standard_room
from ..game_calculation.hongzhong.scoring import SUB_RULE, CONFIG, normalize_config


class HongzhongRoomValidator(GBRoomValidator):
    room_name: str = Field(max_length=128)
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
    hepai_limit: StrictInt = 0
    claim_protection: StrictBool = False
    open_cuohe: StrictBool = False
    tactical_call: StrictBool = False
    tian_di_ren_he: StrictBool = False
    detailed_config: dict[str, Any] = Field(default_factory=lambda: dict(CONFIG), validate_default=True)

    @field_validator("sub_rule")
    @classmethod
    def valid_profile(cls,value):
        if value != SUB_RULE:
            raise ValueError("未开放的红中麻将子规则")
        return value

    @field_validator("detailed_config", mode="before")
    @classmethod
    def valid_config(cls,value):
        return normalize_config(value)

    @field_validator("claim_protection","open_cuohe","tactical_call","tian_di_ren_he","use_flowers","hepai_limit")
    @classmethod
    def fixed_false(cls,value):
        if value:
            raise ValueError("红中麻将不支持此选项")
        return value


async def create_hongzhong_room(manager, player_id, *, gameround=4, sub_rule=SUB_RULE, **config):
    return await create_standard_room(
        manager, player_id, HongzhongRoomValidator, 'hongzhong', gameround=gameround, sub_rule=sub_rule, extra_room_fields={"hepai_limit": 0}, **config
    )


async def handle_create_hongzhong_room(server, connection_id, message, websocket):
    await handle_create_standard_room(server, connection_id, message, websocket, create_hongzhong_room, SUB_RULE)
