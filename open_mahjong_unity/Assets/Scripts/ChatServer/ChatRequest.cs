using System;

[Serializable]
public class ChatRequest
{
    public string type;           // 消息类型: "login", "join_room", "leave_room", "send_chat"
    public object data;           // 根据 type 不同，data 的实际类型也不同
}

[Serializable]
public class ChatLoginRequest
{
    public string username;
    public string userkey;
}

[Serializable]
public class ChatJoinRoomRequest
{
    public int roomId;
}

[Serializable]
public class ChatLeaveRoomRequest
{
    public int roomId;
}

[Serializable]
public class ChatSendChatRequest
{
    public string content;
    public int targetRoomId;
}