using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

/// <summary>牌背颜色与 3D 牌背图片。引用由场景拖好，运行时只改数值和贴图。</summary>
public class CardBackConfigPanel : MonoBehaviour
{
    public static CardBackConfigPanel Instance { get; private set; }

    private const string ImageAccept = "image/png,image/jpeg,image/jpg,image/webp,application/zip,.zip";

    [SerializeField] private Image previewImage;
    [SerializeField] private GameObject tilePreviewPrefab;
    [SerializeField] private Slider sliderR;
    [SerializeField] private Slider sliderG;
    [SerializeField] private Slider sliderB;
    [FormerlySerializedAs("sliderGray"), SerializeField] private Slider sliderBrightness;
    [SerializeField] private TMP_Text valueR;
    [SerializeField] private TMP_Text valueG;
    [SerializeField] private TMP_Text valueB;
    [FormerlySerializedAs("valueGray"), SerializeField] private TMP_Text valueBrightness;
    [SerializeField] private TMP_InputField hexInput;
    [SerializeField] private Button hexApplyButton;
    [SerializeField] private Button restoreButton;
    [SerializeField] private Button pickImageButton;
    [SerializeField] private Button dropZoneButton;
    [SerializeField] private Button clearImageButton;
    [SerializeField] private Button[] colorSwatches;

    private Color currentColor = ConfigManager.DefaultCardBackColor;
    private float currentBrightness;
    private Texture2D currentTexture;
    private Sprite previewSprite;
    private Image previewArtwork;
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
        // 打开设置只展示当前状态；已初始化的对局不应再重放存档、清掉逐牌提示色。
        CardBackManager.EnsureSavedConfigApplied();
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
        if (sliderBrightness != null)
            sliderBrightness.onValueChanged.AddListener(SetBrightness);
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
            currentBrightness = ConfigManager.Instance.CardBackBrightness;
            currentTexture = CardBackManager.LoadSavedTexture();
        }
        SyncUIFromColor();
        UpdatePreview();
    }

    private void SyncUIFromColor()
    {
        syncing = true;
        SceneConfigColorUi.SyncChannel(sliderR, valueR, currentColor.r);
        SceneConfigColorUi.SyncChannel(sliderG, valueG, currentColor.g);
        SceneConfigColorUi.SyncChannel(sliderB, valueB, currentColor.b);
        SceneConfigColorUi.SyncBrightness(sliderBrightness, valueBrightness, currentBrightness);
        hexInput.text = ColorUtility.ToHtmlStringRGB(currentColor);
        syncing = false;
    }

    private void UpdatePreview()
    {
        if (previewArtwork == null)
        {
            TileTextureLayout.FitRenderedCardPreview(previewImage.rectTransform, tilePreviewPrefab);
            var artwork = new GameObject("CardBackArtwork", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            artwork.layer = previewImage.gameObject.layer;
            artwork.transform.SetParent(previewImage.transform, false);
            previewArtwork = artwork.GetComponent<Image>();
            previewArtwork.raycastTarget = false;
            // Match the back shader: stretch the artwork, composite alpha over the base color.
            previewArtwork.preserveAspect = false;
            previewArtwork.rectTransform.anchorMin = Vector2.zero;
            previewArtwork.rectTransform.anchorMax = Vector2.one;
            previewArtwork.rectTransform.offsetMin = previewArtwork.rectTransform.offsetMax = Vector2.zero;
        }
        if (previewSprite != null) Destroy(previewSprite);
        previewSprite = null;
        previewImage.sprite = null;
        previewImage.preserveAspect = false;
        previewImage.color = ConfigManager.ApplyColorBrightness(currentColor, currentBrightness);
        if (currentTexture != null)
        {
            previewSprite = Sprite.Create(
                currentTexture,
                new Rect(0f, 0f, currentTexture.width, currentTexture.height),
                new Vector2(0.5f, 0.5f));
        }
        previewArtwork.sprite = previewSprite;
        previewArtwork.color = Color.white;
        previewArtwork.enabled = previewSprite != null;
    }

    private void SetColor(Color color)
    {
        if (syncing || SceneConfigColorUi.IsLayoutRefresh) return;
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

    private void SetBrightness(float value)
    {
        if (syncing || SceneConfigColorUi.IsLayoutRefresh) return;
        currentBrightness = Mathf.Clamp(value / 100f, -1f, 1f);
        ConfigManager.Instance?.SetCardBackBrightness(currentBrightness);
        SyncUIFromColor();
        UpdatePreview();
        CardBackManager.Apply(currentColor, currentTexture);
    }

    private void ApplyHex()
    {
        SceneConfigUi.ApplyHex(hexInput, SetColor, "颜色已应用");
    }

    private void RestoreDefault()
    {
        currentColor = ConfigManager.DefaultCardBackColor;
        currentBrightness = 0f;
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetCardBackBrightness(0f);
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
        LocalAssetPick.ReadFile(LocalAssetPick.ImageAndZipFileTypes, (bytes, name) => {
            ApplyBodyBytes(bytes, name);
        }, err => {
            if (!string.IsNullOrEmpty(err) && err != "empty") SceneConfigUi.ShowTip(err);
        });
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

        byte[] bytes = SceneConfigTextureCapture.EncodePng(source);
        ApplyCardBackPng(bytes);
        SceneConfigUi.ShowTip("牌背图片已应用");
    }
#endif
}
