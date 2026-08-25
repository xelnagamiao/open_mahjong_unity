using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>牌背颜色与图片。引用由场景写入，运行时只改数值和贴图。</summary>
public class CardBackConfigPanel : MonoBehaviour
{
    public static CardBackConfigPanel Instance { get; private set; }

    private static readonly Color[] PresetColors =
    {
        new Color(0.218f, 0.372f, 0.66f, 1f),  // 默认蓝
        new Color(0.72f, 0.10f, 0.14f, 1f),
        new Color(0.95f, 0.55f, 0.10f, 1f),
        new Color(0.93f, 0.80f, 0.20f, 1f),
        new Color(0.12f, 0.55f, 0.25f, 1f),
        new Color(0.10f, 0.65f, 0.65f, 1f),
        new Color(0.45f, 0.25f, 0.70f, 1f),
        new Color(0.80f, 0.30f, 0.55f, 1f),
        new Color(0.15f, 0.15f, 0.18f, 1f),
        new Color(0.92f, 0.92f, 0.92f, 1f),
    };

    private const string ImageAccept = "image/png,image/jpeg,image/jpg,image/webp,application/zip,.zip";

    [SerializeField] private Image previewImage;
    [SerializeField] private Slider sliderR;
    [SerializeField] private Slider sliderG;
    [SerializeField] private Slider sliderB;
    [SerializeField] private TMP_Text valueR;
    [SerializeField] private TMP_Text valueG;
    [SerializeField] private TMP_Text valueB;
    [SerializeField] private TMP_InputField hexInput;
    [SerializeField] private Button hexApplyButton;
    [SerializeField] private Button restoreButton;
    [SerializeField] private Button pickImageButton;
    [SerializeField] private Button dropZoneButton;
    [SerializeField] private Button clearImageButton;
    [SerializeField] private Button[] colorSwatches;

    private Color currentColor = ConfigManager.DefaultCardBackColor;
    private Texture2D currentTexture;
    private Sprite previewSprite;
    private bool syncing;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BindUi();
    }

    private void Start()
    {
        CardBackManager.ApplySavedConfig();
    }

    public void ReloadSaved()
    {
        LoadSavedIntoUI();
    }

    private void BindUi()
    {
        AutoWireMissingRefs();

        if (hexApplyButton != null) hexApplyButton.onClick.AddListener(ApplyHex);
        if (restoreButton != null) restoreButton.onClick.AddListener(RestoreDefault);
        if (pickImageButton != null) pickImageButton.gameObject.SetActive(false);
        if (dropZoneButton != null) dropZoneButton.gameObject.SetActive(false);
        if (clearImageButton != null) clearImageButton.gameObject.SetActive(false);
        if (sliderR != null) {
            sliderR.onValueChanged.AddListener(v => SetColor(new Color(v / 255f, currentColor.g, currentColor.b, 1f)));
        }
        if (sliderG != null) {
            sliderG.onValueChanged.AddListener(v => SetColor(new Color(currentColor.r, v / 255f, currentColor.b, 1f)));
        }
        if (sliderB != null) {
            sliderB.onValueChanged.AddListener(v => SetColor(new Color(currentColor.r, currentColor.g, v / 255f, 1f)));
        }
        int n = colorSwatches != null ? Mathf.Min(colorSwatches.Length, PresetColors.Length) : 0;
        bool boundFromArray = false;
        for (int i = 0; i < n; i++)
        {
            if (colorSwatches[i] == null) continue;
            Color c = PresetColors[i];
            colorSwatches[i].onClick.AddListener(() => SetColor(c));
            boundFromArray = true;
        }
        if (!boundFromArray)
        {
            for (int i = 0; i < PresetColors.Length; i++)
            {
                Button button = FindInChildren<Button>(transform, "Swatch" + i);
                if (button == null) continue;
                Color c = PresetColors[i];
                button.onClick.AddListener(() => SetColor(c));
            }
        }
        LoadSavedIntoUI();
    }

    /// <summary>场景 Inspector 引用经常是空的。按子物体名字补挂。</summary>
    private void AutoWireMissingRefs()
    {
        if (previewImage == null) previewImage = FindInChildren<Image>(transform, "PreviewImage");
        if (sliderR == null) sliderR = FindInChildren<Slider>(transform, "RSlider");
        if (sliderG == null) sliderG = FindInChildren<Slider>(transform, "GSlider");
        if (sliderB == null) sliderB = FindInChildren<Slider>(transform, "BSlider");
        if (valueR == null) valueR = FindInChildren<TMP_Text>(transform, "RValue");
        if (valueG == null) valueG = FindInChildren<TMP_Text>(transform, "GValue");
        if (valueB == null) valueB = FindInChildren<TMP_Text>(transform, "BValue");
        if (hexInput == null) hexInput = FindInChildren<TMP_InputField>(transform, "HexInput");
        if (hexApplyButton == null) hexApplyButton = FindInChildren<Button>(transform, "HexApply");
        if (restoreButton == null) restoreButton = FindInChildren<Button>(transform, "RestoreButton");
        if (pickImageButton == null) pickImageButton = FindInChildren<Button>(transform, "PickImageButton");
        if (dropZoneButton == null) dropZoneButton = FindInChildren<Button>(transform, "DropZone");
        if (clearImageButton == null) clearImageButton = FindInChildren<Button>(transform, "ClearImageButton");
        if (colorSwatches == null || colorSwatches.Length == 0)
        {
            colorSwatches = CollectNamedButtons(transform, "Swatch", PresetColors.Length);
        }
    }

    private static Button[] CollectNamedButtons(Transform root, string prefix, int maxCount)
    {
        var list = new System.Collections.Generic.List<Button>();
        for (int i = 0; i < maxCount; i++)
        {
            Button button = FindInChildren<Button>(root, prefix + i);
            if (button == null) break;
            list.Add(button);
        }
        return list.ToArray();
    }

    private static T FindInChildren<T>(Transform root, string name) where T : Component
    {
        if (root == null || string.IsNullOrEmpty(name)) return null;
        foreach (Transform child in root)
        {
            if (child.name == name)
            {
                T comp = child.GetComponent<T>();
                if (comp != null) return comp;
            }
            T nested = FindInChildren<T>(child, name);
            if (nested != null) return nested;
        }
        return null;
    }

    public void ShowPanel()
    {
        gameObject.SetActive(true);
    }

    public void HidePanel()
    {
        gameObject.SetActive(false);
    }

    private void LoadSavedIntoUI()
    {
        if (ConfigManager.Instance != null)
        {
            currentColor = ConfigManager.Instance.CardBackColor;
            currentTexture = CardBackManager.LoadSavedTexture();
        }
        SyncUIFromColor();
        UpdatePreview();
    }

    private void SyncUIFromColor()
    {
        syncing = true;
        if (sliderR != null) sliderR.value = currentColor.r * 255f;
        if (sliderG != null) sliderG.value = currentColor.g * 255f;
        if (sliderB != null) sliderB.value = currentColor.b * 255f;

        string r = Mathf.RoundToInt(currentColor.r * 255f).ToString();
        string g = Mathf.RoundToInt(currentColor.g * 255f).ToString();
        string b = Mathf.RoundToInt(currentColor.b * 255f).ToString();
        if (valueR != null) valueR.text = r;
        if (valueG != null) valueG.text = g;
        if (valueB != null) valueB.text = b;

        string hex = ColorUtility.ToHtmlStringRGB(currentColor);
        if (hexInput != null) hexInput.text = hex;
        syncing = false;
    }

    private void UpdatePreview()
    {
        if (previewImage == null) return;
        if (previewSprite != null) Destroy(previewSprite);
        previewSprite = null;
        if (currentTexture != null)
        {
            previewSprite = Sprite.Create(
                currentTexture,
                new Rect(0f, 0f, currentTexture.width, currentTexture.height),
                new Vector2(0.5f, 0.5f));
            previewImage.sprite = previewSprite;
            // 有图时预览不乘算颜色：白色显示原图，颜色作为底层背景
            previewImage.color = Color.white;
        }
        else
        {
            previewImage.sprite = null;
            previewImage.color = currentColor;
        }
    }

    private void SetColor(Color color)
    {
        if (syncing) return;
        color.a = 1f;
        currentColor = color;
        SyncUIFromColor();
        UpdatePreview();
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetCardBackColor(currentColor);
        }
        CardBackManager.Apply(currentColor, currentTexture);
    }

    private void ApplyHex()
    {
        string hex = hexInput != null ? hexInput.text : "";
        if (hex == null) hex = "";
        hex = hex.Trim();
        if (hex.StartsWith("#")) hex = hex.Substring(1);
        if (hex.Length == 6) hex += "FF";
        if (hex.Length != 8 || !ColorUtility.TryParseHtmlString("#" + hex, out Color color))
        {
            ShowTip("HEX 格式不正确");
            return;
        }
        SetColor(color);
        ShowTip("颜色已应用");
    }

    private void RestoreDefault()
    {
        currentColor = ConfigManager.DefaultCardBackColor;
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetCardBackColor(currentColor);
        }
        SyncUIFromColor();
        UpdatePreview();
        CardBackManager.Apply(currentColor, currentTexture);
        ShowTip("已恢复默认颜色（图片请在「牌面背景」中管理）");
    }

    private void ClearImage()
    {
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetSelectedCardBackImage("", false);
        }
        if (currentTexture != null)
        {
            Destroy(currentTexture);
            currentTexture = null;
        }
        UpdatePreview();
        CardBackManager.Apply(currentColor, null);
        CardBackManager.ClearPersistedHandBackground();
        ShowTip("已清除牌背与手牌背景");
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.Delete(UnityAssetIdb.KeyCardBack, null);
#endif
    }

    // ==================== 图片上传 ====================

    private void OpenFilePicker()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.PickAndPut(UnityAssetIdb.KeyCardBack, ImageAccept, ApplyPickedAsset, err => {
            if (!string.IsNullOrEmpty(err) && err != "empty") ShowTip(err);
        });
#elif (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        NativeGallery.GetImageFromGallery(
            path =>
            {
                if (!string.IsNullOrEmpty(path)) SaveImageFromPath(path);
            },
            "选择牌体图片",
            "image/*");
#else
        var extensions = new[]
        {
            new SFB.ExtensionFilter("牌体", "zip", "png", "jpg", "jpeg", "bmp", "tga", "webp"),
        };
        string[] paths = SFB.StandaloneFileBrowser.OpenFilePanel("选择牌背与手牌背景（可多选或 zip）", "", extensions, true);
        if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0]))
        {
            return;
        }
        if (paths.Length >= 2)
        {
            ApplyTwoFiles(paths[0], paths[1]);
            return;
        }
        SaveImageFromPath(paths[0]);
#endif
    }

    private void SaveImageFromPath(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
        {
            ShowTip("文件不存在");
            return;
        }
        try
        {
            byte[] bytes = File.ReadAllBytes(sourcePath);
            if (ApplyBodyBytes(bytes))
            {
                return;
            }
            string dir = Path.Combine(Application.persistentDataPath, CardBackManager.BackImageDirName);
            Directory.CreateDirectory(dir);
            string ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrEmpty(ext)) ext = ".png";
            string target = Path.Combine(dir, "CardBack_" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + ext);
            File.Copy(sourcePath, target, false);

            if (ConfigManager.Instance != null)
            {
                ConfigManager.Instance.SetSelectedCardBackImage(target, true);
            }
            Texture2D tex = CardBackManager.LoadTextureFromFile(target);
            ApplyPickedTexture(tex, "牌背图片已应用");
        }
        catch (Exception e)
        {
            Debug.LogError("保存牌背图片失败: " + e.Message);
            ShowTip("保存图片失败");
        }
    }

    private void ApplyTwoFiles(string pathA, string pathB)
    {
        string handPath = null;
        string backPath = null;
        if (CardBackManager.IsHandBgFileName(pathA)) handPath = pathA;
        else if (CardBackManager.IsBackFileName(pathA)) backPath = pathA;
        if (CardBackManager.IsHandBgFileName(pathB)) handPath = pathB;
        else if (CardBackManager.IsBackFileName(pathB)) backPath = pathB;
        if (handPath == null && backPath == null)
        {
            backPath = pathA;
            handPath = pathB;
        }
        else if (handPath == null)
        {
            handPath = backPath == pathA ? pathB : pathA;
        }
        else if (backPath == null)
        {
            backPath = handPath == pathA ? pathB : pathA;
        }
        try
        {
            if (!string.IsNullOrEmpty(backPath) && File.Exists(backPath) && !CardBackManager.IsZip(File.ReadAllBytes(backPath)))
            {
                SaveImageFromPath(backPath);
            }
            if (!string.IsNullOrEmpty(handPath) && File.Exists(handPath))
            {
                CardBackManager.PersistHandBackground(File.ReadAllBytes(handPath));
            }
            ShowTip("牌体已应用");
        }
        catch (Exception e)
        {
            Debug.LogError("保存牌体失败: " + e.Message);
            ShowTip("保存牌体失败");
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private void ApplyPickedAsset(string key, byte[] bytes)
    {
        if (ApplyBodyBytes(bytes))
        {
            return;
        }
        ApplyIndexedDbImage(key, bytes);
    }

    private void ApplyIndexedDbImage(string key, byte[] bytes)
    {
        if (ApplyBodyBytes(bytes))
        {
            return;
        }
        Texture2D tex = UnityAssetIdb.ToTexture(bytes);
        if (tex == null)
        {
            ShowTip("图片解析失败");
            return;
        }
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetSelectedCardBackImage(string.IsNullOrEmpty(key) ? UnityAssetIdb.KeyCardBack : key, true);
        }
        ApplyPickedTexture(tex, "牌背图片已应用");
    }
#endif

    private bool ApplyBodyBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return false;
        if (CardBackManager.TryParseBodyZip(bytes, out byte[] backPng, out byte[] handBgPng))
        {
            if (backPng != null)
            {
                PersistCardBackPng(backPng);
            }
            if (handBgPng != null)
            {
                CardBackManager.PersistHandBackground(handBgPng);
            }
            ShowTip(backPng != null && handBgPng != null ? "牌背与手牌背景已应用" : (handBgPng != null ? "手牌背景已应用" : "牌背图片已应用"));
            return true;
        }
        if (CardBackManager.IsZip(bytes))
        {
            ShowTip("压缩包需包含 back.png 与 hand-bg.png");
            return true;
        }
        return false;
    }

    private void PersistCardBackPng(byte[] png)
    {
        Texture2D tex = CardBackManager.DecodePng(png);
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.Put(UnityAssetIdb.KeyCardBack, png, null);
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetSelectedCardBackImage(UnityAssetIdb.KeyCardBack, true);
        }
        ApplyPickedTexture(tex, null);
#else
        try
        {
            string dir = Path.Combine(Application.persistentDataPath, CardBackManager.BackImageDirName);
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, "CardBack_" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".png");
            File.WriteAllBytes(target, png);
            if (ConfigManager.Instance != null)
            {
                ConfigManager.Instance.SetSelectedCardBackImage(target, true);
            }
            if (tex == null) tex = CardBackManager.LoadTextureFromFile(target);
            ApplyPickedTexture(tex, null);
        }
        catch (Exception e)
        {
            Debug.LogError("保存牌背失败: " + e.Message);
            ShowTip("保存牌背失败");
        }
#endif
    }

    private void ApplyPickedTexture(Texture2D tex, string tip)
    {
        if (tex == null)
        {
            ShowTip("图片加载失败");
            return;
        }
        if (currentTexture != null && currentTexture != tex)
        {
            Destroy(currentTexture);
        }
        currentTexture = tex;
        UpdatePreview();
        CardBackManager.Apply(currentColor, currentTexture);
        if (!string.IsNullOrEmpty(tip)) ShowTip(tip);
    }

    private static void SetButtonLabel(Button button, string text)
    {
        if (button == null) return;
        TMP_Text tmp = button.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null) tmp.text = text;
    }

    // ==================== 编辑器拖拽（Project 图片资源）====================
    // 拖拽接收由挂在常驻对象上的 CardBackEditorDragReceiver 处理，
    // 本方法负责把拖进来的纹理落盘并应用为牌背图片。

#if UNITY_EDITOR
    public void ApplyEditorDroppedTexture(Texture2D source)
    {
        if (source == null) return;

        // 用 RenderTexture 拷贝，兼容 Read/Write 关闭的导入纹理
        RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(source, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
        copy.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        byte[] bytes = copy.EncodeToPNG();
        Destroy(copy);
        if (bytes == null || bytes.Length == 0)
        {
            ShowTip("图片编码失败");
            return;
        }
        try
        {
            string dir = Path.Combine(Application.persistentDataPath, CardBackManager.BackImageDirName);
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, "CardBack_" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".png");
            File.WriteAllBytes(target, bytes);
            if (ConfigManager.Instance != null)
            {
                ConfigManager.Instance.SetSelectedCardBackImage(target, true);
            }
            Texture2D tex = CardBackManager.LoadTextureFromFile(target);
            ApplyPickedTexture(tex, "牌背图片已应用");
        }
        catch (Exception ex)
        {
            Debug.LogError("保存拖拽图片失败: " + ex.Message);
            ShowTip("保存图片失败");
        }
    }
#endif

    private void ShowTip(string message)
    {
        if (NotificationManager.Instance != null)
        {
            NotificationManager.Instance.ShowTip("设置", true, message);
        }
        else
        {
            Debug.Log("[CardBackConfigPanel] " + message);
        }
    }
}
