using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 场景设置「牌面背景」页：手牌牌面背景与 2D 手牌牌背（里宝暗面）分开上传。
/// </summary>
public class CardFaceBackgroundPanel : MonoBehaviour {
    public static CardFaceBackgroundPanel Instance { get; private set; }

    public const string FormatHelp =
        "手牌牌面背景：2D 牌体（含顶部牌沿），建议 272×389。\n"
        + "手牌牌背：2D 暗面图样（里宝牌未翻开等），不是 3D 牌背。\n"
        + "也可上传 zip：hand-back.png + hand-bg.png。\n"
        + "3D 牌背颜色请到「牌背」页设置。\n"
        + "3D 牌面纯色与「使用 3D 牌面背景」互斥，开启后花纹仍保留、底色换成所选颜色。\n"
        + "透明花纹牌面请在「牌面」页打开「使用牌面背景」，整图牌面请关闭。";

    private const string ImageAccept = "image/png,image/jpeg,image/jpg,image/webp,application/zip,.zip";

    [SerializeField] private Image handBgPreview;
    [SerializeField] private Image cardBackPreview;
    [SerializeField] private Image tableBgPreview;
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
    [SerializeField] private TMP_Text tableFaceValueR;
    [SerializeField] private TMP_Text tableFaceValueG;
    [SerializeField] private TMP_Text tableFaceValueB;
    [SerializeField] private TMP_InputField tableFaceHexInput;
    [SerializeField] private Button tableFaceHexApplyButton;
    [SerializeField] private Button useTableFaceSolidButton;
    [SerializeField] private Button noTableFaceSolidButton;
    [SerializeField] private Button restoreTableFaceColorButton;
    [SerializeField] private TMP_Text helpText;

    private enum PickMode { HandBg, CardBack, TableBg, Pair }

    private PickMode pickMode = PickMode.Pair;
    private Sprite handBgSprite;
    private Sprite cardBackSprite;
    private Sprite tableBgSprite;
    private bool syncingTableFaceColor;

    private void Awake() {
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
        tableFaceSliderR.onValueChanged.AddListener(v => SetTableFaceRgb(v / 255f, CurrentTableFaceColor.g, CurrentTableFaceColor.b));
        tableFaceSliderG.onValueChanged.AddListener(v => SetTableFaceRgb(CurrentTableFaceColor.r, v / 255f, CurrentTableFaceColor.b));
        tableFaceSliderB.onValueChanged.AddListener(v => SetTableFaceRgb(CurrentTableFaceColor.r, CurrentTableFaceColor.g, v / 255f));
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
        AssignPreview(tableBgPreview, ref tableBgSprite, ResolveTableBgTexture());
        RefreshSolidColorUi();
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
        Color color = CurrentTableFaceColor;
        bool useSolid = ConfigManager.Instance != null && ConfigManager.Instance.TableFaceUseSolidColor;
        tableFaceColorPreview.sprite = null;
        tableFaceColorPreview.color = color;
        tableFaceHexInput.text = ColorUtility.ToHtmlStringRGB(color);
        SetSolidButton(useTableFaceSolidButton, useSolid);
        SetSolidButton(noTableFaceSolidButton, !useSolid);
        syncingTableFaceColor = true;
        tableFaceSliderR.value = color.r * 255f;
        tableFaceSliderG.value = color.g * 255f;
        tableFaceSliderB.value = color.b * 255f;
        tableFaceValueR.text = Mathf.RoundToInt(color.r * 255f).ToString();
        tableFaceValueG.text = Mathf.RoundToInt(color.g * 255f).ToString();
        tableFaceValueB.text = Mathf.RoundToInt(color.b * 255f).ToString();
        syncingTableFaceColor = false;
    }

    private void SetTableFaceRgb(float r, float g, float b) {
        if (syncingTableFaceColor) return;
        Color color = new Color(r, g, b, 1f);
        CardBackManager.SetTableFaceColor(color);
        RefreshSolidColorUi();
    }

    private void ApplyTableFaceHex() {
        if (!SceneConfigUi.TryParseHex(tableFaceHexInput.text, out Color color)) {
            SceneConfigUi.ShowTip("颜色格式应为 RRGGBB");
            return;
        }
        color.a = 1f;
        CardBackManager.SetTableFaceColor(color);
        RefreshSolidColorUi();
        SceneConfigUi.ShowTip("已应用 3D 牌面纯色");
    }

    private void SetTableFaceSolid(bool enabled) {
        CardBackManager.SetTableFaceSolidColorEnabled(enabled);
        RefreshSolidColorUi();
        if (CardFaceConfigPanel.Instance != null) {
            CardFaceConfigPanel.Instance.RefreshHighlights();
        }
    }

    private void RestoreTableFaceColor() {
        CardBackManager.SetTableFaceColor(ConfigManager.DefaultTableFaceColor);
        CardBackManager.SetTableFaceSolidColorEnabled(false);
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
        RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(source, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
        copy.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        byte[] png = copy.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(copy);
        CardBackManager.PersistTableBackground(png);
        RefreshPreviews();
        SceneConfigUi.ShowTip("3D 牌面背景已应用");
    }
#endif
}
