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
    [SerializeField] private TMP_InputField tableFaceHexInput;
    [SerializeField] private Button tableFaceHexApplyButton;
    [SerializeField] private Button useTableFaceSolidButton;
    [SerializeField] private Button noTableFaceSolidButton;
    [SerializeField] private Button restoreTableFaceColorButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text helpText;

    private enum PickMode { HandBg, CardBack, TableBg, Pair }

    private PickMode pickMode = PickMode.Pair;
    private Sprite handBgSprite;
    private Sprite cardBackSprite;
    private Sprite tableBgSprite;

    private void Awake() {
        Instance = this;
        if (uploadHandBgButton != null) uploadHandBgButton.onClick.AddListener(() => OpenPicker(PickMode.HandBg));
        if (uploadCardBackButton != null) uploadCardBackButton.onClick.AddListener(() => OpenPicker(PickMode.CardBack));
        if (uploadPairZipButton != null) uploadPairZipButton.onClick.AddListener(() => OpenPicker(PickMode.Pair));
        if (restoreHandBgButton != null) restoreHandBgButton.onClick.AddListener(RestoreHandBg);
        if (clearCardBackButton != null) clearCardBackButton.onClick.AddListener(ClearCardBack);
        if (uploadTableBgButton != null) uploadTableBgButton.onClick.AddListener(() => OpenPicker(PickMode.TableBg));
        if (restoreTableBgButton != null) restoreTableBgButton.onClick.AddListener(RestoreTableBg);
        if (clearTableBgButton != null) clearTableBgButton.onClick.AddListener(ClearTableBg);
        if (tableFaceHexApplyButton != null) tableFaceHexApplyButton.onClick.AddListener(ApplyTableFaceHex);
        if (useTableFaceSolidButton != null) useTableFaceSolidButton.onClick.AddListener(() => SetTableFaceSolid(true));
        if (noTableFaceSolidButton != null) noTableFaceSolidButton.onClick.AddListener(() => SetTableFaceSolid(false));
        if (restoreTableFaceColorButton != null) restoreTableFaceColorButton.onClick.AddListener(RestoreTableFaceColor);
        if (helpText != null) helpText.text = FormatHelp;
    }

    private void OnEnable() {
        RefreshPreviews();
        RefreshSolidColorUi();
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.BindDrop(UnityAssetIdb.KeyHandBg, OnWebGlBytes, err => {
            if (!string.IsNullOrEmpty(err) && err != "empty") ShowTip(err);
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
            if (!string.IsNullOrEmpty(err) && err != "empty") ShowTip(err);
        });
#elif (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        NativeGallery.GetImageFromGallery(path => {
            if (!string.IsNullOrEmpty(path)) ApplyLocalPath(path);
        }, mode == PickMode.HandBg ? "选择手牌背景"
            : mode == PickMode.CardBack ? "选择手牌牌背"
            : mode == PickMode.TableBg ? "选择 3D 牌面背景"
            : "选择手牌牌背与手牌背景（zip 或两张图）", "image/*");
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
            ShowTip("文件不存在");
            return;
        }
        try {
            ApplyBytes(File.ReadAllBytes(path), Path.GetFileName(path));
        }
        catch (Exception e) {
            ShowTip("读取失败: " + e.Message);
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
            SetStatus("手牌牌背与手牌背景已应用");
            ShowTip("手牌牌背与手牌背景已应用");
            RefreshPreviews();
        }
        catch (Exception e) {
            ShowTip("保存失败: " + e.Message);
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
            SetStatus("已从 zip 应用牌体");
            ShowTip("手牌牌背与手牌背景已应用");
            RefreshPreviews();
            return;
        }
        if (CardBackManager.TryParseTableBgZip(bytes, out byte[] tableBgPng)) {
            CardBackManager.PersistTableBackground(tableBgPng);
            SetStatus("已从 zip 应用 3D 牌面背景");
            ShowTip("3D 牌面背景已应用");
            RefreshPreviews();
            return;
        }
        if (CardBackManager.IsZip(bytes)) {
            ShowTip("压缩包需包含 hand-back.png / hand-bg.png 或 table-bg.png");
            return;
        }
        if (pickMode == PickMode.TableBg || CardBackManager.IsTableBgFileName(name)) {
            CardBackManager.PersistTableBackground(bytes);
            SetStatus("3D 牌面背景已应用");
            ShowTip("3D 牌面背景已应用");
        }
        else if (pickMode == PickMode.HandBg || CardBackManager.IsHandBgFileName(name)) {
            CardBackManager.PersistHandBackground(bytes);
            SetStatus("手牌牌面背景已应用");
            ShowTip("手牌牌面背景已应用");
        }
        else {
            CardBackManager.PersistHandBack(bytes);
            SetStatus("手牌牌背已应用");
            ShowTip("手牌牌背已应用");
        }
        RefreshPreviews();
    }

    private void RestoreHandBg() {
        CardBackManager.ClearPersistedHandBackground();
        SetStatus("已恢复默认手牌背景");
        ShowTip("已恢复默认手牌背景");
        RefreshPreviews();
    }

    private void ClearCardBack() {
        CardBackManager.ClearPersistedHandBack();
        SetStatus("已恢复默认手牌牌背");
        ShowTip("已恢复默认手牌牌背");
        RefreshPreviews();
    }

    private void RestoreTableBg() {
        CardBackManager.ClearPersistedTableBackground();
        SetStatus("已恢复默认 3D 牌面背景");
        ShowTip("已恢复默认 3D 牌面背景");
        RefreshPreviews();
    }

    private void ClearTableBg() {
        CardBackManager.ClearPersistedTableBackground();
        SetStatus("已删除 3D 牌面背景");
        ShowTip("已删除 3D 牌面背景");
        RefreshPreviews();
    }

    private void RefreshPreviews() {
        AssignPreview(handBgPreview, ref handBgSprite, ResolveHandBgTexture());
        AssignPreview(cardBackPreview, ref cardBackSprite, ResolveHandBackTexture());
        AssignPreview(tableBgPreview, ref tableBgSprite, ResolveTableBgTexture());
        RefreshSolidColorUi();
        bool customBg = ConfigManager.Instance != null && ConfigManager.Instance.GetSelectedHandBackground().isCustom;
        bool customBack = ConfigManager.Instance != null && ConfigManager.Instance.GetSelectedHandBack().isCustom;
        bool customTable = ConfigManager.Instance != null && ConfigManager.Instance.GetSelectedTableBackground().isCustom;
        SetStatus((customBg ? "手牌背景：已上传" : "手牌背景：默认")
            + "　"
            + (customBack ? "手牌牌背：已上传" : "手牌牌背：默认")
            + "　"
            + (customTable ? "3D 牌面背景：已上传" : "3D 牌面背景：默认"));
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

    public void RefreshSolidColorUi() {
        Color color = ConfigManager.Instance != null
            ? ConfigManager.Instance.TableFaceColor
            : ConfigManager.DefaultTableFaceColor;
        bool useSolid = ConfigManager.Instance != null && ConfigManager.Instance.TableFaceUseSolidColor;
        if (tableFaceColorPreview != null) {
            tableFaceColorPreview.sprite = null;
            tableFaceColorPreview.color = color;
        }
        if (tableFaceHexInput != null) {
            tableFaceHexInput.text = ColorUtility.ToHtmlStringRGB(color);
        }
        SetSolidButton(useTableFaceSolidButton, useSolid);
        SetSolidButton(noTableFaceSolidButton, !useSolid);
    }

    private void ApplyTableFaceHex() {
        if (tableFaceHexInput == null) return;
        string hex = (tableFaceHexInput.text ?? "").Trim().TrimStart('#');
        if (hex.Length == 6) hex += "FF";
        if (hex.Length != 8 || !ColorUtility.TryParseHtmlString("#" + hex, out Color color)) {
            ShowTip("颜色格式应为 RRGGBB");
            return;
        }
        color.a = 1f;
        CardBackManager.SetTableFaceColor(color);
        RefreshSolidColorUi();
        ShowTip("已应用 3D 牌面纯色");
    }

    private void SetTableFaceSolid(bool enabled) {
        CardBackManager.SetTableFaceSolidColorEnabled(enabled);
        RefreshSolidColorUi();
        if (CardFaceConfigPanel.Instance != null) {
            CardFaceConfigPanel.Instance.RefreshHighlights();
        }
        ShowTip(enabled ? "已使用 3D 牌面纯色（已关闭 3D 牌面背景）" : "已关闭 3D 牌面纯色");
    }

    private void RestoreTableFaceColor() {
        CardBackManager.SetTableFaceColor(ConfigManager.DefaultTableFaceColor);
        CardBackManager.SetTableFaceSolidColorEnabled(false);
        RefreshSolidColorUi();
        if (CardFaceConfigPanel.Instance != null) {
            CardFaceConfigPanel.Instance.RefreshHighlights();
        }
        ShowTip("已恢复默认 3D 牌面颜色");
    }

    private static void SetSolidButton(Button button, bool on) {
        if (button == null) return;
        if (button.transition != Selectable.Transition.None) {
            button.transition = Selectable.Transition.None;
        }
        Image image = button.GetComponent<Image>();
        if (image != null) {
            image.color = on
                ? new Color(0.28f, 0.48f, 0.92f, 1f)
                : new Color(0.17f, 0.21f, 0.30f, 1f);
        }
    }

    private static void AssignPreview(Image image, ref Sprite sprite, Texture2D texture) {
        if (image == null) return;
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

    private void SetStatus(string message) {
        if (statusText != null) statusText.text = message ?? "";
    }

    private static void ShowTip(string message) {
        if (NotificationManager.Instance != null) {
            NotificationManager.Instance.ShowTip("设置", true, message);
        }
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
        ShowTip("3D 牌面背景已应用");
    }
#endif
}
