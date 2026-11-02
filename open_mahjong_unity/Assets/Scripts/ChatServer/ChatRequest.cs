using System;

[Serializable]
public class ChatRequest {
    public string requestId;
    public string type;           // 消息类型: "login", "join_room", "leave_room", "send_chat"
    public object data;           // 根据 type 不同，data 的实际类型也不同
}

[Serializable]
public class ChatLoginRequest {
    public string username;
    public string userkey;
}

[Serializable]
public class ChatJoinRoomRequest {
    public int roomId;
}

[Serializable]
public class ChatLeaveRoomRequest {
    public int roomId;
}

[Serializable]
public class ChatSendChatRequest {
    public string content;
    public int roomId;
}


[Serializable]
public sealed class ChatConversationRequest {
    public string type, request_id, conversation_key, event_id, content;
    public int peer_user_id;
    public long before_id, last_message_id;
}
