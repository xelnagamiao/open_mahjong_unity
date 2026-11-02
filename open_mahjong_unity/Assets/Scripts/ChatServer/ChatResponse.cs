using System;

[Serializable]
public class ChatResponse {
    public string requestType, requestId, code;
    public bool success;
    public int protocolVersion;
    public string responseType; // 预留不同类型的响应 目前 Chat 表示聊天消息 False 表示错误信息 Tips 表示提示信息
    public int roomId; // 房间ID
    public string content; // 消息内容
}


[Serializable]
public sealed class ChatConversationInfo {
    public string key, kind, title, event_id, last_message, last_message_at;
    public int peer_user_id, profile_image_id, unread_count;
    public bool can_send;
}

[Serializable]
public sealed class ChatVenueInfo {
    public string event_id, name, kind, status;
    public bool can_send;
}

[Serializable]
public sealed class ConversationChatMessage {
    public long id;
    public string conversation_key, sender_name, content, sent_at;
    public int sender_user_id, profile_image_id;
}

[Serializable]
public sealed class ChatConversationResponse {
    public string type, request_id, message, removed_key;
    public int user_id;
    public bool success, has_more;
    public ChatConversationInfo[] conversations;
    public ChatVenueInfo[] venues;
    public ChatConversationInfo conversation;
    public ConversationChatMessage[] messages;
    public ConversationChatMessage chat_message;
}
