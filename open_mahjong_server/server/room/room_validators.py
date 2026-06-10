from pydantic import BaseModel, validator
from typing import List, Optional

class GBRoomValidator(BaseModel):
    room_name: str
    game_round: int
    round_timer: int
    step_timer: int
    random_seed: int
    open_cuohe: bool = False
    show_moqie_hint: bool = False
    tactical_call: bool = False
    
    @validator('room_name')
    def validate_room_name(cls, v):
        if not v.strip():
            raise ValueError('房间名不能为空')
        return v.strip()

    @validator('game_round')
    def validate_game_round(cls, v):
        if v < 1 or v > 4:
            raise ValueError('游戏圈数必须在1-4之间')
        return v

    @validator('round_timer')
    def validate_timers(cls, v):
        if v < 0 or v > 1000:
            raise ValueError('局时不能小于0或大于1000')
        return v
    
    @validator('step_timer')
    def validate_step_timer(cls, v):
        if v < 0 or v > 100:
            raise ValueError('步时不能小于0或大于100')
        return v
    
    @validator('random_seed')
    def validate_random_seed(cls, v):
        if v is None:
            return 0
        if isinstance(v, int):
            if v < 0:
                raise ValueError('随机种子不能为负数')
            return v
        if isinstance(v, str):
            v = v.strip()
            if v == "" or v == "0":
                return 0
            if len(v) == 64 and all(c in '0123456789abcdefABCDEF' for c in v):
                return int(v, 16)
            try:
                return int(v)
            except ValueError:
                raise ValueError('随机种子必须是整数或十六进制字符串')
        raise ValueError('随机种子类型无效')

class RiichiRoomValidator(BaseModel):
    room_name: str
    game_round: int
    round_timer: int
    step_timer: int
    random_seed: int
    open_cuohe: bool = False
    show_moqie_hint: bool = False
    hepai_limit: int = 1  # 自定义起和番数，低于此番数视为错和（仅在 open_cuohe=True 时触发罚分）
    red_dora: bool = True
    allow_kuikae: bool = False  # 允许食替（吃什么打什么）；默认关即标准日麻禁切
    hepai_way: str = "head_bump"  # head_bump / multi_ron / three_ron_abort
    open_xiru: bool = True   # 西入（非全庄时预定局数打完后按点数/连庄延长）
    open_tobi: bool = True   # 击飞（任一家低于 0 分则本局结束后整场终了）

    @validator('room_name')
    def validate_room_name(cls, v):
        if not v.strip():
            raise ValueError('房间名不能为空')
        return v.strip()

    @validator('game_round')
    def validate_game_round(cls, v):
        if v < 1 or v > 4:
            raise ValueError('游戏圈数必须在1-4之间')
        return v

    @validator('round_timer')
    def validate_timers(cls, v):
        if v < 0 or v > 1000:
            raise ValueError('局时不能小于0或大于1000')
        return v

    @validator('step_timer')
    def validate_step_timer(cls, v):
        if v < 0 or v > 100:
            raise ValueError('步时不能小于0或大于100')
        return v

    @validator('random_seed')
    def validate_random_seed(cls, v):
        if v is None:
            return 0
        if isinstance(v, int):
            if v < 0:
                raise ValueError('随机种子不能为负数')
            return v
        if isinstance(v, str):
            v = v.strip()
            if v == "" or v == "0":
                return 0
            if len(v) == 64 and all(c in '0123456789abcdefABCDEF' for c in v):
                return int(v, 16)
            try:
                return int(v)
            except ValueError:
                raise ValueError('随机种子必须是整数或十六进制字符串')
        raise ValueError('随机种子类型无效')

    @validator('hepai_way')
    def validate_hepai_way(cls, v):
        if v not in ("head_bump", "multi_ron", "three_ron_abort"):
            raise ValueError('hepai_way 必须在 head_bump / multi_ron / three_ron_abort 中')
        return v

    @validator('hepai_limit')
    def validate_hepai_limit(cls, v):
        if v < 1 or v > 64:
            raise ValueError('起和番数必须在 1-64 之间')
        return v

class MMCValidator(BaseModel):
    pass