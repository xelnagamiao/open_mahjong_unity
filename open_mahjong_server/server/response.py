from pydantic import BaseModel
from typing import Dict, Optional, List



# 0.4 定义发送数据格式BaseModel系列 能够创建符合json定义的格式
class RoomListData(BaseModel):
    room_id: str
    host_name: str
    room_name: str
    game_time: int
    player_list: list[str]
    player_count: int
    cuttime: int
    has_password: bool

class RoomResponse(BaseModel):
    player_list: list[str]
    player_count: int
    host_name: str
    game_time: int
    room_id: str
    room_name: str
    cuttime: int

class PlayerPosition(BaseModel):
    username: str
    position: int

class GameInfo(BaseModel):
    player_list: list[str]
    player_positions: list[PlayerPosition]  # 改用列表
    current_player_index: int
    tile_count: int
    random_seed: int
    game_status: str
    corrent_round: int
    players_info: list[dict]
    cuttime: int
    game_time: int
    self_hand_tiles: Optional[list[int]] = None

class LoginResponse(BaseModel):
    # 消息头
    type: str
    success: bool
    message: str
    username: str

class Cut_response(BaseModel):
    cut_player_index: int
    cut_class: bool
    cut_tiles: int

class Deal_tile_info(BaseModel):
    remaining_time: int
    deal_player_index: int
    deal_tiles: int
    remain_tiles: int

class Ask_action_info(BaseModel):
    remaining_time: int
    action_list: list[str]
    cut_tile: int

class Action_info(BaseModel):
    remaining_time: int
    do_action_type: str
    current_player_index: int


class Response(BaseModel):
    type: str
    success: bool
    message: str
    # 消息体
    room_list: Optional[list[RoomListData]] = None # 用于执行get_room_list时返回房间列表数据
    room_info: Optional[RoomResponse] = None # 用于在join_room和房间信息更新时广播房间信息
    game_info: Optional[GameInfo] = None # 用于执行game_start_chinese时返回游戏信息
    cut_info: Optional[Cut_response] = None # 用于执行cut_tiles时返回切牌信息
    deal_tile_info: Optional[Deal_tile_info] = None # 用于执行deal_tile时返回发牌信息
    ask_action_info: Optional[Ask_action_info] = None # 用于询问玩家操作
    action_info: Optional[Action_info] = None # 用于执行玩家操作