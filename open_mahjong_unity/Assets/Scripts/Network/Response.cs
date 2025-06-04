using System;

// 5.Response 接收数据类型
[Serializable]
public class RoomData // 刷新房间列表数据类型 通过get_room_list更新 标识了 had_password：bool
{
    public string room_id;
    public string host_name;
    public string room_name;
    public int game_time;
    public string[] player_list;
    public int player_count;
    public bool has_password;
}

[Serializable]
public class RoomInfo // 房间信息数据类型 通过get_room_info更新
{
    public string[] player_list;
    public int player_count;
    public string host_name;
    public int game_time;
    public string room_id;
    public string room_name;
}

[Serializable]
public class BuhuaAnimationInfo
{
    public int player_index;
    public int deal_tiles;
    public int remain_tiles;
}

[Serializable]
public class Response 
{
    public string type;
    public bool success;
    public string message;
    public string username;
    public RoomData[] room_list;
    public RoomInfo room_info;
    public GameInfo game_info;
    public CutTileResponse cut_info;
    public AskHandActionInfo ask_hand_action_info;
    public AskActionInfo ask_action_info;
    public ActionInfo action_info;
    public BuhuaAnimationInfo buhua_animation_info;
}

[Serializable]
public class CutTileResponse
{
    public int cut_player_index;
    public bool cut_class;
    public int cut_tiles; 
}

[Serializable]
public class AskHandActionInfo
{
    public int player_index;
    public int deal_tiles;
    public int remaining_time;
    public int remain_tiles;
    public string[] action_list;
}

[Serializable]
public class AskActionInfo
{
    public int remaining_time;
    public string[] action_list;
    public int cut_tile;
}

[Serializable]
public class ActionInfo
{
    public int remaining_time;
    public string do_action_type;
    public int current_player_index;
    public int tile_id;
}

// 6.Request 发送数据类型 ##################################################################

[Serializable]
public class PlayerInfo
{
    public string username;             // 玩家名
    public int hand_tiles_count;        // 手牌数量
    public string[] discard_tiles;      // 弃牌
    public string[] combination_tiles;   // 组合牌
    public int remaining_time;          // 剩余时间
    public int current_player_index;    // 东南西北位置
    public int score;                   // 得分
}

[Serializable]
public class PlayerPosition  // 新增类来替代 Dictionary
{
    public string username;
    public int position;
}

[Serializable]
public class GameInfo
{
    public string[] player_list;
    public PlayerPosition[] player_positions;  // 改用数组
    public int current_player_index;
    public int tile_count;
    public int random_seed;
    public string game_status;
    public int current_round;
    public int cuttime;
    public int game_time;
    public PlayerInfo[] players_info;
    public int[] self_hand_tiles;
}


