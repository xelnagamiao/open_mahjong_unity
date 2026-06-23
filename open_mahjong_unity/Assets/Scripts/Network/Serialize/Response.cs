// 5.Response 接收数据类型

using System.Collections.Generic;

public class RoomInfo {
    public string room_id;
    public string room_type;
    public string room_rule;
    public string sub_rule;       // 子规则，如 guobiao/standard、guobiao/xiaolin、qingque/standard
    public int hepai_limit;       // 起和番数（国标有效）
    public bool tourist_limit;    // 是否限制游客加入
    public bool allow_spectator;  // 是否允许观战
    public int max_player;
    public int[] player_list;
    public int[] ready_list; // 已准备玩家的 user_id 列表（房主不计入，机器人默认已准备不入列）
    // key: user_id (string in JSON) -> setting
    public Dictionary<string, UserSettings> player_settings;
    public bool has_password;
    public bool tips;
    public string host_name;
    public int host_user_id;
    public string room_name;
    public int game_round;
    public int round_timer;
    public int step_timer;
    public bool is_game_running; // 游戏是否正在运行
    public bool is_player_set_random_seed; // 复式：是否玩家指定主种子
    public bool open_cuohe; // 是否开启错和
    public bool show_moqie_hint; // 手摸切灰显（河牌摸切灰、手切正常）
    public bool tactical_call; // 战术鸣牌（国标/青雀）
    public bool claim_protection; // 鸣牌保护（国标/青雀）
    public bool? red_dora;  // 立直麻将专属：是否启用赤宝牌
    public bool? allow_kuikae; // 立直麻将专属：是否允许食替
    public bool? open_xiru; // 立直麻将专属：是否西入
    public bool? open_tobi; // 立直麻将专属：是否击飞
    public string hepai_way; // 立直麻将专属：和牌方式 head_bump / multi_ron / three_ron_abort
    public bool? blood_battle; // 四川麻将专属：是否开启血战到底
}

public class GameEndInfo { // 显示游戏结束结果
    public string master_seed; // 主种子（256 位，JSON 为十进制或 hex 字符串）
    public string commitment; // 承诺值
    public string salt; // 盐字符串
    public Dictionary<string, Dictionary<string, object>> player_final_data; // endgame数据，键为玩家标识（国标为座位索引，可能并列名次），value 含 username、rank、score、pt 等；展示请以 value["rank"] 排序
}

public class ShowResultInfo { // 显示结算结果
    public int hepai_player_index; // 和牌玩家索引
    public Dictionary<int, int> player_to_score; // 所有玩家分数
    public int hu_score; // 和牌分数
    public string[] hu_fan; // 和牌番种
    public string hu_class; // 和牌类别
    public int[] hepai_player_hand; // 和牌玩家手牌
    public int[] hepai_player_huapai; // 和牌玩家花牌列表
    public int[][] hepai_player_combination_mask; // 和牌玩家组合掩码
    public int action_tick;
    public int? base_fu; // 古典麻将：基础副数
    public string[] fu_fan_list; // 古典麻将：副番名列表

    // 立直麻将扩展字段
    public int? han;                          // 总番数
    public int? fu;                           // 符数
    public int? aka_count;                    // 赤宝牌数量
    public int? dora_count;                   // 表宝牌数量
    public int? ura_dora_count;               // 里宝牌数量
    public int[] dora_indicators;            // 宝牌指示牌（结算快照，含本局已翻开的杠宝牌）
    public int[] ura_dora_indicators;        // 里宝牌指示牌
    public int? honba;                        // 本场数
    public int? riichi_sticks_collected;      // 和牌者收走的立直棒数
    public Dictionary<int, int> score_changes; // 点数变化 {original_player_index: delta}，全规则通用
    // 荒牌流局：各家听牌张 {player_index: [tile_id, ...]}，未听家不出现；以及是否发生不听罚符
    public Dictionary<int, int[]> tenpai_tiles;
    public Dictionary<int, int[]> tenpai_hands; // 荒牌流局：听牌家的实际手牌，用于倒牌展示
    public bool? exhaustive_penalty;
    // 战术鸣牌：和牌字体动画/音效已在申请阶段播放，结算时跳过
    public bool? silent;
    // 国标局终亮杠：{player_index: [[2,tile,...], ...]}
    public Dictionary<int, int[][]> revealed_angang_masks;
    // 浪涌麻将：和牌基础点乘倍数后的收分（不含本场/供托）；倍数已含浪潮 +1
    public int? langyong_multiplier;
    public int? langyong_scored_points;
    // 四川麻将：定缺完成广播携带的各家定缺花色 {player_index: suit(1万/2饼/3条)}
    public Dictionary<int, int> player_to_dingque;
    // 四川麻将·血战到底：本盘是否继续（多家和牌逐次结算）、本次和牌玩家、是否自摸
    public bool? round_continues;
    public int? win_player_index;
    public bool? is_zimo;
    public int? hepai_tile;                       // 和牌张（自摸时仅和牌者可见真实 id）
    public bool? multi_ron;                       // 一炮多响事件标志（客户端每家补花区摆 1 张变暗牌）
    public bool? is_qianggang;                    // 抢杠和：透明克隆加杠牌，不提前收走
    public bool? recycle_discard;                 // 和牌动画结束后回收河牌（多响仅最后一家为 true）
    public bool? suppress_hand_reveal;            // 血战：和牌时不展开手牌
    public bool? defer_score_settlement;          // 血战：和牌时不展示分数，终局再结算
    public int? ron_discarder_index;              // 点炮/一炮多响：点炮者 seat
    public Dictionary<int, int[]> liuju_hu_hands; // 查牌：已和玩家完整手牌
    public int? cha_payer_index;                  // 查牌：没叫/花猪向已和玩家付分的付款方
    // 四川麻将·流局结算：查大叫/查花猪/退税分变与听牌信息
    public Dictionary<int, int> cha_dajiao_changes;
    public Dictionary<int, int> tenpai_max_fan;
    public int[] hua_zhu_players;
    public Dictionary<int, int> gang_refund_changes;
    public Dictionary<int, int[]> tenpai_tiles_sichuan;
    public Dictionary<int, string> liuju_status; // 流局未和家展示状态 {player_index: "ting"/"no_ting"/"hua_zhu_passive"/"hua_zhu_active"/"hua_zhu"(旧)}
    public Dictionary<int, int[]> liuju_hands;    // 流局未和家手牌（亮牌+状态面板用）
    public string liuju_step;                     // reveal_hu/settle_hu/chajiao/final（cha_refund 已并入 chajiao）
    public bool liuju_status_final;               // 流局逐家状态是否为最后一条
    public bool liuju_refund;                     // 该查叫面板内含刮风下雨退税（加“退税”标签 +0.5s）
}

public class ShowShuheWeiInfo { // 数和尾结算信息
    public Dictionary<int, int> player_fu; // 各玩家副数 {player_index: fu}
    public Dictionary<int, int> player_to_score; // 结算后各玩家总分
    public Dictionary<int, int> score_changes; // 数和尾分数变化
    public Dictionary<int, string[]> player_fan; // 各玩家番型列表（未和牌玩家通常为空）
    public Dictionary<int, string[]> player_fu_types; // 各玩家副种列表
    public string hu_class; // 本局和牌类型
    public int? hepai_player_index; // 和牌玩家索引（无和牌为空）
    public int[] hepai_player_hand; // 和牌玩家手牌
    public int[][] hepai_player_combination_mask; // 和牌玩家组合掩码
}

public class AskHandActionGBInfo { // 询问手牌操作
    public string[] action_list; // 操作列表
    public int remaining_time; // 剩余时间
    public int player_index; // 玩家索引
    public int remain_tiles; // 剩余牌数 只有摸牌以后牌堆牌数会减少
    public int action_tick;
    // 立直麻将：可立直切牌候选 {tile_id: [waiting_tile_id, ...]}，仅 action_list 含 riichi_cut 时下发
    public Dictionary<int, int[]> riichi_candidate_cuts;
    // 立直麻将：吃后切牌阶段的禁切牌列表（食替规则），客户端用于变暗与禁点
    public int[] forbidden_cut_tiles;
}

public class AskOtherActionGBInfo { // 询问切牌后操作
    public string[] action_list; // 操作列表
    public int remaining_time; // 剩余时间
    public int cut_tile; // 切牌
    public int action_tick;
    // 立直麻将赤宝牌吃牌候选：键为方向 "chi_left"/"chi_mid"/"chi_right"，值为每条候选的两张真实牌 ID（含 105/205/305）
    public System.Collections.Generic.Dictionary<string, int[][]> chi_candidates;
    // 战术鸣牌：True 时表示这是申请阶段对更高优先级行为的再次询问，客户端使用战术倒计时
    public bool? is_tactical_recheck;
}

public class DoActionInfo { // 执行操作
    public string[] action_list;
    public int action_player;
    public int action_tick;
    public int? cut_tile;           // 可空类型
    public int? cut_tile_index;     // 可空类型
    public bool? cut_class;         // 可空类型
    public int? deal_tile;          // 可空类型
    public int? buhua_tile;         // 可空类型
    public string combination_target; // 可空类型
    public int[] combination_mask;  // 数组可以为null
    public bool? is_riichi_horizontal; // 立直规则：本张弃牌是否横置（含立直宣告 + 立直牌被吃后续横）
    // 战术鸣牌：True 时表示这是申请阶段广播，仅播放发声与字体动画，不应用状态变化
    public bool? is_claim;
    // 战术鸣牌：True 时表示这是申请阶段后的静默实际行为，仅应用状态变化，不播放发声与字体动画
    public bool? silent;
    // 暗杠/加杠：True=摸杠（末张参与），False=手杠
    public bool? is_mo_gang;
    // 补花：True=摸补（末张花牌），False=手补
    public bool? is_mo_buhua;
    // 四川刮风下雨：即时分变 {player_index: delta}
    public Dictionary<int, int> gang_score_changes;
    public string gang_score_type; // guafeng / xiayu1 / xiayu2
}

public class PlayerInfo { // 房间信息中单个玩家信息
    public string username;             // 玩家名
    public int user_id;                  // 玩家uid
    public int hand_tiles_count;        // 手牌数量
    public int[] hand_tiles;            // 手牌
    public int[] discard_tiles;         // 弃牌 (改为int数组)
    public int[] discard_origin_tiles;        // 理论弃牌
    public string[] combination_tiles;  // 组合牌
    public int[][] combination_mask;   // 组合牌掩码（二维数组，每个副露的掩码是一个子数组）
    
    public int remaining_time;          // 剩余时间
    public int player_index;            // 东南西北位置 (改为player_index)
    public int original_player_index;   // 原始玩家索引 东南西北 0 1 2 3
    public int score;                   // 得分
    public int[] huapai_list;           // 花牌列表
    public int title_used;              // 使用的称号ID
    public int character_used;          // 使用的角色ID
    public int profile_used;            // 使用的头像ID
    public int voice_used;              // 使用的音色ID
    public string[] score_history;      // 分数历史变化列表，每局记录 +？、-？ 或 0
    public int[] round_number_history;  // 实际每手对应局数（支持连庄重复）
    public string[] tag_list;           // 标签列表
    public bool[] discard_riichi_flags; // 立直规则：与 discard_tiles 同序的横置标记，重连/牌谱重建时还原横置弃牌
    public int dingque_suit;            // 四川麻将：定缺花色（1万/2饼/3条，0=未定缺），重连/初始同步
    public bool? is_hu;                 // 四川麻将·血战到底：该玩家本盘是否已和牌退场
}

public class GameInfo { // 游戏开始时传递房间信息
    public int room_id;                 // 房间ID
    public string gamestate_id;         // 游戏状态ID（用于发送游戏操作请求）
    public bool tips;                   // 是否提示
    public int current_player_index;    // 当前玩家索引
    public int action_tick;             // 操作帧
    public int max_round;               // 最大局数
    public int tile_count;              // 牌山剩余牌数
    public string commitment;           // 承诺值（对局内广播，256 位）
    public string salt;                   // 盐字符串
    public int current_round;           // 当前轮数
    public int step_time;               // 步时
    public int round_time;              // 局时
    public string room_type;            // 房间类型（custom/match等）
    public string room_rule;            // 房间规则（guobiao/qingque等）
    public string sub_rule;             // 子规则（如 guobiao/standard、guobiao/xiaolin），用于番表显示
    public int hepai_limit;             // 起和番限制（国标有效，默认8）
    public bool open_cuohe;             // 是否开启错和
    public bool show_moqie_hint;        // 手摸切灰显
    public bool tactical_call;          // 战术鸣牌（国标/青雀）
    public bool claim_protection;       // 鸣牌保护（国标/青雀）
    public bool isPlayerSetRandomSeed;  // 是否设置随机种子
    public int[] player_entry_order;    // shuffle 前对局入场顺序 user_id[4]
    public PlayerInfo[] players_info;   // 玩家信息列表
    public int[] self_hand_tiles;       // 当前玩家手牌 (可选)

    // 立直麻将扩展字段
    public int? honba;                  // 本场棒数
    public int? riichi_sticks;          // 场供立直棒数
    public int[] dora_indicators;      // 初始宝牌指示牌
    public int[] kan_dora_indicators;  // 杠宝牌指示牌
    public string hepai_way;            // 和牌方式 head_bump / multi_ron / three_ron_abort
    public bool? red_dora;              // 是否启用赤宝牌
    public int? dealer_index;           // 当前亲家索引
    public int? view_player_index;      // 实时观战视角座位（客户端作为 self 渲染）
    public bool? blood_battle;          // 四川麻将：是否开启血战到底
}

public class SwitchSeatInfo { // 换位信息
    public int current_round;           // 当前局数
}

public class RefreshPlayerTagListInfo { // 刷新玩家标签列表信息
    public Dictionary<int, string[]> player_to_tag_list; // 玩家索引到标签列表的映射 {player_index: tag_list}
    // 立直宣告广播复用此结构时填入：刚宣告立直的玩家索引（用于音效/点棒动画定位）
    public int? riichi_declared_player_index;
}

public class ReadyStatusInfo { // 准备状态信息
    public Dictionary<int, bool> player_to_ready; // 玩家索引到准备状态的映射 {player_index: ready}
}
public class PlayerRecordInfo { // 玩家对局记录信息
    public int user_id;                 // 用户ID
    public string username;            // 用户名
    public int score;                   // 玩家分数
    public int rank;                    // 排名（1-4）
    public int? original_player_index;  // 开局原始风位 0东1南2西3北
    public int? title_used;             // 使用的称号ID（可为空）
    public int? character_used;        // 使用的角色ID（可为空）
    public int? profile_used;           // 使用的头像ID（可为空）
    public int? voice_used;            // 使用的音色ID（可为空）
}

public class RecordInfo { // 游戏记录元数据（按游戏分组，包含4个玩家，不含完整牌谱）
    public string game_id;              // 对局ID（base62字符串）
    public string rule;                 // 规则类型（GB/JP）
    public string sub_rule;             // 子规则（如 guobiao/standard、guobiao/xiaolin、qingque/standard）
    public string match_type;           // 局数类型（如 1/4、2/4、4/4_rank），用于区分全庄战、半庄战、天梯等
    public string match_queue_type;     // 排位队列（如 beginner_quanzhuang），可选
    public string created_at;           // 创建时间
    public PlayerRecordInfo[] players;  // 该游戏的4个玩家信息（按排名排序）
}

public class RecordDetail { // 完整的游戏牌谱记录（按ID查询时返回）
    public string game_id;              // 对局ID（base62字符串）
    public string rule;                 // 规则类型（GB/JP）
    public string sub_rule;             // 子规则（如 guobiao/standard、guobiao/xiaolin、qingque/standard）
    public Dictionary<string, object> record; // 完整的牌谱记录
    public string created_at;           // 创建时间
    public PlayerRecordInfo[] players;  // 该游戏的4个玩家信息（按排名排序）
}

public class PlayerStatsInfo { // 玩家统计数据信息（单个规则和模式的统计）
    public string rule;                 // 规则标识（GB/JP）
    public string mode;                // 数据模式
    public int? total_games;           // 总对局数
    public int? total_rounds;          // 累计回合数
    public int? win_count;             // 和牌次数
    public int? self_draw_count;       // 自摸次数
    public int? deal_in_count;         // 放铳次数
    public int? total_fan_score;       // 累计番数
    public int? total_win_turn;       // 累计和巡
    public int? total_fangchong_score; // 累计放铳分
    public int? first_place_count;    // 一位次数
    public int? second_place_count;    // 二位次数
    public int? third_place_count;    // 三位次数
    public int? fourth_place_count;    // 四位次数
    public int? fulu_round_count;      // 副露局数
    public int? cuohe_count;           // 错和次数（国标）
    public Dictionary<string, int> fan_stats; // 番种统计数据（字段名 -> 次数）
}

public class UserSettings { // 用户设置信息（称号、头像、角色、音色）
    public int user_id;                // 用户ID
    public string username;            // 用户名
    public int title_id;              // 称号ID
    public int profile_image_id;      // 使用的头像ID
    public int character_id;          // 选择的角色ID
    public int voice_id;              // 选择的音色ID
}

public class RuleStatsResponse { // 单个规则的统计数据响应
    public string rule;                    // 规则标识（guobiao/riichi）
    public PlayerStatsInfo[] history_stats; // 历史统计数据列表（按模式分组）
    public Dictionary<string, int> total_fan_stats; // 汇总番种统计数据（所有模式的总和）
}

public class PlayerInfoResponse { // 玩家信息响应（包含所有统计数据）
    public int user_id;                // 用户ID
    public UserSettings user_settings; // 用户设置信息
    public PlayerStatsInfo[] gb_stats; // 国标麻将统计数据列表
    public PlayerStatsInfo[] jp_stats; // 立直麻将统计数据列表
    public string guobiao_rank;        // 国标段位
    public float guobiao_score;        // 国标分数
}

public class UserConfig { // 用户游戏配置信息（音量等）
    public int user_id;                // 用户ID
    public int volume;                 // 音量设置（0-100）
}

public class RankData { // 段位数据（登录时同步）
    public string guobiao_rank;
    public float guobiao_score;
    public bool is_sponsor;
    public bool is_mcrpl_qualified;
}

public class QueueStatusEntry { // 单个队列的状态
    public int waiting;
    public int playing;
}

public class LoginInfo { // 登录信息
    public int user_id;                // 用户ID
    public string username;             // 用户名
    public string userkey;              // 用户名对应的秘钥
    public bool is_tourist;             // 是否为游客账户
}

public class MessageInfo { // 消息信息
    public string title;    // 消息标题
    public string content;  // 消息内容
}

public class ServerStatsInfo { // 服务器统计信息
    public int online_players;  // 在线人数
    public int waiting_rooms;   // 等待房间数（自定义房间）
    public int playing_rooms;   // 进行房间数（自定义房间）
    public int match_playing_games;  // 进行中的排位匹配对局数
}

public class SpectatorInfo { // 观战信息
    public string rule;         // 规则类型（guobiao/qingque）
    public string sub_rule;     // 子规则类型（guobiao/standard等）
    public string player1_name; // 玩家1 用户名
    public string player2_name; // 玩家2 用户名
    public string player3_name; // 玩家3 用户名
    public string player4_name; // 玩家4 用户名
    public string gamestate_id; // 游戏状态ID
}

public class FriendInfo { // 好友 / 关注信息
    public int user_id;           // 被关注者的 user_id
    public string username;        // 用户名
    public int profile_image_id;   // 头像 ID
    public string state;           // "offline" / "online" / "in_game"
    public string gamestate_id;    // 仅当 state == "in_game" 时有值
}

public class FriendRequestInfo { // 好友申请信息
    public int user_id;
    public string username;
    public int profile_image_id;
}

public class RealtimeSpectatorEntry { // 实时观战者条目
    public int user_id;
    public string username;
}

public class LeaderboardEntry { // 国标段位排行榜条目
    public int rank_position;
    public int user_id;
    public string username;
    public int profile_image_id;
    public string guobiao_rank;
    public float guobiao_score;
}

public class StickerInfo {
    public int player_index; // 当局座位索引（兼容旧逻辑）
    public int original_player_index; // 开局固定风位，结算换座期间按此定位面板
    public string sticker;
}

public class Response { // 所有后端的返回数据都由Response类接收
    // 消息头
    public string type; // 消息类型
    public bool success; // 消息是否成功
    public string message; // 消息内容
    public bool show_tip; // room/get_room_list 时回显：True=显示刷新成功tips
    // 消息体
    public MessageInfo message_info; // 用于返回消息信息
    public RoomInfo[] room_list; // 返回房间列表
    public RoomInfo room_info; // 返回单个房间信息
    public GameInfo game_info; // gameinfo用于开始游戏 其中包含player_info
    public AskHandActionGBInfo ask_hand_action_info; // 国标游戏中询问切片补花暗杠自摸
    public AskOtherActionGBInfo ask_other_action_info; // 国标游戏中询问吃碰杠和
    public DoActionInfo do_action_info; // 国标游戏中执行操作
    public ShowResultInfo show_result_info; // 国标游戏中显示结算结果
    public ShowShuheWeiInfo show_shuhewei_info; // 古典麻将数和尾结算
    public GameEndInfo game_end_info; // 国标游戏中显示游戏结束结果
    public SwitchSeatInfo switch_seat_info; // 国标游戏中换位信息
    public RefreshPlayerTagListInfo refresh_player_tag_list_info; // 刷新玩家标签列表信息
    public ReadyStatusInfo ready_status_info; // 准备状态信息
    public RecordInfo[] record_list; // 返回游戏记录列表（元数据）
    public RecordDetail record_detail; // 返回单个完整牌谱记录
    public PlayerInfoResponse player_info; // 返回玩家信息
    public RuleStatsResponse rule_stats; // 返回单个规则的统计数据
    public LoginInfo login_info; // 返回登录信息
    public UserSettings user_settings; // 返回用户设置信息
    public UserConfig user_config; // 返回用户游戏配置信息
    public RankData rank_data; // 返回段位数据
    public ServerStatsInfo server_stats; // 返回服务器统计信息
    public SpectatorInfo[] spectator_list; // 返回观战列表
    public Dictionary<string, QueueStatusEntry> queue_status; // 匹配队列状态
    public long client_ts; // pong 消息回传：客户端发送 ping 的时间戳（毫秒）
    // 好友 / 关注 / 实时观战
    public FriendInfo[] friend_list;             // 关注列表
    public FriendRequestInfo[] friend_request_list; // 好友申请列表
    public int? friend_count;                    // 当前关注人数
    public int? friend_max;                      // 关注上限
    public string realtime_request_id;           // 实时观战请求 ID
    public int? realtime_from_user_id;           // 发起者 user_id
    public string realtime_from_username;        // 发起者 username
    public int? realtime_to_user_id;             // 接收者 user_id
    public string realtime_to_username;          // 接收者 username
    public string realtime_gamestate_id;         // 关联对局 gamestate_id
    public RealtimeSpectatorEntry[] realtime_spectators; // 当前实时观战者列表
    public int[] dora_indicators; // update_dora 顶层字段：初始宝牌指示牌
    public int[] kan_dora_indicators; // update_dora 顶层字段：杠宝牌指示牌
    public LeaderboardEntry[] leaderboard_list; // 国标段位排行榜
    public StickerInfo sticker_info; // 对局表情包广播
}

