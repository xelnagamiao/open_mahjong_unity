using System;
using System.Collections.Generic;
using NativeWebSocket;
using Newtonsoft.Json;
using UnityEngine;

public partial class ChatManager
{
    sealed class ConversationRequestState
    {
        public ChatConversationRequest request;
        public float started;
    }
    readonly Dictionary<string, ConversationRequestState> conversationRequests = new Dictionary<string, ConversationRequestState>();
    int conversationUserId;
    string latestConversationOpen;
    public bool CanSendConversations => HasGameSession;

    void FailConversationRequests()
    {
        var requests = new List<ConversationRequestState>(conversationRequests.Values);
        conversationRequests.Clear(); latestConversationOpen = null;
        foreach (var pending in requests)
            if (pending.request.type == "chat/send" || pending.request.type == "chat/open_direct" ||
                pending.request.type == "chat/open_venue" || pending.request.type == "chat/remove")
                ChatMessageStore.Instance?.ReportConversationFailure(pending.request.type, "连接已中断，请重新连接后核对聊天记录");
    }

    void BindConversationUser(int userId)
    {
        if (conversationUserId != userId)
        {
            conversationUserId = userId;
            latestConversationOpen = null; conversationRequests.Clear();
        }
        ChatMessageStore.Instance?.BindConversationUser(userId);
    }

    void TickConversationRequests()
    {
        if (conversationUserId > 0 && (!UserDataManager.Instance || UserDataManager.Instance.UserId != conversationUserId))
            BindConversationUser(0);
        if (conversationRequests.Count == 0) return;
        List<string> expired = null;
        foreach (var pair in conversationRequests)
            if (Time.realtimeSinceStartup - pair.Value.started > 20)
            { if (expired == null) expired = new List<string>(); expired.Add(pair.Key); }
        if (expired == null) return;
        foreach (var id in expired)
        {
            var request = conversationRequests[id].request; conversationRequests.Remove(id);
            ChatMessageStore.Instance?.ReportConversationFailure(request.type, "聊天请求超时，请重试");
        }
    }

    public void RequestConversations() => SendConversation(new ChatConversationRequest { type = "chat/list" });
    public void RequestChatVenues() => SendConversation(new ChatConversationRequest { type = "chat/venues" });
    public void OpenDirectConversation(int userId) => SendConversation(new ChatConversationRequest { type = "chat/open_direct", peer_user_id = userId });
    public void OpenVenueConversation(string eventId) => SendConversation(new ChatConversationRequest { type = "chat/open_venue", event_id = eventId });
    public void RequestConversationHistory(string key, long beforeId = 0) => SendConversation(new ChatConversationRequest { type = "chat/history", conversation_key = key, before_id = beforeId });
    public void SendConversationMessage(string key, string content) => SendConversation(new ChatConversationRequest { type = "chat/send", conversation_key = key, content = content });
    public void RemoveConversation(string key) => SendConversation(new ChatConversationRequest { type = "chat/remove", conversation_key = key });
    public void MarkConversationRead(string key)
    {
        var messages = ChatMessageStore.Instance?.GetConversationMessages(key);
        long last = messages != null && messages.Count > 0 ? messages[messages.Count - 1].id : 0;
        SendConversation(new ChatConversationRequest { type = "chat/read", conversation_key = key, last_message_id = last });
    }

    async void SendConversation(ChatConversationRequest request)
    {
        if (!CanSendConversations)
        { ChatMessageStore.Instance?.ReportConversationFailure(request.type, "请先登录并连接服务器"); return; }
        var socket = NetworkManager.Instance.GetWebSocket();
        int user = conversationUserId;
        request.request_id = Guid.NewGuid().ToString("N");
        if (request.type == "chat/open_direct" || request.type == "chat/open_venue") latestConversationOpen = request.request_id;
        conversationRequests[request.request_id] = new ConversationRequestState { request = request, started = Time.realtimeSinceStartup };
        try { await socket.SendText(JsonConvert.SerializeObject(request)); }
        catch (Exception)
        {
            if (user != conversationUserId || !conversationRequests.Remove(request.request_id)) return;
            ChatMessageStore.Instance?.ReportConversationFailure(request.type, "聊天请求未确认，请重新连接后核对");
        }
    }

    public void HandleConversationResponse(ChatConversationResponse response)
    {
        if (response == null || !CanSendConversations) return;
        if (response.type == "chat/message")
        {
            if (response.user_id == conversationUserId) ChatMessageStore.Instance?.ApplyConversationResponse(response, false);
            return;
        }
        if (string.IsNullOrEmpty(response.request_id) || !conversationRequests.TryGetValue(response.request_id, out var pending)) return;
        if (pending.request.type != response.type || (response.user_id != conversationUserId && response.user_id != 0)) return;
        conversationRequests.Remove(response.request_id);
        if (!response.success)
        { ChatMessageStore.Instance?.ReportConversationFailure(response.type, response.message); return; }
        ChatMessageStore.Instance?.ApplyConversationResponse(response, response.request_id == latestConversationOpen);
    }
}
