using System;
using System.Collections.Generic;
using System.Linq;
using Salasasa.ChatLab;
using UnityEngine;

namespace Salasasa.Chat
{
    public sealed partial class GraphiteChatBridge
    {
        public string SelectedConversationKey { get; private set; } = ChatConversationListModel.LobbyKey;
        public int SidebarTab { get; private set; }
        readonly Dictionary<int, int> legacyUnread = new Dictionary<int, int>();
        readonly Dictionary<string, long> readRequests = new Dictionary<string, long>(StringComparer.Ordinal);
        readonly HashSet<string> pendingRemoval = new HashSet<string>(StringComparer.Ordinal);
        readonly List<SidebarEntry> sidebarEntries = new List<SidebarEntry>();
        string pendingOpenKey = "";
        string pendingOutgoingKey = "", pendingOutgoingText = "";
        long pendingOutgoingAfterId;
        float nextConversationRefresh, nextVenueRefresh;
        bool venuesLoaded;
        bool waitingLegacySend;

        sealed class SidebarEntry
        {
            public string key, title, detail, initial;
            public int profile, unread, fixedChannel = -1;
            public bool removable;
        }

        string DraftKey => SelectedConversationKey == ChatConversationListModel.RoomKey ? "room:" + RoomChannel : SelectedConversationKey;
        ChatConversationInfo SelectedConversation => source.Conversations.FirstOrDefault(item => item.key == SelectedConversationKey);
        bool SelectedCanCompose => SelectedChannel < 2 || SelectedConversation?.can_send == true;
        bool SelectedConnected => SelectedChannel < 2
            ? (SelectedChannel == 0 || RoomChannel > 0) && (ValidationMode && ValidationSend != null || ChatManager.Instance && ChatManager.Instance.CanSendToRoom(SelectedChannel == 0 ? 0 : RoomChannel))
            : ChatManager.Instance && ChatManager.Instance.CanSendConversations;
        bool CanReadSelected => lobby.ChatOpen && Available && WindowsManager.Instance?.GetCurrentWindow() != "login";
        bool WaitingForSend => waitingLegacySend || !string.IsNullOrEmpty(pendingOutgoingKey);

        void InitializeConversations()
        {
            source.ConversationsChanged += ConversationsChanged;
            source.VenuesChanged += VenuesChanged;
            source.ConversationMessageReceived += ConversationMessageReceived;
            source.ConversationOpened += ConversationOpened;
            source.ConversationFailed += ConversationFailed;
        }

        void DisposeConversations()
        {
            if (!source) return;
            source.ConversationsChanged -= ConversationsChanged;
            source.VenuesChanged -= VenuesChanged;
            source.ConversationMessageReceived -= ConversationMessageReceived;
            source.ConversationOpened -= ConversationOpened;
            source.ConversationFailed -= ConversationFailed;
        }

        void ClearConversationState()
        {
            SelectedConversationKey = ChatConversationListModel.LobbyKey; SidebarTab = 0;
            legacyUnread.Clear(); pendingRemoval.Clear(); readRequests.Clear(); sidebarEntries.Clear();
            pendingOpenKey = ""; venuesLoaded = false; nextConversationRefresh = nextVenueRefresh = 0;
            pendingOutgoingKey = pendingOutgoingText = "";
            waitingLegacySend = false;
            query = ""; if (view && view.search) view.search.SetTextWithoutNotify("");
        }

        public void ShowChats() => SetSidebar(0);
        public void ShowVenues() { nextVenueRefresh = 0; SetSidebar(1); }
        void SetSidebar(int tab)
        {
            SidebarTab = Mathf.Clamp(tab, 0, 2);
            query = ""; view.search.SetTextWithoutNotify("");
            ResetFriendScroll(); HidePopup(); Refresh();
        }

        public void SelectConversation(string key, bool keepSidebar = false)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!ChatConversationListModel.IsFixed(key) && !source.Conversations.Any(item => item.key == key)) return;
            drafts[DraftKey] = view.composer.text;
            SelectedConversationKey = key;
            pendingOpenKey = "";
            if (!keepSidebar) { SidebarTab = 0; query = ""; view.search.SetTextWithoutNotify(""); ResetFriendScroll(); }
            view.composer.SetTextWithoutNotify(drafts.TryGetValue(DraftKey, out var text) ? text : "");
            HidePopup(); Refresh();
            if (!ChatConversationListModel.IsFixed(key)) ChatManager.Instance?.RequestConversationHistory(key);
            MarkSelectedRead();
            RenderMessages(true);
        }

        public void OpenPrivateConversation(int uid)
        {
            var user = UserDataManager.Instance;
            if (uid <= 0 || !user || user.IsTourist || uid == user.UserId || !ChatManager.Instance || !ChatManager.Instance.CanSendConversations) return;
            ShowChatWindow();
            pendingOpenKey = ChatConversationListModel.DirectKey(user.UserId, uid);
            ChatManager.Instance.OpenDirectConversation(uid); RefreshList();
        }

        public void OpenVenueConversation(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId) || !UserDataManager.Instance || UserDataManager.Instance.IsTourist || !ChatManager.Instance || !ChatManager.Instance.CanSendConversations) return;
            ShowChatWindow();
            pendingOpenKey = ChatConversationListModel.VenueKey(eventId);
            ChatManager.Instance.OpenVenueConversation(eventId); RefreshList();
        }

        void ShowChatWindow()
        {
            if (lobby.ChatOpen) return;
            if (AtTable) lobby.ToggleTableChat(); else lobby.Open("chat");
        }

        void ConversationOpened(ChatConversationInfo conversation)
        {
            if (conversation == null || conversation.key != pendingOpenKey) return;
            pendingRemoval.Remove(conversation.key);
            SelectConversation(conversation.key, SidebarTab == 2 && conversation.kind == "dm" || SidebarTab == 1 && conversation.kind == "venue");
        }

        public void RemoveConversation(string key)
        {
            if (ChatConversationListModel.IsFixed(key) || string.IsNullOrEmpty(key) || pendingRemoval.Contains(key)) return;
            if (!source.Conversations.Any(item => item.key == key) || !ChatManager.Instance || !ChatManager.Instance.CanSendConversations) return;
            pendingRemoval.Add(key);
            if (SelectedConversationKey == key) SelectChannel(0);
            ChatManager.Instance.RemoveConversation(key);
            RefreshList();
        }

        void ConversationsChanged()
        {
            ConfirmConversationSend();
            pendingRemoval.RemoveWhere(key => !source.Conversations.Any(item => item.key == key));
            if (!ChatConversationListModel.IsFixed(SelectedConversationKey) && SelectedConversation == null)
                SelectConversation(ChatConversationListModel.LobbyKey, true);
            else { RefreshConversationHeader(); RefreshList(); RenderMessages(false); UpdateComposer(); MarkSelectedRead(); }
        }

        void VenuesChanged()
        {
            venuesLoaded = true;
            RefreshConversationHeader();
            if (SidebarTab == 1) RefreshList();
        }

        void ConversationFailed(string type, string message)
        {
            if(type=="chat/open_direct"||type=="chat/open_venue")pendingOpenKey = "";
            if(type=="chat/remove")pendingRemoval.Clear();
            if(type=="chat/read")readRequests.Clear();
            if(type=="chat/send")pendingOutgoingKey=pendingOutgoingText="";
            Refresh();
            if (!string.IsNullOrWhiteSpace(message)) GameHost.Current.ShowTip(PlatformText.Get("聊天"), false, PlatformText.Get(message));
        }

        void ConversationMessageReceived(ConversationChatMessage message)
        {
            if (message == null) return;
            ConfirmConversationSend();
            bool selected = message.conversation_key == SelectedConversationKey;
            bool bottom = view.messageScroll.verticalNormalizedPosition < .08f;
            if (selected) { RenderMessages(bottom); MarkSelectedRead(); }
            RefreshList();
        }

        void PollConversations(bool visible)
        {
            if (!visible) { nextConversationRefresh = nextVenueRefresh = 0; return; }
            var manager = ChatManager.Instance;
            if (!manager || !manager.CanSendConversations || !UserDataManager.Instance || UserDataManager.Instance.IsTourist) return;
            if (Time.unscaledTime >= nextConversationRefresh)
            {
                nextConversationRefresh = Time.unscaledTime + 5f;
                manager.RequestConversations();
            }
            if (SidebarTab == 1 && Time.unscaledTime >= nextVenueRefresh)
            {
                nextVenueRefresh = Time.unscaledTime + 10f;
                manager.RequestChatVenues();
            }
        }

        void BeginConversationSend(string text)
        {
            pendingOutgoingKey=SelectedConversationKey;pendingOutgoingText=text;
            pendingOutgoingAfterId=source.GetConversationMessages(SelectedConversationKey).Select(message=>message.id).DefaultIfEmpty(0).Max();
            drafts[DraftKey]=text;
        }

        void ConfirmConversationSend()
        {
            if(!WaitingForSend)return;
            bool confirmed=source.GetConversationMessages(pendingOutgoingKey).Any(message=>message.id>pendingOutgoingAfterId
                && message.sender_user_id==UserDataManager.Instance?.UserId && (message.content??"").Trim()==pendingOutgoingText.Trim());
            if(!confirmed)return;
            if(drafts.TryGetValue(pendingOutgoingKey,out var saved)&&saved==pendingOutgoingText)drafts[pendingOutgoingKey]="";
            if(SelectedConversationKey==pendingOutgoingKey&&view.composer.text==pendingOutgoingText)view.composer.SetTextWithoutNotify("");
            pendingOutgoingKey=pendingOutgoingText="";UpdateComposer();
        }

        void ReceiveLegacyUnread(Message message)
        {
            if (message.mine || message.type != "Chat" && message.type != "Sercet") return;
            int channel = message.room == 0 ? 0 : 1;
            if (message.conversationKey == SelectedConversationKey && CanReadSelected) legacyUnread[channel] = 0;
            else legacyUnread[channel] = (legacyUnread.TryGetValue(channel, out var count) ? count : 0) + 1;
        }

        void MarkSelectedRead()
        {
            if (!CanReadSelected) return;
            if (SelectedChannel < 2)
            {
                legacyUnread[SelectedChannel] = 0;
                RefreshList(); return;
            }
            var manager = ChatManager.Instance;
            if (!manager || !manager.CanSendConversations) return;
            long latest = source.GetConversationMessages(SelectedConversationKey).Select(message => message.id).DefaultIfEmpty(0).Max();
            if (latest <= 0 || readRequests.TryGetValue(SelectedConversationKey, out var sent) && sent >= latest) return;
            readRequests[SelectedConversationKey] = latest;
            manager.MarkConversationRead(SelectedConversationKey);
        }

        void RoomChanged(string previous)
        {
            if (SelectedChannel != 1) return;
            int.TryParse(previous, out int previousId);
            drafts["room:" + previousId] = view.composer.text;
            view.composer.SetTextWithoutNotify(drafts.TryGetValue(DraftKey, out var text) ? text : "");
            MarkSelectedRead();
        }

        void RefreshConversationHeader()
        {
            if (SelectedChannel == 0) { view.title.text = PlatformText.Get("大厅"); }
            else if (SelectedChannel == 1)
            {
                view.title.text = PlatformText.Get("房间") + (RoomChannel > 0 ? " " + RoomChannel : "");
            }
            else
            {
                var conversation = SelectedConversation;
                view.title.text = conversation?.title ?? "";
                var friend = conversation?.peer_user_id > 0 ? FriendRelationCache.Friends.FirstOrDefault(item => item.user_id == conversation.peer_user_id) : null;
                bool showPresence = conversation?.kind == "dm";
                view.status.gameObject.SetActive(showPresence);
                if (showPresence)
                {
                    int presence = ChatFriendListModel.PresenceOrder(friend?.state);
                    view.status.color = presence == 0 ? new Color32(71, 194, 117, 255) : presence == 1 ? new Color32(226, 91, 91, 255) : new Color32(142, 149, 157, 255);
                    UpdatePresencePosition();
                }
            }
            if (SelectedChannel < 2) view.status.gameObject.SetActive(false);
            for (int i = 0; i < view.tabLabels.Length; i++) view.tabLabels[i].text = PlatformText.Get(i == 0 ? "聊天" : i == 1 ? "基地" : "好友");
        }

        void RefreshConversationList()
        {
            if (FriendsVisible) return;
            renderingFriends = true;
            sidebarEntries.Clear();
            if (SidebarTab == 1)
            {
                foreach (var venue in ChatConversationListModel.Venues(source.ChatVenues, query))
                    sidebarEntries.Add(new SidebarEntry {
                        key = venue.event_id, title = venue.name, initial = VenueInitial(venue.kind),
                        detail = PlatformText.Get(venue.kind == "base" ? "基地" : "赛事") + " · " + venue.event_id + (venue.can_send ? "" : " · " + PlatformText.Get("只读"))
                    });
            }
            else
            {
                for (int channel = 0; channel < 2; channel++)
                {
                    string name = PlatformText.Get(channel == 0 ? "大厅" : "房间");
                    int room = channel == 0 ? 0 : RoomChannel;
                    string key = channel == 0 ? ChatConversationListModel.LobbyKey : ChatConversationListModel.RoomKey;
                    var last = history.LastOrDefault(message => message.conversationKey == key);
                    if (!ChatConversationListModel.Matches(query, name, channel == 0 ? "大厅" : "房间", last?.text)) continue;
                    sidebarEntries.Add(new SidebarEntry { key = key,
                        title = name, detail = last?.text ?? (channel == 1 && room == 0 ? PlatformText.Get("未进入房间") : ""),
                        fixedChannel = channel, unread = legacyUnread.TryGetValue(channel, out var count) ? count : 0 });
                }
                foreach (var conversation in ChatConversationListModel.Conversations(source.Conversations, query, pendingRemoval))
                {
                    var venue = source.ChatVenues.FirstOrDefault(item => item.event_id == conversation.event_id);
                    sidebarEntries.Add(new SidebarEntry { key = conversation.key, title = conversation.title, detail = conversation.last_message ?? "",
                        profile = conversation.profile_image_id, unread = conversation.unread_count, removable = true,
                        initial = conversation.kind == "dm" ? FirstCharacter(conversation.title) : VenueInitial(venue?.kind) });
                }
            }
            if (friendListContent) friendListContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, sidebarEntries.Count * friendRowHeight);
            view.empty.gameObject.SetActive(sidebarEntries.Count == 0);
            bool waiting = SidebarTab == 1 && !venuesLoaded && ChatManager.Instance && ChatManager.Instance.CanSendConversations;
            view.empty.text = PlatformText.Get(waiting ? "加载中…" : SidebarTab == 1 && !venuesLoaded ? "未连接聊天服务器" : SidebarTab == 1 ? string.IsNullOrWhiteSpace(query) ? "没有已加入的赛事或基地" : "没有找到匹配的赛事或基地" : "没有找到匹配的聊天室");
            renderingFriends = false;
            RenderConversationRows();
        }

        void RenderConversationRows()
        {
            if (FriendsVisible) return;
            int start = ChatFriendListModel.WindowStart(sidebarEntries.Count, view.rows.Length, friendRowHeight, friendListContent ? friendListContent.anchoredPosition.y : 0);
            for (int i = 0; i < view.rows.Length; i++)
            {
                var row = view.rows[i]; int index = start + i;
                row.gameObject.SetActive(index < sidebarEntries.Count);
                if (index >= sidebarEntries.Count) { row.boundId = ""; continue; }
                var item = sidebarEntries[index]; ResetListRow(row, index, item.key);
                row.title.text = item.title ?? ""; row.detail.text = item.detail ?? "";
                row.fill.color = SidebarTab == 0 && item.key == SelectedConversationKey || SidebarTab == 1 && (pendingOpenKey == ChatConversationListModel.VenueKey(item.key) || SelectedConversationKey == ChatConversationListModel.VenueKey(item.key)) ? style.listSelection : Color.clear;
                if (item.fixedChannel >= 0) RestoreChannelPortrait(row, item.fixedChannel); else SetPortrait(row, item.profile, item.initial);
                row.badgeRoot.SetActive(item.unread > 0); row.badge.text = item.unread > 99 ? "99+" : item.unread.ToString();
                if (row.deleteButton) { row.deleteButton.gameObject.SetActive(item.removable); row.deleteButton.interactable = !pendingRemoval.Contains(item.key) && ChatManager.Instance && ChatManager.Instance.CanSendConversations; }
                if(item.removable){var titleRect=row.title.rectTransform;titleRect.offsetMax=new Vector2(-32,titleRect.offsetMax.y);}
                RefreshFriendTextClipping(row.title); RefreshFriendTextClipping(row.detail);
            }
            if (friendListMask) friendListMask.PerformClipping();
        }

        static string FirstCharacter(string value) => string.IsNullOrEmpty(value) ? "" : System.Globalization.StringInfo.GetNextTextElement(value);
        static string VenueInitial(string kind) => FirstCharacter(PlatformText.Get(kind=="base"?"基地":"赛事"));
        Message ToDisplayMessage(ConversationChatMessage message) => new Message {
            author = message.sender_name ?? "", text = message.content ?? "", time = ChatConversationListModel.TimeText(message.sent_at),
            type = "Conversation", room = -1, conversationKey = message.conversation_key, senderUserId = message.sender_user_id,
            mine = message.sender_user_id == UserDataManager.Instance?.UserId, sourceContent = (message.sender_name ?? "") + ": " + (message.content ?? "")
        };
    }
}
