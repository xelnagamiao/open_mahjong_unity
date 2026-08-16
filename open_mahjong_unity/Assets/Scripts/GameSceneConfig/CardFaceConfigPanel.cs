using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 场景设置「牌面」页：标准麻将可上传 zip，虹雀只读官方图。
/// 引用由场景烘焙写入，运行时只换图和切 tab。
/// </summary>
public class CardFaceConfigPanel : MonoBehaviour {
    public static CardFaceConfigPanel Instance { get; private set; }

    public const string FormatHelp =
        "上传格式（仅标准麻将）\n"
        + "• 一个 .zip，可选带 manifest.json（format=om-tilepack，family=standard）\n"
        + "• 必须同时包含两个文件夹（缺一不可）：\n"
        + "  手牌牌面/{id}.png  或 hand/{id}.png   手牌牌面，建议 272×389，透明花纹，直接叠加\n"
        + "  3D牌面/{id}.png    或 table/{id}.png  3D 牌面，按原图比例缩小后居中贴进 220×366\n"
        + "• 万 11–19，条 21–29，筒 31–39\n"
        + "• 字 41–47（东南西北中发白）\n"
        + "• 花 51–58，赤宝 105 / 205 / 305，纯白白板 2（可选）\n"
        + "• 根目录 PNG 不会当手牌\n"
        + "• 手牌牌面原样叠在「牌面背景」上，不会裁切；自定义请自行摆好位置\n"
        + "• 仅 PNG；单边 ≤1024；单张 ≤500KB；解压后 ≤20MB\n"
        + "• 缺图回退官方牌面；虹雀锁定官方 HQv3.1\n"
        + "• 透明花纹请打开「使用牌面背景」；已自带牌体的整图请选「不使用牌面背景」";

    private static readonly Color TabOn = new Color(0.28f, 0.48f, 0.92f, 1f);
    private static readonly Color TabOff = new Color(0.17f, 0.21f, 0.30f, 1f);

    [SerializeField] private Button tabStandardButton;
    [SerializeField] private Button tabHongqueButton;
    [SerializeField] private Button uploadButton;
    [SerializeField] private Button restoreButton;
    [SerializeField] private Button packFluffyButton;
    [SerializeField] private Button packHkButton;
    [SerializeField] private Button useBackgroundButton;
    [SerializeField] private Button noBackgroundButton;
    [SerializeField] private Button showHandButton;
    [SerializeField] private Button showTableButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text helpText;
    [SerializeField] private GameObject standardActions;
    [SerializeField] private GameObject standardViewActions;
    [SerializeField] private GameObject standardPreviewRoot;
    [SerializeField] private GameObject hongquePreviewRoot;

    private bool showingHongque;
    private bool showingTablePreview;
    private CardFacePreviewSlot[] standardSlots;
    private CardFacePreviewSlot[] hongqueSlots;

    private void Awake() {
        Instance = this;
        tabStandardButton.onClick.AddListener(() => SetTab(false));
        tabHongqueButton.onClick.AddListener(() => SetTab(true));
        uploadButton.onClick.AddListener(OnUploadClicked);
        restoreButton.onClick.AddListener(OnRestoreClicked);
        if (packFluffyButton != null) {
            packFluffyButton.onClick.AddListener(() => OnSelectPack(TilePackIds.PackFluffy));
        }
        if (packHkButton != null) {
            packHkButton.onClick.AddListener(() => OnSelectPack(TilePackIds.PackHkMahjong));
        }
        if (useBackgroundButton != null) {
            useBackgroundButton.onClick.AddListener(() => OnToggleBackground(true));
        }
        if (noBackgroundButton != null) {
            noBackgroundButton.onClick.AddListener(() => OnToggleBackground(false));
        }
        if (showHandButton != null) {
            showHandButton.onClick.AddListener(() => OnTogglePreview(false));
        }
        if (showTableButton != null) {
            showTableButton.onClick.AddListener(() => OnTogglePreview(true));
        }
        closeButton.onClick.AddListener(HidePanel);
        helpText.text = FormatHelp;
        standardSlots = standardPreviewRoot.GetComponentsInChildren<CardFacePreviewSlot>(true);
        hongqueSlots = hongquePreviewRoot.GetComponentsInChildren<CardFacePreviewSlot>(true);
    }

    private void OnEnable() {
        TileFaceResolver.OnPackChanged += RefreshPreview;
        RefreshPreview();
    }

    private void OnDisable() {
        TileFaceResolver.OnPackChanged -= RefreshPreview;
    }

    public void ShowPanel() {
        showingHongque = false;
        gameObject.SetActive(true);
        RefreshTabs();
        RefreshPreview();
    }

    public void HidePanel() {
        gameObject.SetActive(false);
    }

    private void SetTab(bool hongque) {
        showingHongque = hongque;
        RefreshTabs();
        RefreshPreview();
    }

    private void RefreshTabs() {
        SetTabColor(tabStandardButton, !showingHongque);
        SetTabColor(tabHongqueButton, showingHongque);
        standardActions.SetActive(!showingHongque);
        uploadButton.gameObject.SetActive(!showingHongque);
        restoreButton.gameObject.SetActive(!showingHongque);
        if (packFluffyButton != null) packFluffyButton.gameObject.SetActive(!showingHongque);
        if (packHkButton != null) packHkButton.gameObject.SetActive(!showingHongque);
        if (useBackgroundButton != null) useBackgroundButton.gameObject.SetActive(!showingHongque);
        if (noBackgroundButton != null) noBackgroundButton.gameObject.SetActive(!showingHongque);
        if (showHandButton != null) showHandButton.gameObject.SetActive(!showingHongque);
        if (showTableButton != null) showTableButton.gameObject.SetActive(!showingHongque);
        if (standardViewActions != null) standardViewActions.SetActive(!showingHongque);
        HighlightPackButtons();
    }

    private static void SetTabColor(Button button, bool on) {
        Image image = button.GetComponent<Image>();
        if (image != null) image.color = on ? TabOn : TabOff;
    }

    private void OnUploadClicked() {
        if (showingHongque) return;
        SetStatus("正在选择 zip…");
        TilePackStorage.PickZip(OnZipBytes, err => {
            if (!string.IsNullOrEmpty(err) && err != "empty") {
                SetStatus(err);
                ShowTip(err);
            }
        });
    }

    private void OnZipBytes(byte[] zipBytes) {
        TilePackImporter.Result imported = TilePackImporter.Import(zipBytes);
        if (imported == null || !imported.Success) {
            string error = imported != null ? imported.Error : "导入失败";
            SetStatus(error);
            ShowTip(error);
            return;
        }
        TileFaceResolver.ApplyImported(imported, persist: true, enableFlag: true);
        string status = $"已应用自定义牌面（{imported.HandPngs.Count} 张手牌";
        if (imported.TablePngs.Count > 0) {
            status += $"，{imported.TablePngs.Count} 张桌面";
        }
        status += "）";
        if (imported.Warnings.Count > 0) {
            status += "。" + imported.Warnings[0];
        }
        SetStatus(status);
        ShowTip("自定义牌面已应用");
        RefreshPreview();
    }

    private void OnRestoreClicked() {
        OnSelectPack(TilePackIds.PackOfficial);
    }

    private void OnSelectPack(string packId) {
        TileFaceResolver.SelectPack(packId);
        HighlightPackButtons();
        RefreshPreview();
        if (packId == TilePackIds.PackOfficial) {
            ShowTip("已切换官方标准牌面");
        }
        else if (packId == TilePackIds.PackFluffy) {
            ShowTip("已切换 FluffyStuff 牌面");
        }
        else if (packId == TilePackIds.PackHkMahjong) {
            ShowTip("已切换香港麻将牌面");
        }
    }

    private void HighlightPackButtons() {
        string packId = ConfigManager.Instance != null
            ? ConfigManager.Instance.StandardTilePackId
            : TilePackIds.PackOfficial;
        SetTabColor(restoreButton, packId == TilePackIds.PackOfficial);
        SetTabColor(uploadButton, packId == TilePackIds.PackCustom);
        if (packFluffyButton != null) {
            SetTabColor(packFluffyButton, packId == TilePackIds.PackFluffy);
        }
        if (packHkButton != null) {
            SetTabColor(packHkButton, packId == TilePackIds.PackHkMahjong);
        }
        bool useBg = ConfigManager.Instance != null && ConfigManager.Instance.UseHandFaceBackground;
        if (useBackgroundButton != null) SetTabColor(useBackgroundButton, useBg);
        if (noBackgroundButton != null) SetTabColor(noBackgroundButton, !useBg);
        if (showHandButton != null) SetTabColor(showHandButton, !showingTablePreview);
        if (showTableButton != null) SetTabColor(showTableButton, showingTablePreview);
    }

    private void OnToggleBackground(bool enabled) {
        TileFaceResolver.SetUseHandFaceBackground(enabled);
        HighlightPackButtons();
        RefreshPreview();
        ShowTip(enabled ? "已使用牌面背景（花纹原样叠加）" : "已关闭牌面背景");
    }

    private void OnTogglePreview(bool table) {
        showingTablePreview = table;
        HighlightPackButtons();
        RefreshPreview();
    }

    private void RefreshPreview() {
        standardPreviewRoot.SetActive(!showingHongque);
        hongquePreviewRoot.SetActive(showingHongque);
        HighlightPackButtons();
        if (showingHongque) {
            SetStatus("虹雀使用官方 HQv3.1 牌面，不可上传自定义。");
            ApplySlots(hongqueSlots, false);
            return;
        }
        string packId = ConfigManager.Instance != null
            ? ConfigManager.Instance.StandardTilePackId
            : TilePackIds.PackOfficial;
        int customCount = TilePackIds.IsLayeredPack(packId) ? TileFaceResolver.CountPackFaces() : 0;
        bool useBg = ConfigManager.Instance != null && ConfigManager.Instance.UseHandFaceBackground;
        if (packId == TilePackIds.PackFluffy) {
            SetStatus($"当前：FluffyStuff（{customCount} 张）" + (useBg ? "，使用牌面背景" : "，不使用牌面背景"));
        }
        else if (packId == TilePackIds.PackHkMahjong) {
            SetStatus($"当前：香港麻将（{customCount} 张）" + (useBg ? "，使用牌面背景" : "，不使用牌面背景"));
        }
        else if (packId == TilePackIds.PackCustom) {
            SetStatus($"当前：自定义标准牌面（{customCount} 张）" + (useBg ? "，使用牌面背景" : "，不使用牌面背景"));
        }
        else {
            SetStatus("当前：官方标准牌面（雪风）" + (useBg ? "，使用牌面背景" : "，不使用牌面背景"));
        }
        ApplySlots(standardSlots, TilePackIds.IsLayeredPack(packId));
    }

    private void ApplySlots(CardFacePreviewSlot[] slots, bool dimMissingCustom) {
        bool table = showingTablePreview && !showingHongque;
        bool useBg = !table
            && ConfigManager.Instance != null
            && ConfigManager.Instance.UseHandFaceBackground;
        Sprite background = useBg ? TileFaceResolver.LoadHandBackground() : null;
        for (int i = 0; i < slots.Length; i++) {
            CardFacePreviewSlot slot = slots[i];
            if (slot == null) continue;
            bool dim = dimMissingCustom && !TileFaceResolver.HasCustomFace(slot.tileId);
            Sprite sprite = table
                ? TileFaceResolver.LoadTableSprite(slot.tileId)
                : TileFaceResolver.LoadSprite(slot.tileId);
            bool layer = useBg && background != null && TileFaceResolver.ShouldLayerHandFace(slot.tileId);
            slot.Apply(sprite, layer ? background : null, dim);
        }
    }

    private void SetStatus(string message) {
        statusText.text = message ?? "";
    }

    private static void ShowTip(string message) {
        if (NotificationManager.Instance != null) {
            NotificationManager.Instance.ShowTip("设置", true, message);
        }
    }
}
