"""Fixed MIL 2025 Hangzhou profile and existing room lifecycle integration."""

from typing import Any, Dict

from pydantic import Field, StrictBool, StrictInt, field_validator

from .room_validators import GBRoomValidator
from .standard_room import create_standard_room, handle_create_standard_room
from ..game_calculation.hangzhou.rules import RULE_VERSION, SUB_RULE, normalize_config


def enforce_hangzhou_tactical_room(room):
    if room.get("room_rule") != "hangzhou":
        return
    from ..gamestate.public.spectator_rules import AI_RESERVED_MAX_USER_ID
    if any(type(uid) is int and 0 <= uid <= AI_RESERVED_MAX_USER_ID for uid in room.get("player_list", [])):
        room["tactical_call"] = False


class HangzhouRoomValidator(GBRoomValidator):
    sub_rule: str = SUB_RULE
    game_round: StrictInt
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
    detailed_config: Dict[str, Any] = Field(default_factory=lambda: {"rule_version": RULE_VERSION}, validate_default=True)

    @field_validator("sub_rule")
    @classmethod
    def valid_profile(cls, value):
        if value != SUB_RULE:
            raise ValueError("杭州麻将仅支持 MIL 2025 标准规")
        return value

    @field_validator("detailed_config", mode="before")
    @classmethod
    def valid_config(cls, value):
        return normalize_config(value)

    @field_validator("use_flowers", "claim_protection", "open_cuohe", "tian_di_ren_he")
    @classmethod
    def fixed_false(cls, value):
        if value:
            raise ValueError("杭州 MIL 标准规不支持此选项")
        return False


async def create_hangzhou_room(manager, player_id, *, gameround=4, sub_rule=SUB_RULE, **config):
    return await create_standard_room(
        manager, player_id, HangzhouRoomValidator, 'hangzhou', gameround=gameround, sub_rule=sub_rule, extra_room_fields={"hepai_limit": 0}, **config
    )


async def handle_create_hangzhou_room(server, connection_id, message, websocket):
    await handle_create_standard_room(server, connection_id, message, websocket, create_hangzhou_room, SUB_RULE)
