using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public readonly struct MessageAction {
    public readonly string Label;
    public readonly Action Click;
    public readonly bool ClosePanel;

    public MessageAction(string label, Action click = null, bool closePanel = true) {
        Label = label ?? "";
        Click = click;
        ClosePanel = closePanel;
    }
}

public class MessagePrefab : MonoBehaviour {
    [SerializeField] private PanelPopupTransition popupTransition;
    [SerializeField] private TMP_Text HeaderText;
    [SerializeField] private TMP_Text ContentText;
    [SerializeField] private Button YesButton;
    [SerializeField] private TMP_Text YesButtonText;
    [SerializeField] private Button BackButton;
    [SerializeField] private TMP_Text BackButtonText;

    [Header("通知布局（Canvas 单位）")]
    [SerializeField, Min(320)] private float minimumWidth = 640;
    [SerializeField, Min(320)] private float preferredWidth = 720;

    private const float ScreenMargin = 24;
    private const float BodyTop = 88;
    private const float BodyBottom = 104;
    private const float MinimumBodyHeight = 112;
    private readonly Vector3[] canvasCorners = new Vector3[4];
    private readonly List<ButtonSlot> extraSlots = new List<ButtonSlot>();
    private ButtonSlot[] activeSlots = Array.Empty<ButtonSlot>();
    private MessageAction[] currentActions = Array.Empty<MessageAction>();
    [SerializeField] private RectTransform bodyViewport;
    [SerializeField] private ScrollRect bodyScroll;
    [SerializeField] private Scrollbar bodyScrollbar;
    private Vector2 lastAvailableSize;
    private bool hasMessage;
    private TextAlignmentOptions bodyAlignment = TextAlignmentOptions.Center;
    private bool selectFirstButton;

    private GameObject modalOwner;
    private bool closing;
    private bool abandonConfirmOpen;

    private readonly struct ButtonSlot {
        public readonly Button Button;
        public readonly TMP_Text Text;

        public ButtonSlot(Button button, TMP_Text text) {
            Button = button;
            Text = text;
        }
    }

    private void Awake() {
        if (popupTransition == null) {
            popupTransition = GetComponent<PanelPopupTransition>();
        }
    }

    public void ShowMessage(string header, string content, string type = "") {
        Show(header, content, BuildActions(type));
    }

    public void ShowConfirmation(string header, string content, Action onConfirm,
        string confirmText = "确定", string cancelText = "取消") {
        var actions = new List<MessageAction>(2);
        if (!string.IsNullOrEmpty(cancelText)) {
            actions.Add(new MessageAction(cancelText));
        }
        actions.Add(new MessageAction(string.IsNullOrEmpty(confirmText) ? "确定" : confirmText, onConfirm));
        Show(header, content, actions, true);
    }

    public void Show(string header, string content, IReadOnlyList<MessageAction> actions, bool selectFirst = false) {
        closing = false;
        selectFirstButton = selectFirst;
        HeaderText.text = header;
        ContentText.text = content;
        hasMessage = true;
        BindActions(actions);
        LayoutMessage();
        if (selectFirstButton && activeSlots.Length > 0 && EventSystem.current != null) {
            EventSystem.current.SetSelectedGameObject(activeSlots[0].Button.gameObject);
        }

        if (popupTransition != null) {
            popupTransition.Show();
        } else {
            gameObject.SetActive(true);
        }
    }

    public void SetModalOwner(GameObject owner) => modalOwner = owner;

    public void SetBodyAlignment(TextAlignmentOptions alignment) {
        bodyAlignment = alignment;
        if (hasMessage) LayoutMessage();
    }

    private MessageAction[] BuildActions(string type) {
        if (type == "reconnect_ask") {
            return new[] {
                new MessageAction("放弃比赛", ConfirmAbandonReconnect, false),
                new MessageAction("重新连接", () => ReconnectClick("yes")),
            };
        }
        if (type == "error_version") {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new[] {
                new MessageAction("关闭"),
                new MessageAction("去下载", OpenMobileDownloadPage),
            };
#else
            return new[] { new MessageAction("好的") };
#endif
        }
        if (type == "login_kickout") {
            return new[] {
                new MessageAction("关闭", DisconnectCloseClick),
                new MessageAction("重新登陆", ReturnToLoginClick),
            };
        }
        if (type == "disconnect") {
            return new[] {
                new MessageAction("关闭", DisconnectCloseClick),
                new MessageAction("重连", ReturnToLoginClick),
            };
        }
        if (type == "logout_confirm") {
            return new[] {
                new MessageAction("否"),
                new MessageAction("是", ReturnToLoginClick),
            };
        }
        return new[] { new MessageAction("好的") };
    }

    private void BindActions(IReadOnlyList<MessageAction> actions) {
        int count = actions != null ? actions.Count : 0;
        if (count <= 0) {
            currentActions = new[] { new MessageAction("好的") };
            count = 1;
        } else {
            currentActions = new MessageAction[count];
            for (int i = 0; i < count; i++) {
                currentActions[i] = actions[i];
            }
        }

        activeSlots = ResolveSlots(count);
        for (int i = 0; i < activeSlots.Length; i++) {
            int index = i;
            ButtonSlot slot = activeSlots[i];
            if (slot.Text != null) {
                slot.Text.text = currentActions[i].Label;
            }
            slot.Button.onClick.RemoveAllListeners();
            slot.Button.onClick.AddListener(() => HandleButton(index));
            slot.Button.interactable = true;
        }
        WireNavigation();
    }

    private ButtonSlot[] ResolveSlots(int count) {
        SetTemplateActive(YesButton, false);
        SetTemplateActive(BackButton, false);
        for (int i = 0; i < extraSlots.Count; i++) {
            extraSlots[i].Button.gameObject.SetActive(false);
        }

        if (count <= 0) {
            return Array.Empty<ButtonSlot>();
        }

        var slots = new ButtonSlot[count];
        if (count == 1) {
            SetTemplateActive(YesButton, true);
            slots[0] = new ButtonSlot(YesButton, YesButtonText);
            return slots;
        }

        SetTemplateActive(BackButton, true);
        SetTemplateActive(YesButton, true);
        slots[0] = new ButtonSlot(BackButton, BackButtonText);
        slots[count - 1] = new ButtonSlot(YesButton, YesButtonText);
        EnsureExtraSlots(count - 2);
        for (int i = 0; i < count - 2; i++) {
            extraSlots[i].Button.gameObject.SetActive(true);
            slots[i + 1] = extraSlots[i];
        }
        return slots;
    }

    private void EnsureExtraSlots(int extraCount) {
        while (extraSlots.Count < extraCount) {
            Button clone = Instantiate(YesButton, YesButton.transform.parent);
            clone.name = "MessageButtonExtra";
            clone.onClick.RemoveAllListeners();
            extraSlots.Add(new ButtonSlot(clone, clone.GetComponentInChildren<TMP_Text>(true)));
        }
    }

    private static void SetTemplateActive(Button button, bool active) {
        if (button != null) {
            button.gameObject.SetActive(active);
        }
    }

    private void WireNavigation() {
        int count = activeSlots.Length;
        for (int i = 0; i < count; i++) {
            var nav = new Navigation { mode = Navigation.Mode.Explicit };
            if (count == 1) {
                nav.selectOnLeft = nav.selectOnRight = nav.selectOnUp = nav.selectOnDown = activeSlots[0].Button;
            } else {
                Button previous = activeSlots[(i - 1 + count) % count].Button;
                Button next = activeSlots[(i + 1) % count].Button;
                nav.selectOnLeft = nav.selectOnUp = previous;
                nav.selectOnRight = nav.selectOnDown = next;
            }
            activeSlots[i].Button.navigation = nav;
        }
    }

    private void HandleButton(int index) {
        if (closing || index < 0 || index >= currentActions.Length) {
            return;
        }
        Action click = currentActions[index].Click;
        if (currentActions[index].ClosePanel) {
            CloseMessage();
        }
        click?.Invoke();
    }

    private void LayoutMessage() {
        var rect = (RectTransform)transform;
        lastAvailableSize = AvailableCanvasSize();
        // MessagePos is a 100 x 100 positioning marker, not the screen boundary.
        // Short notices retain a stable width; only genuinely small canvases override the minimum.
        float width = Mathf.Min(Mathf.Max(minimumWidth, preferredWidth), Mathf.Max(1, lastAvailableSize.x - ScreenMargin * 2));
        float horizontalPadding = width < 480 ? 24 : 40;
        float bodyWidth = Mathf.Max(1, width - horizontalPadding * 2);
        HeaderText.fontSize = 32;
        HeaderText.enableAutoSizing = true;
        HeaderText.fontSizeMin = 22;
        HeaderText.fontSizeMax = 32;
        HeaderText.textWrappingMode = TextWrappingModes.NoWrap;
        HeaderText.alignment = TextAlignmentOptions.Center;
        HeaderText.margin = Vector4.zero;
        ContentText.fontSize = 24;
        ContentText.enableAutoSizing = false;
        ContentText.textWrappingMode = TextWrappingModes.Normal;
        // TMP centers each explicit/wrapped line, not merely the containing block.
        ContentText.alignment = bodyAlignment;
        ContentText.margin = Vector4.zero;
        ContentText.lineSpacing = 6;
        ContentText.overflowMode = TextOverflowModes.Overflow;
        float maximumBodyHeight = Mathf.Max(32, lastAvailableSize.y - ScreenMargin * 2 - BodyTop - BodyBottom);
        float preferredHeight = ContentText.GetPreferredValues(ContentText.text, bodyWidth, Mathf.Infinity).y + 8;
        float bodyHeight = Mathf.Min(Mathf.Max(MinimumBodyHeight, preferredHeight), maximumBodyHeight);
        bool overflow = preferredHeight > bodyHeight + .5f;
        if (overflow) preferredHeight = ContentText.GetPreferredValues(ContentText.text, Mathf.Max(1, bodyWidth - 20), Mathf.Infinity).y + 8;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(width, bodyHeight + BodyTop + BodyBottom);
        PlaceMessageRect(HeaderText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(.5f, 1), new Vector2(0, -24), new Vector2(-horizontalPadding * 2, 44));
        PlaceMessageRect(bodyViewport, new Vector2(0, 1), new Vector2(1, 1), new Vector2(.5f, 1), new Vector2(0, -BodyTop), new Vector2(-horizontalPadding * 2, bodyHeight));
        PlaceMessageRect(ContentText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(.5f, 1), new Vector2(overflow ? -10 : 0, 0), new Vector2(overflow ? -20 : 0, overflow ? preferredHeight : bodyHeight));
        bodyScroll.vertical = overflow;
        bodyScrollbar.gameObject.SetActive(overflow);
        bodyScroll.StopMovement();
        bodyScroll.verticalNormalizedPosition = 1;
        LayoutButtons(width, horizontalPadding);
    }

    private void LayoutButtons(float width, float horizontalPadding) {
        int count = activeSlots.Length;
        float available = Mathf.Max(1, width - horizontalPadding * 2);
        float gap = 20;
        float buttonWidth = count <= 1
            ? Mathf.Min(220, available)
            : Mathf.Min(190, (available - gap * (count - 1)) / Mathf.Max(1, count));
        float total = count <= 0 ? 0 : buttonWidth * count + gap * Mathf.Max(0, count - 1);
        float start = -total * 0.5f;
        for (int i = 0; i < count; i++) {
            float x = start + buttonWidth * 0.5f + i * (buttonWidth + gap);
            PlaceMessageRect((RectTransform)activeSlots[i].Button.transform,
                new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(.5f, 0),
                new Vector2(x, 24), new Vector2(buttonWidth, 52));
            TMP_Text text = activeSlots[i].Text;
            if (text == null) {
                continue;
            }
            text.enableAutoSizing = true;
            text.fontSize = text.fontSizeMax = 24;
            text.fontSizeMin = 16;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.alignment = TextAlignmentOptions.Center;
            text.margin = new Vector4(12, 0, 12, 0);
        }
    }

    private Vector2 AvailableCanvasSize() {
        var canvas = GetComponentInParent<Canvas>();
        var parent = transform.parent as RectTransform;
        if (canvas != null && parent != null) {
            var root = (RectTransform)canvas.rootCanvas.transform;
            root.GetWorldCorners(canvasCorners);
            // Convert into the marker's units, independently of the dialog's popup animation scale.
            Vector3 min = parent.InverseTransformPoint(canvasCorners[0]);
            Vector3 max = min;
            for (int i = 1; i < canvasCorners.Length; i++) {
                Vector3 corner = parent.InverseTransformPoint(canvasCorners[i]);
                min = Vector3.Min(min, corner); max = Vector3.Max(max, corner);
            }
            if (max.x > min.x && max.y > min.y) return new Vector2(max.x - min.x, max.y - min.y);
        }
        return new Vector2(Mathf.Max(minimumWidth, preferredWidth) + ScreenMargin * 2, 1080);
    }

    private void LateUpdate() {
        if (hasMessage && !closing && (AvailableCanvasSize() - lastAvailableSize).sqrMagnitude > 1)
            LayoutMessage();
    }

#if UNITY_EDITOR
    public void BakeFixedBody() { EnsureBodyViewport(); LayoutMessage(); }

    private void EnsureBodyViewport() {
        if (bodyViewport != null) return;
        var go = new GameObject("MessageBody", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
        go.layer = gameObject.layer;
        bodyViewport = (RectTransform)go.transform;
        bodyViewport.SetParent(transform, false);
        bodyViewport.SetSiblingIndex(ContentText.transform.GetSiblingIndex());
        go.GetComponent<Image>().color = Color.clear;
        ContentText.transform.SetParent(bodyViewport, false);
        bodyScroll = go.GetComponent<ScrollRect>();
        bodyScroll.viewport = bodyViewport;
        bodyScroll.content = ContentText.rectTransform;
        bodyScroll.horizontal = false;
        bodyScroll.movementType = ScrollRect.MovementType.Clamped;
        bodyScroll.scrollSensitivity = 40;
        var rail = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        rail.layer = go.layer;
        var railRect = (RectTransform)rail.transform;
        railRect.SetParent(bodyViewport, false);
        railRect.anchorMin = new Vector2(1, 0); railRect.anchorMax = Vector2.one;
        railRect.offsetMin = new Vector2(-6, 4); railRect.offsetMax = new Vector2(0, -4);
        rail.GetComponent<Image>().color = new Color(1, 1, 1, .12f);
        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.layer = go.layer;
        var handleRect = (RectTransform)handle.transform;
        handleRect.SetParent(railRect, false);
        handleRect.anchorMin = Vector2.zero; handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = handleRect.offsetMax = Vector2.zero;
        handle.GetComponent<Image>().color = new Color(1, 1, 1, .65f);
        bodyScrollbar = rail.GetComponent<Scrollbar>();
        bodyScrollbar.direction = Scrollbar.Direction.BottomToTop;
        bodyScrollbar.handleRect = handleRect;
        bodyScrollbar.targetGraphic = handle.GetComponent<Image>();
        bodyScrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
        bodyScroll.verticalScrollbar = bodyScrollbar;
    }
#endif


    private static void PlaceMessageRect(RectTransform rect, Vector2 min, Vector2 max, Vector2 pivot, Vector2 position, Vector2 size) {
        // The legacy prefab scaled both buttons to 0.43; use the actual layout dimensions.
        rect.localScale = Vector3.one;
        rect.anchorMin = min; rect.anchorMax = max; rect.pivot = pivot;
        rect.anchoredPosition = position; rect.sizeDelta = size;
    }

    public void CloseMessage() {
        if (closing) return;
        closing = true;
        for (int i = 0; i < activeSlots.Length; i++) {
            if (activeSlots[i].Button != null) {
                activeSlots[i].Button.interactable = false;
            }
        }
        GameObject owner = modalOwner != null ? modalOwner : gameObject;
        if (popupTransition != null) {
            popupTransition.Hide(() => Destroy(owner));
        } else {
            Destroy(owner);
        }
    }

    public void ReconnectClick(string type) {
        if (type == "yes") {
            NetworkManager.Instance.ReconnectResponse(true);
        } else if (type == "no") {
            NetworkManager.Instance.ReconnectResponse(false);
        }
    }

    private void ConfirmAbandonReconnect() {
        if (closing || abandonConfirmOpen || NotificationManager.Instance == null) {
            return;
        }
        abandonConfirmOpen = true;
        MessagePrefab reconnectPanel = this;
        NotificationManager.Instance.ShowModal(
            "确认放弃对局",
            "放弃后将退出当前对局，无法再重连到这场游戏。此操作不可撤销，您确定吗？",
            new MessageAction("再想想", () => reconnectPanel.abandonConfirmOpen = false),
            new MessageAction("确定放弃", () => {
                reconnectPanel.abandonConfirmOpen = false;
                reconnectPanel.CloseMessage();
                ReconnectClick("no");
            })
        );
    }

    private void ReturnToLoginClick() {
        AppSession.ReturnToLogin();
    }

    private void DisconnectCloseClick() {
        AppSession.QuitOrReconnectOnDisconnectClose();
    }

    private void OpenMobileDownloadPage() {
        Application.OpenURL(ConfigManager.mobileDownloadUrl);
    }
}
