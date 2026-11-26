"""新加坡麻将独立建房入口，采用用户规则书的固定配置。"""

from pydantic import Field, StrictBool, StrictInt, field_validator

from .room_validators import GBRoomValidator
from .standard_room import create_standard_room, handle_create_standard_room
from ..game_calculation.singapore.rules import SUB_RULE, CONFIG

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
    detailed_config: dict = Field(default_factory=lambda: dict(CONFIG), validate_default=True)

    @field_validator("sub_rule")
    @classmethod
    def valid_sub_rule(cls, value):
        if value != SUB_RULE:
            raise ValueError("不支持的新加坡麻将子规则")
        return value

    @field_validator("detailed_config", mode="before")
    @classmethod
    def fixed_config(cls, value):
        if value is None or value == {}:
            return dict(CONFIG)
        if not isinstance(value, dict) or value != CONFIG:
            raise ValueError("新加坡麻将使用固定规则书配置")
        return dict(CONFIG)

    @field_validator("claim_protection", "open_cuohe", "tactical_call", "tian_di_ren_he")
    @classmethod
    def disabled_options(cls, value):
        if value:
            raise ValueError("新加坡麻将不支持此建房选项")
        return value

    @field_validator("use_flowers")
    @classmethod
    def required_replacement(cls, value):
        if not value:
            raise ValueError("新加坡麻将固定使用花季和动物补牌")
        return value

    @field_validator("hepai_limit")
    @classmethod
    def fixed_minimum(cls, value):
        if value != 1:
            raise ValueError("新加坡麻将固定1台起和")
        return value


async def create_singapore_room(manager, player_id, *, gameround=4, sub_rule=SUB_RULE, **config):
    return await create_standard_room(
        manager, player_id, SingaporeRoomValidator, 'singapore', gameround=gameround, sub_rule=sub_rule, **config
    )


async def handle_create_singapore_room(server, connection_id, message, websocket):
    await handle_create_standard_room(server, connection_id, message, websocket, create_singapore_room, SUB_RULE)
