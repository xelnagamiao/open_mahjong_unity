using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>正面/背面边缘颜色。引用由场景写入，运行时只改颜色和模式。</summary>
public class CardEdgePanel : MonoBehaviour
{
    public static CardEdgePanel Instance { get; private set; }

    /// <summary>背面边缘颜色模式。</summary>
    public enum BackEdgeMode
    {
        /// <summary>独立设置背面边缘颜色。</summary>
        Independent = 0,
        /// <summary>背面边缘颜色跟随牌背颜色。</summary>
        FollowBack = 1,
        /// <summary>背面边缘颜色跟随正面边缘颜色。</summary>
        FollowFront = 2,
    }

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

    private static readonly Color SelectedColor = new Color(1f, 0.55f, 0f, 1f); // 橙色
    private static readonly Color UnselectedColor = new Color(0.2f, 0.24f, 0.32f, 1f);

    [SerializeField] private Image sidePreview;
    [SerializeField] private TMP_InputField sideHexInput;
    [SerializeField] private Button sideHexApplyButton;
    [SerializeField] private Toggle backEdgeModeIndependent;
    [SerializeField] private Toggle backEdgeModeFollowBack;
    [SerializeField] private Toggle backEdgeModeFollowFront;
    [SerializeField] private Toggle backTexExtendEdgeToggle;
    [SerializeField] private Image backSidePreview;
    [SerializeField] private TMP_InputField backEdgeHexInput;
    [SerializeField] private Button backEdgeHexApplyButton;
    [SerializeField] private Button restoreFrontEdgeButton;
    [SerializeField] private Button restoreBackEdgeButton;
    [SerializeField] private CanvasGroup backEdgeSectionGroup;
    [SerializeField] private Button[] sideSwatches;
    [SerializeField] private Button[] backEdgeSwatches;

    private Color currentSideColor = ConfigManager.DefaultSideColor;
    private Color currentBackEdgeColor = ConfigManager.DefaultBackEdgeColor;
    private BackEdgeMode currentBackEdgeMode = BackEdgeMode.FollowBack;
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

    private void BindUi()
    {
        sideHexApplyButton.onClick.AddListener(ApplySideHex);
        backEdgeHexApplyButton.onClick.AddListener(ApplyBackEdgeHex);
        restoreFrontEdgeButton.onClick.AddListener(RestoreFrontEdgeDefault);
        restoreBackEdgeButton.onClick.AddListener(RestoreBackEdgeDefault);
        backEdgeModeIndependent.onValueChanged.AddListener(on => { if (on) SetBackEdgeMode(BackEdgeMode.Independent); });
        backEdgeModeFollowBack.onValueChanged.AddListener(on => { if (on) SetBackEdgeMode(BackEdgeMode.FollowBack); });
        backEdgeModeFollowFront.onValueChanged.AddListener(on => { if (on) SetBackEdgeMode(BackEdgeMode.FollowFront); });
        EnsureBackTexExtendEdgeToggle();
        if (backTexExtendEdgeToggle != null) {
            backTexExtendEdgeToggle.onValueChanged.AddListener(OnBackTexExtendEdgeChanged);
        }
        BindSwatches(sideSwatches, SetSideColor);
        BindSwatches(backEdgeSwatches, SetBackEdgeColor);
        LoadSavedIntoUI();
        ApplyBackEdgeInteractable();
    }

    private static void BindSwatches(Button[] buttons, System.Action<Color> apply)
    {
        int n = buttons != null ? Mathf.Min(buttons.Length, PresetColors.Length) : 0;
        for (int i = 0; i < n; i++)
        {
            Color c = PresetColors[i];
            buttons[i].onClick.AddListener(() => apply(c));
        }
    }

    /// <summary>把正面边缘颜色还原为初始默认值。</summary>
    public void RestoreFrontEdgeDefault()
    {
        currentSideColor = ConfigManager.DefaultSideColor;

        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetSideColor(currentSideColor);
        }

        SyncUIFromColor();
        CardBackManager.ApplySideColor(currentSideColor);
        ShowTip("已恢复默认");
    }

    /// <summary>把背面边缘还原为初始默认：颜色跟随牌背，模式恢复为跟随牌背。</summary>
    public void RestoreBackEdgeDefault()
    {
        currentBackEdgeColor = ConfigManager.DefaultBackEdgeColor;
        currentBackEdgeMode = BackEdgeMode.FollowBack;

        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetBackEdgeMode(currentBackEdgeMode);
            ConfigManager.Instance.SetBackEdgeColor(currentBackEdgeColor);
        }

        SyncUIFromColor();
        CardBackManager.SetBackEdgeMode(currentBackEdgeMode, currentBackEdgeColor);
        ApplyBackEdgeInteractable();
        ShowTip("已恢复默认");
    }

    private void LoadSavedIntoUI()
    {
        if (ConfigManager.Instance != null)
        {
            currentSideColor = ConfigManager.Instance.SideColor;
            currentBackEdgeColor = ConfigManager.Instance.BackEdgeColor;
            currentBackEdgeMode = ConfigManager.Instance.BackEdgeMode;
        }
        SyncUIFromColor();
    }

    private void SyncUIFromColor()
    {
        syncing = true;
        if (sideHexInput != null) sideHexInput.text = ColorUtility.ToHtmlStringRGB(currentSideColor);
        if (sidePreview != null) sidePreview.color = currentSideColor;
        if (backEdgeHexInput != null) backEdgeHexInput.text = ColorUtility.ToHtmlStringRGB(currentBackEdgeColor);
        if (backSidePreview != null) backSidePreview.color = currentBackEdgeColor;

        if (backEdgeModeIndependent != null) backEdgeModeIndependent.isOn = currentBackEdgeMode == BackEdgeMode.Independent;
        if (backEdgeModeFollowBack != null) backEdgeModeFollowBack.isOn = currentBackEdgeMode == BackEdgeMode.FollowBack;
        if (backEdgeModeFollowFront != null) backEdgeModeFollowFront.isOn = currentBackEdgeMode == BackEdgeMode.FollowFront;
        if (backTexExtendEdgeToggle != null) {
            backTexExtendEdgeToggle.isOn = ConfigManager.Instance != null && ConfigManager.Instance.BackTexExtendEdge;
        }
        syncing = false;

        UpdateModeToggleColors();
    }

    private void SetSideColor(Color color)
    {
        color.a = 1f;
        currentSideColor = color;
        SyncUIFromColor();
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetSideColor(currentSideColor);
        }
        CardBackManager.ApplySideColor(currentSideColor);
    }

    private void ApplySideHex()
    {
        string hex = sideHexInput != null ? sideHexInput.text : "";
        if (hex == null) hex = "";
        hex = hex.Trim();
        if (hex.StartsWith("#")) hex = hex.Substring(1);
        if (hex.Length == 6) hex += "FF";
        if (hex.Length != 8 || !ColorUtility.TryParseHtmlString("#" + hex, out Color color))
        {
            ShowTip("HEX 格式不正确");
            return;
        }
        SetSideColor(color);
        ShowTip("正面边缘颜色已应用");
    }

    private void SetBackEdgeColor(Color color)
    {
        color.a = 1f;
        currentBackEdgeColor = color;
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetBackEdgeColor(currentBackEdgeColor);
        }
        // 非独立模式点击背面边缘色块：自动切到独立模式并应用该颜色，保证点击始终有响应。
        if (currentBackEdgeMode != BackEdgeMode.Independent)
        {
            SetBackEdgeMode(BackEdgeMode.Independent);
        }
        else
        {
            SyncUIFromColor();
            CardBackManager.ApplyBackEdgeColor(currentBackEdgeColor);
        }
    }

    /// <summary>设置背面边缘颜色模式：独立 / 跟随牌背 / 跟随正面边缘。</summary>
    public void SetBackEdgeMode(BackEdgeMode mode)
    {
        if (syncing) return;
        currentBackEdgeMode = mode;

        if (mode == BackEdgeMode.FollowBack)
        {
            currentBackEdgeColor = CardBackManager.CurrentColor;
        }
        else if (mode == BackEdgeMode.FollowFront)
        {
            currentBackEdgeColor = currentSideColor;
        }

        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetBackEdgeMode(mode);
            ConfigManager.Instance.SetBackEdgeColor(currentBackEdgeColor);
        }

        SyncUIFromColor();
        CardBackManager.SetBackEdgeMode(mode, currentBackEdgeColor);
        ApplyBackEdgeInteractable();
    }

    /// <summary>
    /// 背面边缘区交互状态：独立模式可编辑；跟随模式仅做视觉提示（变暗），
    /// 色块始终可点（点击自动切到独立模式并应用）。
    /// </summary>
    private void ApplyBackEdgeInteractable()
    {
        bool editable = currentBackEdgeMode == BackEdgeMode.Independent;
        if (backSidePreview != null) backSidePreview.color = currentBackEdgeColor;
        if (backEdgeHexInput != null) backEdgeHexInput.interactable = editable;
        if (backEdgeHexApplyButton != null) backEdgeHexApplyButton.interactable = editable;

        if (backEdgeSectionGroup != null)
        {
            backEdgeSectionGroup.interactable = true;
            backEdgeSectionGroup.blocksRaycasts = true;
            backEdgeSectionGroup.alpha = editable ? 1f : 0.55f;
        }
    }

    /// <summary>选中的模式 Toggle 显示橙色，其余恢复默认色。</summary>
    private void UpdateModeToggleColors()
    {
        SetToggleColor(backEdgeModeIndependent, currentBackEdgeMode == BackEdgeMode.Independent);
        SetToggleColor(backEdgeModeFollowBack, currentBackEdgeMode == BackEdgeMode.FollowBack);
        SetToggleColor(backEdgeModeFollowFront, currentBackEdgeMode == BackEdgeMode.FollowFront);
        SetToggleColor(backTexExtendEdgeToggle, backTexExtendEdgeToggle != null && backTexExtendEdgeToggle.isOn);
    }

    private void OnBackTexExtendEdgeChanged(bool enabled)
    {
        if (syncing) return;
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetBackTexExtendEdge(enabled);
        }
        CardBackManager.ApplyBackTexExtendEdge(enabled);
        UpdateModeToggleColors();
        ShowTip(enabled ? "牌背图片将延伸到背部边缘" : "牌背图片仅覆盖牌背大面");
    }

    private void EnsureBackTexExtendEdgeToggle()
    {
        if (backTexExtendEdgeToggle != null) return;
        Toggle template = backEdgeModeFollowFront != null ? backEdgeModeFollowFront
            : (backEdgeModeFollowBack != null ? backEdgeModeFollowBack : backEdgeModeIndependent);
        if (template == null) return;

        Transform parent = template.transform.parent;
        Transform existing = parent != null ? parent.Find("BackTexExtendEdge") : null;
        if (existing != null)
        {
            backTexExtendEdgeToggle = existing.GetComponent<Toggle>();
            return;
        }

        GameObject clone = Object.Instantiate(template.gameObject, parent, false);
        clone.name = "BackTexExtendEdge";
        RectTransform rt = clone.GetComponent<RectTransform>();
        if (rt != null)
        {
            RectTransform independentRt = backEdgeModeIndependent != null
                ? backEdgeModeIndependent.GetComponent<RectTransform>() : null;
            RectTransform followFrontRt = backEdgeModeFollowFront != null
                ? backEdgeModeFollowFront.GetComponent<RectTransform>() : null;
            float left = independentRt != null
                ? independentRt.anchoredPosition.x - independentRt.sizeDelta.x * 0.5f
                : template.GetComponent<RectTransform>().anchoredPosition.x - 160f;
            float right = followFrontRt != null
                ? followFrontRt.anchoredPosition.x + followFrontRt.sizeDelta.x * 0.5f
                : template.GetComponent<RectTransform>().anchoredPosition.x + 160f;
            float width = Mathf.Max(150f, right - left);
            rt.anchoredPosition = new Vector2((left + right) * 0.5f, template.GetComponent<RectTransform>().anchoredPosition.y - 54f);
            rt.sizeDelta = new Vector2(width, template.GetComponent<RectTransform>().sizeDelta.y);
        }

        TMP_Text label = clone.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.text = "牌背图片延伸牌背边缘";
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
        }

        backTexExtendEdgeToggle = clone.GetComponent<Toggle>();
        if (backTexExtendEdgeToggle != null)
        {
            backTexExtendEdgeToggle.onValueChanged.RemoveAllListeners();
            backTexExtendEdgeToggle.group = null;
            backTexExtendEdgeToggle.isOn = false;
        }
    }

    private static void SetToggleColor(Toggle toggle, bool selected)
    {
        if (toggle == null) return;
        Graphic graphic = toggle.targetGraphic != null ? toggle.targetGraphic : toggle.graphic;
        if (graphic != null)
        {
            graphic.color = selected ? SelectedColor : UnselectedColor;
        }
    }

    private void ApplyBackEdgeHex()
    {
        string hex = backEdgeHexInput != null ? backEdgeHexInput.text : "";
        if (hex == null) hex = "";
        hex = hex.Trim();
        if (hex.StartsWith("#")) hex = hex.Substring(1);
        if (hex.Length == 6) hex += "FF";
        if (hex.Length != 8 || !ColorUtility.TryParseHtmlString("#" + hex, out Color color))
        {
            ShowTip("HEX 格式不正确");
            return;
        }
        SetBackEdgeColor(color);
        ShowTip("背面边缘颜色已应用");
    }

    private void ShowTip(string message)
    {
        if (NotificationManager.Instance != null)
        {
            NotificationManager.Instance.ShowTip("设置", true, message);
        }
        else
        {
            Debug.Log("[CardEdgePanel] " + message);
        }
    }

#if UNITY_EDITOR
    /// <summary>编辑器拖拽入口：把拖入的图片应用到侧面贴图（_SideTex）。</summary>
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

        Material shared = Resources.Load<Material>(CardBackManager.MaterialResourcePath);
        if (shared != null)
        {
            shared.SetTexture("_SideTex", copy);
        }
        ShowTip("侧面贴图已应用");
    }
#endif
}
