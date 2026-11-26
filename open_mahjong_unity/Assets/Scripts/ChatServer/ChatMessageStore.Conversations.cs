using System;
using System.Collections.Generic;

public sealed partial class ChatMessageStore
{
    public int ConversationUserId { get; private set; }
    readonly List<ChatConversationInfo> conversations = new List<ChatConversationInfo>();
    readonly List<ChatVenueInfo> chatVenues = new List<ChatVenueInfo>();
    readonly Dictionary<string, ConversationHistory> histories = new Dictionary<string, ConversationHistory>();
    public IReadOnlyList<ChatConversationInfo> Conversations => conversations;
    public IReadOnlyList<ChatVenueInfo> ChatVenues => chatVenues;
    public event Action ConversationsChanged, VenuesChanged;
    public event Action<ConversationChatMessage> ConversationMessageReceived;
    public event Action<ChatConversationInfo> ConversationOpened;
    public event Action<string, string> ConversationFailed;

    public IReadOnlyList<ConversationChatMessage> GetConversationMessages(string key)
        => key != null && histories.TryGetValue(key, out var history) ? history.Messages : (IReadOnlyList<ConversationChatMessage>)Array.Empty<ConversationChatMessage>();
    public bool HasLatestHistory(string key) => key != null && histories.TryGetValue(key, out var history) && history.LatestLoaded;
    public long GetEarlierHistoryCursor(string key) => key != null && histories.TryGetValue(key, out var history) ? history.BeforeId : 0;
    public bool HasEarlierMessages(string key) => GetEarlierHistoryCursor(key) > 0;

    public void BindConversationUser(int userId)
    {
        if (ConversationUserId == userId) return;
        ConversationUserId = userId;
        conversations.Clear(); chatVenues.Clear(); histories.Clear();
        Clear();
        ConversationsChanged?.Invoke(); VenuesChanged?.Invoke();
    }

    public void ReportConversationFailure(string type, string message) => ConversationFailed?.Invoke(type, message);

    public void ApplyConversationResponse(ChatConversationResponse response, bool activateConversation = true, long beforeId = 0)
    {
        if (response == null || response.user_id != ConversationUserId || ConversationUserId <= 0) return;
        if (!response.success) { ReportConversationFailure(response.type, response.message); return; }
        if (response.conversations != null)
        {
            conversations.Clear();
            foreach (var item in response.conversations) Upsert(item);
            var unavailable = new List<string>();
            foreach (var key in histories.Keys)
                if (!conversations.Exists(item => item.key == key)) unavailable.Add(key);
            foreach (var key in unavailable) histories.Remove(key);
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
                    histories.Remove(conversation.key);
                }
            VenuesChanged?.Invoke();
        }
        if (!string.IsNullOrEmpty(response.removed_key))
        {
            conversations.RemoveAll(item => item.key == response.removed_key);
            histories.Remove(response.removed_key);
        }
        Upsert(response.conversation);
        string keyForPage = response.conversation?.key;
        if (response.messages != null && !string.IsNullOrEmpty(keyForPage))
            GetHistory(keyForPage).ApplyPage(keyForPage, response.messages, response.has_more, beforeId);
        var live = response.chat_message;
        bool received = live != null && live.id > 0 && !string.IsNullOrEmpty(live.conversation_key) && GetHistory(live.conversation_key).Add(live);
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

    ConversationHistory GetHistory(string key)
    {
        if (!histories.TryGetValue(key, out var history)) histories[key] = history = new ConversationHistory();
        return history;
    }

    sealed class ConversationHistory
    {
        static readonly IComparer<ConversationChatMessage> ById = Comparer<ConversationChatMessage>.Create((a, b) => a.id.CompareTo(b.id));
        public readonly List<ConversationChatMessage> Messages = new List<ConversationChatMessage>();
        public long BeforeId { get; private set; }
        public bool LatestLoaded { get; private set; }

        public bool Add(ConversationChatMessage message)
        {
            int position = Messages.BinarySearch(message, ById);
            if (position >= 0) return false;
            Messages.Insert(~position, message);
            return true;
        }

        public void ApplyPage(string key, ConversationChatMessage[] page, bool more, long beforeId)
        {
            if (beforeId != 0 && beforeId != BeforeId) return;
            if (beforeId == 0) LatestLoaded = true;
            long first = 0;
            // The server returns each page in ascending message ID order.
            foreach (var item in page)
            {
                if (item == null || item.conversation_key != key || item.id <= 0 || (beforeId > 0 && item.id >= beforeId)) continue;
                if (first == 0) first = item.id;
                Add(item);
            }
            if (first == 0)
            {
                if (beforeId > 0) BeforeId = 0;
                return;
            }
            if (beforeId > 0)
            {
                BeforeId = more ? first : 0;
            }
            else if (BeforeId == 0 || first <= BeforeId)
            {
                BeforeId = more ? first : 0;
            }
        }
    }
}
