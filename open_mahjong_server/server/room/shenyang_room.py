"""MIL 沈阳房间契约；固定竞技底本，不暴露其他规则的馆规参数。"""
from typing import Any, Dict
from pydantic import StrictBool, field_validator, Field
from .room_validators import GBRoomValidator
from .standard_room import create_standard_room, handle_create_standard_room
from ..game_calculation.shenyang.rules import SUB_RULE, VERSION


class ShenyangRoomValidator(GBRoomValidator):
    sub_rule: str = SUB_RULE
    tips: StrictBool = True
    use_flowers: StrictBool = False
    claim_protection: StrictBool = False
    tourist_limit: StrictBool = False
    allow_spectator: StrictBool = True
    count_tips: StrictBool = False
    pointer_tips: StrictBool = True
    detailed_config: Dict[str, Any] = {"rule_version": VERSION}

    @field_validator("sub_rule")
    @classmethod
    def valid_sub_rule(cls, value):
        if value != SUB_RULE:
            raise ValueError("沈阳麻将只支持 MIL 2025 标准规")
        return value

    @field_validator("detailed_config", mode="before")
    @classmethod
    def valid_config(cls, value):
        if value is None:
            return {"rule_version": VERSION}
        if not isinstance(value, dict) or set(value) - {"rule_version"}:
            raise ValueError("沈阳麻将未开放自定义馆规")
        if value.get("rule_version", VERSION) != VERSION:
            raise ValueError("不支持的沈阳规则版本")
        return {"rule_version": VERSION}

    @field_validator("use_flowers", "open_cuohe", "tactical_call", "claim_protection", "tian_di_ren_he")
    @classmethod
    def fixed_false(cls, value):
        if value:
            raise ValueError("沈阳 MIL 标准规不支持此选项")
        return False


async def create_shenyang_room(manager, player_id, *, gameround=1, sub_rule=SUB_RULE, **config):
    return await create_standard_room(
        manager, player_id, ShenyangRoomValidator, 'shenyang', gameround=gameround, sub_rule=sub_rule, extra_room_fields={"hepai_limit": 0}, **config
    )


async def handle_create_shenyang_room(server, connection_id, message, websocket):
    await handle_create_standard_room(server, connection_id, message, websocket, create_shenyang_room, SUB_RULE, default_gameround=1)
