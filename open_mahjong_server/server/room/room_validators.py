from pydantic import BaseModel, validator
from typing import List, Optional

class GBRoomValidator(BaseModel):
    room_name: str
    game_round: int
    round_timer: int
    step_timer: int
    random_seed: int
    open_cuohe: bool = False
    
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
        if v < 0 or v > 4294967295:
            raise ValueError('随机种子必须在0-4294967295之间')
        return v

class RiichiRoomValidator(BaseModel):
    pass

class MMCValidator(BaseModel):
    pass