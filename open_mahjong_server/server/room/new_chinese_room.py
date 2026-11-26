from typing import Any
from ..game_calculation.sanhua import rules as sanhua

from pydantic import Field, StrictBool, StrictInt, model_validator, field_validator, ValidationInfo

from .room_validators import GBRoomValidator
from .standard_room import create_standard_room, handle_create_standard_room

from ..game_calculation.new_chinese.rules import SUB_RULE, RULE_VERSION as EDITION, normalize_config as normalize_new_chinese_config, room_fan_limit


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
    detailed_config: dict[str, Any] = Field(default_factory=normalize_new_chinese_config, validate_default=True)

    @field_validator("game_round")
    @classmethod
    def validate_game_round(cls, value):
        if value not in (4, 8, 12, 16):
            raise ValueError("圈数须为4、8、12或16")
        return value

    @field_validator("starting_score")
    @classmethod
    def zero_start(cls, value):
        if value != 0:
            raise ValueError("新中式麻将固定0起始分")
        return value

    @model_validator(mode="before")
    @classmethod
    def fixed_sanhua_settings(cls, data):
        if isinstance(data, dict) and data.get("sub_rule") == sanhua.SUB_RULE:
            if data.get("hepai_limit", 4) != 4 or data.get("detailed_config") not in (None, {}, {"rule_version": sanhua.VERSION}):
                raise ValueError("散花天才麻将固定四番起和")
            return {**data, "hepai_limit": 4, "detailed_config": {"rule_version": sanhua.VERSION}}
        return data

    @model_validator(mode="after")
    def apply_fan_limit(self):
        if self.sub_rule == sanhua.SUB_RULE:
            return self
        self.hepai_limit = room_fan_limit(self.hepai_limit, self.detailed_config["exchange_three"])
        return self

    @field_validator("sub_rule")
    @classmethod
    def valid_profile(cls, value):
        if value not in (SUB_RULE, sanhua.SUB_RULE):
            raise ValueError("新中式麻将子规则无效")
        return value

    @field_validator("detailed_config", mode="before")
    @classmethod
    def valid_config(cls, value, info: ValidationInfo):
        if info.data.get("sub_rule") == sanhua.SUB_RULE:
            return {"rule_version": sanhua.VERSION}
        return normalize_new_chinese_config(value)

    @field_validator("use_flowers", "claim_protection", "open_cuohe", "tactical_call", "tian_di_ren_he")
    @classmethod
    def fixed_false(cls, value):
        if value:
            raise ValueError("新中式规则 不支持此选项")
        return False


async def create_new_chinese_room(manager, player_id, *, sub_rule=SUB_RULE, **config):
    return await create_standard_room(manager, player_id, NewChineseRoomValidator, "new_chinese", sub_rule=sub_rule, **config)


async def handle_create_new_chinese_room(server, connection_id, message, websocket):
    await handle_create_standard_room(server, connection_id, message, websocket, create_new_chinese_room, SUB_RULE)
