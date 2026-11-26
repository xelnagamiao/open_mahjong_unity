from typing import Any

from pydantic import Field, StrictBool, StrictInt, field_validator

from .room_validators import GBRoomValidator
from .standard_room import create_standard_room, handle_create_standard_room

from ..game_calculation.shangrao.rules import SUB_RULE, RULE_VERSION as EDITION, normalize_config as normalize_shangrao_config


class ShangraoRoomValidator(GBRoomValidator):
    sub_rule: str = SUB_RULE
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
    detailed_config: dict[str, Any] = Field(default_factory=normalize_shangrao_config, validate_default=True)

    @field_validator("sub_rule")
    @classmethod
    def valid_profile(cls, value):
        if value != SUB_RULE:
            raise ValueError("上饶麻将子规则无效")
        return value

    @field_validator("detailed_config", mode="before")
    @classmethod
    def valid_config(cls, value):
        return normalize_shangrao_config(value)

    @field_validator("use_flowers", "claim_protection", "open_cuohe", "tactical_call", "tian_di_ren_he")
    @classmethod
    def fixed_false(cls, value):
        if value:
            raise ValueError("上饶规则 不支持此选项")
        return False


async def create_shangrao_room(manager, player_id, *, gameround=4, sub_rule=SUB_RULE, **config):
    return await create_standard_room(
        manager, player_id, ShangraoRoomValidator, 'shangrao', gameround=gameround, sub_rule=sub_rule, extra_room_fields={"hepai_limit": 1}, **config
    )


async def handle_create_shangrao_room(server, connection_id, message, websocket):
    await handle_create_standard_room(server, connection_id, message, websocket, create_shangrao_room, SUB_RULE)
