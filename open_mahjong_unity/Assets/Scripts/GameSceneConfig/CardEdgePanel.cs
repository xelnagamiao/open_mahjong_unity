using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    private bool toggleColorReady;
    private bool toggleColorsNeedRefresh;

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
        SceneConfigUi.BindSwatches(sideSwatches, SetSideColor);
        SceneConfigUi.BindSwatches(backEdgeSwatches, SetBackEdgeColor);
        LoadSavedIntoUI();
    }

    /// <summary>把正面边缘颜色还原为初始默认值。</summary>
    public void RestoreFrontEdgeDefault()
    {
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
        SceneConfigUi.ShowTip("正面边缘已恢复默认");
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
        if (currentFrontEdgeMode == FrontEdgeMode.FollowBackEdge)
        {
            CardBackManager.ApplyFrontEdgeColor(currentBackEdgeColor);
        }
        SceneConfigUi.ShowTip("已恢复默认");
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
        sideHexInput.text = ColorUtility.ToHtmlStringRGB(currentSideColor);
        sidePreview.color = currentSideColor;
        backEdgeHexInput.text = ColorUtility.ToHtmlStringRGB(backPreview);
        backSidePreview.color = backPreview;
        frontEdgeHexInput.text = ColorUtility.ToHtmlStringRGB(frontPreview);
        frontSidePreview.color = frontPreview;

        backEdgeModeIndependent.isOn = currentBackEdgeMode == BackEdgeMode.Independent;
        backEdgeModeFollowBack.isOn = currentBackEdgeMode == BackEdgeMode.FollowBack;
        backEdgeModeFollowFront.isOn = currentBackEdgeMode == BackEdgeMode.FollowFront;
        frontEdgeModeIndependent.isOn = currentFrontEdgeMode == FrontEdgeMode.Independent;
        frontEdgeModeFollowTableBg.isOn = currentFrontEdgeMode == FrontEdgeMode.FollowTableBg;
        frontEdgeModeFollowBackEdge.isOn = currentFrontEdgeMode == FrontEdgeMode.FollowBackEdge;
        syncing = false;
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
            CardBackManager.ApplyBackEdgeColor(currentBackEdgeColor);
            if (currentFrontEdgeMode == FrontEdgeMode.FollowBackEdge)
            {
                CardBackManager.ApplyFrontEdgeColor(currentBackEdgeColor);
            }
        }
    }

    private void SetFrontEdgeColor(Color color)
    {
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
            CardBackManager.ApplyFrontEdgeColor(currentFrontEdgeColor);
            if (currentBackEdgeMode == BackEdgeMode.FollowFront)
            {
                CardBackManager.ApplyBackEdgeColor(currentFrontEdgeColor);
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
                ConfigManager.Instance != null ? ConfigManager.Instance.BackEdgeColor : currentBackEdgeColor);
        }
    }

    /// <summary>设置正面边缘颜色模式：独立 / 跟随牌面（拉伸到侧面） / 跟随背面独立边缘色。</summary>
    public void SetFrontEdgeMode(FrontEdgeMode mode)
    {
        if (syncing) return;
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
                ConfigManager.Instance != null ? ConfigManager.Instance.FrontEdgeColor : currentFrontEdgeColor);
        }
    }

    private void ApplyFrontEdgeHex()
    {
        ApplyHex(frontEdgeHexInput, SetFrontEdgeColor, "正面边缘颜色已应用");
    }

    private void ApplyBackEdgeHex()
    {
        ApplyHex(backEdgeHexInput, SetBackEdgeColor, "背面边缘颜色已应用");
    }

    private static void ApplyHex(TMP_InputField input, System.Action<Color> apply, string okTip)
    {
        if (!SceneConfigUi.TryParseHex(input.text, out Color color))
        {
            SceneConfigUi.ShowTip("HEX 格式不正确");
            return;
        }
        apply(color);
        SceneConfigUi.ShowTip(okTip);
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
