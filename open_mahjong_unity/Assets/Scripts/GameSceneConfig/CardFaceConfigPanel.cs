using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// 场景设置「牌面」页：标准麻将可上传 zip，虹雀只读官方图。
/// 引用由场景拖好，运行时只换图和切 tab。
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

    [SerializeField] private Button tabStandardButton;
    [SerializeField] private Button tabHongqueButton;
    [SerializeField] private Button uploadButton;
    [SerializeField] private Button restoreButton;
    [SerializeField] private Button packFluffyButton;
    [SerializeField] private Button packHkButton;
    [SerializeField] private Button customPackButton;
    [SerializeField] private TMP_Text customPackNameText;
    [SerializeField] private Button useBackgroundButton;
    [SerializeField] private Button noBackgroundButton;
    [SerializeField] private Button useTableBackgroundButton;
    [SerializeField] private Button noTableBackgroundButton;
    [SerializeField] private Button showHandButton;
    [SerializeField] private Button showTableButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text helpText;
    [FormerlySerializedAs("standardActions")]
    [SerializeField] private GameObject standardPacks;
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
        SceneConfigUi.BindClick(packFluffyButton, () => OnSelectPack(TilePackIds.PackFluffy));
        SceneConfigUi.BindClick(packHkButton, () => OnSelectPack(TilePackIds.PackHkMahjong));
        SceneConfigUi.BindClick(customPackButton, () => OnSelectPack(TilePackIds.PackCustom));
        SceneConfigUi.BindClick(useBackgroundButton, () => OnToggleBackground(true));
        SceneConfigUi.BindClick(noBackgroundButton, () => OnToggleBackground(false));
        SceneConfigUi.BindClick(useTableBackgroundButton, () => OnToggleTableBackground(true));
        SceneConfigUi.BindClick(noTableBackgroundButton, () => OnToggleTableBackground(false));
        SceneConfigUi.BindClick(showHandButton, () => OnTogglePreview(false));
        SceneConfigUi.BindClick(showTableButton, () => OnTogglePreview(true));
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
        standardPacks.SetActive(!showingHongque);
        uploadButton.gameObject.SetActive(!showingHongque);
        restoreButton.gameObject.SetActive(!showingHongque);
        packFluffyButton.gameObject.SetActive(!showingHongque);
        packHkButton.gameObject.SetActive(!showingHongque);
        useBackgroundButton.gameObject.SetActive(!showingHongque && !showingTablePreview);
        noBackgroundButton.gameObject.SetActive(!showingHongque && !showingTablePreview);
        useTableBackgroundButton.gameObject.SetActive(!showingHongque && showingTablePreview);
        noTableBackgroundButton.gameObject.SetActive(!showingHongque && showingTablePreview);
        showHandButton.gameObject.SetActive(!showingHongque);
        showTableButton.gameObject.SetActive(!showingHongque);
        standardViewActions.SetActive(!showingHongque);
        HighlightPackButtons();
    }

    public void RefreshHighlights() {
        HighlightPackButtons();
    }

    private static void SetTabColor(Button button, bool on) {
        SceneConfigUi.SetButtonSelected(button, on);
    }

    private void OnUploadClicked() {
        if (showingHongque) return;
        SetStatus("正在选择 zip…");
        TilePackStorage.PickZip(OnZipPicked, err => {
            if (!string.IsNullOrEmpty(err) && err != "empty") {
                SetStatus(err);
                SceneConfigUi.ShowTip(err);
            }
        });
    }

    private void OnZipPicked(byte[] zipBytes, string fileName) {
        TilePackImporter.Result imported = TilePackImporter.Import(zipBytes);
        if (imported == null || !imported.Success) {
            string error = imported != null ? imported.Error : "导入失败";
            SetStatus(error);
            SceneConfigUi.ShowTip(error);
            return;
        }
        if (ConfigManager.Instance != null) {
            ConfigManager.Instance.SetCustomTilePackFileName(fileName ?? "");
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
        RefreshCustomPackChip();
        RefreshPreview();
    }

    private void OnRestoreClicked() {
        OnSelectPack(TilePackIds.PackOfficial);
    }

    private void OnSelectPack(string packId) {
        TileFaceResolver.SelectPack(packId);
        HighlightPackButtons();
        RefreshPreview();
    }

    private void HighlightPackButtons() {
        string packId = ConfigManager.Instance != null
            ? ConfigManager.Instance.StandardTilePackId
            : TilePackIds.PackOfficial;
        SetTabColor(restoreButton, packId == TilePackIds.PackOfficial);
        SetTabColor(packFluffyButton, packId == TilePackIds.PackFluffy);
        SetTabColor(packHkButton, packId == TilePackIds.PackHkMahjong);
        SetTabColor(customPackButton, packId == TilePackIds.PackCustom);
        RefreshCustomPackChip();
        bool useBg = ConfigManager.Instance != null && ConfigManager.Instance.UseHandFaceBackground;
        SetTabColor(useBackgroundButton, useBg);
        SetTabColor(noBackgroundButton, !useBg);
        bool useTableBg = ConfigManager.Instance != null && ConfigManager.Instance.UseTableFaceBackground;
        SetTabColor(useTableBackgroundButton, useTableBg);
        SetTabColor(noTableBackgroundButton, !useTableBg);
        SetTabColor(showHandButton, !showingTablePreview);
        SetTabColor(showTableButton, showingTablePreview);
    }

    private void OnToggleBackground(bool enabled) {
        TileFaceResolver.SetUseHandFaceBackground(enabled);
        HighlightPackButtons();
        RefreshPreview();
    }

    private void OnToggleTableBackground(bool enabled) {
        // 只切「使用/不使用」开关，不动已上传的 3D 牌面背景纹理：
        // 关闭后 CurrentTableBackground 仍保留，下次打开会立刻生效。
        CardBackManager.SetTableFaceBackgroundEnabled(enabled);
        HighlightPackButtons();
        if (CardFaceBackgroundPanel.Instance != null) {
            CardFaceBackgroundPanel.Instance.RefreshSolidColorUi();
        }
        RefreshPreview();
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
        bool useTableBg = ConfigManager.Instance != null && ConfigManager.Instance.UseTableFaceBackground;
        if (packId == TilePackIds.PackFluffy) {
            SetStatus($"当前：FluffyStuff（{customCount} 张）"
                + (showingTablePreview
                    ? (useTableBg ? "，使用 3D 牌面背景" : "，不使用 3D 牌面背景")
                    : (useBg ? "，使用牌面背景" : "，不使用牌面背景")));
        }
        else if (packId == TilePackIds.PackHkMahjong) {
            SetStatus($"当前：香港麻将（{customCount} 张）"
                + (showingTablePreview
                    ? (useTableBg ? "，使用 3D 牌面背景" : "，不使用 3D 牌面背景")
                    : (useBg ? "，使用牌面背景" : "，不使用牌面背景")));
        }
        else if (packId == TilePackIds.PackCustom) {
            SetStatus($"当前：{CustomPackDisplayName()}（{customCount} 张）"
                + (showingTablePreview
                    ? (useTableBg ? "，使用 3D 牌面背景" : "，不使用 3D 牌面背景")
                    : (useBg ? "，使用牌面背景" : "，不使用牌面背景")));
        }
        else {
            SetStatus("当前：官方标准牌面（雪风）"
                + (showingTablePreview
                    ? (useTableBg ? "，使用 3D 牌面背景" : "，不使用 3D 牌面背景")
                    : (useBg ? "，使用牌面背景" : "，不使用牌面背景")));
        }
        ApplySlots(standardSlots, TilePackIds.IsLayeredPack(packId));
    }

    private void ApplySlots(CardFacePreviewSlot[] slots, bool dimMissingCustom) {
        bool table = showingTablePreview && !showingHongque;
        bool useBg = !table
            && ConfigManager.Instance != null
            && ConfigManager.Instance.UseHandFaceBackground;
        Sprite handBackground = useBg ? TileFaceResolver.LoadHandBackground() : null;
        bool useTableBg = table
            && ConfigManager.Instance != null
            && ConfigManager.Instance.UseTableFaceBackground
            && CardBackManager.CurrentTableBackground != null;
        Texture2D tableTex = useTableBg ? CardBackManager.CurrentTableBackground : null;
        Sprite tableBackground = tableTex != null
            ? Sprite.Create(tableTex,
                new Rect(0f, 0f, tableTex.width, tableTex.height),
                new Vector2(0.5f, 0.5f), 100f)
            : null;
        for (int i = 0; i < slots.Length; i++) {
            CardFacePreviewSlot slot = slots[i];
            bool dim = dimMissingCustom && !TileFaceResolver.HasCustomFace(slot.tileId);
            Sprite sprite = table
                ? TileFaceResolver.LoadTableSprite(slot.tileId)
                : TileFaceResolver.LoadSprite(slot.tileId);
            // 3D 牌面预览：启用 3D 牌面背景时，tableBackground 作为底图，3D 牌面 sprite 作为前景花纹。
            // 2D 手牌牌面预览：handBackground 作为底图，sprite 作为前景。
            Sprite baseSprite = useTableBg ? tableBackground : (useBg ? handBackground : null);
            bool layer = baseSprite != null && sprite != null;
            slot.Apply(sprite, layer ? baseSprite : null, dim);
        }
    }

    private void RefreshCustomPackChip() {
        string fileName = ConfigManager.Instance != null
            ? ConfigManager.Instance.CustomTilePackFileName
            : "";
        bool hasCustom = !string.IsNullOrEmpty(fileName)
            || (ConfigManager.Instance != null
                && ConfigManager.Instance.StandardTilePackId == TilePackIds.PackCustom);
        customPackButton.gameObject.SetActive(hasCustom);
        if (!hasCustom) {
            return;
        }
        customPackNameText.text = CustomPackDisplayName(fileName);
        customPackNameText.ForceMeshUpdate();
        float width = Mathf.Clamp(customPackNameText.preferredWidth + 28f, 88f, 220f);
        RectTransform rt = (RectTransform)customPackButton.transform;
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
    }

    private static string CustomPackDisplayName() {
        string fileName = ConfigManager.Instance != null
            ? ConfigManager.Instance.CustomTilePackFileName
            : "";
        return CustomPackDisplayName(fileName);
    }

    private static string CustomPackDisplayName(string fileName) {
        if (string.IsNullOrEmpty(fileName)) {
            return "自定义";
        }
        return Path.GetFileNameWithoutExtension(fileName);
    }

    private void SetStatus(string message) {
        statusText.text = message ?? "";
    }
}
