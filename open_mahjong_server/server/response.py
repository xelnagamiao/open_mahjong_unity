from pydantic import BaseModel
from typing import Dict, Optional, List



# 0.4 定义发送数据格式BaseModel系列 能够创建符合json定义的格式

class PlayerInfo(BaseModel):
    user_id: int  # 用户ID
    username: str  # 用户名（用于显示）
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
    round_random_seed: Optional[int] = None
    current_round: int
    step_time: int
    round_time: int
    players_info: List[PlayerInfo]
    self_hand_tiles: Optional[List[int]] = None

class Ask_hand_action_info(BaseModel):
    remaining_time: int
    player_index: int
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
    cut_tile: Optional[int] = None # 在切牌时广播切牌
    cut_class: Optional[bool] = None # 在切牌时广播切牌手模切类型
    cut_tile_index: Optional[int] = None # 在切牌时广播切牌位置
    deal_tile: Optional[int] = None # 在摸牌时广播摸牌
    buhua_tile: Optional[int] = None # 在补花时广播补花
    combination_mask: Optional[List[int]] = None # 在鸣牌时传递鸣牌形状
    combination_target: Optional[str] = None # 在鸣牌时传递鸣牌目标
    action_tick: int # 用于同步操作时钟

class Show_result_info(BaseModel):
    hepai_player_index: Optional[int] = None  # 和牌玩家索引
    player_to_score: Optional[Dict[int, int]] = None  # 所有玩家分数
    hu_score: Optional[int] = None  # 和牌分数
    hu_fan: Optional[List[str]] = None  # 和牌番种
    hu_class: str  # 和牌类别
    hepai_player_hand: Optional[List[int]] = None  # 和牌玩家手牌
    hepai_player_huapai: Optional[List[int]] = None  # 和牌玩家花牌列表
    hepai_player_combination_mask: Optional[List[List[int]]] = None  # 和牌玩家组合掩码
    action_tick: int

class Game_end_info(BaseModel):
    """游戏结束信息"""
    game_random_seed: int  # 游戏随机种子（用于验证）
    player_final_data: Dict[int, Dict[str, int]]  # 玩家最终数据 {user_id: {"rank": int, "score": int, "pt": int}}

class Player_record_info(BaseModel):
    """玩家对局记录信息"""
    user_id: int  # 用户ID
    username: str  # 用户名
    score: int  # 玩家分数
    rank: int  # 排名（1-4）
    character_used: Optional[str] = None  # 使用的角色

class Record_info(BaseModel):
    """游戏记录信息（按游戏分组，包含4个玩家）"""
    game_id: int  # 对局ID
    rule: str  # 规则类型（GB/JP）
    record: Dict  # 完整的牌谱记录（JSONB）
    created_at: str  # 创建时间
    players: List[Player_record_info]  # 该游戏的4个玩家信息（按排名排序）

class Player_stats_info(BaseModel):
    """玩家统计数据信息（单个规则和模式的统计）"""
    rule: str  # 规则标识（GB/JP）
    mode: str  # 数据模式
    total_games: Optional[int] = None
    total_rounds: Optional[int] = None
    win_count: Optional[int] = None
    self_draw_count: Optional[int] = None
    deal_in_count: Optional[int] = None
    total_fan_score: Optional[int] = None
    total_win_turn: Optional[int] = None
    total_fangchong_score: Optional[int] = None
    first_place_count: Optional[int] = None
    second_place_count: Optional[int] = None
    third_place_count: Optional[int] = None
    fourth_place_count: Optional[int] = None
    # 其他字段使用 Dict 存储，因为不同规则的番种字段不同
    fan_stats: Optional[Dict[str, int]] = None  # 番种统计数据（字段名 -> 次数）

class Player_info_response(BaseModel):
    """玩家信息响应（包含所有统计数据）"""
    user_id: int  # 用户ID
    username: Optional[str] = None  # 用户名
    gb_stats: List[Player_stats_info]  # 国标麻将统计数据列表
    jp_stats: List[Player_stats_info]  # 立直麻将统计数据列表

class Response(BaseModel):
    type: str
    success: bool
    message: str
    # 消息体
    user_id: Optional[int] = None  # 用于在玩家登录时返回用户ID
    username: Optional[str] = None # 用于在玩家登录时返回玩家信息
    userkey: Optional[str] = None # 用于在玩家登录时返回用户名对应的秘钥
    room_list: Optional[list[dict]] = None # 用于执行get_room_list时返回房间列表数据
    room_info: Optional[dict] = None # 用于在join_room和房间信息更新时广播单个房间信息
    game_info: Optional[GameInfo] = None # 用于执行game_start_chinese时返回游戏信息
    ask_hand_action_info: Optional[Ask_hand_action_info] = None # 用于询问玩家手牌操作 出牌 自摸 补花 暗杠 加杠
    ask_other_action_info: Optional[Ask_other_action_info] = None # 用于询问切牌后其他家玩家操作 吃 碰 杠 胡
    do_action_info: Optional[Do_action_info] = None # 用于广播玩家操作
    show_result_info: Optional[Show_result_info] = None # 用于广播结算结果
    game_end_info: Optional[Game_end_info] = None # 用于广播游戏结束信息
    record_list: Optional[List[Record_info]] = None # 用于返回游戏记录列表
    player_info: Optional[Player_info_response] = None # 用于返回玩家信息