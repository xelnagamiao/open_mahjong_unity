"""MIL 山西房间契约；固定竞技底本，不暴露其他规则的馆规参数。"""
from typing import Any, Dict
from pydantic import StrictBool, field_validator, Field
from .room_validators import GBRoomValidator
from .standard_room import create_standard_room, handle_create_standard_room
from ..game_calculation.shanxi.rules import SUB_RULE, VERSION


class ShanxiRoomValidator(GBRoomValidator):
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
            raise ValueError("山西麻将只支持 MIL 2023 标准规")
        return value

    @field_validator("detailed_config", mode="before")
    @classmethod
    def valid_config(cls, value):
        if value is None:
            return {"rule_version": VERSION}
        if not isinstance(value, dict) or set(value) - {"rule_version"}:
            raise ValueError("山西麻将未开放自定义馆规")
        if value.get("rule_version", VERSION) != VERSION:
            raise ValueError("不支持的山西规则版本")
        return {"rule_version": VERSION}

    @field_validator("use_flowers", "open_cuohe", "tactical_call", "claim_protection", "tian_di_ren_he")
    @classmethod
    def fixed_false(cls, value):
        if value:
            raise ValueError("山西 MIL 标准规不支持此选项")
        return False


async def create_shanxi_room(manager, player_id, *, room_name, gameround=1, password='',
                             roundTimerValue=20, stepTimerValue=5, tips=True, random_seed=0,
                             sub_rule=SUB_RULE, detailed_config=None, tourist_limit=False,
                             allow_spectator=True, event_id=None, count_tips=False, pointer_tips=True):
    return await create_standard_room(
        manager, player_id, ShanxiRoomValidator, 'shanxi', room_name=room_name, gameround=gameround, password=password,
        roundTimerValue=roundTimerValue, stepTimerValue=stepTimerValue, tips=tips, random_seed=random_seed,
        sub_rule=sub_rule, detailed_config=detailed_config, tourist_limit=tourist_limit,
        allow_spectator=allow_spectator, event_id=event_id, count_tips=count_tips, pointer_tips=pointer_tips, extra_room_fields={"hepai_limit": 0},
    )


async def handle_create_shanxi_room(server, connection_id, message, websocket):
    await handle_create_standard_room(server, connection_id, message, websocket, create_shanxi_room, SUB_RULE, default_gameround=1, forwarded_keys=())
