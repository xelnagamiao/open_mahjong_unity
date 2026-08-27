using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>牌背颜色与 3D 牌背图片。引用由场景拖好，运行时只改数值和贴图。</summary>
public class CardBackConfigPanel : MonoBehaviour
{
    public static CardBackConfigPanel Instance { get; private set; }

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

    private void OnEnable()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.BindDrop(UnityAssetIdb.KeyCardBack, OnWebGlBytes, err => {
            if (!string.IsNullOrEmpty(err) && err != "empty") SceneConfigUi.ShowTip(err);
        });
#endif
    }

    private void OnDisable()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.UnbindDrop();
#endif
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
        SceneConfigUi.BindClick(hexApplyButton, ApplyHex);
        SceneConfigUi.BindClick(restoreButton, RestoreDefault);
        SceneConfigUi.BindClick(pickImageButton, OpenFilePicker);
        SceneConfigUi.BindClick(dropZoneButton, OpenFilePicker);
        SceneConfigUi.BindClick(clearImageButton, ClearImage);
        sliderR.onValueChanged.AddListener(v => SetColor(new Color(v / 255f, currentColor.g, currentColor.b, 1f)));
        sliderG.onValueChanged.AddListener(v => SetColor(new Color(currentColor.r, v / 255f, currentColor.b, 1f)));
        sliderB.onValueChanged.AddListener(v => SetColor(new Color(currentColor.r, currentColor.g, v / 255f, 1f)));
        SceneConfigUi.BindSwatches(colorSwatches, SetColor);
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
        if (previewSprite != null) Destroy(previewSprite);
        previewSprite = null;
        if (currentTexture != null)
        {
            previewSprite = Sprite.Create(
                currentTexture,
                new Rect(0f, 0f, currentTexture.width, currentTexture.height),
                new Vector2(0.5f, 0.5f));
            previewImage.sprite = previewSprite;
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
        if (!SceneConfigUi.TryParseHex(hexInput.text, out Color color))
        {
            SceneConfigUi.ShowTip("HEX 格式不正确");
            return;
        }
        SetColor(color);
        SceneConfigUi.ShowTip("颜色已应用");
    }

    private void RestoreDefault()
    {
        currentColor = ConfigManager.DefaultCardBackColor;
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetCardBackColor(currentColor);
        }
        ClearPersistedImage();
        CardBackManager.ClearPersistedCardBack();
        SyncUIFromColor();
        UpdatePreview();
    }

    private void ClearImage()
    {
        ClearPersistedImage();
        CardBackManager.ClearPersistedCardBack();
        UpdatePreview();
        SceneConfigUi.ShowTip("已清除牌背图片");
    }

    private void ClearPersistedImage()
    {
        if (currentTexture != null)
        {
            Destroy(currentTexture);
            currentTexture = null;
        }
    }

    private void OpenFilePicker()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.PickAndPut(UnityAssetIdb.KeyCardBack, ImageAccept, OnWebGlBytes, err => {
            if (!string.IsNullOrEmpty(err) && err != "empty") SceneConfigUi.ShowTip(err);
        });
#elif (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        NativeGallery.GetImageFromGallery(path => {
            if (!string.IsNullOrEmpty(path)) ApplyLocalPath(path);
        }, "选择牌背图片", "image/*");
#else
        var extensions = new[]
        {
            new SFB.ExtensionFilter("牌背图片", "zip", "png", "jpg", "jpeg", "bmp", "tga", "webp"),
        };
        string[] paths = SFB.StandaloneFileBrowser.OpenFilePanel("选择 3D 牌背图片", "", extensions, false);
        if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;
        ApplyLocalPath(paths[0]);
#endif
    }

    private void ApplyLocalPath(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
        {
            SceneConfigUi.ShowTip("文件不存在");
            return;
        }
        try
        {
            ApplyBodyBytes(File.ReadAllBytes(sourcePath), Path.GetFileName(sourcePath));
        }
        catch (Exception e)
        {
            Debug.LogError("保存牌背图片失败: " + e.Message);
            SceneConfigUi.ShowTip("保存图片失败");
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private void OnWebGlBytes(string key, byte[] bytes)
    {
        ApplyBodyBytes(bytes, key);
    }
#endif

    private void ApplyBodyBytes(byte[] bytes, string name)
    {
        if (bytes == null || bytes.Length == 0)
        {
            SceneConfigUi.ShowTip("图片加载失败");
            return;
        }
        if (CardBackManager.TryParseBodyZip(bytes, out byte[] backPng, out byte[] handBgPng))
        {
            if (backPng != null)
            {
                ApplyCardBackPng(backPng);
            }
            if (handBgPng != null)
            {
                SceneConfigUi.ShowTip(backPng != null
                    ? "牌背图片已应用；手牌背景请到「牌面背景」页上传"
                    : "这是手牌背景，请到「牌面背景」页上传");
            }
            else if (backPng != null)
            {
                SceneConfigUi.ShowTip("牌背图片已应用");
            }
            return;
        }
        if (CardBackManager.IsZip(bytes))
        {
            SceneConfigUi.ShowTip("压缩包需包含 back.png");
            return;
        }
        ApplyCardBackPng(bytes);
        SceneConfigUi.ShowTip("牌背图片已应用");
    }

    private void ApplyCardBackPng(byte[] png)
    {
        CardBackManager.PersistCardBackImage(png);
        currentTexture = CardBackManager.LoadSavedTexture();
        UpdatePreview();
    }

#if UNITY_EDITOR
    public void ApplyEditorDroppedTexture(Texture2D source)
    {
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

        byte[] bytes = copy.EncodeToPNG();
        Destroy(copy);
        ApplyCardBackPng(bytes);
        SceneConfigUi.ShowTip("牌背图片已应用");
    }
#endif
}
