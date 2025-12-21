// 5.Response 接收数据类型

using System.Collections.Generic;

public class RoomInfo
{
    public string room_id;
    public string room_type;
    public int max_player;
    public int[] player_list;
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
}

public class GameEndInfo // 显示游戏结束结果
{
    public long game_random_seed; // 游戏随机种子
    public Dictionary<string, Dictionary<string, object>> player_final_data; // endgame数据 {user_id: {"rank": int, "score": int, "pt": int, "username": string}}
}

public class ShowResultInfo // 显示结算结果
{
    public int hepai_player_index; // 和牌玩家索引
    public Dictionary<int, int> player_to_score; // 所有玩家分数
    public int hu_score; // 和牌分数
    public string[] hu_fan; // 和牌番种
    public string hu_class; // 和牌类别
    public int[] hepai_player_hand; // 和牌玩家手牌
    public int[] hepai_player_huapai; // 和牌玩家花牌列表
    public int[][] hepai_player_combination_mask; // 和牌玩家组合掩码
    public int action_tick;
}

public class AskHandActionGBInfo // 询问手牌操作
{
    public string[] action_list; // 操作列表
    public int remaining_time; // 剩余时间
    public int player_index; // 玩家索引
    public int remain_tiles; // 剩余牌数 只有摸牌以后牌堆牌数会减少
    public int action_tick;
}

public class AskOtherActionGBInfo // 询问切牌后操作
{
    public string[] action_list; // 操作列表
    public int remaining_time; // 剩余时间
    public int cut_tile; // 切牌
    public int action_tick;
}

public class DoActionInfo // 执行操作
{
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
}

public class PlayerInfo // 房间信息中单个玩家信息
{
    public string username;             // 玩家名
    public int user_id;                  // 玩家uid
    public int hand_tiles_count;        // 手牌数量
    public int[] discard_tiles;         // 弃牌 (改为int数组)
    public string[] combination_tiles;  // 组合牌
    public int[] conbination_mask;      // 组合牌掩码
    public int remaining_time;          // 剩余时间
    public int player_index;            // 东南西北位置 (改为player_index)
    public int score;                   // 得分
    public int[] huapai_list;           // 花牌列表
    public int title_used;              // 使用的称号ID
    public int character_used;          // 使用的角色ID
    public int profile_used;            // 使用的头像ID
    public int voice_used;              // 使用的音色ID
}

public class GameInfo // 游戏开始时传递房间信息
{
    public int room_id;                 // 房间ID
    public bool tips;                   // 是否提示
    public int current_player_index;    // 当前玩家索引
    public int action_tick;             // 操作帧
    public int max_round;               // 最大局数
    public int tile_count;              // 牌山剩余牌数
    public long round_random_seed;       // 局内随机种子
    public int current_round;           // 当前轮数
    public int step_time;               // 步时
    public int round_time;              // 局时
    public PlayerInfo[] players_info;   // 玩家信息列表
    public int[] self_hand_tiles;       // 当前玩家手牌 (可选)
}

public class PlayerRecordInfo // 玩家对局记录信息
{
    public int user_id;                 // 用户ID
    public string username;            // 用户名
    public int score;                   // 玩家分数
    public int rank;                    // 排名（1-4）
    public int? title_used;             // 使用的称号ID（可为空）
    public int? character_used;        // 使用的角色ID（可为空）
    public int? profile_used;           // 使用的头像ID（可为空）
    public int? voice_used;            // 使用的音色ID（可为空）
}

public class RecordInfo // 游戏记录信息（按游戏分组，包含4个玩家）
{
    public int game_id;                 // 对局ID
    public string rule;                 // 规则类型（GB/JP）
    public Dictionary<string, object> record; // 完整的牌谱记录
    public string created_at;           // 创建时间
    public PlayerRecordInfo[] players;  // 该游戏的4个玩家信息（按排名排序）
}

public class PlayerStatsInfo // 玩家统计数据信息（单个规则和模式的统计）
{
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
    public Dictionary<string, int> fan_stats; // 番种统计数据（字段名 -> 次数）
}

public class UserSettings // 用户设置信息（称号、头像、角色、音色）
{
    public int user_id;                // 用户ID
    public string username;            // 用户名
    public int title_id;              // 称号ID
    public int profile_image_id;      // 使用的头像ID
    public int character_id;          // 选择的角色ID
    public int voice_id;              // 选择的音色ID
}

public class PlayerInfoResponse // 玩家信息响应（包含所有统计数据）
{
    public int user_id;                // 用户ID
    public UserSettings user_settings; // 用户设置信息
    public PlayerStatsInfo[] gb_stats; // 国标麻将统计数据列表
    public PlayerStatsInfo[] jp_stats; // 立直麻将统计数据列表
}

public class UserConfig // 用户游戏配置信息（音量等）
{
    public int user_id;                // 用户ID
    public int volume;                 // 音量设置（0-100）
}

public class LoginInfo // 登录信息
{
    public int user_id;                // 用户ID
    public string username;             // 用户名
    public string userkey;              // 用户名对应的秘钥
}

public class Response // 所有后端的返回数据都由Response类接收
{
    // 消息头
    public string type; // 消息类型
    public bool success; // 消息是否成功
    public string message; // 消息内容
    // 消息体
    public string username; // 登录用返回用户名（已废弃，使用 login_info）
    public string userkey; // 登录用返回用户key（已废弃，使用 login_info）
    public int user_id; // 登录用返回用户ID（已废弃，使用 login_info）
    public RoomInfo[] room_list; // 返回房间列表
    public RoomInfo room_info; // 返回单个房间信息
    public GameInfo game_info; // gameinfo用于开始游戏 其中包含player_info
    public AskHandActionGBInfo ask_hand_action_info; // 国标游戏中询问切片补花暗杠自摸
    public AskOtherActionGBInfo ask_other_action_info; // 国标游戏中询问吃碰杠和
    public DoActionInfo do_action_info; // 国标游戏中执行操作
    public ShowResultInfo show_result_info; // 国标游戏中显示结算结果
    public GameEndInfo game_end_info; // 国标游戏中显示游戏结束结果
    public RecordInfo[] record_list; // 返回游戏记录列表
    public PlayerInfoResponse player_info; // 返回玩家信息
    public LoginInfo login_info; // 返回登录信息
    public UserSettings user_settings; // 返回用户设置信息
    public UserConfig user_config; // 返回用户游戏配置信息
}

