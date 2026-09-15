using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

/// <summary>正面/背面边缘颜色。引用由场景写入，运行时只改颜色和模式。</summary>
public class CardEdgePanel : MonoBehaviour
{
    public static CardEdgePanel Instance { get; private set; }

    /// <summary>正面边缘颜色模式（与背面边缘三档对称）。</summary>
    public enum FrontEdgeMode
    {
        /// <summary>独立设置正面边缘颜色。</summary>
        Independent = 0,
        /// <summary>把 3D 牌面背景颜色与图像拉伸到侧面。</summary>
        FollowTableBg = 1,
        /// <summary>正面边缘跟随背面边缘的独立颜色（不拉伸贴图）。</summary>
        FollowBackEdge = 2,
    }

    /// <summary>背面边缘颜色模式。</summary>
    public enum BackEdgeMode
    {
        /// <summary>独立设置背面边缘颜色。</summary>
        Independent = 0,
        /// <summary>背面边缘颜色跟随牌背颜色。</summary>
        FollowBack = 1,
        /// <summary>背面边缘跟随正面边缘的独立颜色（不拉伸贴图）。</summary>
        FollowFront = 2,
    }

    [SerializeField] private Image sidePreview;
    [SerializeField] private TMP_InputField sideHexInput;
    [SerializeField] private Button sideHexApplyButton;
    [SerializeField] private Toggle backEdgeModeIndependent;
    [SerializeField] private Toggle backEdgeModeFollowBack;
    [SerializeField] private Toggle backEdgeModeFollowFront;
    [SerializeField] private Toggle frontEdgeModeIndependent;
    [SerializeField] private Toggle frontEdgeModeFollowTableBg;
    [SerializeField] private Toggle frontEdgeModeFollowBackEdge;
    [SerializeField] private Image backSidePreview;
    [SerializeField] private Image frontSidePreview;
    [SerializeField] private TMP_InputField backEdgeHexInput;
    [SerializeField] private Button backEdgeHexApplyButton;
    [SerializeField] private TMP_InputField frontEdgeHexInput;
    [SerializeField] private Button frontEdgeHexApplyButton;
    [SerializeField] private Button restoreFrontEdgeButton;
    [SerializeField] private Button restoreBackEdgeButton;
    [SerializeField] private Button[] sideSwatches;
    [SerializeField] private Button[] backEdgeSwatches;
    [Header("边缘调色器")]
    [SerializeField] private Button frontEdgeColorButton;
    [SerializeField] private Button backEdgeColorButton;
    [SerializeField] private Button edgeColorPickerCloseButton;
    [SerializeField] private GameObject edgeColorPicker;
    [SerializeField] private TMP_Text edgeColorPickerTitle;
    [SerializeField] private Slider edgeColorSliderR;
    [SerializeField] private Slider edgeColorSliderG;
    [SerializeField] private Slider edgeColorSliderB;
    [FormerlySerializedAs("edgeColorSliderGray"), SerializeField] private Slider edgeColorSliderBrightness;
    [SerializeField] private TMP_Text edgeColorValueR;
    [SerializeField] private TMP_Text edgeColorValueG;
    [SerializeField] private TMP_Text edgeColorValueB;
    [FormerlySerializedAs("edgeColorValueGray"), SerializeField] private TMP_Text edgeColorValueBrightness;
    [Header("模式 Toggle 颜色（含 Alpha，可在 Inspector 改）")]
    [SerializeField] private Color toggleDefaultColor = new Color(0.28f, 0.48f, 0.92f, 1f);
    [SerializeField] private Color toggleSelectedColor = new Color(1f, 0.5f, 0f, 1f);
    [SerializeField] private float toggleColorFade = 0.1f;

    private Color currentSideColor = ConfigManager.DefaultSideColor;
    private Color currentBackEdgeColor = ConfigManager.DefaultBackEdgeColor;
    private Color currentFrontEdgeColor = Color.white;
    private BackEdgeMode currentBackEdgeMode = BackEdgeMode.FollowBack;
    private FrontEdgeMode currentFrontEdgeMode = FrontEdgeMode.Independent;
    private bool syncing;
    private bool syncingEdgeColorPicker;
    private bool toggleColorReady;
    private bool toggleColorsNeedRefresh;
    private EdgeColorTarget edgeColorTarget = EdgeColorTarget.Front;

    private enum EdgeColorTarget
    {
        Front,
        Back,
    }

    private Color EffectiveFrontColor => ConfigManager.ApplyColorBrightness(currentFrontEdgeColor, ConfigManager.Instance?.FrontEdgeBrightness ?? 0f);
    private Color EffectiveBackColor => ConfigManager.ApplyColorBrightness(currentBackEdgeColor, ConfigManager.Instance?.BackEdgeBrightness ?? 0f);

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
        LoadSavedIntoUI();
        CloseEdgeColorPicker();
        toggleColorsNeedRefresh = true;
        UpdateModeToggleColors(instant: true);
    }

    private void LateUpdate()
    {
        if (!toggleColorsNeedRefresh) return;
        toggleColorsNeedRefresh = false;
        UpdateModeToggleColors(instant: true);
    }

    private void Start()
    {
        UpdateModeToggleColors(instant: true);
        toggleColorReady = true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        UpdateModeToggleColors(instant: true);
    }
#endif

    private void BindUi()
    {
        // The merged scene aliases the legacy side controls to the front controls.
        // Binding both callbacks lets the first refresh overwrite the typed HEX.
        if (sideHexApplyButton != frontEdgeHexApplyButton)
            SceneConfigUi.BindClick(sideHexApplyButton, ApplySideHex);
        SceneConfigUi.BindClick(backEdgeHexApplyButton, ApplyBackEdgeHex);
        SceneConfigUi.BindClick(frontEdgeHexApplyButton, ApplyFrontEdgeHex);
        SceneConfigUi.BindClick(restoreFrontEdgeButton, RestoreFrontEdgeDefault);
        SceneConfigUi.BindClick(restoreBackEdgeButton, RestoreBackEdgeDefault);
        SceneConfigUi.ConfigureToggle(backEdgeModeIndependent);
        SceneConfigUi.ConfigureToggle(backEdgeModeFollowBack);
        SceneConfigUi.ConfigureToggle(backEdgeModeFollowFront);
        SceneConfigUi.ConfigureToggle(frontEdgeModeIndependent);
        SceneConfigUi.ConfigureToggle(frontEdgeModeFollowTableBg);
        SceneConfigUi.ConfigureToggle(frontEdgeModeFollowBackEdge);
        SceneConfigUi.BindToggleOn(backEdgeModeIndependent, () => SetBackEdgeMode(BackEdgeMode.Independent));
        SceneConfigUi.BindToggleOn(backEdgeModeFollowBack, () => SetBackEdgeMode(BackEdgeMode.FollowBack));
        SceneConfigUi.BindToggleOn(backEdgeModeFollowFront, () => SetBackEdgeMode(BackEdgeMode.FollowFront));
        SceneConfigUi.BindToggleOn(frontEdgeModeIndependent, () => SetFrontEdgeMode(FrontEdgeMode.Independent));
        SceneConfigUi.BindToggleOn(frontEdgeModeFollowTableBg, () => SetFrontEdgeMode(FrontEdgeMode.FollowTableBg));
        SceneConfigUi.BindToggleOn(frontEdgeModeFollowBackEdge, () => SetFrontEdgeMode(FrontEdgeMode.FollowBackEdge));
        SceneConfigUi.BindSwatches(sideSwatches, SetFrontEdgeColor);
        SceneConfigUi.BindSwatches(backEdgeSwatches, SetBackEdgeColor);
        if (frontEdgeColorButton != null)
            SceneConfigUi.BindClick(frontEdgeColorButton, () => ToggleEdgeColorPicker(EdgeColorTarget.Front));
        if (backEdgeColorButton != null)
            SceneConfigUi.BindClick(backEdgeColorButton, () => ToggleEdgeColorPicker(EdgeColorTarget.Back));
        if (edgeColorPickerCloseButton != null)
            SceneConfigUi.BindClick(edgeColorPickerCloseButton, CloseEdgeColorPicker);
        if (edgeColorSliderR != null)
            edgeColorSliderR.onValueChanged.AddListener(v => SetEdgePickerRgb(v / 255f, GetEdgePickerColor().g, GetEdgePickerColor().b));
        if (edgeColorSliderG != null)
            edgeColorSliderG.onValueChanged.AddListener(v => SetEdgePickerRgb(GetEdgePickerColor().r, v / 255f, GetEdgePickerColor().b));
        if (edgeColorSliderB != null)
            edgeColorSliderB.onValueChanged.AddListener(v => SetEdgePickerRgb(GetEdgePickerColor().r, GetEdgePickerColor().g, v / 255f));
        if (edgeColorSliderBrightness != null)
            edgeColorSliderBrightness.onValueChanged.AddListener(SetEdgePickerBrightness);
        LoadSavedIntoUI();
        CloseEdgeColorPicker();
    }

    /// <summary>把正面边缘颜色还原为初始默认值。</summary>
    public void RestoreFrontEdgeDefault()
    {
        ResetSideTint();
        ConfigManager.Instance?.SetFrontEdgeBrightness(0f);
        currentFrontEdgeColor = Color.white;
        currentFrontEdgeMode = FrontEdgeMode.Independent;

        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetFrontEdgeMode(currentFrontEdgeMode);
            ConfigManager.Instance.SetFrontEdgeColor(currentFrontEdgeColor);
        }

        SyncUIFromColor();
        CardBackManager.SetFrontEdgeMode(currentFrontEdgeMode, currentFrontEdgeColor);
        if (currentBackEdgeMode == BackEdgeMode.FollowFront)
        {
            CardBackManager.ApplyBackEdgeColor(currentFrontEdgeColor);
        }
    }

    /// <summary>把背面边缘还原为初始默认：颜色跟随牌背，模式恢复为跟随牌背。</summary>
    public void RestoreBackEdgeDefault()
    {
        ConfigManager.Instance?.SetBackEdgeBrightness(0f);
        currentBackEdgeColor = ConfigManager.DefaultBackEdgeColor;
        currentBackEdgeMode = BackEdgeMode.FollowBack;

        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetBackEdgeMode(currentBackEdgeMode);
            ConfigManager.Instance.SetBackEdgeColor(currentBackEdgeColor);
        }

        SyncUIFromColor();
        CardBackManager.SetBackEdgeMode(currentBackEdgeMode, currentBackEdgeColor);
        if (currentFrontEdgeMode == FrontEdgeMode.FollowBackEdge)
        {
            CardBackManager.ApplyFrontEdgeColor(currentBackEdgeColor);
        }
    }

    public void ReloadSaved()
    {
        LoadSavedIntoUI();
        CloseEdgeColorPicker();
    }

    private void LoadSavedIntoUI()
    {
        if (ConfigManager.Instance != null)
        {
            currentSideColor = ConfigManager.Instance.SideColor;
            currentBackEdgeColor = ConfigManager.Instance.BackEdgeColor;
            currentBackEdgeMode = ConfigManager.Instance.BackEdgeMode;
            currentFrontEdgeColor = ConfigManager.Instance.FrontEdgeColor;
            currentFrontEdgeMode = ConfigManager.Instance.FrontEdgeMode;
        }
        SyncUIFromColor();
    }

    private void SyncUIFromColor()
    {
        syncing = true;
        Color backPreview = CardBackManager.ResolveBackEdgeColor(currentBackEdgeMode, currentBackEdgeColor);
        Color frontPreview = CardBackManager.ResolveFrontEdgeColor(currentFrontEdgeMode, currentFrontEdgeColor);
        if (sideHexInput != frontEdgeHexInput)
            sideHexInput.text = ColorUtility.ToHtmlStringRGB(currentSideColor);
        if (sidePreview != frontSidePreview)
            sidePreview.color = currentSideColor;
        backEdgeHexInput.text = ColorUtility.ToHtmlStringRGB(currentBackEdgeColor);
        backSidePreview.color = backPreview;
        frontEdgeHexInput.text = ColorUtility.ToHtmlStringRGB(currentFrontEdgeColor);
        frontSidePreview.color = frontPreview;

        backEdgeModeIndependent.isOn = currentBackEdgeMode == BackEdgeMode.Independent;
        backEdgeModeFollowBack.isOn = currentBackEdgeMode == BackEdgeMode.FollowBack;
        backEdgeModeFollowFront.isOn = currentBackEdgeMode == BackEdgeMode.FollowFront;
        frontEdgeModeIndependent.isOn = currentFrontEdgeMode == FrontEdgeMode.Independent;
        frontEdgeModeFollowTableBg.isOn = currentFrontEdgeMode == FrontEdgeMode.FollowTableBg;
        frontEdgeModeFollowBackEdge.isOn = currentFrontEdgeMode == FrontEdgeMode.FollowBackEdge;
        syncing = false;
        RefreshEdgeColorPicker();
        UpdateModeToggleColors(!toggleColorReady);
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
        ApplyHex(sideHexInput, SetSideColor, "正面边缘颜色已应用");
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
            CardBackManager.ApplyBackEdgeColor(EffectiveBackColor);
            if (currentFrontEdgeMode == FrontEdgeMode.FollowBackEdge)
            {
                CardBackManager.ApplyFrontEdgeColor(EffectiveBackColor);
            }
        }
    }

    private void SetFrontEdgeColor(Color color)
    {
        ResetMergedSideTint();
        color.a = 1f;
        currentFrontEdgeColor = color;
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetFrontEdgeColor(currentFrontEdgeColor);
        }
        if (currentFrontEdgeMode != FrontEdgeMode.Independent)
        {
            SetFrontEdgeMode(FrontEdgeMode.Independent);
        }
        else
        {
            SyncUIFromColor();
            CardBackManager.ApplyFrontEdgeColor(EffectiveFrontColor);
            if (currentBackEdgeMode == BackEdgeMode.FollowFront)
            {
                CardBackManager.ApplyBackEdgeColor(EffectiveFrontColor);
            }
        }
    }

    /// <summary>设置背面边缘颜色模式：独立 / 跟随牌背 / 跟随正面独立边缘色。</summary>
    public void SetBackEdgeMode(BackEdgeMode mode)
    {
        if (syncing) return;
        currentBackEdgeMode = mode;

        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetBackEdgeMode(mode);
        }

        SyncUIFromColor();
        CardBackManager.SetBackEdgeMode(mode, currentBackEdgeColor);
        if (currentFrontEdgeMode == FrontEdgeMode.FollowBackEdge)
        {
            CardBackManager.ApplyFrontEdgeColor(
                EffectiveBackColor);
        }
    }

    /// <summary>设置正面边缘颜色模式：独立 / 跟随牌面（拉伸到侧面） / 跟随背面独立边缘色。</summary>
    public void SetFrontEdgeMode(FrontEdgeMode mode)
    {
        if (syncing) return;
        ResetMergedSideTint();
        currentFrontEdgeMode = mode;

        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetFrontEdgeMode(mode);
        }

        SyncUIFromColor();
        CardBackManager.SetFrontEdgeMode(mode, currentFrontEdgeColor);
        if (currentBackEdgeMode == BackEdgeMode.FollowFront)
        {
            CardBackManager.ApplyBackEdgeColor(
                EffectiveFrontColor);
        }
    }

    private void ApplyFrontEdgeHex()
    {
        ApplyHex(frontEdgeHexInput, SetFrontEdgeColor, "正面边缘颜色已应用");
    }

    private void ResetMergedSideTint()
    {
        if (sideHexApplyButton == frontEdgeHexApplyButton) ResetSideTint();
    }

    private void ResetSideTint()
    {
        currentSideColor = ConfigManager.DefaultSideColor;
        ConfigManager.Instance?.SetSideColor(currentSideColor);
        CardBackManager.ApplySideColor(currentSideColor);
    }

    private void ApplyBackEdgeHex()
    {
        ApplyHex(backEdgeHexInput, SetBackEdgeColor, "背面边缘颜色已应用");
    }

    private void ToggleEdgeColorPicker(EdgeColorTarget target)
    {
        if (edgeColorPicker == null) return;
        if (edgeColorPicker.activeSelf && edgeColorTarget == target)
        {
            CloseEdgeColorPicker();
            return;
        }
        edgeColorTarget = target;
        edgeColorPicker.SetActive(true);
        RefreshEdgeColorPicker();
    }

    private void CloseEdgeColorPicker()
    {
        if (edgeColorPicker != null) edgeColorPicker.SetActive(false);
        if (frontEdgeColorButton != null) SceneConfigUi.SetButtonSelected(frontEdgeColorButton, false);
        if (backEdgeColorButton != null) SceneConfigUi.SetButtonSelected(backEdgeColorButton, false);
    }

    private Color GetEdgePickerColor()
    {
        return edgeColorTarget == EdgeColorTarget.Front
            ? currentFrontEdgeColor : currentBackEdgeColor;
    }

    private void RefreshEdgeColorPicker()
    {
        if (edgeColorPicker == null || !edgeColorPicker.activeSelf) return;
        Color color = GetEdgePickerColor();
        if (edgeColorPickerTitle != null)
            edgeColorPickerTitle.text = edgeColorTarget == EdgeColorTarget.Front ? "正面边缘调色" : "背面边缘调色";
        syncingEdgeColorPicker = true;
        SceneConfigColorUi.SyncChannel(edgeColorSliderR, edgeColorValueR, color.r);
        SceneConfigColorUi.SyncChannel(edgeColorSliderG, edgeColorValueG, color.g);
        SceneConfigColorUi.SyncChannel(edgeColorSliderB, edgeColorValueB, color.b);
        SceneConfigColorUi.SyncBrightness(edgeColorSliderBrightness, edgeColorValueBrightness,
            edgeColorTarget == EdgeColorTarget.Front
                ? ConfigManager.Instance?.FrontEdgeBrightness ?? 0f
                : ConfigManager.Instance?.BackEdgeBrightness ?? 0f);
        syncingEdgeColorPicker = false;
        if (frontEdgeColorButton != null) SceneConfigUi.SetButtonSelected(frontEdgeColorButton, edgeColorTarget == EdgeColorTarget.Front);
        if (backEdgeColorButton != null) SceneConfigUi.SetButtonSelected(backEdgeColorButton, edgeColorTarget == EdgeColorTarget.Back);
    }

    private void SetEdgePickerRgb(float r, float g, float b)
    {
        if (syncingEdgeColorPicker || SceneConfigColorUi.IsLayoutRefresh) return;
        Color color = new Color(r, g, b, 1f);
        if (edgeColorTarget == EdgeColorTarget.Front) SetFrontEdgeColor(color);
        else SetBackEdgeColor(color);
    }

    private void SetEdgePickerBrightness(float value)
    {
        if (syncingEdgeColorPicker || SceneConfigColorUi.IsLayoutRefresh) return;
        if (edgeColorTarget == EdgeColorTarget.Front)
        {
            ConfigManager.Instance?.SetFrontEdgeBrightness(value / 100f);
            SetFrontEdgeColor(currentFrontEdgeColor);
        }
        else
        {
            ConfigManager.Instance?.SetBackEdgeBrightness(value / 100f);
            SetBackEdgeColor(currentBackEdgeColor);
        }
    }

    private static void ApplyHex(TMP_InputField input, System.Action<Color> apply, string okTip)
    {
        SceneConfigUi.ApplyHex(input, apply, okTip);
    }

    /// <summary>牌背/牌面变化后刷新预览色（不改独立色存储）。</summary>
    public void RefreshPreviews()
    {
        if (!isActiveAndEnabled) return;
        SyncUIFromColor();
    }

    /// <summary>未选中默认色，选中高亮色；打开面板瞬间到位，点击切换带过渡。</summary>
    private void UpdateModeToggleColors(bool instant)
    {
        float fade = toggleColorFade;
        Color def = toggleDefaultColor;
        Color on = toggleSelectedColor;
        SceneConfigUi.SetToggleSelected(backEdgeModeIndependent, currentBackEdgeMode == BackEdgeMode.Independent, def, on, instant, fade);
        SceneConfigUi.SetToggleSelected(backEdgeModeFollowBack, currentBackEdgeMode == BackEdgeMode.FollowBack, def, on, instant, fade);
        SceneConfigUi.SetToggleSelected(backEdgeModeFollowFront, currentBackEdgeMode == BackEdgeMode.FollowFront, def, on, instant, fade);
        SceneConfigUi.SetToggleSelected(frontEdgeModeIndependent, currentFrontEdgeMode == FrontEdgeMode.Independent, def, on, instant, fade);
        SceneConfigUi.SetToggleSelected(frontEdgeModeFollowTableBg, currentFrontEdgeMode == FrontEdgeMode.FollowTableBg, def, on, instant, fade);
        SceneConfigUi.SetToggleSelected(frontEdgeModeFollowBackEdge, currentFrontEdgeMode == FrontEdgeMode.FollowBackEdge, def, on, instant, fade);
    }

#if UNITY_EDITOR
    /// <summary>编辑器拖拽入口：把拖入的图片应用到侧面贴图（_SideTex）。</summary>
    public void ApplyEditorDroppedTexture(Texture2D source)
    {
        if (source == null) return;

        Texture2D copy = SceneConfigTextureCapture.Copy(source, source.width, source.height);

        Material shared = Resources.Load<Material>(CardBackManager.MaterialResourcePath);
        if (shared != null)
        {
            shared.SetTexture("_SideTex", copy);
        }
        if (MahjongObjectPool.Instance != null)
        {
            MahjongObjectPool.Instance.ForEachStandaloneMaterial(mat =>
            {
                if (mat != null) mat.SetTexture("_SideTex", copy);
            });
        }
        SceneConfigUi.ShowTip("侧面贴图已应用");
    }
#endif
}
