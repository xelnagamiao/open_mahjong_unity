// 5.Response 接收数据类型

public class RoomInfo
{
    public string room_id;
    public string room_type;
    public int max_player;
    public string[] player_list;
    public bool has_password;
    public bool tips;
    public string host_name;
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
    public RoomInfo[] room_list; // 返回房间列表
    public RoomInfo room_info; // 返回单个房间信息
    public GameInfo game_info; // gameinfo用于开始游戏 其中包含player_info
    public AskHandActionGBInfo ask_hand_action_info; // 国标游戏中询问摸牌后自己的操作
    public AskOtherActionGBInfo ask_other_action_info; // 国标游戏中询问切牌后其他家或许有的操作
    public DoActionInfo do_action_info; // 国标游戏中执行操作
    public ShowResultInfo show_result_info; // 国标游戏中显示结算结果
}

public class ShowResultInfo // 显示结算结果
{
    public int hepai_player_index; // 和牌玩家索引
    public int[] player_to_score; // 所有玩家分数
    public int hu_score; // 和牌分数
    public string hu_fan; // 和牌番种
    public string hu_class; // 和牌类别
    public int[] hepai_player_hand; // 和牌玩家手牌
    public int[] hepai_player_huapai; // 和牌玩家花牌列表
    public int[] hepai_player_combination_mask; // 和牌玩家组合掩码
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
    public bool? cut_class;         // 可空类型
    public int? deal_tile;          // 可空类型
    public int? buhua_tile;         // 可空类型
    public string combination_target; // 可空类型
    public int[] combination_mask;  // 数组可以为null
}

public class PlayerInfo // 房间信息中单个玩家信息
{
    public string username;             // 玩家名
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
    public int random_seed;             // 随机种子
    public int current_round;           // 当前轮数
    public int step_time;               // 步时
    public int round_time;              // 局时
    public PlayerInfo[] players_info;   // 玩家信息列表
    public int[] self_hand_tiles;       // 当前玩家手牌 (可选)
}


