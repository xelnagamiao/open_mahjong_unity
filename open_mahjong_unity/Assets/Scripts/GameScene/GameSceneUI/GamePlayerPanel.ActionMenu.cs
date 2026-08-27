using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public partial class GamePlayerPanel {
    const string InfoSpritePath = "Icon/iconmonstr.com/iconmonstr-eye-3-240";
    const string StickerIconPath = "Icon/iconmonstr.com/iconmonstr-x-mark-square-lined-240";
    const string StickerMutedIconPath = "Icon/iconmonstr.com/iconmonstr-x-mark-square-filled-240";
    const string MenuFontResource = "font/Chinese/AlibabaPuHuiTi/AlibabaPuHuiTi-3-55-Regular SDF";

    static readonly Color MenuBg = new Color(0.08f, 0.08f, 0.1f, 0.94f);
    static readonly Color ButtonBg = new Color(0.18f, 0.18f, 0.22f, 0.96f);
    static readonly Color ButtonMutedBg = new Color(0.45f, 0.16f, 0.16f, 0.96f);
    static readonly Color LabelColor = new Color(1f, 1f, 0.95f, 1f);

    int _boundUserId;
    string _boundState;
    string _boundPosition;
    int _ignoreClickOutsideFrame = -1;

    public void HideActionMenu() {
        if (actionMenu != null) actionMenu.SetActive(false);
    }

    /// <summary>对局内点头像/面板时弹出信息+屏蔽菜单；返回 true 表示已处理。</summary>
    public bool TryHandleProfileClick() {
        if (_boundState != "gamestate") return false;
        EnsureActionMenu();
        if (actionMenu == null) return false;

        bool willShow = !actionMenu.activeSelf;
        GameCanvas.Instance?.HideAllPlayerActionMenus();
        if (!willShow) return true;

        RefreshMuteVisual();
        if (infoButton != null) infoButton.gameObject.SetActive(true);
        if (muteButton != null) muteButton.gameObject.SetActive(_boundUserId >= 10);
        actionMenu.SetActive(true);
        actionMenu.transform.SetAsLastSibling();
        RectTransform menuRt = actionMenu.transform as RectTransform;
        if (menuRt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(menuRt);
        _ignoreClickOutsideFrame = Time.frameCount;
        return true;
    }

    public bool IsStickerMutedForBoundPlayer() {
        return NormalGameStateManager.Instance != null
            && NormalGameStateManager.Instance.IsStickerMuted(_boundUserId);
    }

    public void EnsureActionMenu() {
        Transform found = transform.Find(ActionMenuName);
        if (found != null) BindExistingActionMenu(found);
        else if (actionMenu == null) CreateActionMenu();
        LoadMuteSpritesIfNeeded();
        ApplyMuteIconsToButton();
        BindActionMenuButtons();
        RefreshMuteVisual();
    }

    void BindActionMenuContext(int userId, string state, string position) {
        _boundUserId = userId;
        _boundState = state;
        _boundPosition = position;
        EnsureActionMenu();
        HideActionMenu();
        RefreshMuteVisual();
    }

    void OnInfoClicked() {
        HideActionMenu();
        ProfileOnClick.OpenPlayerInfo(_boundUserId);
    }

    void OnMuteClicked() {
        var mgr = NormalGameStateManager.Instance;
        if (mgr == null || _boundUserId < 10) return;
        bool muted = mgr.ToggleStickerMute(_boundUserId);
        if (muted) ClearSticker();
        RefreshMuteVisual();
        string name = playerNameText != null ? playerNameText.text : "该玩家";
        NotificationManager.Instance?.ShowTip(
            "system",
            true,
            muted ? $"已屏蔽 {name} 的表情（本场有效）" : $"已恢复显示 {name} 的表情");
    }

    void RefreshMuteVisual() {
        if (infoButton != null) infoButton.gameObject.SetActive(true);
        if (muteButton != null) muteButton.gameObject.SetActive(_boundUserId < 10 ? false : true);

        bool muted = IsStickerMutedForBoundPlayer();
        ApplyMuteIconsToButton();
        if (muteButtonLabel != null) muteButtonLabel.text = muted ? "显示" : "屏蔽";
        Image muteBg = muteButton != null ? muteButton.GetComponent<Image>() : null;
        if (muteBg != null) muteBg.color = muted ? ButtonMutedBg : ButtonBg;
    }

    void ApplyMuteIconsToButton() {
        if (muteButtonImage == null) return;
        bool muted = IsStickerMutedForBoundPlayer();
        Sprite sprite = muted ? stickerMutedSprite : stickerVisibleSprite;
        if (sprite != null) muteButtonImage.sprite = sprite;
    }

    void BindActionMenuButtons() {
        if (infoButton != null) {
            infoButton.onClick.RemoveListener(OnInfoClicked);
            infoButton.onClick.AddListener(OnInfoClicked);
        }
        if (muteButton != null) {
            muteButton.onClick.RemoveListener(OnMuteClicked);
            muteButton.onClick.AddListener(OnMuteClicked);
        }
    }

    void BindExistingActionMenu(Transform root) {
        actionMenu = root.gameObject;
        Transform info = root.Find("InfoButton");
        Transform mute = root.Find("MuteButton");
        if (info != null) {
            infoButton = info.GetComponent<Button>();
            info.gameObject.SetActive(true);
        }
        if (mute != null) {
            muteButton = mute.GetComponent<Button>();
            mute.gameObject.SetActive(true);
            Transform icon = mute.Find("Icon");
            if (icon != null) muteButtonImage = icon.GetComponent<Image>();
            Transform label = mute.Find("Label");
            if (label != null) muteButtonLabel = label.GetComponent<TMP_Text>();
        }
        ApplyActionMenuPosition();
    }

    void CreateActionMenu() {
        LoadMuteSpritesIfNeeded();
        GameObject root = new GameObject(ActionMenuName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        root.layer = gameObject.layer;
        root.transform.SetParent(transform, false);
        Image bg = root.GetComponent<Image>();
        bg.color = MenuBg;
        bg.raycastTarget = true;

        HorizontalLayoutGroup layout = root.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = root.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        actionMenu = root;
        infoButton = CreateMenuButton(root.transform, "InfoButton", "信息", Resources.Load<Sprite>(InfoSpritePath));
        muteButton = CreateMenuButton(root.transform, "MuteButton", "屏蔽", stickerVisibleSprite);
        Transform muteIcon = muteButton.transform.Find("Icon");
        if (muteIcon != null) muteButtonImage = muteIcon.GetComponent<Image>();
        Transform muteLabel = muteButton.transform.Find("Label");
        if (muteLabel != null) muteButtonLabel = muteLabel.GetComponent<TMP_Text>();
        ApplyActionMenuPosition();
        HideActionMenu();
    }

    Button CreateMenuButton(Transform parent, string name, string label, Sprite iconSprite) {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        go.layer = gameObject.layer;
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(56f, 68f);

        Image bg = go.GetComponent<Image>();
        bg.color = ButtonBg;
        bg.raycastTarget = true;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = bg;
        button.transition = Selectable.Transition.ColorTint;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        VerticalLayoutGroup vlayout = go.GetComponent<VerticalLayoutGroup>();
        vlayout.padding = new RectOffset(4, 4, 4, 2);
        vlayout.spacing = 2f;
        vlayout.childAlignment = TextAnchor.MiddleCenter;
        vlayout.childControlWidth = false;
        vlayout.childControlHeight = false;
        vlayout.childForceExpandWidth = true;
        vlayout.childForceExpandHeight = false;

        LayoutElement le = go.GetComponent<LayoutElement>();
        le.preferredWidth = 56f;
        le.preferredHeight = 68f;
        le.minWidth = 56f;
        le.minHeight = 68f;

        GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        iconGo.layer = gameObject.layer;
        iconGo.transform.SetParent(go.transform, false);
        Image icon = iconGo.GetComponent<Image>();
        icon.sprite = iconSprite;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.color = Color.white;
        LayoutElement iconLe = iconGo.GetComponent<LayoutElement>();
        iconLe.preferredWidth = 28f;
        iconLe.preferredHeight = 28f;
        iconGo.GetComponent<RectTransform>().sizeDelta = new Vector2(28f, 28f);

        GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        labelGo.layer = gameObject.layer;
        labelGo.transform.SetParent(go.transform, false);
        TextMeshProUGUI tmp = labelGo.GetComponent<TextMeshProUGUI>();
        TMP_FontAsset font = Resources.Load<TMP_FontAsset>(MenuFontResource);
        if (font != null) tmp.font = font;
        tmp.text = label;
        tmp.fontSize = 16f;
        tmp.color = LabelColor;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        LayoutElement labelLe = labelGo.GetComponent<LayoutElement>();
        labelLe.preferredHeight = 20f;
        labelGo.GetComponent<RectTransform>().sizeDelta = new Vector2(52f, 20f);
        return button;
    }

    void ApplyActionMenuPosition() {
        if (actionMenu == null) return;
        RectTransform rt = actionMenu.transform as RectTransform;
        if (rt == null) return;
        bool placeLeft = gameObject.name != "#SelfPlayer";
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(placeLeft ? 1f : 0f, 0.5f);
        rt.anchoredPosition = new Vector2(placeLeft ? -58f : 58f, 54f);
    }

    void LoadMuteSpritesIfNeeded() {
        stickerVisibleSprite = Resources.Load<Sprite>(StickerIconPath);
        stickerMutedSprite = Resources.Load<Sprite>(StickerMutedIconPath);
    }

    void Update() {
        if (actionMenu == null || !actionMenu.activeSelf) return;
        if (Time.frameCount <= _ignoreClickOutsideFrame) return;
        if (!Input.GetMouseButtonDown(0)) return;
        if (IsPointerOverActionMenuOrAvatar()) return;
        HideActionMenu();
    }

    bool IsPointerOverActionMenuOrAvatar() {
        if (EventSystem.current == null) return false;
        PointerEventData eventData = new PointerEventData(EventSystem.current) {
            position = Input.mousePosition
        };
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        for (int i = 0; i < results.Count; i++) {
            GameObject hit = results[i].gameObject;
            if (hit == null) continue;
            Transform hitTransform = hit.transform;
            if (actionMenu != null && hitTransform.IsChildOf(actionMenu.transform)) return true;
            if (playerProfilePicture != null && hitTransform.IsChildOf(playerProfilePicture.transform)) return true;
            if (playerProfileEdgePicture != null && hitTransform.IsChildOf(playerProfileEdgePicture.transform)) return true;
        }
        return false;
    }
}

/// <summary>把头像边框点击转到对局操作菜单，避免点到边框时仍直接开资料。</summary>
public sealed class PlayerPanelClickRelay : MonoBehaviour, IPointerClickHandler {
    public void OnPointerClick(PointerEventData eventData) {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        GamePlayerPanel panel = GetComponentInParent<GamePlayerPanel>();
        if (panel != null && panel.TryHandleProfileClick()) return;
    }
}
