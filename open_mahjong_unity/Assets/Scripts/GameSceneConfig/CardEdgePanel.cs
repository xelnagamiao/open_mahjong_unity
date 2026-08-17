using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>正面/背面边缘颜色。引用由场景写入，运行时只改颜色和模式。</summary>
public class CardEdgePanel : MonoBehaviour
{
    public static CardEdgePanel Instance { get; private set; }

    /// <summary>正面边缘颜色模式（与背面边缘颜色模式对称）。</summary>
    public enum FrontEdgeMode
    {
        /// <summary>独立设置正面边缘颜色。</summary>
        Independent = 0,
        /// <summary>正面边缘颜色跟随 3D 牌面背景（背景亮则底色偏白，缺失则回退到独立色）。</summary>
        FollowTableBg = 1,
        /// <summary>正面边缘颜色跟随背面边缘颜色。</summary>
        FollowBackEdge = 2,
    }

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
    [SerializeField] private Toggle frontEdgeModeIndependent;
    [SerializeField] private Toggle frontEdgeModeFollowTableBg;
    [SerializeField] private Toggle frontEdgeModeFollowBackEdge;
    [SerializeField] private Toggle frontTexFollowTableBgToggle;
    [SerializeField] private Toggle frontTexFollowTableBgToEdgeToggle;
    [SerializeField] private Image backSidePreview;
    [SerializeField] private Image frontSidePreview;
    [SerializeField] private TMP_InputField backEdgeHexInput;
    [SerializeField] private Button backEdgeHexApplyButton;
    [SerializeField] private TMP_InputField frontEdgeHexInput;
    [SerializeField] private Button frontEdgeHexApplyButton;
    [SerializeField] private Button restoreFrontEdgeButton;
    [SerializeField] private Button restoreBackEdgeButton;
    [SerializeField] private CanvasGroup backEdgeSectionGroup;
    [SerializeField] private CanvasGroup frontEdgeSectionGroup;
    [SerializeField] private Button[] sideSwatches;
    [SerializeField] private Button[] backEdgeSwatches;
    [SerializeField] private Button[] frontEdgeSwatches;

    private Color currentSideColor = ConfigManager.DefaultSideColor;
    private Color currentBackEdgeColor = ConfigManager.DefaultBackEdgeColor;
    private Color currentFrontEdgeColor = Color.white;
    private BackEdgeMode currentBackEdgeMode = BackEdgeMode.FollowBack;
    private FrontEdgeMode currentFrontEdgeMode = FrontEdgeMode.Independent;
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
        AutoWireMissingRefs();

        if (sideHexApplyButton != null) sideHexApplyButton.onClick.AddListener(ApplySideHex);
        if (backEdgeHexApplyButton != null) backEdgeHexApplyButton.onClick.AddListener(ApplyBackEdgeHex);
        if (restoreFrontEdgeButton != null) restoreFrontEdgeButton.onClick.AddListener(RestoreFrontEdgeDefault);
        if (restoreBackEdgeButton != null) restoreBackEdgeButton.onClick.AddListener(RestoreBackEdgeDefault);
        if (backEdgeModeIndependent != null) {
            backEdgeModeIndependent.onValueChanged.AddListener(on => { if (on) SetBackEdgeMode(BackEdgeMode.Independent); });
        }
        if (backEdgeModeFollowBack != null) {
            backEdgeModeFollowBack.onValueChanged.AddListener(on => { if (on) SetBackEdgeMode(BackEdgeMode.FollowBack); });
        }
        if (backEdgeModeFollowFront != null) {
            backEdgeModeFollowFront.onValueChanged.AddListener(on => { if (on) SetBackEdgeMode(BackEdgeMode.FollowFront); });
        }
        if (backTexExtendEdgeToggle != null) {
            backTexExtendEdgeToggle.onValueChanged.AddListener(OnBackTexExtendEdgeChanged);
        }
        if (frontEdgeModeIndependent != null) {
            frontEdgeModeIndependent.onValueChanged.AddListener(on => { if (on) SetFrontEdgeMode(FrontEdgeMode.Independent); });
        }
        if (frontEdgeModeFollowTableBg != null) {
            frontEdgeModeFollowTableBg.onValueChanged.AddListener(on => { if (on) SetFrontEdgeMode(FrontEdgeMode.FollowTableBg); });
        }
        if (frontEdgeModeFollowBackEdge != null) {
            frontEdgeModeFollowBackEdge.onValueChanged.AddListener(on => { if (on) SetFrontEdgeMode(FrontEdgeMode.FollowBackEdge); });
        }
        if (frontTexFollowTableBgToggle != null) {
            frontTexFollowTableBgToggle.onValueChanged.AddListener(OnFrontTexFollowTableBgChanged);
        }
        if (frontTexFollowTableBgToEdgeToggle != null) {
            frontTexFollowTableBgToEdgeToggle.onValueChanged.AddListener(OnFrontTexFollowTableBgToEdgeChanged);
        }
        if (frontEdgeHexApplyButton != null) frontEdgeHexApplyButton.onClick.AddListener(ApplyFrontEdgeHex);
        BindSwatches(sideSwatches, SetSideColor);
        BindSwatches(backEdgeSwatches, SetBackEdgeColor);
        BindSwatches(frontEdgeSwatches, SetFrontEdgeColor);
        BindNamedSwatches("SideSwatch", SetSideColor, sideSwatches);
        BindNamedSwatches("BackEdgeSwatch", SetBackEdgeColor, backEdgeSwatches);
        BindNamedSwatches("FrontEdgeSwatch", SetFrontEdgeColor, frontEdgeSwatches);
        LoadSavedIntoUI();
        ApplyBackEdgeInteractable();
        ApplyFrontEdgeInteractable();
    }

    /// <summary>
    /// 场景 Inspector 引用经常是空的（控件在，没拖上去）。按子物体名字补挂，避免 BindUi 空引用。
    /// </summary>
    private void AutoWireMissingRefs()
    {
        if (sidePreview == null) {
            sidePreview = FindInChildren<Image>(transform, "SidePreview")
                ?? FindInChildren<Image>(transform, "FrontSidePreview");
        }
        if (sideHexInput == null) sideHexInput = FindInChildren<TMP_InputField>(transform, "SideHexInput");
        if (sideHexApplyButton == null) sideHexApplyButton = FindInChildren<Button>(transform, "SideHexApply");
        if (backEdgeModeIndependent == null) backEdgeModeIndependent = FindInChildren<Toggle>(transform, "BackEdgeModeIndependent");
        if (backEdgeModeFollowBack == null) backEdgeModeFollowBack = FindInChildren<Toggle>(transform, "BackEdgeModeFollowBack");
        if (backEdgeModeFollowFront == null) backEdgeModeFollowFront = FindInChildren<Toggle>(transform, "BackEdgeModeFollowFront");
        if (backTexExtendEdgeToggle == null) backTexExtendEdgeToggle = FindInChildren<Toggle>(transform, "BackTexExtendEdge");
        if (frontEdgeModeIndependent == null) frontEdgeModeIndependent = FindInChildren<Toggle>(transform, "FrontEdgeModeIndependent");
        if (frontEdgeModeFollowTableBg == null) frontEdgeModeFollowTableBg = FindInChildren<Toggle>(transform, "FrontEdgeModeFollowTableBg");
        if (frontEdgeModeFollowBackEdge == null) frontEdgeModeFollowBackEdge = FindInChildren<Toggle>(transform, "FrontEdgeModeFollowBackEdge");
        if (frontTexFollowTableBgToggle == null) frontTexFollowTableBgToggle = FindInChildren<Toggle>(transform, "FrontTexFollowTableBg");
        if (frontTexFollowTableBgToEdgeToggle == null) {
            frontTexFollowTableBgToEdgeToggle = FindInChildren<Toggle>(transform, "FrontTexFollowTableBgToEdge");
        }
        if (backSidePreview == null) backSidePreview = FindInChildren<Image>(transform, "BackSidePreview");
        if (frontSidePreview == null) frontSidePreview = FindInChildren<Image>(transform, "FrontSidePreview");
        if (backEdgeHexInput == null) backEdgeHexInput = FindInChildren<TMP_InputField>(transform, "BackEdgeHexInput");
        if (backEdgeHexApplyButton == null) backEdgeHexApplyButton = FindInChildren<Button>(transform, "BackEdgeHexApply");
        if (frontEdgeHexInput == null) {
            frontEdgeHexInput = FindInChildren<TMP_InputField>(transform, "FrontEdgeHexInput")
                ?? sideHexInput;
        }
        if (frontEdgeHexApplyButton == null) {
            frontEdgeHexApplyButton = FindInChildren<Button>(transform, "FrontEdgeHexApply");
        }
        if (restoreFrontEdgeButton == null) restoreFrontEdgeButton = FindInChildren<Button>(transform, "RestoreFrontEdgeButton");
        if (restoreBackEdgeButton == null) restoreBackEdgeButton = FindInChildren<Button>(transform, "RestoreBackEdgeButton");
        if (backEdgeSectionGroup == null) {
            Transform section = FindInChildren<Transform>(transform, "BackEdgeSection");
            if (section != null) backEdgeSectionGroup = section.GetComponent<CanvasGroup>();
        }
        if (frontEdgeSectionGroup == null) {
            Transform section = FindInChildren<Transform>(transform, "FrontEdgeSection");
            if (section != null) frontEdgeSectionGroup = section.GetComponent<CanvasGroup>();
        }
    }

    private static void BindSwatches(Button[] buttons, System.Action<Color> apply)
    {
        int n = buttons != null ? Mathf.Min(buttons.Length, PresetColors.Length) : 0;
        for (int i = 0; i < n; i++)
        {
            if (buttons[i] == null) continue;
            Color c = PresetColors[i];
            buttons[i].onClick.AddListener(() => apply(c));
        }
    }

    private void BindNamedSwatches(string prefix, System.Action<Color> apply, Button[] alreadyBound)
    {
        if (alreadyBound != null && alreadyBound.Length > 0) return;
        for (int i = 0; i < PresetColors.Length; i++)
        {
            Button button = FindInChildren<Button>(transform, prefix + i);
            if (button == null) continue;
            Color c = PresetColors[i];
            button.onClick.AddListener(() => apply(c));
        }
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
        ApplyFrontEdgeInteractable();
        ShowTip("正面边缘已恢复默认");
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
            currentFrontEdgeColor = ConfigManager.Instance.FrontEdgeColor;
            currentFrontEdgeMode = ConfigManager.Instance.FrontEdgeMode;
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
        if (frontEdgeHexInput != null) frontEdgeHexInput.text = ColorUtility.ToHtmlStringRGB(currentFrontEdgeColor);
        if (frontSidePreview != null) frontSidePreview.color = currentFrontEdgeColor;

        if (backEdgeModeIndependent != null) backEdgeModeIndependent.isOn = currentBackEdgeMode == BackEdgeMode.Independent;
        if (backEdgeModeFollowBack != null) backEdgeModeFollowBack.isOn = currentBackEdgeMode == BackEdgeMode.FollowBack;
        if (backEdgeModeFollowFront != null) backEdgeModeFollowFront.isOn = currentBackEdgeMode == BackEdgeMode.FollowFront;
        if (backTexExtendEdgeToggle != null) {
            backTexExtendEdgeToggle.isOn = ConfigManager.Instance != null && ConfigManager.Instance.BackTexExtendEdge;
        }
        if (frontEdgeModeIndependent != null) frontEdgeModeIndependent.isOn = currentFrontEdgeMode == FrontEdgeMode.Independent;
        if (frontEdgeModeFollowTableBg != null) frontEdgeModeFollowTableBg.isOn = currentFrontEdgeMode == FrontEdgeMode.FollowTableBg;
        if (frontEdgeModeFollowBackEdge != null) frontEdgeModeFollowBackEdge.isOn = currentFrontEdgeMode == FrontEdgeMode.FollowBackEdge;
        if (frontTexFollowTableBgToggle != null) {
            frontTexFollowTableBgToggle.isOn = ConfigManager.Instance != null && ConfigManager.Instance.FrontTexFollowTableBg;
        }
        if (frontTexFollowTableBgToEdgeToggle != null) {
            frontTexFollowTableBgToEdgeToggle.isOn = ConfigManager.Instance != null && ConfigManager.Instance.FrontTexFollowTableBgToEdge;
            // 跟随 3D 牌面背景未启用时禁用「到边缘」选项，避免视觉冲突
            frontTexFollowTableBgToEdgeToggle.interactable = frontTexFollowTableBgToggle != null && frontTexFollowTableBgToggle.isOn;
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

    /// <summary>设置正面边缘颜色模式：独立 / 跟随牌面背景 / 跟随背面边缘。</summary>
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
        ApplyFrontEdgeInteractable();
    }

    /// <summary>
    /// 正面边缘区交互状态：独立模式可编辑；跟随模式仅做视觉提示（变暗）。
    /// </summary>
    private void ApplyFrontEdgeInteractable()
    {
        bool editable = currentFrontEdgeMode == FrontEdgeMode.Independent;
        if (frontSidePreview != null) frontSidePreview.color = CardBackManager.CurrentFrontEdgeColor;
        if (frontEdgeHexInput != null) frontEdgeHexInput.interactable = editable;
        if (frontEdgeHexApplyButton != null) frontEdgeHexApplyButton.interactable = editable;

        if (frontEdgeSectionGroup != null)
        {
            frontEdgeSectionGroup.interactable = true;
            frontEdgeSectionGroup.blocksRaycasts = true;
            frontEdgeSectionGroup.alpha = editable ? 1f : 0.55f;
        }
    }

    private void ApplyFrontEdgeHex()
    {
        string hex = frontEdgeHexInput != null ? frontEdgeHexInput.text : "";
        if (hex == null) hex = "";
        hex = hex.Trim();
        if (hex.StartsWith("#")) hex = hex.Substring(1);
        if (hex.Length == 6) hex += "FF";
        if (hex.Length != 8 || !ColorUtility.TryParseHtmlString("#" + hex, out Color color))
        {
            ShowTip("HEX 格式不正确");
            return;
        }
        SetFrontEdgeColor(color);
        ShowTip("正面边缘颜色已应用");
    }

    private void OnFrontTexFollowTableBgChanged(bool enabled)
    {
        if (syncing) return;
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetFrontTexFollowTableBg(enabled);
        }
        // 「跟随到边缘」依赖「跟随 3D 牌面背景」开关，关闭联动时禁用
        if (frontTexFollowTableBgToEdgeToggle != null)
        {
            frontTexFollowTableBgToEdgeToggle.interactable = enabled;
            if (!enabled && frontTexFollowTableBgToEdgeToggle.isOn)
            {
                frontTexFollowTableBgToEdgeToggle.isOn = false;
                if (ConfigManager.Instance != null)
                {
                    ConfigManager.Instance.SetFrontTexFollowTableBgToEdge(false);
                }
                CardBackManager.SetTableBackgroundCoverFace(false);
            }
        }
        CardBackManager.ApplyFrontTexExtendEdge(enabled || (ConfigManager.Instance != null && ConfigManager.Instance.FrontTexExtendEdge));
        UpdateModeToggleColors();
        ShowTip(enabled ? "正面贴图将跟随 3D 牌面背景延伸" : "正面贴图不再跟随 3D 牌面背景");
    }

    /// <summary>正面贴图跟随 3D 牌面背景时，是否把背景拉伸到整张牌正面+侧面边缘。</summary>
    private void OnFrontTexFollowTableBgToEdgeChanged(bool enabled)
    {
        if (syncing) return;
        if (ConfigManager.Instance != null)
        {
            ConfigManager.Instance.SetFrontTexFollowTableBgToEdge(enabled);
        }
        CardBackManager.SetTableBackgroundCoverFace(enabled);
        UpdateModeToggleColors();
        ShowTip(enabled ? "3D 牌面背景将拉伸到整张牌正面+侧面边缘" : "3D 牌面背景仅覆盖中央 220:366 区");
    }

    /// <summary>选中的模式 Toggle 显示橙色，其余恢复默认色。</summary>
    private void UpdateModeToggleColors()
    {
        SetToggleColor(backEdgeModeIndependent, currentBackEdgeMode == BackEdgeMode.Independent);
        SetToggleColor(backEdgeModeFollowBack, currentBackEdgeMode == BackEdgeMode.FollowBack);
        SetToggleColor(backEdgeModeFollowFront, currentBackEdgeMode == BackEdgeMode.FollowFront);
        SetToggleColor(backTexExtendEdgeToggle, backTexExtendEdgeToggle != null && backTexExtendEdgeToggle.isOn);
        SetToggleColor(frontEdgeModeIndependent, currentFrontEdgeMode == FrontEdgeMode.Independent);
        SetToggleColor(frontEdgeModeFollowTableBg, currentFrontEdgeMode == FrontEdgeMode.FollowTableBg);
        SetToggleColor(frontEdgeModeFollowBackEdge, currentFrontEdgeMode == FrontEdgeMode.FollowBackEdge);
        SetToggleColor(frontTexFollowTableBgToggle, frontTexFollowTableBgToggle != null && frontTexFollowTableBgToggle.isOn);
        SetToggleColor(frontTexFollowTableBgToEdgeToggle, frontTexFollowTableBgToEdgeToggle != null && frontTexFollowTableBgToEdgeToggle.isOn);
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

    private static void SetToggleColor(Toggle toggle, bool selected)
    {
        if (toggle == null) return;
        // 关闭 ColorTint 等 transition，避免 Unity 默认 Selectable 在 hover/press 时用 SelectedColor/PressedColor
        // 覆盖我们手动控制的颜色（导致"鼠标指向消失 / 点击有奇怪感觉"）。
        if (toggle.transition != Selectable.Transition.None)
        {
            toggle.transition = Selectable.Transition.None;
        }
        // Unity Toggle：background 通常挂在 targetGraphic（Image），checkmark 挂在 graphic。
        // 我们手动让 background = selected 橙、其余深蓝；checkmark 用白色（透明度区分）。
        Image bg = toggle.targetGraphic as Image;
        if (bg != null)
        {
            bg.color = selected ? SelectedColor : UnselectedColor;
        }
        else if (toggle.targetGraphic != null)
        {
            toggle.targetGraphic.color = selected ? SelectedColor : UnselectedColor;
        }
        Image check = toggle.graphic as Image;
        if (check != null && check != bg)
        {
            check.color = selected ? Color.white : new Color(1f, 1f, 1f, 0.35f);
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
