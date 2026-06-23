from pydantic import BaseModel, field_serializer
from typing import Dict, Optional, List



# 0.4 定义发送数据格式BaseModel系列 能够创建符合json定义的格式

class PlayerInfo(BaseModel):
    user_id: int  # 用户ID
    username: str  # 用户名（用于显示）
    hand_tiles_count: int
    hand_tiles: Optional[List[int]] = None  # 手牌（可选，观战玩家可以看到所有手牌，普通玩家只能看到自己的）
    discard_tiles: List[int]
    discard_origin_tiles: Optional[List[int]] = None  # 理论弃牌
    combination_tiles: List[str]
    combination_mask: Optional[List[List[int]]] = None  # 组合牌掩码（二维数组，每个副露的掩码是一个子列表）
    huapai_list: List[int]
    remaining_time: int
    player_index: int
    original_player_index: int  # 原始玩家索引 东南西北 0 1 2 3
    score: int
    title_used: Optional[int] = None  # 使用的称号ID
    character_used: Optional[int] = None  # 使用的角色ID
    profile_used: Optional[int] = None  # 使用的头像ID
    voice_used: Optional[int] = None  # 使用的音色ID
    score_history: Optional[List[str]] = None  # 分数历史变化列表，每局记录 +？、-？ 或 0
    round_number_history: Optional[List[int]] = None  # 实际每手对应局数（支持连庄重复）
    tag_list: Optional[List[str]] = None  # 标签列表
    discard_riichi_flags: Optional[List[bool]] = None  # 立直规则：与 discard_tiles 同序的横置标记，重连/牌谱重建时还原横置弃牌
    # 四川麻将（血战到底）专用
    dingque_suit: Optional[int] = None  # 定缺花色：1=万 2=饼 3=条，0/None=未定缺
    is_hu: Optional[bool] = None  # 血战：本盘是否已和牌退场

class GameInfo(BaseModel):
    room_id: int
    gamestate_id: str  # 游戏状态ID（用于客户端发送游戏操作请求）
    tips: bool
    current_player_index: int
    action_tick: int
    max_round: int
    tile_count: int
    master_seed: Optional[int] = None  # 主种子
    commitment: Optional[int] = None   # 承诺值
    salt: Optional[str] = None  # 盐字符串
    current_round: int
    step_time: int
    round_time: int
    room_type: str
    room_rule: str
    sub_rule: Optional[str] = None  # 子规则（如 guobiao/standard、guobiao/xiaolin），用于番表显示
    hepai_limit: Optional[int] = None  # 起和番限制（国标有效，默认8）
    open_cuohe: Optional[bool] = False  # 是否开启错和（默认为False）
    show_moqie_hint: Optional[bool] = False  # 是否显示手摸切灰显（河牌摸切灰、手切正常，默认关）
    tactical_call: Optional[bool] = False  # 是否开启战术鸣牌（国标/青雀有效）
    claim_protection: Optional[bool] = True  # 鸣牌保护：无鸣牌权玩家延迟看到切牌/鸣牌过程（国标有效）
    isPlayerSetRandomSeed: Optional[bool] = False  # 是否玩家设置了随机种子（默认为False）
    player_entry_order: Optional[List[int]] = None  # shuffle 前对局入场顺序 user_id[4]
    players_info: List[PlayerInfo]
    self_hand_tiles: Optional[List[int]] = None
    # 立直麻将专用字段
    honba: Optional[int] = None  # 本场棒数
    riichi_sticks: Optional[int] = None  # 场供立直棒数
    dora_indicators: Optional[List[int]] = None  # 初始宝牌指示牌
    kan_dora_indicators: Optional[List[int]] = None  # 已翻开的杠宝牌指示牌
    hepai_way: Optional[str] = None  # 和牌方式：head_bump / multi_ron / three_ron_abort
    red_dora: Optional[bool] = None  # 是否启用赤宝牌
    dealer_index: Optional[int] = None  # 当前亲家索引（原始座位）
    view_player_index: Optional[int] = None  # 实时观战/特殊视角：客户端以此座位作为 self 视角
    # 四川麻将（血战到底）专用
    blood_battle: Optional[bool] = None  # 是否开启血战到底（关=一家和牌即结束本盘）

    # 在 Pydantic Model 中将 hex 字段序列化为十六进制字符串
    @field_serializer('master_seed', when_used='unless-none')
    def _ser_master_seed(self, v: int) -> str:
        return str(v)
    @field_serializer('commitment', when_used='unless-none')
    def _ser_commitment(self, v: int) -> str:
        return str(v)

class Ask_hand_action_info(BaseModel):
    remaining_time: int
    player_index: int
    remain_tiles: int
    action_list: List[str]
    action_tick: int
    # 立直麻将：可立直切牌候选 {tile_id: [waiting_tile, ...]}，仅当 action_list 含 riichi_cut 时下发
    riichi_candidate_cuts: Optional[Dict[int, List[int]]] = None
    # 立直麻将：吃后切牌阶段，本家被禁切的牌（食替规则：吃来源 + 两面搭子的筋）；客户端用于变暗与禁点
    forbidden_cut_tiles: Optional[List[int]] = None

class Ask_other_action_info(BaseModel):
    remaining_time: int
    action_list: List[str]
    cut_tile: int
    action_tick: int
    # 立直麻将赤宝牌场景下，针对每个吃方向可能存在多种真实牌组合，供客户端展示选择
    # 键为方向（"chi_left" / "chi_mid" / "chi_right"），值为候选组合列表，每个候选为两张真实牌 ID
    chi_candidates: Optional[Dict[str, List[List[int]]]] = None
    # 战术鸣牌（国标/青雀）：当前正处于战术鸣牌打断阶段，仅对当前申请的更高优先级行为再次询问
    is_tactical_recheck: Optional[bool] = None

class Do_action_info(BaseModel):
    # 存储操作列表 包含 切牌 吃 碰 杠 胡 补花 [chi_left,chi_mid,chi_right,peng,gang,angang,hu,buhua,cut,deal_tile] 
    # 暗杠会表现为 [angang,deal_tile] 补花会表现为 [buhua,deal_buhua_tile]（is_mo_buhua 标注摸补/手补）
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
    is_riichi_horizontal: Optional[bool] = None  # 立直规则：本张弃牌是否横置（含立直宣告 + 立直牌被吃后续横）
    # 战术鸣牌（国标/青雀）：is_claim 仅播放发声与字体动画，不应用任何状态变化
    is_claim: Optional[bool] = None
    # 战术鸣牌（国标/青雀）：silent 仅应用状态变化，不播放发声与字体动画
    silent: Optional[bool] = None
    # 暗杠/加杠：True=摸杠（末张参与），False=手杠
    is_mo_gang: Optional[bool] = None
    # 补花：True=摸补（末张花牌），False=手补
    is_mo_buhua: Optional[bool] = None
    # 四川麻将（血战到底）专用
    dingque_suit: Optional[int] = None  # 定缺广播：仅对应玩家收到自己的定缺花色（1万2饼3条）
    player_to_dingque: Optional[Dict[int, int]] = None  # 定缺完成广播：{player_index: suit}
    gang_score_changes: Optional[Dict[int, int]] = None  # 刮风下雨即时分变 {player_index: delta}
    gang_score_type: Optional[str] = None  # 刮风下雨类型：guafeng/xiayu1/xiayu2

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
    base_fu: Optional[int] = None  # 古典麻将：基础副数
    fu_fan_list: Optional[List[str]] = None  # 古典麻将：副番名列表
    # 立直麻将专用字段
    han: Optional[int] = None  # 番数
    fu: Optional[int] = None  # 符数
    aka_count: Optional[int] = None  # 赤宝牌数量
    dora_count: Optional[int] = None  # 宝牌数量（含杠宝）
    ura_dora_count: Optional[int] = None  # 里宝牌数量
    dora_indicators: Optional[List[int]] = None  # 宝牌指示牌（结算快照，含本局已翻开的杠宝牌）
    ura_dora_indicators: Optional[List[int]] = None  # 里宝牌指示牌
    honba: Optional[int] = None  # 本场数
    riichi_sticks_collected: Optional[int] = None  # 和牌者收走的立直棒数
    score_changes: Optional[Dict[int, int]] = None  # 各玩家点数变化 {original_player_index: delta}，全规则通用
    # 荒牌流局：各家听牌张（{player_index: [tile_id...]}，未听家给空列表或不出现），以及是否发生不听罚符点棒
    tenpai_tiles: Optional[Dict[int, List[int]]] = None
    # 荒牌流局：听牌家的实际手牌，用于客户端倒牌展示。
    tenpai_hands: Optional[Dict[int, List[int]]] = None
    exhaustive_penalty: Optional[bool] = None
    # 战术鸣牌（国标/青雀）：silent 标志和牌字体动画与音效已由战术鸣牌申请阶段播放，本次结算跳过 ShowActionDisplay/PlayActionSound
    silent: Optional[bool] = None
    # 国标局终亮杠：{player_index: [[2,tile,2,tile,2,tile,2,tile], ...]}，错和不传
    revealed_angang_masks: Optional[Dict[int, List[List[int]]]] = None
    # 浪涌麻将：和牌基础点乘倍数后的收分（不含本场/供托）；倍数已含浪潮 +1
    langyong_multiplier: Optional[int] = None
    langyong_scored_points: Optional[int] = None
    # 四川麻将（血战到底）专用
    player_to_dingque: Optional[Dict[int, int]] = None  # 定缺完成广播：{player_index: suit(1万/2饼/3条)}
    round_continues: Optional[bool] = None  # 血战：本盘是否继续（True=还有玩家未和且牌墙未空）
    win_player_index: Optional[int] = None  # 血战逐次和牌：本次和牌玩家索引
    is_zimo: Optional[bool] = None  # 本次和牌是否自摸
    hepai_tile: Optional[int] = None  # 和牌张（自摸时仅和牌者视角下发真实 id，他人为 0）
    multi_ron: Optional[bool] = None  # 一炮多响事件标志（客户端每家补花区摆 1 张变暗牌）
    is_qianggang: Optional[bool] = None  # 抢杠和：和牌张为加杠牌，客户端透明克隆且不提前收走加杠牌
    recycle_discard: Optional[bool] = None  # 点炮和牌动画结束后回收河牌（单家=true；多响仅最后一家=true）
    suppress_hand_reveal: Optional[bool] = None  # 血战：和牌时不展开手牌，仅补花区标记
    defer_score_settlement: Optional[bool] = None  # 血战：和牌时不展示分数面板，终局再结算
    ron_discarder_index: Optional[int] = None  # 点炮/一炮多响：点炮者 player_index
    liuju_hu_hands: Optional[Dict[int, List[int]]] = None  # 查牌：先展示已和玩家完整手牌 {player_index: hand}
    cha_payer_index: Optional[int] = None  # 查牌：没叫/花猪向已和玩家付分的付款方
    # 流局查大叫/查花猪：{player_index: delta}，以及听牌家最大番/花猪标记
    cha_dajiao_changes: Optional[Dict[int, int]] = None
    tenpai_max_fan: Optional[Dict[int, int]] = None  # {player_index: 理论最大番}
    hua_zhu_players: Optional[List[int]] = None  # 花猪玩家索引列表
    gang_refund_changes: Optional[Dict[int, int]] = None  # 流局退税（刮风下雨退回）{player_index: delta}
    tenpai_tiles_sichuan: Optional[Dict[int, List[int]]] = None  # 各家听牌张（流局亮牌用）
    liuju_status: Optional[Dict[int, str]] = None  # 流局未和家展示状态 {player_index: "ting"/"no_ting"/"hua_zhu_passive"/"hua_zhu_active"}
    liuju_hands: Optional[Dict[int, List[int]]] = None  # 流局未和家手牌（亮牌+状态面板用）
    liuju_step: Optional[str] = None  # 流局/终局演出：reveal_hu/settle_hu/chajiao/final（cha_refund 已并入 chajiao）
    liuju_status_final: Optional[bool] = None  # 流局逐家状态面板是否为最后一条（客户端在此条应用最终分数）
    liuju_refund: Optional[bool] = None  # 该查叫面板内含刮风下雨退税（客户端加“退税”标签并多停 0.5s）

class Show_shuhewei_info(BaseModel):
    player_fu: Dict[int, int]  # 各玩家副数 {player_index: fu}
    player_to_score: Dict[int, int]  # 结算后各玩家总分 {player_index: score}
    score_changes: Dict[int, int]  # 数和尾引起的分数变化 {player_index: delta}
    player_fan: Dict[int, List[str]]  # 各玩家番型列表（未和牌玩家通常为空）
    player_fu_types: Dict[int, List[str]]  # 各玩家副种列表
    hu_class: Optional[str] = None  # 本局和牌类型（无和牌时为 liuju 或空）
    hepai_player_index: Optional[int] = None  # 和牌玩家索引（无和牌时为空）
    hepai_player_hand: Optional[List[int]] = None  # 和牌玩家手牌，用于数和尾前倒牌
    hepai_player_combination_mask: Optional[List[List[int]]] = None  # 和牌玩家组合掩码

class Player_final_data(BaseModel):
    rank: int  # 排名（1-4）
    score: int  # 玩家分数
    pt: float  # 段位 PT 变动
    username: str  # 用户名
    original_player_index: Optional[int] = None  # 开局原始风位 0东1南2西3北，同分排序用
    rank_before: Optional[str] = None  # 对局前段位名
    score_before: Optional[float] = None  # 对局前段位分数
    rank_after: Optional[str] = None  # 对局后段位名
    score_after: Optional[float] = None  # 对局后段位分数

class Game_end_info(BaseModel):
    """游戏结束信息"""
    master_seed: int  # 主种子
    commitment: int  # 承诺值
    salt: str  # 盐字符串
    player_final_data: Dict[str, Player_final_data]  # 玩家最终数据，键为顺位 "1"～"4"

    # 在 Pydantic Model 中将 hex 字段序列化为十六进制字符串
    @field_serializer('master_seed', when_used='unless-none')
    def _ser_master_seed(self, v: int) -> str:
        return str(v)
    @field_serializer('commitment', when_used='unless-none')
    def _ser_commitment(self, v: int) -> str:
        return str(v)

class Switch_seat_info(BaseModel):
    """换位信息"""
    current_round: int  # 当前局数

class Refresh_player_tag_list_info(BaseModel):
    """刷新玩家标签列表信息"""
    player_to_tag_list: Dict[int, List[str]]  # 玩家索引到标签列表的映射 {player_index: tag_list}
    # 立直宣告广播复用此结构时填入：刚宣告立直的玩家索引（用于客户端音效/点棒动画定位）
    riichi_declared_player_index: Optional[int] = None

class Ready_status_info(BaseModel):
    """准备状态信息"""
    player_to_ready: Dict[int, bool]  # 玩家索引到准备状态的映射 {player_index: ready}

class Player_record_info(BaseModel):
    """玩家对局记录信息"""
    user_id: int  # 用户ID
    username: str  # 用户名
    score: int  # 玩家分数
    rank: int  # 排名（1-4）
    original_player_index: Optional[int] = None  # 开局原始风位 0东1南2西3北
    title_used: Optional[int] = None  # 使用的称号ID
    character_used: Optional[int] = None  # 使用的角色ID
    profile_used: Optional[int] = None  # 使用的头像ID
    voice_used: Optional[int] = None  # 使用的音色ID

class Record_info(BaseModel):
    """游戏记录元数据（按游戏分组，包含4个玩家，不含完整牌谱）"""
    game_id: str  # 对局ID（base62字符串）
    rule: str  # 规则类型（GB/JP）
    sub_rule: Optional[str] = None  # 子规则（如 guobiao/standard、guobiao/xiaolin、qingque/standard）
    match_type: Optional[str] = None  # 局数类型（如 1/4、2/4、4/4_rank），用于区分全庄战、半庄战、天梯等
    match_queue_type: Optional[str] = None  # 排位队列（如 beginner_quanzhuang），来自牌谱 game_title
    created_at: str  # 创建时间
    players: List[Player_record_info]  # 该游戏的4个玩家信息（按排名排序）

class Record_detail(BaseModel):
    """完整的游戏牌谱记录（按ID查询时返回）"""
    game_id: str  # 对局ID（base62字符串）
    rule: str  # 规则类型（GB/JP）
    sub_rule: Optional[str] = None  # 子规则（如 guobiao/standard、guobiao/xiaolin、qingque/standard）
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
    fulu_round_count: Optional[int] = None  # 副露局数（有明副露的局数）
    cuohe_count: Optional[int] = None  # 错和次数（国标）
    # 其他字段使用 Dict 存储，因为不同规则的番种字段不同
    fan_stats: Optional[Dict[str, int]] = None  # 番种统计数据（字段名 -> 次数）

class UserSettings(BaseModel):
    """用户设置信息（称号、头像、角色、音色）"""
    user_id: int  # 用户ID
    username: str  # 用户名
    title_id: Optional[int] = 1  # 称号ID（默认值为1）
    profile_image_id: Optional[int] = 1  # 使用的头像ID（默认值为1）
    character_id: Optional[int] = 1  # 选择的角色ID（默认值为1）
    voice_id: Optional[int] = 1  # 选择的音色ID（默认值为1）

class Rule_stats_response(BaseModel):
    """单个规则的统计数据响应"""
    rule: str  # 规则标识（guobiao/riichi）
    history_stats: List[Player_stats_info]  # 历史统计数据列表（按模式分组）
    total_fan_stats: Optional[Dict[str, int]] = None  # 汇总番种统计数据（所有模式的总和）

class Player_info_response(BaseModel):
    """玩家信息响应（包含所有统计数据）"""
    user_id: int  # 用户ID
    username: Optional[str] = None  # 用户名
    user_settings: Optional[UserSettings] = None  # 用户设置信息
    gb_stats: List[Player_stats_info]  # 国标麻将统计数据列表
    jp_stats: List[Player_stats_info]  # 立直麻将统计数据列表
    guobiao_rank: Optional[str] = None  # 国标段位
    guobiao_score: Optional[float] = None  # 国标分数

class UserConfig(BaseModel):
    """用户游戏配置信息（音量等）"""
    user_id: int  # 用户ID
    volume: int  # 音量设置（0-100）

class RankData(BaseModel):
    """段位数据（登录时同步）"""
    guobiao_rank: str = "10级"
    guobiao_score: float = 0
    is_sponsor: bool = False
    is_mcrpl_qualified: bool = False

class ServerStatsInfo(BaseModel):
    """服务器统计信息"""
    online_players: int  # 在线人数
    waiting_rooms: int  # 等待房间数（自定义房间）
    playing_rooms: int  # 进行房间数（自定义房间）
    match_playing_games: int = 0  # 进行中的排位匹配对局数

class SpectatorInfo(BaseModel):
    """观战信息"""
    rule: str  # 规则类型（guobiao/qingque）
    sub_rule: str  # 子规则类型（guobiao/standard 等）
    player1_name: str  # 玩家1 用户名
    player2_name: str  # 玩家2 用户名
    player3_name: str  # 玩家3 用户名
    player4_name: str  # 玩家4 用户名
    gamestate_id: str  # 游戏状态ID

class FriendInfo(BaseModel):
    """好友/关注信息"""
    user_id: int                                 # 被关注者的用户ID
    username: str                                # 用户名
    profile_image_id: int = 1                    # 头像ID
    state: str                                   # "offline" / "online" / "in_game"
    gamestate_id: Optional[str] = None           # 仅在 state == "in_game" 时有效

class FriendRequestInfo(BaseModel):
    """好友申请信息"""
    user_id: int
    username: str
    profile_image_id: int = 1

class RealtimeSpectatorEntry(BaseModel):
    """实时观战者条目（用于推送给被观战玩家显示列表）"""
    user_id: int
    username: str

class LeaderboardEntry(BaseModel):
    """国标段位排行榜条目"""
    rank_position: int
    user_id: int
    username: str
    profile_image_id: int = 1
    guobiao_rank: str
    guobiao_score: float

class LoginInfo(BaseModel):
    """登录信息"""
    user_id: int  # 用户ID
    username: str  # 用户名
    userkey: str  # 用户名对应的秘钥
    is_tourist: bool = False  # 是否为游客账户

class MessageInfo(BaseModel):
    """消息信息"""
    title: str  # 消息标题
    content: str  # 消息内容

class Sticker_info(BaseModel):
    """对局表情包广播"""
    player_index: int  # 当局座位索引（兼容旧客户端）
    original_player_index: int  # 开局固定风位，结算换座期间客户端按此定位面板
    sticker: str  # 格式 pack/id，如 turtle/3

class Response(BaseModel):
    type: str
    success: bool
    message: str
    show_tip: Optional[bool] = None  # room/get_room_list 时回显：True=客户端显示刷新成功tips
    # 消息体
    message_info: Optional[MessageInfo] = None # 用于返回消息信息
    room_list: Optional[list[dict]] = None # 用于执行get_room_list时返回房间列表数据
    room_info: Optional[dict] = None # 用于在join_room和房间信息更新时广播单个房间信息
    game_info: Optional[GameInfo] = None # 用于执行game_start_chinese时返回游戏信息
    ask_hand_action_info: Optional[Ask_hand_action_info] = None # 用于询问玩家手牌操作 出牌 自摸 补花 暗杠 加杠
    ask_other_action_info: Optional[Ask_other_action_info] = None # 用于询问切牌后其他家玩家操作 吃 碰 杠 胡
    do_action_info: Optional[Do_action_info] = None # 用于广播玩家操作
    show_result_info: Optional[Show_result_info] = None # 用于广播结算结果
    show_shuhewei_info: Optional[Show_shuhewei_info] = None # 用于广播数和尾结算
    game_end_info: Optional[Game_end_info] = None # 用于广播游戏结束信息
    switch_seat_info: Optional[Switch_seat_info] = None # 用于广播换位信息
    refresh_player_tag_list_info: Optional[Refresh_player_tag_list_info] = None # 用于广播刷新玩家标签列表
    ready_status_info: Optional[Ready_status_info] = None # 用于广播准备状态
    record_list: Optional[List[Record_info]] = None # 用于返回游戏记录列表（元数据）
    record_detail: Optional[Record_detail] = None # 用于返回单个完整牌谱记录
    player_info: Optional[Player_info_response] = None # 用于返回玩家信息
    rule_stats: Optional[Rule_stats_response] = None # 用于返回单个规则的统计数据
    login_info: Optional[LoginInfo] = None # 用于返回登录信息
    user_settings: Optional[UserSettings] = None # 用于返回用户设置信息
    user_config: Optional[UserConfig] = None # 用于返回用户游戏配置信息
    rank_data: Optional[RankData] = None # 用于返回段位数据
    server_stats: Optional[ServerStatsInfo] = None # 用于返回服务器统计信息
    client_ts: Optional[int] = None # pong 心跳：原样回传客户端发送 ping 时的时间戳（毫秒）
    spectator_list: Optional[List[SpectatorInfo]] = None # 用于返回观战列表
    # 好友 / 关注 / 实时观战 相关字段
    friend_list: Optional[List[FriendInfo]] = None
    friend_request_list: Optional[List[FriendRequestInfo]] = None
    realtime_request_id: Optional[str] = None
    realtime_from_user_id: Optional[int] = None
    realtime_from_username: Optional[str] = None
    realtime_to_user_id: Optional[int] = None
    realtime_to_username: Optional[str] = None
    realtime_gamestate_id: Optional[str] = None
    realtime_spectators: Optional[List[RealtimeSpectatorEntry]] = None
    friend_count: Optional[int] = None
    friend_max: Optional[int] = None
    leaderboard_list: Optional[List[LeaderboardEntry]] = None
    sticker_info: Optional[Sticker_info] = None