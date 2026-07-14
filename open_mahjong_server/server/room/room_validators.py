from pydantic import BaseModel, validator
from typing import List, Optional, Union

from ..gamestate.public.random_seed_manager import parse_user_master_seed

class GBRoomValidator(BaseModel):
    room_name: str
    game_round: int
    round_timer: int
    step_timer: int
    random_seed: Union[int, str] = 0
    open_cuohe: bool = False
    cuohe_type: int = 0  # 0=错和者-30/其余+10；1=错和者-40/其余+0
    show_moqie_hint: bool = False
    tactical_call: bool = False
    claim_protection: bool = True
    
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
        try:
            return parse_user_master_seed(v)
        except ValueError as e:
            raise ValueError(str(e)) from e

    @validator('cuohe_type')
    def validate_cuohe_type(cls, v):
        if v not in (0, 1):
            raise ValueError('错和形式必须在 0 或 1 之间')
        return v

class RiichiRoomValidator(BaseModel):
    room_name: str
    game_round: int
    round_timer: int
    step_timer: int
    random_seed: Union[int, str] = 0
    open_cuohe: bool = False
    show_moqie_hint: bool = False
    hepai_limit: int = 1  # 自定义起和番数，低于此番数视为错和（仅在 open_cuohe=True 时触发罚分）
    red_dora: bool = True
    allow_kuikae: bool = False  # 允许食替（吃什么打什么）；默认关即标准日麻禁切
    hepai_way: str = "multi_ron"  # head_bump / multi_ron / three_ron_abort
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
        try:
            return parse_user_master_seed(v)
        except ValueError as e:
            raise ValueError(str(e)) from e

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

class SichuanRoomValidator(BaseModel):
    room_name: str
    game_round: int
    round_timer: int
    step_timer: int
    random_seed: Union[int, str] = 0
    show_moqie_hint: bool = False
    tactical_call: bool = False
    claim_protection: bool = True
    blood_battle: bool = True  # 血战到底：开=和牌后续打至三家和或流局；关=一家和牌即结束本盘

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
        try:
            return parse_user_master_seed(v)
        except ValueError as e:
            raise ValueError(str(e)) from e


class ChangshaRoomValidator(BaseModel):
    room_name: str
    game_round: int
    round_timer: int
    step_timer: int
    random_seed: Union[int, str] = 0
    open_cuohe: bool = False
    show_moqie_hint: bool = False
    tactical_call: bool = False
    claim_protection: bool = True
    open_kong_replacement_count: int = 2
    initial_hu_si_xi: bool = True
    initial_hu_ban_ban_hu: bool = True
    initial_hu_que_yi_se: bool = True
    initial_hu_liu_liu_shun: bool = True
    initial_hu_san_tong: bool = True
    bird_count: int = 2
    dealer_bird: bool = True

    @validator('room_name')
    def validate_room_name(cls, v):
        if not v.strip():
            raise ValueError('room_name must not be empty')
        return v.strip()

    @validator('game_round')
    def validate_game_round(cls, v):
        if v not in (1, 2, 4):
            raise ValueError('changsha game_round must be 1, 2, or 4 for 4/8/16 hands')
        return v

    @validator('round_timer')
    def validate_timers(cls, v):
        if v < 0 or v > 1000:
            raise ValueError('round_timer must be between 0 and 1000')
        return v

    @validator('step_timer')
    def validate_step_timer(cls, v):
        if v < 0 or v > 100:
            raise ValueError('step_timer must be between 0 and 100')
        return v

    @validator('random_seed')
    def validate_random_seed(cls, v):
        try:
            return parse_user_master_seed(v)
        except ValueError as e:
            raise ValueError(str(e)) from e

    @validator('open_kong_replacement_count')
    def validate_open_kong_replacement_count(cls, v):
        if v < 1 or v > 4:
            raise ValueError('open_kong_replacement_count must be between 1 and 4')
        return v

    @validator('bird_count')
    def validate_bird_count(cls, v):
        if v not in (0, 1, 2, 4):
            raise ValueError('bird_count must be 0, 1, 2, or 4')
        return v


class JiandanRoomValidator(BaseModel):
    """Validate only options used by the first-win Jiandan room."""

    room_name: str
    game_round: int
    round_timer: int
    step_timer: int
    random_seed: Union[int, str] = 0
    tactical_call: bool = False
    claim_protection: bool = True

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
    def validate_round_timer(cls, v):
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
        try:
            return parse_user_master_seed(v)
        except ValueError as e:
            raise ValueError(str(e)) from e


class MMCValidator(BaseModel):
    pass
