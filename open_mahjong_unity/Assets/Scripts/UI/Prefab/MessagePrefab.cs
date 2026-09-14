using UnityEngine;
using TMPro;
using UnityEngine.UI;

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
    private RectTransform bodyViewport;
    private ScrollRect bodyScroll;
    private Scrollbar bodyScrollbar;
    private Vector2 lastAvailableSize;
    private bool hasMessage;

    private System.Action confirmationAction;
    private GameObject modalOwner;
    private bool closing;

    private void Awake() {
        if (popupTransition == null) {
            popupTransition = GetComponent<PanelPopupTransition>();
        }
    }

    public void ShowMessage(string header, string content, string type = "") {
        closing = false;
        confirmationAction = null;
        YesButton.interactable = BackButton.interactable = true;
        HeaderText.text = header;
        ContentText.text = content;
        hasMessage = true;
        LayoutMessage();

        YesButton.onClick.RemoveAllListeners();
        BackButton.onClick.RemoveAllListeners();

        if (type == "reconnect_ask") {
            YesButtonText.text = "重新连接";
            BackButtonText.text = "放弃比赛";
            YesButton.onClick.AddListener(() => ReconnectClick("yes"));
            BackButton.onClick.AddListener(() => ReconnectClick("no"));
        } else if (type == "error_version") {
#if UNITY_ANDROID && !UNITY_EDITOR
            YesButtonText.text = "去下载";
            BackButtonText.text = "关闭";
            YesButton.onClick.AddListener(OpenMobileDownloadPage);
            BackButton.onClick.AddListener(CloseMessage);
#else
            YesButtonText.text = "好的";
            BackButtonText.text = "关闭";
            YesButton.onClick.AddListener(CloseMessage);
            BackButton.onClick.AddListener(CloseMessage);
#endif
        } else if (type == "login_kickout") {
            YesButtonText.text = "重新登陆";
            BackButtonText.text = "关闭";
            YesButton.onClick.AddListener(ReturnToLoginClick);
            BackButton.onClick.AddListener(DisconnectCloseClick);
        } else if (type == "disconnect") {
            YesButtonText.text = "重连";
            BackButtonText.text = "关闭";
            YesButton.onClick.AddListener(ReturnToLoginClick);
            BackButton.onClick.AddListener(DisconnectCloseClick);
        } else if (type == "logout_confirm") {
            YesButtonText.text = "是";
            BackButtonText.text = "否";
            YesButton.onClick.AddListener(ReturnToLoginClick);
            BackButton.onClick.AddListener(CloseMessage);
        } else {
            YesButtonText.text = "好的";
            BackButtonText.text = "关闭";
            YesButton.onClick.AddListener(CloseMessage);
            BackButton.onClick.AddListener(CloseMessage);
        }

        if (popupTransition != null) {
            popupTransition.Show();
        } else {
            gameObject.SetActive(true);
        }
    }

    public void SetModalOwner(GameObject owner) => modalOwner = owner;

    public void ShowConfirmation(string header, string content, System.Action onConfirm,
        string confirmText = "确定", string cancelText = "取消") {
        ShowMessage(header, content);
        confirmationAction = onConfirm;
        YesButtonText.text = confirmText;
        BackButtonText.text = cancelText;
        YesButton.onClick.RemoveAllListeners();
        BackButton.onClick.RemoveAllListeners();
        YesButton.onClick.AddListener(ConfirmMessage);
        BackButton.onClick.AddListener(CloseMessage);
        BackButton.navigation = new Navigation { mode = Navigation.Mode.Explicit,
            selectOnLeft = YesButton, selectOnRight = YesButton, selectOnUp = YesButton, selectOnDown = YesButton };
        YesButton.navigation = new Navigation { mode = Navigation.Mode.Explicit,
            selectOnLeft = BackButton, selectOnRight = BackButton, selectOnUp = BackButton, selectOnDown = BackButton };
        // Keyboard submission starts on the non-destructive action.
        if (UnityEngine.EventSystems.EventSystem.current != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(BackButton.gameObject);
    }

    private void ConfirmMessage() {
        if (closing) return;
        System.Action action = confirmationAction;
        CloseMessage();
        action?.Invoke();
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
        ContentText.alignment = TextAlignmentOptions.Center;
        ContentText.margin = Vector4.zero;
        ContentText.lineSpacing = 6;
        ContentText.overflowMode = TextOverflowModes.Overflow;
        float maximumBodyHeight = Mathf.Max(32, lastAvailableSize.y - ScreenMargin * 2 - BodyTop - BodyBottom);
        float preferredHeight = ContentText.GetPreferredValues(ContentText.text, bodyWidth, Mathf.Infinity).y + 8;
        float bodyHeight = Mathf.Min(Mathf.Max(MinimumBodyHeight, preferredHeight), maximumBodyHeight);
        bool overflow = preferredHeight > bodyHeight + .5f;
        if (overflow) preferredHeight = ContentText.GetPreferredValues(ContentText.text, Mathf.Max(1, bodyWidth - 20), Mathf.Infinity).y + 8;
        EnsureBodyViewport();
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
        float buttonWidth = Mathf.Min(190, (width - horizontalPadding * 2 - 20) / 2);
        PlaceMessageRect((RectTransform)BackButton.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(1, 0), new Vector2(-10, 24), new Vector2(buttonWidth, 52));
        PlaceMessageRect((RectTransform)YesButton.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 0), new Vector2(10, 24), new Vector2(buttonWidth, 52));
        foreach (var text in new[] { YesButtonText, BackButtonText }) {
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

    private static void PlaceMessageRect(RectTransform rect, Vector2 min, Vector2 max, Vector2 pivot, Vector2 position, Vector2 size) {
        // The legacy prefab scaled both buttons to 0.43; use the actual layout dimensions.
        rect.localScale = Vector3.one;
        rect.anchorMin = min; rect.anchorMax = max; rect.pivot = pivot;
        rect.anchoredPosition = position; rect.sizeDelta = size;
    }

    public void CloseMessage() {
        if (closing) return;
        closing = true;
        confirmationAction = null;
        YesButton.interactable = BackButton.interactable = false;
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
        CloseMessage();
    }

    private void ReturnToLoginClick() {
        AppSession.ReturnToLogin();
        CloseMessage();
    }

    private void DisconnectCloseClick() {
        AppSession.QuitOrReconnectOnDisconnectClose();
        CloseMessage();
    }

    private void OpenMobileDownloadPage() {
        Application.OpenURL(ConfigManager.mobileDownloadUrl);
        CloseMessage();
    }
}
