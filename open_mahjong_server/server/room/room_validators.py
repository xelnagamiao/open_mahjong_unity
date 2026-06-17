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
        try:
            return parse_user_master_seed(v)
        except ValueError as e:
            raise ValueError(str(e)) from e

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


class MMCValidator(BaseModel):
    pass