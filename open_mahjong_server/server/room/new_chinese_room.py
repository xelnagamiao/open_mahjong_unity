from typing import Any

from pydantic import Field, StrictBool, StrictInt, validator

from .room_validators import GBRoomValidator
from .standard_room import create_standard_room, handle_create_standard_room

from ..game_calculation.new_chinese.rules import SUB_RULE, RULE_VERSION as EDITION, normalize_config as normalize_new_chinese_config


class NewChineseRoomValidator(GBRoomValidator):
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
    detailed_config: dict[str, Any] = Field(default_factory=normalize_new_chinese_config)

    @validator("game_round")
    def validate_game_round(cls, value):
        if value not in (4, 8, 12, 16):
            raise ValueError("圈数须为4、8、12或16")
        return value

    @validator("starting_score", "hepai_limit")
    def zero_start(cls, value):
        if value != 0:
            raise ValueError("新中式麻将固定0起始分、0番起和")
        return value

    @validator("sub_rule")
    def valid_profile(cls, value):
        if value != SUB_RULE:
            raise ValueError("新中式麻将子规则无效")
        return value

    @validator("detailed_config", pre=True, always=True)
    def valid_config(cls, value):
        return normalize_new_chinese_config(value)

    @validator("use_flowers", "claim_protection", "open_cuohe", "tactical_call", "tian_di_ren_he")
    def fixed_false(cls, value):
        if value:
            raise ValueError("新中式规则 不支持此选项")
        return False


async def create_new_chinese_room(manager, player_id, *, sub_rule=SUB_RULE, **config):
    return await create_standard_room(manager, player_id, NewChineseRoomValidator, "new_chinese", sub_rule=sub_rule, **config)


async def handle_create_new_chinese_room(server, connection_id, message, websocket):
    await handle_create_standard_room(server, connection_id, message, websocket, create_new_chinese_room, SUB_RULE)
