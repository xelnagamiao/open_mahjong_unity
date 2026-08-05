using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 牌背设置面板：只负责牌背颜色与牌背图片。
/// 界面已在场景里手工搭建（CardBackPanel），本组件按名字自动查找并挂接事件；
/// Inspector 里手动拖拽的引用优先。牌边设置由独立的 CardEdgePanel 负责。
/// </summary>
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

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void UploadFileJS(string gameObjectName, string methodName, string filter);

    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void InitCardBackDrop(string gameObjectName, string methodName);
#endif

    [Header("拖拽挂接（留空则按场景内名字自动查找）")]
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

    // 兼容烘焙进场景的旧版 Text/InputField
    private Text legacyValueR;
    private Text legacyValueG;
    private Text legacyValueB;
    private InputField legacyHexInput;

    private Color currentColor = ConfigManager.DefaultCardBackColor;
    private Texture2D currentTexture;
    private Sprite previewSprite;
    private bool syncing;
    private bool wired;

    /// <summary>挂到场景里已画好的 CardBackPanel 上；已存在组件则直接复用。</summary>
    public static CardBackConfigPanel AttachToScenePanel(Transform sceneConfigRoot)
    {
        if (sceneConfigRoot == null) return null;
        CardBackConfigPanel existing = sceneConfigRoot.GetComponentInChildren<CardBackConfigPanel>(true);
        if (existing != null) return existing;
        Transform panel = FindChildByName(sceneConfigRoot, "CardBackPanel");
        if (panel == null) return null;
        return panel.gameObject.AddComponent<CardBackConfigPanel>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        AutoWire();
    }

    private void Start()
    {
        CardBackManager.ApplySavedConfig();
    }

    private void OnEnable()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try
        {
            InitCardBackDrop(gameObject.name, "OnCardBackFileDropped");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"初始化牌背拖拽失败: {e.Message}");
        }
#endif
    }

    /// <summary>按名字查找并挂接事件；Inspector 已拖拽的引用优先。</summary>
    private void AutoWire()
    {
        if (wired) return;
        wired = true;

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

        // 旧版 Text/InputField 兜底（烘焙进场景的旧版面板）
        if (valueR == null) legacyValueR = FindInChildren<Text>(transform, "RValue");
        if (valueG == null) legacyValueG = FindInChildren<Text>(transform, "GValue");
        if (valueB == null) legacyValueB = FindInChildren<Text>(transform, "BValue");
        if (hexInput == null) legacyHexInput = FindInChildren<InputField>(transform, "HexInput");

        if (hexApplyButton != null) hexApplyButton.onClick.AddListener(ApplyHex);
        if (restoreButton != null) restoreButton.onClick.AddListener(RestoreDefault);
        if (pickImageButton != null) pickImageButton.onClick.AddListener(OpenFilePicker);
        if (dropZoneButton != null) dropZoneButton.onClick.AddListener(OpenFilePicker);
        if (clearImageButton != null) clearImageButton.onClick.AddListener(ClearImage);

        if (sliderR != null)
            sliderR.onValueChanged.AddListener(v => SetColor(new Color(v / 255f, currentColor.g, currentColor.b, 1f)));
        if (sliderG != null)
            sliderG.onValueChanged.AddListener(v => SetColor(new Color(currentColor.r, v / 255f, currentColor.b, 1f)));
        if (sliderB != null)
            sliderB.onValueChanged.AddListener(v => SetColor(new Color(currentColor.r, currentColor.g, v / 255f, 1f)));

        for (int i = 0; i < PresetColors.Length; i++)
        {
            Button swatch = FindInChildren<Button>(transform, "Swatch" + i);
            if (swatch == null) continue;
            Color c = PresetColors[i];
            swatch.onClick.AddListener(() => SetColor(c));
        }

        LoadSavedIntoUI();
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
        else if (legacyValueR != null) legacyValueR.text = r;
        if (valueG != null) valueG.text = g;
        else if (legacyValueG != null) legacyValueG.text = g;
        if (valueB != null) valueB.text = b;
        else if (legacyValueB != null) legacyValueB.text = b;

        string hex = ColorUtility.ToHtmlStringRGB(currentColor);
        if (hexInput != null) hexInput.text = hex;
        else if (legacyHexInput != null) legacyHexInput.text = hex;
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
        string hex = hexInput != null ? hexInput.text : (legacyHexInput != null ? legacyHexInput.text : "");
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
            ConfigManager.Instance.SetSelectedCardBackImage("", false);
        }
        if (currentTexture != null)
        {
            Destroy(currentTexture);
            currentTexture = null;
        }
        SyncUIFromColor();
        UpdatePreview();
        CardBackManager.Apply(currentColor, null);
        ShowTip("已恢复默认");
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
        ShowTip("已清除牌背图片");
    }

    // ==================== 图片上传 ====================

    private void OpenFilePicker()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try
        {
            UploadFileJS(gameObject.name, "OnCardBackFileSelected", "image/png,image/jpeg,image/jpg");
        }
        catch (Exception e)
        {
            Debug.LogError("WebGL 打开文件框失败: " + e.Message);
        }
#elif (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        NativeGallery.GetImageFromGallery(
            path =>
            {
                if (!string.IsNullOrEmpty(path)) SaveImageFromPath(path);
            },
            "选择牌背图片",
            "image/*");
#else
        var extensions = new[]
        {
            new SFB.ExtensionFilter("Image Files", "png", "jpg", "jpeg", "bmp", "tga"),
        };
        string[] paths = SFB.StandaloneFileBrowser.OpenFilePanel("选择牌背图片", "", extensions, false);
        if (paths != null && paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
        {
            SaveImageFromPath(paths[0]);
        }
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
            ApplyPickedTexture(tex);
        }
        catch (Exception e)
        {
            Debug.LogError("保存牌背图片失败: " + e.Message);
            ShowTip("保存图片失败");
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    public void OnCardBackFileSelected(string data)
    {
        HandleWebGLFileData(data);
    }

    public void OnCardBackFileDropped(string data)
    {
        HandleWebGLFileData(data);
    }

    private void HandleWebGLFileData(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            ShowTip("未选择文件");
            return;
        }
        try
        {
            string[] parts = data.Split('|');
            if (parts.Length < 1) return;
            string base64 = parts[0];
            int commaIndex = base64.IndexOf(",");
            if (commaIndex >= 0) base64 = base64.Substring(commaIndex + 1);
            byte[] bytes = Convert.FromBase64String(base64);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, bytes))
            {
                Destroy(tex);
                ShowTip("图片解析失败");
                return;
            }

            string ext = parts.Length > 2 ? parts[2] : ".png";
            string saved = base64 + "|" + ext + "|" + (parts.Length > 1 ? parts[1] : "cardback");
            PlayerPrefs.SetString(CardBackManager.WebGLImageKey, saved);
            PlayerPrefs.Save();
            if (ConfigManager.Instance != null)
            {
                ConfigManager.Instance.SetSelectedCardBackImage(CardBackManager.WebGLImageKey, true);
            }
            ApplyPickedTexture(tex);
        }
        catch (Exception e)
        {
            Debug.LogError("WebGL 牌背图片处理失败: " + e.Message);
            ShowTip("图片处理失败");
        }
    }
#endif

    private void ApplyPickedTexture(Texture2D tex)
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
        ShowTip("牌背图片已应用");
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
            ApplyPickedTexture(tex);
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

    // ==================== 场景查找 ====================

    private static Transform FindChildByName(Transform root, string name)
    {
        if (root == null) return null;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == name) return child;
        }
        return null;
    }

    private static T FindInChildren<T>(Transform root, string name) where T : Component
    {
        if (root == null) return null;
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
}
