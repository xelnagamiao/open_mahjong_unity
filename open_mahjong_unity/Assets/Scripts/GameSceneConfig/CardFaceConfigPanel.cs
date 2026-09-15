using System.IO;
using System.Collections.Generic;
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
        + "ZIP 内同时放入 hand/ 和 table/ 文件夹，也接受“手牌牌面/”与“3D牌面/”。\n"
        + "可选加入 manifest.json（format=om-tilepack，family=standard）。\n\n"
        + "hand/{id}.png：2D 手牌牌面，建议宽高比 272:389、272×389 像素。\n"
        + "table/{id}.png：3D 牌面，建议宽高比 1:1.33、400×532 像素（或 600×798）。\n"
        + "以上均为建议，不要求匹配指定尺寸或比例。保留原图尺寸、透明通道与完整内容；显示时等比居中，空余部分由背景或底色补齐。\n\n"
        + "牌号：万11–19、筒21–29、索31–39、字41–47、花51–58；赤宝105/205/305，纯白白板2。\n"
        + "使用 PNG。资源上限：单边≤1024、单张≤500KB、解压后≤20MB。可缺少部分牌面，缺图回退官方。\n"
        + "虹雀牌组固定使用官方 HQv3.1。\n"
        + "根目录 PNG 不会当作牌面；3D 预览与对局使用相同排版。\n\n"
        + "示例：MyTiles.zip/hand/11.png 与 MyTiles.zip/table/11.png。\n"
        + "透明花纹可叠加牌面背景；选择“背景铺满”时保留铺满显示。\n"
        + "手牌背景与牌背在对应标签管理；3D 牌面背景请到「3D 卡牌设计」中的「3D牌面背景」标签设置。";

    [SerializeField] private Button tabStandardButton;
    [SerializeField] private Button tabHongqueButton;
    [SerializeField] private Button uploadButton;
    [SerializeField] private Button restoreButton;
    [SerializeField] private Button packFluffyButton;
    [SerializeField] private Button packHkButton;
    [SerializeField] private Button customPackButton;
    [SerializeField] private TMP_Text customPackNameText;
    [SerializeField] private TMP_Dropdown customPackDropdown;
    private List<TilePackLibrary.Entry> customEntries = new List<TilePackLibrary.Entry>();
    [SerializeField] private Button showHandButton;
    [SerializeField] private Button showTableButton;
    [SerializeField] private TMP_Text helpText;
    [FormerlySerializedAs("standardActions")]
    [SerializeField] private GameObject standardPacks;
    [SerializeField] private GameObject standardViewActions;
    [SerializeField] private GameObject standardPreviewRoot;
    [SerializeField] private GameObject hongquePreviewRoot;

    private bool showingHongque;
    private bool showingTablePreview;
    public bool ShowingTablePreview => showingTablePreview;
    private CardFacePreviewSlot[] standardSlots;
    private CardFacePreviewSlot[] hongqueSlots;

    private void Awake() {
        if (helpText != null) helpText.text = FormatHelp;
        Instance = this;
        tabStandardButton.onClick.AddListener(() => SetTab(false));
        tabHongqueButton.onClick.AddListener(() => SetTab(true));
        uploadButton.onClick.AddListener(OnUploadClicked);
        restoreButton.onClick.AddListener(OnRestoreClicked);
        SceneConfigUi.BindClick(packFluffyButton, () => OnSelectPack(TilePackIds.PackFluffy));
        SceneConfigUi.BindClick(packHkButton, () => OnSelectPack(TilePackIds.PackHkMahjong));
        SceneConfigUi.BindClick(customPackButton, () => OnSelectPack(TilePackIds.PackCustom));
        if (customPackDropdown != null) customPackDropdown.onValueChanged.AddListener(OnCustomPackSelected);
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
        RestorePanel();
    }

    public void RestorePanel() {
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
        RefreshViewActionVisibility();
        HighlightPackButtons();
    }

    /// <summary>
    /// 牌组页只切换预览对象；背景设置由独立页面管理。
    /// </summary>
    private void RefreshViewActionVisibility() {
        bool standard = !showingHongque;
        standardViewActions.SetActive(standard);
        showHandButton.gameObject.SetActive(standard);
        showTableButton.gameObject.SetActive(standard);
    }

    public void RefreshHighlights() {
        HighlightPackButtons();
    }

    private static void SetTabColor(Button button, bool on) {
        SceneConfigUi.SetButtonSelected(button, on);
    }

    private void OnUploadClicked() {
        if (showingHongque) return;
        TilePackStorage.PickZip(OnZipPicked, err => {
            if (!string.IsNullOrEmpty(err) && err != "empty") {
                SceneConfigUi.ShowTip(err);
            }
        });
    }

    private void OnZipPicked(byte[] zipBytes, string fileName) {
        TilePackImporter.Result imported = TilePackImporter.Import(zipBytes);
        if (imported == null || !imported.Success) {
            string error = imported != null ? imported.Error : "导入失败";
            SceneConfigUi.ShowTip(error);
            return;
        }
        TilePackLibrary.SaveNew(zipBytes, fileName, imported, entry => {
        TileFaceResolver.ApplyLibraryPack(entry.id, imported);
        string status = $"已应用自定义牌面（{imported.HandPngs.Count} 张手牌";
        if (imported.TablePngs.Count > 0) {
            status += $"，{imported.TablePngs.Count} 张桌面";
        }
        status += "）";
        if (imported.Warnings.Count > 0) {
            status += "。" + imported.Warnings[0];
        }
        SceneConfigUi.ShowTip(status);
        RefreshCustomPackChip();
        RefreshPreview();
        }, error => SceneConfigUi.ShowTip(error));
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
        SetTabColor(customPackButton, TilePackIds.IsCustomPack(packId));
        if (customPackDropdown != null && customPackDropdown.targetGraphic != null)
            customPackDropdown.targetGraphic.color = TilePackIds.IsCustomPack(packId)
                ? SceneConfigUi.TabOn : SceneConfigUi.TabOff;
        RefreshCustomPackChip();
        SetTabColor(showHandButton, !showingTablePreview);
        SetTabColor(showTableButton, showingTablePreview);
    }

    private void OnTogglePreview(bool table) {
        showingTablePreview = table;
        RefreshViewActionVisibility();
        HighlightPackButtons();
        RefreshPreview();
    }

    private void RefreshPreview() {
        RefreshViewActionVisibility();
        standardPreviewRoot.SetActive(!showingHongque);
        hongquePreviewRoot.SetActive(showingHongque);
        HighlightPackButtons();
        if (showingHongque) {
            ApplySlots(hongqueSlots, false);
            return;
        }
        string packId = ConfigManager.Instance != null
            ? ConfigManager.Instance.StandardTilePackId
            : TilePackIds.PackOfficial;
        ApplySlots(standardSlots, TilePackIds.IsLayeredPack(packId));
    }

    private void ApplySlots(CardFacePreviewSlot[] slots, bool dimMissingCustom) {
        bool table = showingTablePreview && !showingHongque;
        bool useTableBg = table
            && ConfigManager.Instance != null
            && ConfigManager.Instance.UseTableFaceBackground
            && !ConfigManager.Instance.TableFaceUseSolidColor;
        Sprite tableBackground = useTableBg ? TileFaceResolver.LoadTableBackground() : null;
        for (int i = 0; i < slots.Length; i++) {
            CardFacePreviewSlot slot = slots[i];
            bool dim = dimMissingCustom && !TileFaceResolver.HasCustomFace(slot.tileId);
            Sprite sprite = table
                ? TileFaceResolver.PreviewTable(slot.tileId)
                : TileFaceResolver.PreviewHand(slot.tileId);
            // 使用对局相同的叠图规则；背景由独立页面管理。
            if (table) slot.ApplyTable(sprite, tableBackground, TileFaceResolver.TablePreviewBaseColor, dim,
                TileFaceResolver.TableImageScaleFor(slot.tileId, applyWhiteDragonFaceSetting: false));
            else {
                Sprite background = !showingHongque && TileFaceResolver.ShouldLayerHandFace(slot.tileId)
                    ? TileFaceResolver.LoadHandBackground() : null;
                slot.Apply(sprite, background, dim);
            }
        }
    }

    private void RefreshCustomPackChip() {
        if (customPackDropdown != null) {
            customPackButton.gameObject.SetActive(false);
            customPackDropdown.gameObject.SetActive(!showingHongque);
            customEntries = TilePackLibrary.GetEntries();
            var options = new List<TMP_Dropdown.OptionData> { new TMP_Dropdown.OptionData(customEntries.Count == 0 ? "自定义牌面" : "选择自定义牌面") };
            var counts = new Dictionary<string, int>();
            int selected = 0;
            string current = ConfigManager.Instance != null ? ConfigManager.Instance.StandardTilePackId : "";
            for (int i = 0; i < customEntries.Count; i++) {
                var entry = customEntries[i];
                counts.TryGetValue(entry.DisplayName, out int count); counts[entry.DisplayName] = ++count;
                options.Add(new TMP_Dropdown.OptionData(entry.DisplayName + (count > 1 ? " (" + count + ")" : "")));
                if (entry.id == current) selected = i + 1;
            }
            customPackDropdown.options = options;
            customPackDropdown.SetValueWithoutNotify(selected);
            customPackDropdown.RefreshShownValue();
            customPackDropdown.interactable = customEntries.Count > 0;
            return;
        }
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

    private void OnCustomPackSelected(int index) {
        if (index > 0 && index <= customEntries.Count) OnSelectPack(customEntries[index - 1].id);
        else RefreshCustomPackChip();
    }

    private static string CustomPackDisplayName(string fileName) {
        if (string.IsNullOrEmpty(fileName)) {
            return "自定义";
        }
        return Path.GetFileNameWithoutExtension(fileName);
    }

}
