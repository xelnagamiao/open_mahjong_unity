from pydantic import BaseModel
from typing import Dict, Optional, List



# 0.4 定义发送数据格式BaseModel系列 能够创建符合json定义的格式

class PlayerInfo(BaseModel):
    username: str
    hand_tiles_count: int
    discard_tiles: List[int]
    combination_tiles: List[str]
    huapai_list: List[int]
    remaining_time: int
    player_index: int
    score: int

class GameInfo(BaseModel):
    room_id: int
    tips: bool
    current_player_index: int
    action_tick: int
    max_round: int
    tile_count: int
    random_seed: int
    current_round: int
    step_time: int
    round_time: int
    players_info: List[PlayerInfo]
    self_hand_tiles: Optional[List[int]] = None

class Ask_hand_action_info(BaseModel):
    remaining_time: int
    player_index: int
    deal_tiles: int
    remain_tiles: int
    action_list: List[str]
    action_tick: int

class Ask_other_action_info(BaseModel):
    remaining_time: int
    action_list: List[str]
    cut_tile: int
    action_tick: int

class Do_action_info(BaseModel):
    # 存储操作列表 包含 切牌 吃 碰 杠 胡 补花 [chi_left,chi_mid,chi_right,peng,gang,angang,hu,buhua,cut,deal_tile] 
    # 暗杠会表现为 [angang,deal_tile] 补花会表现为 [buhua,deal_tile]
    action_list: List[str] 
    action_player: int # 存储操作玩家索引
    cut_tile: int # 在切牌时广播切牌
    cut_class: bool # 在切牌时广播切牌手模切类型
    deal_tile: int # 在摸牌时广播摸牌
    buhua_tile: int # 在补花时广播补花
    combination_mask: List[int] # 在鸣牌时传递鸣牌形状
    action_tick: int # 用于同步操作时钟

class Response(BaseModel):
    type: str
    success: bool
    message: str
    # 消息体
    username: str # 用于在玩家登录时返回玩家信息
    userkey: str # 用于在玩家登录时返回用户名对应的秘钥
    room_list: Optional[list[dict]] = None # 用于执行get_room_list时返回房间列表数据
    room_info: Optional[dict] = None # 用于在join_room和房间信息更新时广播单个房间信息
    game_info: Optional[GameInfo] = None # 用于执行game_start_chinese时返回游戏信息
    ask_hand_action_info: Optional[Ask_hand_action_info] = None # 用于询问玩家手牌操作 出牌 自摸 补花 暗杠 加杠
    ask_other_action_info: Optional[Ask_other_action_info] = None # 用于询问切牌后其他家玩家操作 吃 碰 杠 胡
    do_action_info: Optional[Do_action_info] = None # 用于广播玩家操作