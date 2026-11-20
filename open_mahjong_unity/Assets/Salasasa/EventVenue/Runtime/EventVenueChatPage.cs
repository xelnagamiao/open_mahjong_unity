using System.Collections.Generic;
using System.Linq;
using Salasasa.Chat;
using Salasasa.ChatLab;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Events
{
    public sealed class EventVenueChatPage : MonoBehaviour
    {
        public EventVenuePanel panel;
        public ChatMessageStore store;
        public ScrollRect scroll;
        public ChatMessageRow messageTemplate;
        public TMP_Text status;
        public TMP_InputField composer;
        public Button send;

        readonly List<ChatMessageRow> rows = new List<ChatMessageRow>();
        string key, error;
        int visit, venueVersion;
        bool connected, opening, opened, sending;
        long lastRead;
        int shown;
        float messageWidth;
        bool Connected => panel.CanChat && store.ConversationUserId == UserDataManager.Instance.UserId &&
            ChatManager.Instance && ChatManager.Instance.CanSendConversations;
        ChatConversationInfo Conversation => opened ? store.Conversations.FirstOrDefault(c => c.key == key) : null;

        void OnEnable()
        {
            store.ConversationsChanged += Refresh;
            ConfigManager.OnLanguageChanged += Refresh;
            send.onClick.AddListener(Send);
            composer.onSubmit.AddListener(Submit);
            composer.onValueChanged.AddListener(DraftChanged);
            Refresh();
        }

        void OnDisable()
        {
            store.ConversationsChanged -= Refresh;
            ConfigManager.OnLanguageChanged -= Refresh;
            send.onClick.RemoveListener(Send);
            composer.onSubmit.RemoveListener(Submit);
            composer.onValueChanged.RemoveListener(DraftChanged);
            visit++;
            opened = opening = connected = false;
            key = error = null;
            lastRead = 0;
        }

        public void ClearVenue()
        {
            venueVersion++;
            visit++;
            opened = opening = connected = sending = false;
            key = error = null;
            lastRead = 0;
            composer.SetTextWithoutNotify("");
        }

        void Update()
        {
            if (connected != Connected) Reload();
            else if (Mathf.Abs(scroll.content.rect.width - messageWidth) > .5f) Refresh();
        }

        public void Reload()
        {
            if (!isActiveAndEnabled) return;
            connected = Connected;
            if (!connected)
            {
                visit++;
                opened = opening = false;
                error = null;
                Refresh();
                return;
            }
            if (opening) return;
            int currentVisit = ++visit;
            key = ChatConversationListModel.VenueKey(panel.EventId);
            opening = true;
            error = null;
            Refresh();
            ChatManager.Instance.OpenVenueConversation(panel.EventId, failure =>
            {
                if (!this || visit != currentVisit) return;
                opening = false;
                opened = failure == null;
                error = failure;
                Refresh();
            });
        }

        void DraftChanged(string _) => UpdateComposer(Conversation);
        void Submit(string _) { if (!composer.wasCanceled) Send(); }

        void Send()
        {
            if (!Connected || Conversation?.can_send != true || panel.ChatClosed || sending || string.IsNullOrWhiteSpace(composer.text)) return;
            string draft = composer.text;
            int version = venueVersion;
            sending = true;
            error = null;
            Refresh();
            ChatManager.Instance.SendConversationMessage(key, draft, failure =>
            {
                if (!this || version != venueVersion) return;
                sending = false;
                if (failure == null && composer.text == draft) composer.SetTextWithoutNotify("");
                error = failure;
                if (isActiveAndEnabled) Refresh();
            });
        }

        void UpdateComposer(ChatConversationInfo info)
        {
            bool writable = connected && info?.can_send == true && !panel.ChatClosed;
            composer.interactable = writable;
            send.interactable = writable && !sending && !string.IsNullOrWhiteSpace(composer.text);
            ((TMP_Text)composer.placeholder).text = PlatformText.Get(info != null && !writable ? "此会话仅供阅读" : "输入消息…");
        }

        public void Refresh()
        {
            if (!isActiveAndEnabled) return;
            var info = Connected ? Conversation : null;
            var messages = store.GetConversationMessages(info?.key);
            // The server returns the most recent 50 messages on entry.
            int count = Mathf.Min(50, messages.Count);
            bool atBottom = shown == 0 || scroll.content.rect.height <= scroll.viewport.rect.height || scroll.verticalNormalizedPosition < .02f;
            messageWidth = scroll.content.rect.width;
            var palette = panel.view;
            Color outgoing = Color.Lerp(palette.field, palette.accent, .4f);
            float top = 4f;
            for (int i = 0; i < count; i++)
            {
                if (i == rows.Count) rows.Add(Instantiate(messageTemplate, scroll.content));
                var row = rows[i];
                row.gameObject.SetActive(true);
                var message = messages[messages.Count - count + i];
                bool mine = message.sender_user_id == store.ConversationUserId;
                ConfigManager.SetProfilePortrait(row.portrait,
                    message.profile_image_id > 0 ? message.profile_image_id : ConfigManager.DefaultAvatarItemId, palette.field);
                row.author.text = message.sender_name;
                row.time.text = ChatConversationListModel.TimeText(message.sent_at);
                row.body.text = message.content;
                row.author.color = row.time.color = palette.muted;
                row.body.color = palette.ink;
                row.bubble.color = mine ? outgoing : palette.field;
                top += row.LayoutBubble(messageWidth, top, mine) + 8f;
            }
            for (int i = count; i < shown; i++) rows[i].gameObject.SetActive(false);
            shown = count;
            scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, top);
            if (atBottom) scroll.verticalNormalizedPosition = 0;
            status.text = PlatformText.Get(error ?? (!Connected ? "未连接聊天服务器" : opening ? "加载中…" : count == 0 ? "暂无数据" : ""));
            UpdateComposer(info);
            if (info != null && info.unread_count > 0 && count > 0)
            {
                long latest = messages[messages.Count - 1].id;
                if (latest > lastRead)
                {
                    lastRead = latest;
                    ChatManager.Instance.MarkConversationRead(key);
                }
            }
        }
    }
}
