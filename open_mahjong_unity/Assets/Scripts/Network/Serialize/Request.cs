using System;

public class LoginRequest { // 登录请求
    public string type;
    public string username;
    public string password;
    public bool is_tourist; // 是否为游客登录
}

public class CreateGBRoomRequest { // 创建国标房间请求
    public string type;
    public string rule;
    public string sub_rule;
    public string roomname; // 服务器期望的字段名
    public int gameround; // 服务器期望的字段名
    public int roundTimerValue; // 服务器期望的字段名
    public int stepTimerValue; // 服务器期望的字段名
    public bool tips;
    public string password;
    public string random_seed; // 复式主种子：64 位 hex 字符串，空或 "0" 表示关
    public bool open_cuohe; // 是否开启错和
    public int cuohe_type; // 错和形式：0=错和者-30/其余+10，1=错和者-40/其余+0（仅国标）
    public int hepai_limit; // 起和番限制
    public bool tourist_limit; // 游客限制
    public bool allow_spectator; // 允许观战
    public bool tactical_call; // 战术鸣牌（国标/青雀）
}

public class CreateRiichiRoomRequest { // 创建立直麻将房间请求
    public string type;
    public string rule;
    public string sub_rule;
    public string roomname;
    public int gameround;
    public int roundTimerValue;
    public int stepTimerValue;
    public bool tips;
    public string password;
    public string random_seed; // 复式主种子：64 位 hex 字符串，空或 "0" 表示关
    public bool open_cuohe; // 是否开启错和（日麻：向其余三家各赔 3000 并重打本局；国标：罚分后继续本局）
    public int hepai_limit; // 自定义起和番数，低于该番数视为错和
    public bool red_dora; // 是否启用赤宝牌
    public bool allow_kuikae; // 是否允许食替（吃什么打什么）
    public bool open_xiru; // 是否西入
    public bool open_tobi; // 是否击飞
    public string hepai_way; // 和牌方式：head_bump / multi_ron / three_ron_abort
    public bool tourist_limit;
    public bool allow_spectator;
}

public class CreateSichuanRoomRequest { // 创建四川麻将（血战到底）房间请求
    public string type;
    public string rule;
    public string sub_rule;
    public string roomname;
    public int gameround;
    public int roundTimerValue;
    public int stepTimerValue;
    public bool tips;
    public string password;
    public string random_seed; // 复式主种子：64 位 hex 字符串，空或 "0" 表示关
    public bool tourist_limit;
    public bool allow_spectator;
    public bool tactical_call; // 战术鸣牌
    public bool blood_battle; // 血战到底：开=和牌后续打至三家和或流局；关=一家和牌即结束本盘
}

public class GetRoomListRequest { // 获取房间列表请求
    public string type;
    public bool show_tip; // True=手动刷新显示tips，False=静默刷新
}

public class SyncMyRoomRequest { // 同步当前玩家房间状态（重连后拉取权威房间信息）
    public string type;
}

public class JoinRoomRequest { // 加入房间请求
    public string type;
    public string room_id;
    public string password;
}

public class LeaveRoomRequest { // 离开房间请求
    public string type;
    public string room_id;
}

public class StartGameRequest { // 开始游戏请求
    public string type;
    public string room_id;
}

public class SendChineseGameTileRequest { // 发送国标游戏牌请求
    public string type;
    public bool? cutClass;
    public int TileId;
    public int cutIndex;
    public string gamestate_id; // 游戏状态ID
}

public class SendActionRequest { // 发送国标游戏操作请求
    public string type; // 消息类型 "send_action"
    public string action; // 操作类型 "cut" "chi_left" "chi_mid" "chi_right" "peng" "gang" "angang" "hu" "buhua"
    public int? targetTile; // 暗杠目标牌 加杠目标牌
    public bool? cutClass; // 切牌类型
    public int? TileId; // 切牌
    public string gamestate_id; // 游戏状态ID
    public int? chiComboIndex; // 立直麻将赤宝牌吃牌候选索引，默认 0 表示优先非赤 5
    public int? action_tick; // 本次操作所回应的询问帧；服务端用于丢弃战术鸣牌前的过期提交
}

public class SetRyuukyokuTenpaiRequest {
    public string type;
    public string gamestate_id;
    public bool tenpai;
}

public class GetRecordListRequest { // 获取游戏记录请求
    public string type;
    public int limit = 20;
    public int offset = 0;
}

public class GetRankRecordListRequest { // 获取全服最近天梯对局记录请求
    public string type;
    public int limit = 20;
}

public class GetLeaderboardRequest { // 获取国标段位排行榜请求
    public string type;
}

public class GetPlayerInfoRequest { // 获取玩家信息请求
    public string type;
    public string userid;
}

public class GetGuobiaoStatsRequest { // 获取国标统计数据请求
    public string type;
    public string userid;
    public bool need_player_info; // 是否需要玩家信息（第一次加载时需要）
}

public class GetRiichiStatsRequest { // 获取立直统计数据请求
    public string type;
    public string userid;
    public bool need_player_info; // 是否需要玩家信息（第一次加载时需要）
}

public class GetQingqueStatsRequest { // 获取青雀统计数据请求
    public string type;
    public string userid;
    public bool need_player_info; // 是否需要玩家信息（第一次加载时需要）
}

public class SendReleaseVersionRequest { // 发送发布版本号请求
    public string type;
    public int release_version;
}

public class GetServerStatsRequest { // 获取服务器统计信息请求
    public string type;
}

public class PingRequest { // 心跳/延迟测量请求 服务器原样回传 client_ts
    public string type;
    public long client_ts; // 客户端发送时刻(毫秒)
}

public class ReconnectRequest { // 重连请求
    public string type;
    public bool reconnect;
}

public class AddBotToRoomRequest { // 添加机器人请求
    public string type;
    public string room_id;
}

public class KickPlayerFromRoomRequest { // 房主移除玩家请求
    public string type;
    public string room_id;
    public int target_user_id;
}

public class SetReadyRequest { // 设置准备状态请求
    public string type;
    public string room_id;
    public bool ready;
}

public class GetSpectatorListRequest { // 获取观战列表请求
    public string type;
}

public class AddSpectatorRequest { // 添加观战请求
    public string type;
    public string gamestate_id;
}

public class RemoveSpectatorRequest { // 移除观战请求
    public string type;
    public string gamestate_id;
}

public class SendStickerRequest {
    public string type;
    public string gamestate_id;
    public string sticker;
}