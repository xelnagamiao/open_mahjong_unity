using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 牌背配置面板：运行时直接在场景里生成 UI（无需预制体/场景编辑）。
/// 支持：预设颜色 + RGB 滑条 + HEX 输入；上传牌背图片（桌面文件框 / 移动相册 / WebGL 文件框）；
/// 拖拽：WebGL 浏览器把图片文件拖进窗口，编辑器里把 Project 里的图片资源拖进 Game 视图。
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

    private static readonly Color PanelBg = new Color(0.06f, 0.07f, 0.10f, 0.97f);
    private static readonly Color ButtonBg = new Color(0.17f, 0.21f, 0.30f, 1f);
    private static readonly Color Accent = new Color(0.28f, 0.48f, 0.92f, 1f);
    private static readonly Color LabelColor = new Color(0.82f, 0.85f, 0.90f, 1f);

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void UploadFileJS(string gameObjectName, string methodName, string filter);

    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void InitCardBackDrop(string gameObjectName, string methodName);
#endif

    private GameObject panelObject;
    private Image previewImage;
    private Slider sliderR;
    private Slider sliderG;
    private Slider sliderB;
    private TMP_InputField hexInput;
    private TMP_Text valueR;
    private TMP_Text valueG;
    private TMP_Text valueB;

    private Color currentColor = ConfigManager.DefaultCardBackColor;
    private Texture2D currentTexture;
    private Sprite previewSprite;
    private bool syncing;

    /// <summary>确保场景配置面板下存在牌背面板（不重复创建）。</summary>
    public static void EnsureCreated(Transform parent)
    {
        if (parent == null) return;
        if (parent.GetComponentInChildren<CardBackConfigPanel>(true) != null) return;
        parent.gameObject.AddComponent<CardBackConfigPanel>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        BuildUI();
        LoadSavedIntoUI();
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

    // ==================== UI 构建 ====================

    private void BuildUI()
    {
        RectTransform root = (RectTransform)transform;

        // 旧版烘焙进场景的同名对象先清理，再按新样式重建，避免重复
        DestroyCardBackChildren(transform);
        Transform nav = transform.Find("NavigateBar");
        if (nav != null) DestroyCardBackChildren(nav);

        // 左侧导航栏里加一个“牌背”tab（与桌布/边框/角色并列，由 VerticalLayoutGroup 自动排列）
        if (nav != null)
        {
            Button tabButton = NewButton((RectTransform)nav, "CardBackButton", "牌背", ButtonBg, Color.white);
            RectTransform tabRt = (RectTransform)tabButton.transform;
            tabRt.anchorMin = Vector2.zero;
            tabRt.anchorMax = Vector2.zero;
            tabRt.pivot = new Vector2(0.5f, 0.5f);
            tabRt.anchoredPosition = Vector2.zero;
            tabRt.sizeDelta = new Vector2(354.42f, 107.253f);

            // 插到“角色”按钮后面、隐藏按钮前面
            Transform characterButton = nav.Find("CharacterButton");
            tabButton.transform.SetSiblingIndex(
                characterButton != null ? characterButton.GetSiblingIndex() + 1 : nav.childCount - 1);
            tabButton.onClick.AddListener(ShowInSceneConfig);
        }

        // 主面板：放在配置内容区（与 TableClothPanel 同一水平中心）
        panelObject = NewRect("CardBackPanel", root).gameObject;
        RectTransform panelRt = (RectTransform)panelObject.transform;
        panelRt.anchorMin = new Vector2(0f, 1f);
        panelRt.anchorMax = new Vector2(0f, 1f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.anchoredPosition = new Vector2(1169f, -490f);
        panelRt.sizeDelta = new Vector2(900f, 820f);

        Image bg = panelObject.AddComponent<Image>();
        bg.color = PanelBg;

        // 标题 + 关闭
        TMP_Text title = NewText(panelRt, "Title", "牌背设置", 22, Color.white, TextAnchor.MiddleCenter);
        StretchTop(title.rectTransform, 0f, 40f, 0f);

        Button closeBtn = NewButton(panelRt, "CloseButton", "关闭", ButtonBg, Color.white);
        RectTransform closeRt = (RectTransform)closeBtn.transform;
        closeRt.anchorMin = new Vector2(1f, 1f);
        closeRt.anchorMax = new Vector2(1f, 1f);
        closeRt.pivot = new Vector2(1f, 1f);
        closeRt.anchoredPosition = new Vector2(-8f, -6f);
        closeRt.sizeDelta = new Vector2(56f, 30f);
        closeBtn.onClick.AddListener(() => panelObject.SetActive(false));

        // 当前预览
        TMP_Text previewLabel = NewText(panelRt, "PreviewLabel", "当前牌背", 15, LabelColor, TextAnchor.MiddleLeft);
        StretchTop(previewLabel.rectTransform, 10f, 18f, 56f);
        previewImage = NewImage(panelRt, "PreviewImage", currentColor);
        RectTransform previewRt = (RectTransform)previewImage.transform;
        previewRt.anchorMin = new Vector2(0.5f, 1f);
        previewRt.anchorMax = new Vector2(0.5f, 1f);
        previewRt.pivot = new Vector2(0.5f, 1f);
        previewRt.anchoredPosition = new Vector2(0f, -88f);
        previewRt.sizeDelta = new Vector2(280f, 150f);

        // 颜色区
        TMP_Text colorLabel = NewText(panelRt, "ColorLabel", "颜色", 17, Color.white, TextAnchor.MiddleLeft);
        StretchTop(colorLabel.rectTransform, 10f, 22f, 208f);

        int swatchCols = 5;
        float swatchSize = 62f;
        float swatchGap = 76f;
        float startX = -((swatchCols - 1) * swatchGap) * 0.5f;
        for (int i = 0; i < PresetColors.Length; i++)
        {
            int row = i / swatchCols;
            int col = i % swatchCols;
            Color preset = PresetColors[i];
            Button swatch = NewButton(panelRt, "Swatch" + i, "", preset, Color.white);
            RectTransform swRt = (RectTransform)swatch.transform;
            swRt.anchorMin = new Vector2(0.5f, 1f);
            swRt.anchorMax = new Vector2(0.5f, 1f);
            swRt.pivot = new Vector2(0.5f, 1f);
            swRt.anchoredPosition = new Vector2(startX + col * swatchGap, -238f - row * (swatchSize + 10f));
            swRt.sizeDelta = new Vector2(swatchSize, swatchSize);
            Color c = preset;
            swatch.onClick.AddListener(() => SetColor(c));
        }

        // RGB 滑条
        CreateSliderRow(panelRt, "R", ref sliderR, ref valueR, -366f, Color.red, currentColor.r * 255f, v => SetColor(new Color(v, currentColor.g, currentColor.b, 1f)));
        CreateSliderRow(panelRt, "G", ref sliderG, ref valueG, -400f, Color.green, currentColor.g * 255f, v => SetColor(new Color(currentColor.r, v, currentColor.b, 1f)));
        CreateSliderRow(panelRt, "B", ref sliderB, ref valueB, -434f, Color.blue, currentColor.b * 255f, v => SetColor(new Color(currentColor.r, currentColor.g, v, 1f)));

        // HEX 输入
        TMP_Text hexLabel = NewText(panelRt, "HexLabel", "HEX", 14, LabelColor, TextAnchor.MiddleRight);
        RectTransform hexLabelRt = hexLabel.rectTransform;
        hexLabelRt.anchorMin = new Vector2(0f, 1f);
        hexLabelRt.anchorMax = new Vector2(0f, 1f);
        hexLabelRt.pivot = new Vector2(0f, 1f);
        hexLabelRt.anchoredPosition = new Vector2(16f, -462f);
        hexLabelRt.sizeDelta = new Vector2(48f, 24f);

        hexInput = CreateInput(panelRt, "HexInput", "RRGGBB 或 RRGGBBAA");
        RectTransform hexRt = (RectTransform)hexInput.transform;
        hexRt.anchorMin = new Vector2(0f, 1f);
        hexRt.anchorMax = new Vector2(0f, 1f);
        hexRt.pivot = new Vector2(0f, 1f);
        hexRt.anchoredPosition = new Vector2(72f, -470f);
        hexRt.sizeDelta = new Vector2(360f, 28f);

        Button hexApply = NewButton(panelRt, "HexApply", "应用", Accent, Color.white);
        RectTransform hexApplyRt = (RectTransform)hexApply.transform;
        hexApplyRt.anchorMin = new Vector2(0f, 1f);
        hexApplyRt.anchorMax = new Vector2(0f, 1f);
        hexApplyRt.pivot = new Vector2(0f, 1f);
        hexApplyRt.anchoredPosition = new Vector2(448f, -470f);
        hexApplyRt.sizeDelta = new Vector2(76f, 28f);
        hexApply.onClick.AddListener(ApplyHex);

        // 恢复默认
        Button restore = NewButton(panelRt, "RestoreButton", "恢复默认颜色并清除图片", ButtonBg, Color.white);
        RectTransform restoreRt = (RectTransform)restore.transform;
        restoreRt.anchorMin = new Vector2(0.5f, 1f);
        restoreRt.anchorMax = new Vector2(0.5f, 1f);
        restoreRt.pivot = new Vector2(0.5f, 1f);
        restoreRt.anchoredPosition = new Vector2(0f, -512f);
        restoreRt.sizeDelta = new Vector2(440f, 36f);
        restore.onClick.AddListener(RestoreDefault);

        // 图片区
        TMP_Text imageLabel = NewText(panelRt, "ImageLabel", "牌背图片", 17, Color.white, TextAnchor.MiddleLeft);
        StretchTop(imageLabel.rectTransform, 10f, 22f, 566f);

        Button pickButton = NewButton(panelRt, "PickImageButton", "选择图片", Accent, Color.white);
        RectTransform pickRt = (RectTransform)pickButton.transform;
        pickRt.anchorMin = new Vector2(0.5f, 1f);
        pickRt.anchorMax = new Vector2(0.5f, 1f);
        pickRt.pivot = new Vector2(0.5f, 1f);
        pickRt.anchoredPosition = new Vector2(0f, -602f);
        pickRt.sizeDelta = new Vector2(440f, 38f);
        pickButton.onClick.AddListener(OpenFilePicker);

        // 拖拽区
        Button dropZone = NewButton(panelRt, "DropZone", "", new Color(0.10f, 0.12f, 0.16f, 1f), Color.white);
        RectTransform dropRt = (RectTransform)dropZone.transform;
        dropRt.anchorMin = new Vector2(0.5f, 1f);
        dropRt.anchorMax = new Vector2(0.5f, 1f);
        dropRt.pivot = new Vector2(0.5f, 1f);
        dropRt.anchoredPosition = new Vector2(0f, -652f);
        dropRt.sizeDelta = new Vector2(440f, 96f);
        dropZone.onClick.AddListener(OpenFilePicker);
        TMP_Text dropText = NewText(dropRt, "DropText", "把图片文件拖到这里\n(WebGL 浏览器 / 编辑器 Project 资源)", 13, LabelColor, TextAnchor.MiddleCenter);
        Stretch(dropText.rectTransform);

        Button clearButton = NewButton(panelRt, "ClearImageButton", "清除图片（恢复纯色）", ButtonBg, Color.white);
        RectTransform clearRt = (RectTransform)clearButton.transform;
        clearRt.anchorMin = new Vector2(0.5f, 1f);
        clearRt.anchorMax = new Vector2(0.5f, 1f);
        clearRt.pivot = new Vector2(0.5f, 1f);
        clearRt.anchoredPosition = new Vector2(0f, -764f);
        clearRt.sizeDelta = new Vector2(440f, 36f);
        clearButton.onClick.AddListener(ClearImage);

        // 随机桌面：填满副露/手牌/牌河，方便预览牌背颜色与图片
        Button randomTableBtn = NewButton(panelRt, "RandomTableButton", "随机桌面：生成副露/手牌/牌河", Accent, Color.white);
        RectTransform randomRt = (RectTransform)randomTableBtn.transform;
        randomRt.anchorMin = new Vector2(0.5f, 1f);
        randomRt.anchorMax = new Vector2(0.5f, 1f);
        randomRt.pivot = new Vector2(0.5f, 1f);
        randomRt.anchoredPosition = new Vector2(0f, -814f);
        randomRt.sizeDelta = new Vector2(440f, 38f);
        randomTableBtn.onClick.AddListener(RandomTableButton.GenerateRandomTable);

        panelObject.SetActive(false);
    }

    private void ShowInSceneConfig()
    {
        SceneConfigPanel sceneConfig = GetComponent<SceneConfigPanel>();
        if (sceneConfig != null) sceneConfig.ShowCardBackPanel();
    }

    public GameObject PanelObject => panelObject;

    public void ShowPanel()
    {
        if (panelObject != null) panelObject.SetActive(true);
    }

    public void HidePanel()
    {
        if (panelObject != null) panelObject.SetActive(false);
    }

    private void CreateSliderRow(
        RectTransform parent,
        string name,
        ref Slider slider,
        ref TMP_Text valueText,
        float y,
        Color accent,
        float initialValue,
        Action<float> onChanged)
    {
        TMP_Text label = NewText(parent, name + "Label", name, 14, LabelColor, TextAnchor.MiddleRight);
        RectTransform labelRt = label.rectTransform;
        labelRt.anchorMin = new Vector2(0f, 1f);
        labelRt.anchorMax = new Vector2(0f, 1f);
        labelRt.pivot = new Vector2(0f, 1f);
        labelRt.anchoredPosition = new Vector2(16f, y);
        labelRt.sizeDelta = new Vector2(44f, 20f);

        slider = CreateSlider(parent, name + "Slider", 0f, 255f, initialValue);
        RectTransform sliderRt = (RectTransform)slider.transform;
        sliderRt.anchorMin = new Vector2(0f, 1f);
        sliderRt.anchorMax = new Vector2(0f, 1f);
        sliderRt.pivot = new Vector2(0f, 1f);
        sliderRt.anchoredPosition = new Vector2(66f, y + 8f);
        sliderRt.sizeDelta = new Vector2(560f, 16f);
        slider.fillRect.GetComponent<Image>().color = accent;
        slider.onValueChanged.AddListener(v => onChanged(v));

        valueText = NewText(parent, name + "Value", "0", 14, Color.white, TextAnchor.MiddleLeft);
        RectTransform valueRt = valueText.rectTransform;
        valueRt.anchorMin = new Vector2(0f, 1f);
        valueRt.anchorMax = new Vector2(0f, 1f);
        valueRt.pivot = new Vector2(0f, 1f);
        valueRt.anchoredPosition = new Vector2(640f, y);
        valueRt.sizeDelta = new Vector2(64f, 20f);
    }

    // ==================== 状态同步 ====================

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
        sliderR.value = currentColor.r * 255f;
        sliderG.value = currentColor.g * 255f;
        sliderB.value = currentColor.b * 255f;
        valueR.text = Mathf.RoundToInt(currentColor.r * 255f).ToString();
        valueG.text = Mathf.RoundToInt(currentColor.g * 255f).ToString();
        valueB.text = Mathf.RoundToInt(currentColor.b * 255f).ToString();
        hexInput.text = ColorUtility.ToHtmlStringRGB(currentColor);
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
        }
        else
        {
            previewImage.sprite = null;
        }
        previewImage.color = currentColor;
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
        string hex = (hexInput != null ? hexInput.text : "").Trim();
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
        HandleWebGLFileData(data, saveToPrefs: true);
    }

    public void OnCardBackFileDropped(string data)
    {
        HandleWebGLFileData(data, saveToPrefs: true);
    }

    private void HandleWebGLFileData(string data, bool saveToPrefs)
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

    // ==================== 编辑器拖拽（Project 图片资源） ====================

#if UNITY_EDITOR
    private void OnGUI()
    {
        Event e = Event.current;
        if (e.type == EventType.DragUpdated)
        {
            bool hasImage = false;
            foreach (UnityEngine.Object obj in UnityEditor.DragAndDrop.objectReferences)
            {
                if (obj is Texture2D || obj is Sprite)
                {
                    hasImage = true;
                    break;
                }
            }
            if (hasImage)
            {
                UnityEditor.DragAndDrop.visualMode = UnityEditor.DragAndDropVisualMode.Copy;
                e.Use();
            }
        }
        else if (e.type == EventType.DragPerform)
        {
            bool applied = false;
            foreach (UnityEngine.Object obj in UnityEditor.DragAndDrop.objectReferences)
            {
                Texture2D tex = obj as Texture2D;
                if (tex == null && obj is Sprite sprite)
                {
                    tex = sprite.texture;
                }
                if (tex != null)
                {
                    SaveTextureAsCustom(tex);
                    applied = true;
                }
            }
            if (applied)
            {
                UnityEditor.DragAndDrop.AcceptDrag();
                e.Use();
            }
        }
    }

    private void SaveTextureAsCustom(Texture2D source)
    {
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

    // ==================== UI 组件工厂 ====================

    private static TMP_FontAsset _tmpFont;
    private static TMP_FontAsset TmpFont()
    {
        if (_tmpFont != null) return _tmpFont;
        _tmpFont = Resources.Load<TMP_FontAsset>("font/Chinese/AlibabaPuHuiTi/AlibabaPuHuiTi-3-55-Regular SDF");
        if (_tmpFont == null) _tmpFont = TMP_Settings.defaultFontAsset;
        if (_tmpFont == null) _tmpFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        return _tmpFont;
    }

    private static Sprite _whiteSprite;
    private static Sprite WhiteSprite()
    {
        if (_whiteSprite == null)
        {
            _whiteSprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f));
        }
        return _whiteSprite;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static Image NewImage(RectTransform parent, string name, Color color)
    {
        RectTransform rt = NewRect(name, parent);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    private static TMP_Text NewText(RectTransform parent, string name, string content, int size, Color color, TextAnchor anchor)
    {
        RectTransform rt = NewRect(name, parent);
        TMP_Text text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = TmpFont();
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = TmpAlignment(anchor);
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static TextAlignmentOptions TmpAlignment(TextAnchor anchor)
    {
        switch (anchor)
        {
            case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
            case TextAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
            case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
            case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
            default: return TextAlignmentOptions.Midline;
        }
    }

    private static Button NewButton(RectTransform parent, string name, string label, Color bg, Color textColor)
    {
        RectTransform rt = NewRect(name, parent);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = bg;
        img.sprite = WhiteSprite();
        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        button.transition = Selectable.Transition.None;
        if (!string.IsNullOrEmpty(label))
        {
            TMP_Text text = NewText(rt, "Label", label, 15, textColor, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
        }
        return button;
    }

    private static Slider CreateSlider(RectTransform parent, string name, float min, float max, float value)
    {
        RectTransform rt = NewRect(name, parent);
        Image bg = rt.gameObject.AddComponent<Image>();
        bg.color = new Color(0.20f, 0.22f, 0.28f, 1f);

        RectTransform fillRt = NewRect("Fill", rt);
        Stretch(fillRt);
        Image fill = fillRt.gameObject.AddComponent<Image>();
        fill.sprite = WhiteSprite();
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.color = Accent;

        RectTransform handleRt = NewRect("Handle", rt);
        handleRt.anchorMin = new Vector2(0f, 0.5f);
        handleRt.anchorMax = new Vector2(0f, 0.5f);
        handleRt.pivot = new Vector2(0.5f, 0.5f);
        handleRt.sizeDelta = new Vector2(16f, 20f);
        Image handle = handleRt.gameObject.AddComponent<Image>();
        handle.sprite = WhiteSprite();
        handle.color = Color.white;

        Slider slider = rt.gameObject.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;
        slider.fillRect = fillRt;
        slider.handleRect = handleRt;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    private static TMP_InputField CreateInput(RectTransform parent, string name, string placeholderText)
    {
        RectTransform rt = NewRect(name, parent);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0.13f, 0.15f, 0.20f, 1f);

        TMP_Text text = NewText(rt, "Text", "", 14, Color.white, TextAnchor.MiddleLeft);
        RectTransform textRt = text.rectTransform;
        Stretch(textRt);
        textRt.offsetMin = new Vector2(8f, 0f);
        textRt.offsetMax = new Vector2(-8f, 0f);

        TMP_Text ph = NewText(rt, "Placeholder", placeholderText, 14, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleLeft);
        RectTransform phRt = ph.rectTransform;
        Stretch(phRt);
        phRt.offsetMin = new Vector2(8f, 0f);
        phRt.offsetMax = new Vector2(-8f, 0f);

        TMP_InputField input = rt.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = img;
        input.textComponent = text;
        input.placeholder = ph;
        return input;
    }

    private static void DestroyCardBackChildren(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (child == null) continue;
            string childName = child.name;
            if (string.IsNullOrEmpty(childName)) continue;
            if (childName.Contains("CardBack") || childName.Contains("牌背"))
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void StretchTop(RectTransform rt, float left, float height, float y)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -y);
        rt.sizeDelta = new Vector2(-left * 2f, height);
    }
}
