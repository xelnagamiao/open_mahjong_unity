from typing import Any

from pydantic import Field, StrictBool, StrictInt, field_validator

from .room_validators import GBRoomValidator
from .standard_room import create_standard_room, handle_create_standard_room

from ..game_calculation.beijing.rules import SUB_RULE, RULE_VERSION as EDITION, normalize_config as normalize_beijing_config


class BeijingRoomValidator(GBRoomValidator):
    sub_rule: str = SUB_RULE
    starting_score: StrictInt = 0
    hepai_limit: StrictInt = 0
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
    detailed_config: dict[str, Any] = Field(default_factory=normalize_beijing_config, validate_default=True)

    @field_validator("starting_score", "hepai_limit")
    @classmethod
    def zero_start(cls, value):
        if value != 0:
            raise ValueError("北京麻将固定0起始分、0翻起和")
        return value

    @field_validator("sub_rule")
    @classmethod
    def valid_profile(cls, value):
        if value != SUB_RULE:
            raise ValueError("北京麻将子规则无效")
        return value

    @field_validator("detailed_config", mode="before")
    @classmethod
    def valid_config(cls, value):
        return normalize_beijing_config(value)

    @field_validator("use_flowers", "claim_protection", "open_cuohe", "tactical_call", "tian_di_ren_he")
    @classmethod
    def fixed_false(cls, value):
        if value:
            raise ValueError("北京规则 不支持此选项")
        return False


async def create_beijing_room(manager, player_id, *, sub_rule=SUB_RULE, **config):
    return await create_standard_room(manager, player_id, BeijingRoomValidator, "beijing", sub_rule=sub_rule, **config)


async def handle_create_beijing_room(server, connection_id, message, websocket):
    await handle_create_standard_room(server, connection_id, message, websocket, create_beijing_room, SUB_RULE)
