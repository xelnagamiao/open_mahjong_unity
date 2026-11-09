using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Chat
{
    [DisallowMultipleComponent]
    public sealed class TableChatLauncher : MonoBehaviour
    {
        const float PreviewHoldSeconds = 8f;
        const float PreviewFadeSeconds = 1f;
        public GraphiteChatBridge chat;
        public CanvasGroup visibility;
        public Button open;
        public Graphic surface;
        public TMP_Text caption, badge;
        public GameObject badgeRoot, previewRoot;
        public TMP_Text previewBody;
        public int UnreadCount { get; private set; }
        GraphiteChatBridge.Message latest;
        GameSceneMouseInputController registeredInput;
        int observedChannel = -1;
        CanvasGroup previewOpacity;
        float previewReceivedAt;

        void Awake()
        {
            open.onClick.AddListener(Toggle);
            previewOpacity = previewRoot.GetComponent<CanvasGroup>();
            Refresh();
        }
        void OnEnable() => ConfigManager.OnLanguageChanged += Refresh;
        void OnDisable() => ConfigManager.OnLanguageChanged -= Refresh;
        void OnDestroy()
        {
            if (registeredInput) registeredInput.UnregisterAdditionalUiRoot((RectTransform)transform);
            if (open) open.onClick.RemoveListener(Toggle);
        }
        void LateUpdate()
        {
            bool visible = chat.AtTable && chat.Available;
            visibility.alpha = visible ? 1 : 0;
            visibility.interactable = visibility.blocksRaycasts = visible;
            if (visible && GameSceneMouseInputController.Instance && registeredInput != GameSceneMouseInputController.Instance)
            {
                registeredInput = GameSceneMouseInputController.Instance;
                registeredInput.RegisterAdditionalUiRoot((RectTransform)transform);
            }
            int room = chat.RoomChannel;
            if (room != observedChannel) { observedChannel = room; Clear(); }
            if (chat.IsVisible) { UnreadCount = 0; badgeRoot.SetActive(false); }
            float fade = Mathf.Clamp01((Time.unscaledTime - previewReceivedAt - PreviewHoldSeconds) / PreviewFadeSeconds);
            previewOpacity.alpha = latest != null ? 1f - fade * fade * (3f - 2f * fade) : 0f;
            previewRoot.SetActive(visible && latest != null && previewOpacity.alpha > 0f);
            caption.text = PlatformText.Get(chat.IsVisible ? "收起聊天" : "聊天室");
            surface.color = chat.IsVisible ? new Color32(49, 96, 119, 250) : new Color32(29, 36, 47, 238);
        }
        public void Toggle()
        {
            bool opening = !chat.lobby.ChatOpen;
            chat.lobby.ToggleTableChat();
            if (opening) { chat.SelectChannel(chat.RoomChannel > 0 ? 1 : 0); UnreadCount = 0; Refresh(); }
        }
        public void Receive(GraphiteChatBridge.Message message)
        {
            if (!chat.AtTable || !chat.Available || (message.type != "Chat" && message.type != "Sercet")) return;
            if (message.room != 0 && message.room != chat.RoomChannel) return;
            if (observedChannel != chat.RoomChannel) { observedChannel = chat.RoomChannel; Clear(); }
            latest = message;
            previewReceivedAt = Time.unscaledTime;
            if (!message.mine && !chat.IsVisible) UnreadCount++;
            Refresh();
        }
        public void Clear() { latest = null; UnreadCount = 0; if (previewOpacity) previewOpacity.alpha = 0; previewRoot.SetActive(false); Refresh(); }
        void Refresh()
        {
            badge.text = UnreadCount > 99 ? "99+" : UnreadCount.ToString();
            badgeRoot.SetActive(UnreadCount > 0);
            if (latest == null) { previewBody.text = ""; return; }
            string channel = latest.room == 0 ? PlatformText.Get("[大厅]") : PlatformText.Format("[房间{0}]", latest.room);
            string content = latest.mine ? latest.text : latest.sourceContent ?? (latest.author + ": " + latest.text);
            previewBody.text = channel + ": " + content;
            previewBody.color = latest.type == "Sercet" ? Color.green : Color.white;
        }
    }
}
