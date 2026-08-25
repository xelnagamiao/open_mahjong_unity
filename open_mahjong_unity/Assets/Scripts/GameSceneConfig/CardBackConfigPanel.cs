using TMPro;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using System;
using System.IO;
#endif

/// <summary>牌背颜色与图片。引用由场景写入，运行时只改数值和贴图。</summary>
public class CardBackConfigPanel : MonoBehaviour
{
    public static CardBackConfigPanel Instance { get; private set; }

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
        SceneConfigUi.BindClick(hexApplyButton, ApplyHex);
        SceneConfigUi.BindClick(restoreButton, RestoreDefault);
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
        if (!SceneConfigUi.TryParseHex(hexInput != null ? hexInput.text : "", out Color color))
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
        SyncUIFromColor();
        UpdatePreview();
        CardBackManager.Apply(currentColor, currentTexture);
        SceneConfigUi.ShowTip("已恢复默认颜色（图片请在「牌面背景」中管理）");
    }

    private void ApplyPickedTexture(Texture2D tex, string tip)
    {
        if (tex == null)
        {
            SceneConfigUi.ShowTip("图片加载失败");
            return;
        }
        if (currentTexture != null && currentTexture != tex)
        {
            Destroy(currentTexture);
        }
        currentTexture = tex;
        UpdatePreview();
        CardBackManager.Apply(currentColor, currentTexture);
        if (!string.IsNullOrEmpty(tip)) SceneConfigUi.ShowTip(tip);
    }

    // 编辑器拖拽由场景里已挂的 CardBackEditorDragReceiver 接收。

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
            SceneConfigUi.ShowTip("图片编码失败");
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
            SceneConfigUi.ShowTip("保存图片失败");
        }
    }
#endif
}
