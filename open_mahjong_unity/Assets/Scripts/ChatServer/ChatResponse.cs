using System;

[Serializable]
public class ChatResponse {
    public string responseType; // 预留不同类型的响应 目前 Chat 表示聊天消息 False 表示错误信息 Tips 表示提示信息
    public int roomId; // 房间ID
    public string content; // 消息内容
}