using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// GameCanvas 对局表情包：面板开关、发送与接收路由。
/// 未在 Inspector 绑定时会在运行时自动创建按钮与 3×3 面板。
/// </summary>
public partial class GameCanvas {
    [Header("表情包")]
    [SerializeField] private Button openStickerPanelButton;
    [SerializeField] private GameObject stickerPanel;
    [SerializeField] private Button[] stickerButtons = new Button[9];

    private const string DefaultStickerPack = "turtle";
    private const float StickerSendCooldownSeconds = 7f;
    private static readonly Color StickerButtonLockedColor = new Color(0.35f, 0.35f, 0.35f, 1f);
    private bool _stickerUiBuilt;
    private bool _hideStickerButtonForRecord;
    private bool _stickerSendLocked;
    private Coroutine _stickerCooldownCoroutine;

    private void InitializeStickerUi() {
        if (_stickerUiBuilt) return;
        _stickerUiBuilt = true;
        EnsureStickerUiCreated();
        BindStickerUi();
        HideStickerPanel();
        ApplyStickerButtonVisibility();
    }

    private void EnsureStickerUiCreated() {
        RectTransform canvasRect = transform as RectTransform;
        if (canvasRect == null) return;

        if (openStickerPanelButton == null) {
            GameObject btnGo = new GameObject("OpenStickerPanelButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(canvasRect, false);
            RectTransform btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(1f, 0f);
            btnRt.anchorMax = new Vector2(1f, 0f);
            btnRt.pivot = new Vector2(1f, 0f);
            btnRt.anchoredPosition = new Vector2(-20f, 120f);
            btnRt.sizeDelta = new Vector2(72f, 36f);
            Image btnImg = btnGo.GetComponent<Image>();
            btnImg.color = new Color(0.2f, 0.2f, 0.25f, 0.85f);
            openStickerPanelButton = btnGo.GetComponent<Button>();
            GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(btnGo.transform, false);
            RectTransform textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            TMP_Text label = textGo.GetComponent<TextMeshProUGUI>();
            label.text = "表情";
            label.fontSize = 18;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
        }

        if (stickerPanel == null) {
            stickerPanel = new GameObject("StickerPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            stickerPanel.transform.SetParent(canvasRect, false);
            RectTransform panelRt = stickerPanel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(1f, 0f);
            panelRt.anchorMax = new Vector2(1f, 0f);
            panelRt.pivot = new Vector2(1f, 0f);
            panelRt.anchoredPosition = new Vector2(-20f, 165f);
            panelRt.sizeDelta = new Vector2(240f, 240f);
            Image panelBg = stickerPanel.GetComponent<Image>();
            panelBg.color = new Color(0.12f, 0.12f, 0.15f, 0.92f);

            GridLayoutGroup grid = stickerPanel.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(72f, 72f);
            grid.spacing = new Vector2(6f, 6f);
            grid.padding = new RectOffset(8, 8, 8, 8);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;

            if (stickerButtons == null || stickerButtons.Length != 9) {
                stickerButtons = new Button[9];
            }

            for (int i = 0; i < 9; i++) {
                int stickerId = i + 1;
                GameObject itemGo = new GameObject($"StickerBtn_{stickerId}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                itemGo.transform.SetParent(stickerPanel.transform, false);
                Image itemImg = itemGo.GetComponent<Image>();
                itemImg.color = Color.white;
                Sprite preview = LoadStickerSprite(DefaultStickerPack, stickerId);
                if (preview != null) itemImg.sprite = preview;
                stickerButtons[i] = itemGo.GetComponent<Button>();
            }
        }
    }

    private void BindStickerUi() {
        if (openStickerPanelButton != null) {
            openStickerPanelButton.onClick.RemoveAllListeners();
            openStickerPanelButton.onClick.AddListener(ToggleStickerPanel);
        }
        if (stickerButtons == null) return;
        for (int i = 0; i < stickerButtons.Length; i++) {
            if (stickerButtons[i] == null) continue;
            int stickerId = i + 1;
            stickerButtons[i].onClick.RemoveAllListeners();
            stickerButtons[i].onClick.AddListener(() => OnStickerClicked(stickerId));
        }
    }

    public void SetStickerUiForRecordMode(bool isRecord) {
        _hideStickerButtonForRecord = isRecord;
        ApplyStickerButtonVisibility();
        HideStickerPanel();
    }

    private void ApplyStickerButtonVisibility() {
        if (openStickerPanelButton != null) {
            openStickerPanelButton.gameObject.SetActive(!_hideStickerButtonForRecord);
        }
    }

    public void ToggleStickerPanel() {
        if (stickerPanel == null) return;
        stickerPanel.SetActive(!stickerPanel.activeSelf);
    }

    public void HideStickerPanel() {
        if (stickerPanel != null) stickerPanel.SetActive(false);
    }

    private void OnStickerClicked(int stickerId) {
        if (NormalGameStateManager.Instance != null && NormalGameStateManager.Instance.IsRealtimeSpectator) {
            return;
        }
        if (_stickerSendLocked) {
            return;
        }
        string sticker = $"{DefaultStickerPack}/{stickerId}";
        GameStateNetworkManager.Instance?.SendSticker(sticker);
        HideStickerPanel();
        BeginStickerSendCooldown();
    }

    private void BeginStickerSendCooldown() {
        if (_stickerCooldownCoroutine != null) {
            StopCoroutine(_stickerCooldownCoroutine);
        }
        _stickerSendLocked = true;
        SetStickerButtonsLocked(true);
        _stickerCooldownCoroutine = StartCoroutine(StickerSendCooldown());
    }

    private IEnumerator StickerSendCooldown() {
        yield return new WaitForSecondsRealtime(StickerSendCooldownSeconds);
        _stickerSendLocked = false;
        SetStickerButtonsLocked(false);
        _stickerCooldownCoroutine = null;
    }

    private void SetStickerButtonsLocked(bool locked) {
        if (stickerButtons == null) return;
        for (int i = 0; i < stickerButtons.Length; i++) {
            Button btn = stickerButtons[i];
            if (btn == null) continue;
            btn.interactable = !locked;
            Image img = btn.GetComponent<Image>();
            if (img != null) {
                img.color = locked ? StickerButtonLockedColor : Color.white;
            }
        }
    }

    private void ResetStickerSendCooldown() {
        if (_stickerCooldownCoroutine != null) {
            StopCoroutine(_stickerCooldownCoroutine);
            _stickerCooldownCoroutine = null;
        }
        _stickerSendLocked = false;
        SetStickerButtonsLocked(false);
    }

    public void ShowSticker(int playerIndex, string sticker) {
        if (string.IsNullOrEmpty(sticker)) return;
        var mgr = NormalGameStateManager.Instance;
        if (mgr == null || !mgr.indexToPosition.TryGetValue(playerIndex, out string position)) {
            Debug.LogWarning($"ShowSticker: 未知 player_index={playerIndex}");
            return;
        }
        GamePlayerPanel panel = GetPlayerPanelByPosition(position);
        panel?.ShowSticker(sticker);
    }

    private GamePlayerPanel GetPlayerPanelByPosition(string position) {
        switch (position) {
            case "self": return playerSelfPanel;
            case "left": return playerLeftPanel;
            case "top": return playerTopPanel;
            case "right": return playerRightPanel;
            default: return null;
        }
    }

    public void ClearAllStickers() {
        playerSelfPanel?.ClearSticker();
        playerLeftPanel?.ClearSticker();
        playerTopPanel?.ClearSticker();
        playerRightPanel?.ClearSticker();
        ResetStickerSendCooldown();
    }

    private static Sprite LoadStickerSprite(string pack, int id) {
        return Resources.Load<Sprite>($"image/sticker/{pack}/{id}");
    }
}
