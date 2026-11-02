using System;
using System.Collections.Generic;

public sealed partial class ChatMessageStore
{
    public int ConversationUserId { get; private set; }
    readonly List<ChatConversationInfo> conversations = new List<ChatConversationInfo>();
    readonly List<ChatVenueInfo> chatVenues = new List<ChatVenueInfo>();
    readonly Dictionary<string, List<ConversationChatMessage>> conversationMessages = new Dictionary<string, List<ConversationChatMessage>>();
    readonly Dictionary<string, bool> historyMore = new Dictionary<string, bool>();
    public IReadOnlyList<ChatConversationInfo> Conversations => conversations;
    public IReadOnlyList<ChatVenueInfo> ChatVenues => chatVenues;
    public event Action ConversationsChanged, VenuesChanged;
    public event Action<ConversationChatMessage> ConversationMessageReceived;
    public event Action<ChatConversationInfo> ConversationOpened;
    public event Action<string, string> ConversationFailed;

    public IReadOnlyList<ConversationChatMessage> GetConversationMessages(string key)
        => key != null && conversationMessages.TryGetValue(key, out var list) ? list : (IReadOnlyList<ConversationChatMessage>)Array.Empty<ConversationChatMessage>();
    public bool HasEarlierMessages(string key) => key != null && historyMore.TryGetValue(key, out bool more) && more;

    public void BindConversationUser(int userId)
    {
        if (ConversationUserId == userId) return;
        ConversationUserId = userId;
        conversations.Clear(); chatVenues.Clear(); conversationMessages.Clear(); historyMore.Clear();
        Clear();
        ConversationsChanged?.Invoke(); VenuesChanged?.Invoke();
    }

    public void ReportConversationFailure(string type, string message) => ConversationFailed?.Invoke(type, message);

    public void ApplyConversationResponse(ChatConversationResponse response, bool activateConversation = true)
    {
        if (response == null || response.user_id != ConversationUserId || ConversationUserId <= 0) return;
        if (!response.success) { ReportConversationFailure(response.type, response.message); return; }
        if (response.conversations != null)
        {
            conversations.Clear();
            foreach (var item in response.conversations) Upsert(item);
            var unavailable = new List<string>();
            foreach (var key in conversationMessages.Keys)
                if (!conversations.Exists(item => item.key == key)) unavailable.Add(key);
            foreach (var key in unavailable) { conversationMessages.Remove(key); historyMore.Remove(key); }
        }
        if (response.venues != null)
        {
            chatVenues.Clear(); chatVenues.AddRange(response.venues);
            var allowed = new HashSet<string>();
            foreach (var venue in chatVenues)
                if (venue != null && !string.IsNullOrEmpty(venue.event_id)) allowed.Add(venue.event_id);
            foreach (var conversation in conversations.ToArray())
                if (conversation.kind == "venue" && !allowed.Contains(conversation.event_id))
                {
                    conversations.Remove(conversation);
                    conversationMessages.Remove(conversation.key);
                    historyMore.Remove(conversation.key);
                }
            VenuesChanged?.Invoke();
        }
        if (!string.IsNullOrEmpty(response.removed_key))
        {
            conversations.RemoveAll(item => item.key == response.removed_key);
            conversationMessages.Remove(response.removed_key); historyMore.Remove(response.removed_key);
        }
        Upsert(response.conversation);
        if (response.messages != null && response.conversation != null)
        {
            foreach (var item in response.messages) Merge(item);
            historyMore[response.conversation.key] = response.has_more;
        }
        bool received = response.chat_message != null && Merge(response.chat_message);
        conversations.Sort((a, b) => string.CompareOrdinal(b.last_message_at, a.last_message_at));
        ConversationsChanged?.Invoke();
        if (received) ConversationMessageReceived?.Invoke(response.chat_message);
        if (activateConversation && response.conversation != null &&
            (response.type == "chat/open_direct" || response.type == "chat/open_venue"))
            ConversationOpened?.Invoke(response.conversation);
    }

    void Upsert(ChatConversationInfo info)
    {
        if (info == null || string.IsNullOrEmpty(info.key)) return;
        int index = conversations.FindIndex(item => item.key == info.key);
        if (index < 0) conversations.Add(info); else conversations[index] = info;
    }

    bool Merge(ConversationChatMessage message)
    {
        if (message == null || message.id <= 0 || string.IsNullOrEmpty(message.conversation_key)) return false;
        if (!conversationMessages.TryGetValue(message.conversation_key, out var list))
            conversationMessages[message.conversation_key] = list = new List<ConversationChatMessage>();
        if (list.Exists(item => item.id == message.id)) return false;
        list.Add(message); list.Sort((a, b) => a.id.CompareTo(b.id));
        return true;
    }
}
