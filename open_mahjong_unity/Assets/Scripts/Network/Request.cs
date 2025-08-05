using System;

[Serializable]
public class LoginRequest // 登录请求
{
    public string type;
    public string username;
    public string password;
}

[Serializable]
public class CreateGBRoomRequest // 创建国标房间请求
{
    public string type;
    public string rule;
    public string roomname; // 服务器期望的字段名
    public int gameround; // 服务器期望的字段名
    public int roundTimerValue; // 服务器期望的字段名
    public int stepTimerValue; // 服务器期望的字段名
    public bool tips;
    public string password;
}

[Serializable]
public class GetRoomListRequest // 获取房间列表请求
{
    public string type;
}

[Serializable]
public class JoinRoomRequest // 加入房间请求
{
    public string type;
    public string room_id;
    public string password;
}

[Serializable]
public class LeaveRoomRequest // 离开房间请求
{
    public string type;
    public string room_id;
}

[Serializable]
public class StartGameRequest // 开始游戏请求
{
    public string type;
    public string room_id;
}

[Serializable]
public class SendChineseGameTileRequest // 发送国标游戏牌请求
{
    public string type;
    public bool? cutClass;
    public int TileId;
    public string room_id;
}

[Serializable]
public class SendActionRequest // 发送国标游戏操作请求
{
    public string type; // 消息类型 "send_action"
    public string action; // 操作类型 "cut" "chi_left" "chi_mid" "chi_right" "peng" "gang" "angang" "hu" "buhua"
    public bool? cutClass; // 切牌类型
    public int? TileId; // 切牌
    public string room_id;
}