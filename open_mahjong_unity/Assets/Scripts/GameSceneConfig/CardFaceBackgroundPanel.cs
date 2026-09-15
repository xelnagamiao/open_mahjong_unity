using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

/// <summary>
/// 场景设置「牌面背景」页：手牌牌面背景与 2D 手牌牌背（里宝暗面）分开上传。
/// </summary>
public class CardFaceBackgroundPanel : MonoBehaviour {
    public static CardFaceBackgroundPanel Instance { get; private set; }

    public const string FormatHelp =
        "手牌牌面背景：显示在 2D 手牌牌面下方，建议宽高比 272:389（272×389 像素）。\n"
        + "手牌牌背：显示 2D 暗面图样（例如里宝牌未翻开），不是 3D 牌背。\n"
        + "单独上传：点击对应标签下的上传按钮，分别选择 hand-bg.png 或 hand-back.png。\n"
        + "也可一次上传 zip，文件夹示例：\n"
        + "  MyHandImages.zip/\n"
        + "  ├─ hand-bg.png    手牌牌面背景\n"
        + "  └─ hand-back.png  手牌牌背\n"
        + "透明花纹牌面请在「牌面」页打开「使用牌面背景」，整张牌面请关闭。\n"
        + "背景与牌背可以分别上传、分别恢复；3D 牌面背景只用于 3D 卡牌正面，不是手牌背景。\n"
        + "请到「3D 卡牌设计」中的「3D牌面背景」标签设置背景和纯色，3D 牌背请到「牌背」标签设置。";

    private const string ImageAccept = "image/png,image/jpeg,image/jpg,image/webp,application/zip,.zip";

    [SerializeField] private Image handBgPreview;
    [SerializeField] private Image cardBackPreview;
    [SerializeField] private Image tableBgPreview;
    [SerializeField] private GameObject tilePreviewPrefab;
    [SerializeField] private Button uploadHandBgButton;
    [SerializeField] private Button uploadCardBackButton;
    [SerializeField] private Button uploadPairZipButton;
    [SerializeField] private Button restoreHandBgButton;
    [SerializeField] private Button clearCardBackButton;
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
    private Image tableBgArtwork;
    private bool syncingTableFaceColor;

    private void Awake() {
        if (helpText != null) helpText.text = FormatHelp;
        Instance = this;
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
        RefreshPreviews();
        RefreshSolidColorUi();
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.BindDrop(UnityAssetIdb.KeyHandBg, OnWebGlBytes, err => {
            if (!string.IsNullOrEmpty(err) && err != "empty") SceneConfigUi.ShowTip(err);
        });
#endif
    }

    private void OnDisable() {
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
        pickMode = mode;
#if UNITY_WEBGL && !UNITY_EDITOR
        string key = mode == PickMode.CardBack ? UnityAssetIdb.KeyHandBack
            : mode == PickMode.TableBg ? UnityAssetIdb.KeyTableBg
            : UnityAssetIdb.KeyHandBg;
        UnityAssetIdb.PickAndPut(key, ImageAccept, OnWebGlBytes, err => {
            if (!string.IsNullOrEmpty(err) && err != "empty") SceneConfigUi.ShowTip(err);
        });
#elif (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        LocalAssetPick.ReadFile(LocalAssetPick.ImageAndZipFileTypes, (bytes, name) => {
            ApplyBytes(bytes, name);
        }, err => {
            if (!string.IsNullOrEmpty(err) && err != "empty") SceneConfigUi.ShowTip(err);
        });
#else
        bool multi = mode == PickMode.Pair;
        var extensions = new[] {
            new SFB.ExtensionFilter("牌面背景", "zip", "png", "jpg", "jpeg", "webp", "bmp"),
        };
        string title = mode == PickMode.HandBg ? "选择手牌牌面背景"
            : mode == PickMode.CardBack ? "选择手牌牌背"
            : mode == PickMode.TableBg ? "选择 3D 牌面背景"
            : "选择手牌牌背与手牌背景（zip 或两张图）";
        string[] paths = SFB.StandaloneFileBrowser.OpenFilePanel(title, "", extensions, multi);
        if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;
        if (paths.Length >= 2) {
            ApplyTwoFiles(paths[0], paths[1]);
            return;
        }
        ApplyLocalPath(paths[0]);
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
            if (File.Exists(backPath)) CardBackManager.PersistHandBack(File.ReadAllBytes(backPath));
            if (File.Exists(handPath)) CardBackManager.PersistHandBackground(File.ReadAllBytes(handPath));
            SceneConfigUi.ShowTip("手牌牌背与手牌背景已应用");
            RefreshPreviews();
        }
        catch (Exception e) {
            SceneConfigUi.ShowTip("保存失败: " + e.Message);
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private void OnWebGlBytes(string key, byte[] bytes) {
        ApplyBytes(bytes, key);
    }
#endif

    private void ApplyBytes(byte[] bytes, string name) {
        if (bytes == null || bytes.Length == 0) return;
        if (CardBackManager.TryParseFaceBodyZip(bytes, out byte[] handBackPng, out byte[] handBgPng)) {
            if (handBackPng != null) CardBackManager.PersistHandBack(handBackPng);
            if (handBgPng != null) CardBackManager.PersistHandBackground(handBgPng);
            SceneConfigUi.ShowTip("手牌牌背与手牌背景已应用");
            RefreshPreviews();
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
        if (pickMode == PickMode.TableBg || CardBackManager.IsTableBgFileName(name)) {
            CardBackManager.PersistTableBackground(bytes);
            SceneConfigUi.ShowTip("3D 牌面背景已应用");
        }
        else if (pickMode == PickMode.HandBg || CardBackManager.IsHandBgFileName(name)) {
            CardBackManager.PersistHandBackground(bytes);
            SceneConfigUi.ShowTip("手牌牌面背景已应用");
        }
        else {
            CardBackManager.PersistHandBack(bytes);
            SceneConfigUi.ShowTip("手牌牌背已应用");
        }
        RefreshPreviews();
    }

    private void RestoreHandBg() {
        CardBackManager.ClearPersistedHandBackground();
        RefreshPreviews();
    }

    private void ClearCardBack() {
        CardBackManager.ClearPersistedHandBack();
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
        AssignPreview(handBgPreview, ref handBgSprite, ResolveHandBgTexture());
        AssignPreview(cardBackPreview, ref cardBackSprite, ResolveHandBackTexture());
        EnsureTableBackgroundPreview();
        AssignPreview(tableBgArtwork, ref tableBgSprite, ResolveTableBgTexture());
        RefreshSolidColorUi();
    }

    private void EnsureTableBackgroundPreview() {
        if (tableBgArtwork != null) return;
        RectTransform frame = tableBgPreview.rectTransform;
        TileTextureLayout.FitRenderedCardPreview(frame, tilePreviewPrefab);
        tableBgPreview.sprite = null;
        tableBgPreview.preserveAspect = false;
        GameObject artwork = new GameObject("TableBackgroundArtwork", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        artwork.layer = tableBgPreview.gameObject.layer;
        artwork.transform.SetParent(frame, false);
        tableBgArtwork = artwork.GetComponent<Image>();
        tableBgArtwork.raycastTarget = false;
        RectTransform content = tableBgArtwork.rectTransform;
        content.anchorMin = Vector2.zero;
        content.anchorMax = Vector2.one;
        content.offsetMin = content.offsetMax = Vector2.zero;
    }

    private void RefreshTableBackgroundPreviewLayout() {
        EnsureTableBackgroundPreview();
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

#if UNITY_EDITOR
    /// <summary>编辑器拖拽入口：把拖入的图片应用到 3D 牌面背景。</summary>
    public void ApplyEditorDroppedTableBackground(Texture2D source) {
        if (source == null) return;
        byte[] png = SceneConfigTextureCapture.EncodePng(source);
        CardBackManager.PersistTableBackground(png);
        RefreshPreviews();
        SceneConfigUi.ShowTip("3D 牌面背景已应用");
    }
#endif
}
