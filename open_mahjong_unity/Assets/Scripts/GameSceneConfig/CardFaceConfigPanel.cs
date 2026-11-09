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
        + "ZIP 内只需放一套以牌号命名的 PNG，2D 手牌与 3D 自动共用。\n"
        + "包内只需牌号 PNG 和一个 tiles.cfg；不需要 manifest.json 或预览图。\n\n"
        + "推荐透明底原图；经典手牌背景为 272×389，新版为 272×424，花纹保持原图比例。\n"
        + "tiles.cfg 支持 # 注释：[2d] / [3d] 设置整包默认值，[2d.11] / [3d.11] 设置单牌相对微调，参数 x、y、scale 使用百分比。\n"
        + "以上均为建议，不要求匹配指定尺寸或比例。保留原图尺寸、透明通道与完整内容；显示时等比居中，空余部分由背景或底色补齐。\n\n"
        + "牌号：万11–19、筒21–29、索31–39、字41–47、花51–58；赤宝105/205/305，纯白白板2。\n"
        + "使用 PNG。资源上限：单边≤4096、单张≤4MiB、解压后≤128MiB。可缺少部分牌面，缺图回退官方。\n"
        + "虹雀牌组固定使用官方 HQv3.1。\n"
        + "PNG 可直接放在 ZIP 根目录，也可放在文件夹中。旧 hand/、table/ 双份格式继续兼容。\n\n"
        + "示例：11.png、12.png、21.png……与 tiles.cfg；默认 x=0、y=0、scale=100，缩放范围 25–200。\n"
        + "上传后输入名称并确认保存；可保留多套，点击对应牌面包按钮切换，选中后可重命名或删除。返回不保存。\n"
        + "透明花纹可叠加牌面背景；选择“背景铺满”时保留铺满显示。\n"
        + "手牌背景与牌背在对应标签管理；3D 牌面背景请到「卡牌设计」中的「3D牌面背景」分区设置。";

    [SerializeField] private Button tabStandardButton;
    [SerializeField] private Button tabHongqueButton;
    [SerializeField] private Button uploadButton;
    [SerializeField] private Button restoreButton;
    [SerializeField] private Button packFluffyButton;
    [SerializeField] private Button packHkButton;
    [SerializeField] private Button customPackButton;
    [SerializeField] private TMP_Text customPackNameText;
    [SerializeField] private TMP_Dropdown customPackDropdown;
    [SerializeField] private CardFaceUploadedPackPicker uploadedPackPicker;
    private List<TilePackLibrary.Entry> customEntries = new List<TilePackLibrary.Entry>();
    private int deleteCustomOption = -1;
    private int renameCustomOption = -1;
    private HandSurfaceNameDialog packNameDialog;
    private bool deletingCustomPack;
    private MessagePrefab deleteConfirmation;
    [SerializeField] private Button showHandButton;
    [SerializeField] private Button showTableButton;
    [SerializeField] private TMP_Text helpText;
    [FormerlySerializedAs("standardActions")]
    [SerializeField] private GameObject standardPacks;
    [SerializeField] private GameObject standardViewActions;
    [SerializeField] private GameObject standardPreviewRoot;
    [SerializeField] private GameObject hongquePreviewRoot;
    [SerializeField] private bool singleScrollLayout;

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
        TilePackLibrary.Changed += RefreshPreview;
        RefreshPreview();
    }

    private void OnDisable() {
        TileFaceResolver.OnPackChanged -= RefreshPreview;
        TilePackLibrary.Changed -= RefreshPreview;
        if (deleteConfirmation != null) deleteConfirmation.CloseMessage();
        if (packNameDialog != null) packNameDialog.CloseWithOwner();
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
        var card = button != null ? button.GetComponent<CardSurfaceOptionView>() : null;
        if (card != null) { card.SetSelected(on); return; }
        SceneConfigUi.SetButtonSelected(button, on);
    }

    private void OnUploadClicked() {
        if (showingHongque || packNameDialog != null) return;
        TilePackStorage.PickZip(OnZipPicked, err => {
            if (!string.IsNullOrEmpty(err) && err != "empty") {
                SceneConfigUi.ShowTip(err);
            }
        });
    }

    private void OnZipPicked(byte[] zipBytes, string fileName) {
        if (this == null || !isActiveAndEnabled || packNameDialog != null) return;
        TilePackImporter.Result imported = TilePackImporter.Import(zipBytes);
        if (imported == null || !imported.Success) {
            string error = imported != null ? imported.Error : "导入失败";
            SceneConfigUi.ShowTip(error);
            return;
        }
        Texture2D preview = null;
        foreach (var image in imported.HandPngs.Values) { preview = UnityAssetIdb.ToTexture(image); break; }
        packNameDialog = HandSurfaceNameDialog.Open(transform, uploadButton.GetComponentInChildren<TMP_Text>(true)?.font,
            "上传自定义牌面", HandSurfaceLibrary.SuggestedName(fileName, false), preview, (name, close, error) =>
        TilePackLibrary.SaveNew(zipBytes, fileName, imported, entry => {
        close();
        if (this == null || !isActiveAndEnabled) return;
        TileFaceResolver.ApplyLibraryPack(entry.id, imported);
        string status = $"已应用自定义牌面（{imported.HandPngs.Count} 张）";
        if (imported.Warnings.Count > 0) {
            status += "。" + imported.Warnings[0];
        }
        SceneConfigUi.ShowTip(status);
        RefreshCustomPackChip();
        RefreshPreview();
        }, error, name));
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

    public void SelectUploadedPack(string id) => OnSelectPack(id);
    public void RenameSelectedCustomPack() => RenameCustomPack();
    public void DeleteSelectedCustomPack() => ConfirmDeleteCustomPack();

    private void RefreshCustomPackChip() {
        if (uploadedPackPicker != null) {
            customEntries = TilePackLibrary.GetEntries();
            customPackButton.gameObject.SetActive(false);
            if (customPackDropdown != null) customPackDropdown.gameObject.SetActive(false);
            uploadedPackPicker.Refresh();
            return;
        }
        if (customPackDropdown != null) {
            customPackButton.gameObject.SetActive(false);
            customEntries = TilePackLibrary.GetEntries();
            customPackDropdown.gameObject.SetActive(!showingHongque && (!singleScrollLayout || customEntries.Count > 0));
            var options = new List<TMP_Dropdown.OptionData> { new TMP_Dropdown.OptionData(customEntries.Count == 0 ? "无自定义牌面" : "选择自定义牌面") };
            var counts = new Dictionary<string, int>();
            int selected = 0;
            string current = ConfigManager.Instance != null ? ConfigManager.Instance.StandardTilePackId : "";
            for (int i = 0; i < customEntries.Count; i++) {
                var entry = customEntries[i];
                counts.TryGetValue(entry.DisplayName, out int count); counts[entry.DisplayName] = ++count;
                options.Add(new TMP_Dropdown.OptionData(entry.DisplayName + (count > 1 ? " (" + count + ")" : "")));
                if (entry.id == current) selected = i + 1;
            }
            deleteCustomOption = -1;
            renameCustomOption = -1;
            if (selected > 0) {
                renameCustomOption = options.Count;
                options.Add(new TMP_Dropdown.OptionData("重命名当前自定义牌面…"));
                deleteCustomOption = options.Count;
                options.Add(new TMP_Dropdown.OptionData("删除当前自定义牌面…"));
            }
            customPackDropdown.captionText.richText = false;
            customPackDropdown.itemText.richText = false;
            customPackDropdown.options = options;
            customPackDropdown.SetValueWithoutNotify(selected);
            customPackDropdown.RefreshShownValue();
            // Keep the empty state discoverable: the placeholder opens but never selects a pack.
            customPackDropdown.interactable = !deletingCustomPack;
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
        if (deletingCustomPack) { RefreshCustomPackChip(); return; }
        if (index == deleteCustomOption && index >= 0) { RefreshCustomPackChip(); ConfirmDeleteCustomPack(); }
        else if (index == renameCustomOption && index >= 0) { RefreshCustomPackChip(); RenameCustomPack(); }
        else if (index > 0 && index <= customEntries.Count) OnSelectPack(customEntries[index - 1].id);
        else RefreshCustomPackChip();
    }

    private void ConfirmDeleteCustomPack() {
        if (showingHongque || deletingCustomPack || deleteConfirmation != null || NotificationManager.Instance == null) return;
        string id = ConfigManager.Instance != null ? ConfigManager.Instance.StandardTilePackId : "";
        var entry = customEntries.Find(e => e.id == id);
        if (entry == null) return;
        deleteConfirmation = NotificationManager.Instance.ShowConfirmation("删除自定义牌面",
            "确定删除“" + entry.DisplayName + "”吗？\n删除后当前牌面将恢复为官方牌面。",
            () => {
                if (this == null || !isActiveAndEnabled || deletingCustomPack) return;
                deletingCustomPack = true;
                RefreshCustomPackChip();
                TilePackLibrary.Delete(id, () => {
                    if (this == null) return;
                    deletingCustomPack = false;
                    RefreshPreview();
                    SceneConfigUi.ShowTip("已删除自定义牌面");
                }, error => {
                    if (this == null) return;
                    deletingCustomPack = false;
                    RefreshCustomPackChip();
                    SceneConfigUi.ShowTip(error);
                });
            }, "删除", "返回");
    }

    private void RenameCustomPack() {
        if (showingHongque || deletingCustomPack || packNameDialog != null || ConfigManager.Instance == null) return;
        var entry = customEntries.Find(e => e.id == ConfigManager.Instance.StandardTilePackId);
        if (entry == null) return;
        packNameDialog = HandSurfaceNameDialog.Open(transform, uploadButton.GetComponentInChildren<TMP_Text>(true)?.font,
            "重命名自定义牌面", entry.DisplayName, null, (name, close, error) => {
                if (!TilePackLibrary.Rename(entry.id, name, out var reason)) { error(reason); return; }
                close(); RefreshCustomPackChip();
            });
    }

    private static string CustomPackDisplayName(string fileName) {
        if (string.IsNullOrEmpty(fileName)) {
            return "自定义";
        }
        return Path.GetFileNameWithoutExtension(fileName);
    }

}
