"""Room-bound validation for Sichuan duplicate claim clocks."""
from pydantic import BaseModel, StrictBool, StrictInt, validator

from ..game_calculation.sichuan.duplicate_rules import is_duplicate_sub_rule


SECOND_WAIT_SECONDS = (3, 5, 8, 10, 15, 20)


class SichuanDuplicateTiming(BaseModel):
    real_time: StrictBool = False
    tactical_grace_seconds: StrictInt = 5

    @validator("tactical_grace_seconds")
    def validate_seconds(cls, value):
        if value not in SECOND_WAIT_SECONDS:
            raise ValueError("第二等待时间必须为 3、5、8、10、15 或 20 秒")
        return value


def timing_config(sub_rule, config):
    """Keep legacy rooms unchanged, and reject activating this mode elsewhere."""
    timing = SichuanDuplicateTiming(**{
        key: config[key] for key in ("real_time", "tactical_grace_seconds") if key in config
    })
    if not is_duplicate_sub_rule(sub_rule):
        if timing.real_time or timing.tactical_grace_seconds != 5:
            raise ValueError("真实时间与第二等待时间仅支持川麻双打复式")
        return {}
    return timing.model_dump()
