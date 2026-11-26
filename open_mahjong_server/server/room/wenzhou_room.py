"""MIL Wenzhou 2024 room validation, with the existing room lifecycle."""

from typing import Any

from pydantic import Field, StrictBool, StrictInt, field_validator

from .room_validators import GBRoomValidator
from .standard_room import create_standard_room, handle_create_standard_room

SUB_RULE = "wenzhou/mil2024"
EDITION = "mil-wenzhou-2024-om1"


def normalize_wenzhou_config(value=None):
    if value is None:
        return {"edition": EDITION}
    if not isinstance(value, dict) or set(value) - {"edition"}:
        raise ValueError("温州 MIL 2024 不支持未公布的馆规设置")
    if value.get("edition", EDITION) != EDITION:
        raise ValueError("不支持的温州麻将规则版本")
    return {"edition": EDITION}


class WenzhouRoomValidator(GBRoomValidator):
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
    detailed_config: dict[str, Any] = Field(default_factory=lambda: {"edition": EDITION}, validate_default=True)

    @field_validator("sub_rule")
    @classmethod
    def valid_profile(cls, value):
        if value != SUB_RULE:
            raise ValueError("温州麻将仅支持 MIL 2024")
        return value

    @field_validator("detailed_config", mode="before")
    @classmethod
    def valid_config(cls, value):
        return normalize_wenzhou_config(value)

    @field_validator("use_flowers", "claim_protection", "open_cuohe", "tactical_call", "tian_di_ren_he")
    @classmethod
    def fixed_false(cls, value):
        if value:
            raise ValueError("温州 MIL 2024 不支持此选项")
        return False


async def create_wenzhou_room(manager, player_id, *, gameround=4, sub_rule=SUB_RULE, **config):
    return await create_standard_room(
        manager, player_id, WenzhouRoomValidator, 'wenzhou', gameround=gameround, sub_rule=sub_rule, extra_room_fields={"hepai_limit": 0}, **config
    )


async def handle_create_wenzhou_room(server, connection_id, message, websocket):
    await handle_create_standard_room(server, connection_id, message, websocket, create_wenzhou_room, SUB_RULE)
