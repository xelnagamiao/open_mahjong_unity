using System;

[Serializable]
public class LoginRequest
{
    public string type;
    public string username;
    public string password;
}

[Serializable]
public class CreateRoomRequest
{
    public string type;
    public string roomname;
    public int gametime;
    public int cuttime;
    public string password;
}

[Serializable]
public class GetRoomListRequest
{
    public string type;
}

[Serializable]
public class JoinRoomRequest
{
    public string type;
    public string room_id;
    public string password;
}

[Serializable]
public class LeaveRoomRequest
{
    public string type;
    public string room_id;
}

[Serializable]
public class StartGameRequest
{
    public string type;
    public string room_id;
}

[Serializable]
public class SendChineseGameTileRequest
{
    public string type;
    public bool cutClass;
    public int TileId;
    public string room_id;
}

[Serializable]
public class SendActionRequest
{
    public string type;
    public string action;
    public string room_id;
}