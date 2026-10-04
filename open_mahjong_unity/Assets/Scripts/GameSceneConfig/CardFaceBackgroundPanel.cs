using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

/// <summary>
/// 场景设置「牌面背景」页：手牌牌面背景与 2D 手牌牌背（里宝暗面）分开上传。
/// </summary>
public partial class CardFaceBackgroundPanel : MonoBehaviour {
    public static CardFaceBackgroundPanel Instance { get; private set; }

    public const string FormatHelp =
        "手牌牌面背景：显示在 2D 手牌牌面下方。经典 272×389，新版修长底图 272×424；其他尺寸按原图比例显示。\n"
        + "手牌牌背：显示 2D 暗面图样（例如里宝牌未翻开），不是 3D 牌背。\n"
        + "单独上传：点击对应标签下的上传按钮，分别选择 hand-bg.png 或 hand-back.png。\n"
        + "也可一次上传 zip，文件夹示例：\n"
        + "  MyHandImages.zip/\n"
        + "  ├─ hand-bg.png    手牌牌面背景\n"
        + "  └─ hand-back.png  手牌牌背\n"
        + "透明花纹牌面请在「牌面」页打开「使用牌面背景」，整张牌面请关闭。\n"
        + "背景与牌背默认使用靛蓝，经典位于列表末尾。支持多份上传，确认名称后保存；下拉切换，使用“改名”“删除”管理。返回不保存，恢复默认不删除图库。\n"
        + "ZIP 导入只需确认一次名称，默认为 ZIP 文件名，背景与牌背使用同一名称；同类素材不能重名。\n"
        + "牌背右上角“自动跟随”亮起时，切换背景会使用同名牌背；没有同名牌背时保留当前牌背。手动选牌背会关闭跟随。\n"
        + "手牌背景右上角“牌面位置”可调整花纹的水平、垂直位置和等比缩放；每个背景独立保存，返回撤销，确认保存。\n"
        + "3D 牌面背景只用于 3D 卡牌正面，不是手牌背景。\n"
        + "请到「3D 卡牌设计」中的「3D牌面背景」标签设置背景和纯色，3D 牌背请到「牌背」标签设置。";

    private const string ImageAccept = "image/png,image/jpeg,application/zip,.zip";

    [SerializeField] private Image handBgPreview;
    [SerializeField] private Image cardBackPreview;
    [SerializeField] private Image tableBgPreview;
    [SerializeField] private GameObject tilePreviewPrefab;
    [SerializeField] private Button uploadHandBgButton;
    [SerializeField] private Button uploadCardBackButton;
    [SerializeField] private Button uploadPairZipButton;
    [SerializeField] private Button restoreHandBgButton;
    [SerializeField] private Button clearCardBackButton;
    [SerializeField] private HandSurfaceStylePicker handBgStyles;
    [SerializeField] private HandSurfaceStylePicker handBackStyles;
    [SerializeField] private Button handLayoutButton;
    [SerializeField] private Button handBackFollowButton;
    private HandFaceLayoutEditor handLayoutEditor;
    [SerializeField] private Button uploadTableBgButton;
    [SerializeField] private Button restoreTableBgButton;
    [SerializeField] private Button clearTableBgButton;
    [SerializeField] private Image tableFaceColorPreview;
    [SerializeField] private Slider tableFaceSliderR;
    [SerializeField] private Slider tableFaceSliderG;
    [SerializeField] private Slider tableFaceSliderB;
    [FormerlySerializedAs("tableFaceSliderGray"), SerializeField] private Slider tableFaceSliderBrightness;
    [SerializeField] private TMP_Text tableFaceValueR;
    [SerializeField] private TMP_Text tableFaceValueG;
    [SerializeField] private TMP_Text tableFaceValueB;
    [FormerlySerializedAs("tableFaceValueGray"), SerializeField] private TMP_Text tableFaceValueBrightness;
    [SerializeField] private TMP_InputField tableFaceHexInput;
    [SerializeField] private Button tableFaceHexApplyButton;
    [SerializeField] private Button useTableFaceSolidButton;
    [SerializeField] private Button noTableFaceSolidButton;
    [SerializeField] private Button restoreTableFaceColorButton;
    [SerializeField] private TMP_Text helpText;
    [SerializeField] private Button useTableBackgroundButton;
    [SerializeField] private Button noTableBackgroundButton;

    private enum PickMode { HandBg, CardBack, TableBg, Pair }

    private PickMode pickMode = PickMode.Pair;
    private Sprite handBgSprite;
    private Sprite cardBackSprite;
    private Sprite tableBgSprite;
    [SerializeField] private Image tableBgArtwork;
    private bool syncingTableFaceColor;

    private void Awake() {
        if (helpText != null) helpText.text = FormatHelp;
        Instance = this;
        BindHandSurfaceStyles();
        BindHandLayoutButton();
        BindHandBackFollowButton();
        uploadHandBgButton.onClick.AddListener(() => OpenPicker(PickMode.HandBg));
        uploadCardBackButton.onClick.AddListener(() => OpenPicker(PickMode.CardBack));
        uploadPairZipButton.onClick.AddListener(() => OpenPicker(PickMode.Pair));
        restoreHandBgButton.onClick.AddListener(RestoreHandBg);
        clearCardBackButton.onClick.AddListener(ClearCardBack);
        uploadTableBgButton.onClick.AddListener(() => OpenPicker(PickMode.TableBg));
        restoreTableBgButton.onClick.AddListener(RestoreTableBg);
        clearTableBgButton.onClick.AddListener(ClearTableBg);
        tableFaceHexApplyButton.onClick.AddListener(ApplyTableFaceHex);
        useTableFaceSolidButton.onClick.AddListener(() => SetTableFaceSolid(true));
        noTableFaceSolidButton.onClick.AddListener(() => SetTableFaceSolid(false));
        restoreTableFaceColorButton.onClick.AddListener(RestoreTableFaceColor);
        if (useTableBackgroundButton != null)
            useTableBackgroundButton.onClick.AddListener(() => SetTableBackground(true));
        if (noTableBackgroundButton != null)
            noTableBackgroundButton.onClick.AddListener(() => SetTableBackground(false));
        tableFaceSliderR.onValueChanged.AddListener(v => SetTableFaceRgb(v / 255f, CurrentTableFaceColor.g, CurrentTableFaceColor.b));
        tableFaceSliderG.onValueChanged.AddListener(v => SetTableFaceRgb(CurrentTableFaceColor.r, v / 255f, CurrentTableFaceColor.b));
        tableFaceSliderB.onValueChanged.AddListener(v => SetTableFaceRgb(CurrentTableFaceColor.r, CurrentTableFaceColor.g, v / 255f));
        if (tableFaceSliderBrightness != null)
            tableFaceSliderBrightness.onValueChanged.AddListener(SetTableFaceBrightness);
    }

    private void OnEnable() {
        HandSurfaceLibrary.Changed += RefreshHandPreviews;
        HandSurfaceLibrary.EnsureReady(() => { if (this != null && isActiveAndEnabled) RefreshHandPreviews(); });
        RefreshPreviews();
        RefreshSolidColorUi();
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.BindReadOnlyDrop(OnWebGlBytes, err => {
            if (!string.IsNullOrEmpty(err) && err != "empty") SceneConfigUi.ShowTip(err);
        });
#endif
    }

    private void OnDisable() {
        HandSurfaceLibrary.Changed -= RefreshHandPreviews;
        pendingHandImages.Clear();
        if (handNameDialog != null) handNameDialog.CloseWithOwner();
        if (handLayoutEditor != null) handLayoutEditor.CloseWithOwner();
        if (handDeleteConfirmation != null) handDeleteConfirmation.CloseMessage();
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.UnbindDrop();
#endif
    }

    public void ShowPanel() {
        gameObject.SetActive(true);
        RefreshPreviews();
    }

    public void HidePanel() {
        gameObject.SetActive(false);
    }

    private void OpenPicker(PickMode mode) {
        if (handNameDialog != null || handLayoutEditor != null || pendingHandImages.Count != 0) return;
        pickMode = mode;
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.PickBytes(ImageAccept, (name, bytes) => {
            if (this != null && isActiveAndEnabled) { pickMode = mode; ApplyBytes(bytes, name); }
        }, err => {
            if (!string.IsNullOrEmpty(err) && err != "empty" && err != "cancel") SceneConfigUi.ShowTip(err);
        });
#elif (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        LocalAssetPick.ReadFile(LocalAssetPick.ImageAndZipFileTypes, (bytes, name) => {
            ApplyBytes(bytes, name);
        }, err => {
            if (!string.IsNullOrEmpty(err) && err != "empty") SceneConfigUi.ShowTip(err);
        });
#else
        bool multi = mode != PickMode.TableBg;
        var extensions = new[] {
            new SFB.ExtensionFilter("牌面背景", "zip", "png", "jpg", "jpeg"),
        };
        string title = mode == PickMode.HandBg ? "选择手牌牌面背景"
            : mode == PickMode.CardBack ? "选择手牌牌背"
            : mode == PickMode.TableBg ? "选择 3D 牌面背景"
            : "选择手牌牌背与手牌背景（zip 或两张图）";
        string[] paths = SFB.StandaloneFileBrowser.OpenFilePanel(title, "", extensions, multi);
        if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;
        if (mode == PickMode.Pair && paths.Length >= 2) {
            ApplyTwoFiles(paths[0], paths[1]);
            return;
        }
        foreach (string path in paths) ApplyLocalPath(path);
#endif
    }

    private void ApplyLocalPath(string path) {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
            SceneConfigUi.ShowTip("文件不存在");
            return;
        }
        try {
            ApplyBytes(File.ReadAllBytes(path), Path.GetFileName(path));
        }
        catch (Exception e) {
            SceneConfigUi.ShowTip("读取失败: " + e.Message);
        }
    }

    private void ApplyTwoFiles(string pathA, string pathB) {
        string handPath = CardBackManager.IsHandBgFileName(pathA) ? pathA
            : CardBackManager.IsHandBgFileName(pathB) ? pathB : pathB;
        string backPath = CardBackManager.IsHandBackFileName(pathA) ? pathA
            : CardBackManager.IsHandBackFileName(pathB) ? pathB : pathA;
        if (handPath == backPath) {
            handPath = pathB;
            backPath = pathA;
        }
        try {
            if (File.Exists(handPath)) QueueHandImage(File.ReadAllBytes(handPath), Path.GetFileName(handPath), false);
            if (File.Exists(backPath)) QueueHandImage(File.ReadAllBytes(backPath), Path.GetFileName(backPath), true);
        }
        catch (Exception e) {
            SceneConfigUi.ShowTip("保存失败: " + e.Message);
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private void OnWebGlBytes(string key, byte[] bytes) {
        if (this != null && isActiveAndEnabled && handNameDialog == null && handLayoutEditor == null) ApplyBytes(bytes, key);
    }
#endif

    private void ApplyBytes(byte[] bytes, string name) {
        if (bytes == null || bytes.Length == 0) return;
        if (CardBackManager.TryParseFaceBodyZip(bytes, out byte[] handBackPng, out byte[] handBgPng)) {
            QueueHandImport(handBgPng, handBackPng, name);
            return;
        }
        if (CardBackManager.TryParseTableBgZip(bytes, out byte[] tableBgPng)) {
            CardBackManager.PersistTableBackground(tableBgPng);
            SceneConfigUi.ShowTip("3D 牌面背景已应用");
            RefreshPreviews();
            return;
        }
        if (CardBackManager.IsZip(bytes)) {
            SceneConfigUi.ShowTip("压缩包需包含 hand-back.png / hand-bg.png 或 table-bg.png");
            return;
        }
        if (pickMode == PickMode.TableBg || (pickMode == PickMode.Pair && CardBackManager.IsTableBgFileName(name))) {
            CardBackManager.PersistTableBackground(bytes);
            SceneConfigUi.ShowTip("3D 牌面背景已应用");
        }
        else if (pickMode == PickMode.HandBg || (pickMode == PickMode.Pair && CardBackManager.IsHandBgFileName(name))) {
            QueueHandImage(bytes, name, false);
        }
        else {
            QueueHandImage(bytes, name, true);
        }
        RefreshPreviews();
    }

    private void RestoreHandBg() {
        CardBackManager.ClearPersistedHandBackground();
        RefreshPreviews();
    }

    private void ClearCardBack() {
        CardBackManager.SelectBuiltinHandSurface(HandSurfaceStyles.DefaultIndex, true);
        RefreshPreviews();
    }

    private void RestoreTableBg() {
        CardBackManager.ClearPersistedTableBackground();
        RefreshPreviews();
    }

    private void ClearTableBg() {
        CardBackManager.ClearPersistedTableBackground();
        SceneConfigUi.ShowTip("已删除 3D 牌面背景");
        RefreshPreviews();
    }

    private void RefreshPreviews() {
        RefreshHandPreviews();
        AssignPreview(tableBgArtwork, ref tableBgSprite, ResolveTableBgTexture());
        RefreshSolidColorUi();
    }

    private void RefreshHandPreviews() {
        RefreshHandSurfaceStyles();
        RefreshHandBackFollowButton();
        if (handLayoutButton != null && ConfigManager.Instance != null)
            handLayoutButton.interactable = HandSurfaceLibrary.CanEditFaceLayout(ConfigManager.Instance.GetSelectedHandBackground().path);
        AssignPreview(handBgPreview, ref handBgSprite, ResolveHandBgTexture());
        AssignPreview(cardBackPreview, ref cardBackSprite, ResolveHandBackTexture());
    }

    private void BindHandLayoutButton() {
        if (handLayoutButton != null) handLayoutButton.onClick.AddListener(() => {
            if (handNameDialog != null || handLayoutEditor != null || ConfigManager.Instance == null) return;
            string path = ConfigManager.Instance.GetSelectedHandBackground().path;
            int index = HandSurfaceStyles.FindIndex(path, false);
            string name = index >= 0 ? HandSurfaceStyles.DisplayName(index) : HandSurfaceLibrary.Find(path)?.name;
            handLayoutEditor = HandFaceLayoutEditor.Open(transform, HandFont, path, name ?? "自定义");
        });
    }

    private void BindHandBackFollowButton() {
        if (handBackFollowButton != null) handBackFollowButton.onClick.AddListener(() => {
            if (handNameDialog != null || handLayoutEditor != null || ConfigManager.Instance == null) return;
            CardBackManager.SetHandBackAutoFollow(!ConfigManager.Instance.HandBackAutoFollow);
            RefreshHandPreviews();
        });
        RefreshHandBackFollowButton();
    }

    private void RefreshHandBackFollowButton() {
        if (handBackFollowButton == null) return;
        SceneConfigUi.SetButtonSelected(handBackFollowButton, ConfigManager.Instance != null && ConfigManager.Instance.HandBackAutoFollow);
        handBackFollowButton.GetComponentInChildren<TMP_Text>(true).color = Color.white;
    }

    private void BindHandSurfaceStyles() {
        TMP_FontAsset font = uploadHandBgButton.GetComponentInChildren<TMP_Text>(true)?.font;
        handBgStyles?.Bind(font, (index, id) => SelectHandSurface(index, id, false), () => RenameHandSurface(false), () => DeleteHandSurface(false));
        handBackStyles?.Bind(font, (index, id) => SelectHandSurface(index, id, true), () => RenameHandSurface(true), () => DeleteHandSurface(true));
    }

    private void SelectHandSurface(int index, string id, bool back) {
        bool success = index >= 0 ? CardBackManager.SelectBuiltinHandSurface(index, back) : CardBackManager.SelectUploadedHandSurface(id, back);
        if (!success) {
            SceneConfigUi.ShowTip("手牌底图暂时无法加载，请重试");
        }
        RefreshPreviews();
    }

    private void RefreshHandSurfaceStyles() {
        if (ConfigManager.Instance == null) return;
        var background = ConfigManager.Instance.GetSelectedHandBackground();
        var back = ConfigManager.Instance.GetSelectedHandBack();
        handBgStyles?.RefreshSelection(background.path, background.isCustom, false);
        handBackStyles?.RefreshSelection(back.path, back.isCustom, true);
    }


    private void RefreshTableBackgroundPreviewLayout() {
        if (tableBgArtwork == null) return;
        tableBgPreview.color = TileFaceResolver.TablePreviewBaseColor;
        tableBgArtwork.enabled = tableBgArtwork.sprite != null;
        CardEdgePanel.FrontEdgeMode mode = ConfigManager.Instance != null
            ? ConfigManager.Instance.FrontEdgeMode : CardBackManager.FrontEdgeMode;
        // 此缩略图展示已保存的背景：普通模式等比留边，明确拉伸模式铺满。
        tableBgArtwork.preserveAspect = mode != CardEdgePanel.FrontEdgeMode.FollowTableBg;
    }

    private static Texture2D ResolveHandBgTexture() {
        Texture2D custom = CardBackManager.LoadSavedHandBackground();
        if (custom != null) return custom;
        return TileFaceResolver.PeekHandBackgroundTexture();
    }

    private static Texture2D ResolveHandBackTexture() {
        Texture2D custom = CardBackManager.LoadSavedHandBack();
        if (custom != null) return custom;
        return TileFaceResolver.PeekDefaultHandBackTexture();
    }

    private static Texture2D ResolveTableBgTexture() {
        Texture2D custom = CardBackManager.LoadSavedTableBackground();
        return custom;
    }

    private static Color CurrentTableFaceColor => ConfigManager.Instance != null
        ? ConfigManager.Instance.TableFaceColor
        : ConfigManager.DefaultTableFaceColor;

    public void RefreshSolidColorUi() {
        // 旧存档或删除背景后可能同时关闭两种模式，此时回到纯色设置。
        if (ConfigManager.Instance != null && !ConfigManager.Instance.UseTableFaceBackground
            && !ConfigManager.Instance.TableFaceUseSolidColor)
            CardBackManager.SetTableFaceSolidColorEnabled(true);
        RefreshTableBackgroundPreviewLayout();
        Color color = CurrentTableFaceColor;
        bool useSolid = ConfigManager.Instance != null && ConfigManager.Instance.TableFaceUseSolidColor;
        tableFaceColorPreview.sprite = null;
        tableFaceColorPreview.color = ConfigManager.ApplyColorBrightness(color, ConfigManager.Instance?.TableFaceBrightness ?? 0f);
        tableFaceHexInput.text = ColorUtility.ToHtmlStringRGB(color);
        SetSolidButton(useTableFaceSolidButton, useSolid);
        SetSolidButton(noTableFaceSolidButton, !useSolid);
        bool useBackground = ConfigManager.Instance != null && ConfigManager.Instance.UseTableFaceBackground;
        if (useTableBackgroundButton != null) SetSolidButton(useTableBackgroundButton, useBackground);
        if (noTableBackgroundButton != null) SetSolidButton(noTableBackgroundButton, useSolid);
        syncingTableFaceColor = true;
        SceneConfigColorUi.SyncChannel(tableFaceSliderR, tableFaceValueR, color.r);
        SceneConfigColorUi.SyncChannel(tableFaceSliderG, tableFaceValueG, color.g);
        SceneConfigColorUi.SyncChannel(tableFaceSliderB, tableFaceValueB, color.b);
        SceneConfigColorUi.SyncBrightness(tableFaceSliderBrightness, tableFaceValueBrightness, ConfigManager.Instance?.TableFaceBrightness ?? 0f);
        syncingTableFaceColor = false;
    }

    private void SetTableFaceRgb(float r, float g, float b) {
        if (syncingTableFaceColor || SceneConfigColorUi.IsLayoutRefresh) return;
        Color color = new Color(r, g, b, 1f);
        CardBackManager.SetTableFaceColor(color);
        RefreshSolidColorUi();
    }

    private void SetTableFaceBrightness(float value) {
        if (syncingTableFaceColor || SceneConfigColorUi.IsLayoutRefresh) return;
        CardBackManager.SetTableFaceBrightness(value / 100f);
        RefreshSolidColorUi();
    }

    private void ApplyTableFaceHex() {
        SceneConfigUi.ApplyHex(tableFaceHexInput, color => {
            color.a = 1f;
            CardBackManager.SetTableFaceColor(color);
            RefreshSolidColorUi();
        }, "已应用 3D 牌面纯色", "颜色格式应为 RRGGBB");
    }

    private void SetTableFaceSolid(bool enabled) {
        // 两组按钮共享同一个选择：图片背景或纯色。
        if (enabled) CardBackManager.SetTableFaceSolidColorEnabled(true);
        else CardBackManager.SetTableFaceBackgroundEnabled(true);
        RefreshSolidColorUi();
        if (CardFaceConfigPanel.Instance != null) {
            CardFaceConfigPanel.Instance.RefreshHighlights();
        }
    }

    private void SetTableBackground(bool enabled) {
        SetTableFaceSolid(!enabled);
    }

    private void RestoreTableFaceColor() {
        CardBackManager.SetTableFaceBrightness(0f);
        CardBackManager.SetTableFaceColor(ConfigManager.DefaultTableFaceColor);
        CardBackManager.SetTableFaceBackgroundEnabled(true);
        RefreshSolidColorUi();
        if (CardFaceConfigPanel.Instance != null) {
            CardFaceConfigPanel.Instance.RefreshHighlights();
        }
    }

    private static void SetSolidButton(Button button, bool on) {
        SceneConfigUi.SetButtonSelected(button, on);
    }

    private static void AssignPreview(Image image, ref Sprite sprite, Texture2D texture) {
        if (sprite != null) {
            UnityEngine.Object.Destroy(sprite);
            sprite = null;
        }
        if (texture == null) {
            image.sprite = null;
            image.color = new Color(0.18f, 0.20f, 0.24f, 1f);
            return;
        }
        sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        image.sprite = sprite;
        image.color = Color.white;
        image.preserveAspect = true;
    }

}
