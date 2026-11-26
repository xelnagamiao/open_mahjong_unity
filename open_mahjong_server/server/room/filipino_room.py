"""Filipino room configuration, independently versioned from Taiwan rooms."""

from typing import Any, Dict, Optional

from pydantic import field_validator, ValidationInfo, Field

from .room_validators import GBRoomValidator
from .standard_room import create_standard_room, handle_create_standard_room
from ..game_calculation.filipino.rules import Rules, SUB_RULE
PROFILES = (SUB_RULE,)
SUPPORTED_PROFILES = PROFILES


class FilipinoRoomValidator(GBRoomValidator):
    tips: bool = True
    sub_rule: str = PROFILES[0]
    detailed_config: Optional[Dict[str, Any]] = Field(default=None, validate_default=True)
    claim_protection: bool = False

    @field_validator('sub_rule')
    @classmethod
    def validate_filipino_profile(cls, value):
        if value not in SUPPORTED_PROFILES:
            raise ValueError('菲律宾麻将仅支持classic16')
        return value

    @field_validator('detailed_config', mode="before")
    @classmethod
    def validate_filipino_config(cls, value, info: ValidationInfo):
        return Rules.from_dict(value).snapshot()

    @field_validator('open_cuohe', 'tactical_call', 'claim_protection')
    @classmethod
    def no_illegal_hand_variants(cls, value):
        if value:
            raise ValueError('本规则版本仅接受合法和牌及正常吃碰杠')
        return value


async def create_filipino_room(manager, player_id, *, room_name, gameround=1, password='',
                             roundTimerValue=20, stepTimerValue=5, tips=True, random_seed=0,
                             sub_rule=PROFILES[0], detailed_config=None, tourist_limit=False,
                             allow_spectator=True, event_id=None, count_tips=False, pointer_tips=True):
    return await create_standard_room(
        manager, player_id, FilipinoRoomValidator, 'filipino', room_name=room_name, gameround=gameround, password=password,
        roundTimerValue=roundTimerValue, stepTimerValue=stepTimerValue, tips=tips, random_seed=random_seed,
        sub_rule=sub_rule, detailed_config=detailed_config, tourist_limit=tourist_limit,
        allow_spectator=allow_spectator, event_id=event_id, count_tips=count_tips, pointer_tips=pointer_tips, extra_room_fields={"hepai_limit": 0},
    )


async def handle_create_filipino_room(server, connection_id, message, websocket):
    await handle_create_standard_room(server, connection_id, message, websocket, create_filipino_room, PROFILES[0], default_gameround=1, forwarded_keys=())
