"""MIL Changchun 2024 rooms with a fixed, versioned rules profile."""

from typing import Any

from pydantic import Field, StrictBool, StrictInt, field_validator

from .room_validators import GBRoomValidator
from .standard_room import create_standard_room, handle_create_standard_room
from ..game_calculation.changchun.rules import SUB_RULE, EDITION

CONFIG = {"edition": EDITION, "fan_cap": 6, "special_initial_replacement": 0, "ordinary_jokers": False}

def normalize_changchun_config(raw=None):
    if raw is None:
        return dict(CONFIG)
    if not isinstance(raw, dict) or set(raw)-CONFIG.keys():
        raise ValueError("长春MIL2024配置含未支持项目")
    if any(type(v) is not type(CONFIG[k]) or v != CONFIG[k] for k,v in raw.items()):
        raise ValueError("长春MIL2024规则配置不能被覆盖")
    return dict(CONFIG)


def enforce_changchun_tactical_room(room):
    """Seated bots disable tactical claims; never auto-enable an existing room."""
    if room.get('room_rule') != 'changchun':
        return
    players = room.get('player_list', ())
    seats = room.get('seat_list', players)
    has_bot = any(type(user_id) is int and 0 <= user_id <= 10 and user_id in players
                  for user_id in seats)
    if has_bot:
        room['tactical_call'] = False


class ChangchunRoomValidator(GBRoomValidator):
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
    tactical_call: StrictBool = True
    tian_di_ren_he: StrictBool = False
    detailed_config: dict[str, Any] = Field(default_factory=lambda: dict(CONFIG), validate_default=True)

    @field_validator("sub_rule")
    @classmethod
    def valid_profile(cls,value):
        if value != SUB_RULE:
            raise ValueError("未开放的长春麻将子规则")
        return value

    @field_validator("detailed_config", mode="before")
    @classmethod
    def valid_config(cls,value):
        return normalize_changchun_config(value)

    @field_validator("claim_protection","open_cuohe","tian_di_ren_he","use_flowers","hepai_limit")
    @classmethod
    def fixed_false(cls,value):
        if value:
            raise ValueError("长春麻将不支持此选项")
        return value


async def create_changchun_room(manager, player_id, *, gameround=4, sub_rule=SUB_RULE, **config):
    return await create_standard_room(
        manager, player_id, ChangchunRoomValidator, 'changchun', gameround=gameround, sub_rule=sub_rule, extra_room_fields={"hepai_limit": 0}, **config
    )


async def handle_create_changchun_room(server, connection_id, message, websocket):
    await handle_create_standard_room(server, connection_id, message, websocket, create_changchun_room, SUB_RULE)
