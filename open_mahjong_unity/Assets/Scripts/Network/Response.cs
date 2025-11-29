// 5.Response 接收数据类型

using System.Collections.Generic;

public class RoomInfo
{
    public string room_id;
    public string room_type;
    public int max_player;
    public int[] player_list;
    public Dictionary <int,string> player_names;
    public bool has_password;
    public bool tips;
    public string host_user_name;
    public int host_user_id;
    public string room_name;
    public int game_round;
    public int round_timer;
    public int step_timer;
}

public class Response // 所有后端的返回数据都由Response类接收
{
    // 消息头
    public string type; // 消息类型
    public bool success; // 消息是否成功
    public string message; // 消息内容
    // 消息体
    public string username; // 登录用返回用户名
    public string userkey; // 登录用返回用户key
    public int user_id; // 登录用返回用户ID
    public RoomInfo[] room_list; // 返回房间列表
    public RoomInfo room_info; // 返回单个房间信息
    public GameInfo game_info; // gameinfo用于开始游戏 其中包含player_info
    public AskHandActionGBInfo ask_hand_action_info; // 国标游戏中询问切片补花暗杠自摸
    public AskOtherActionGBInfo ask_other_action_info; // 国标游戏中询问吃碰杠和
    public DoActionInfo do_action_info; // 国标游戏中执行操作
    public ShowResultInfo show_result_info; // 国标游戏中显示结算结果
    public GameEndInfo game_end_info; // 国标游戏中显示游戏结束结果
    public RecordInfo[] record_list; // 返回游戏记录列表
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
    public string character_used;      // 使用的角色（可为空）
}

public class RecordInfo // 游戏记录信息（按游戏分组，包含4个玩家）
{
    public int game_id;                 // 对局ID
    public string rule;                 // 规则类型（GB/JP）
    public Dictionary<string, object> record; // 完整的牌谱记录
    public string created_at;           // 创建时间
    public PlayerRecordInfo[] players;  // 该游戏的4个玩家信息（按排名排序）
}


