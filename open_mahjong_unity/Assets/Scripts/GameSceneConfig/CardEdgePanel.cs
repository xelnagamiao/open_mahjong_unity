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
        if (frontEdgeModeIndependent != null) {
            frontEdgeModeIndependent.onValueChanged.AddListener(on => { if (on) SetFrontEdgeMode(FrontEdgeMode.Independent); });
        }
        if (frontEdgeModeFollowTableBg != null) {
            frontEdgeModeFollowTableBg.onValueChanged.AddListener(on => { if (on) SetFrontEdgeMode(FrontEdgeMode.FollowTableBg); });
        }
        if (frontEdgeModeFollowBackEdge != null) {
            frontEdgeModeFollowBackEdge.onValueChanged.AddListener(on => { if (on) SetFrontEdgeMode(FrontEdgeMode.FollowBackEdge); });
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
        if (frontEdgeModeIndependent == null) frontEdgeModeIndependent = FindInChildren<Toggle>(transform, "FrontEdgeModeIndependent");
        if (frontEdgeModeFollowTableBg == null) frontEdgeModeFollowTableBg = FindInChildren<Toggle>(transform, "FrontEdgeModeFollowTableBg");
        if (frontEdgeModeFollowBackEdge == null) frontEdgeModeFollowBackEdge = FindInChildren<Toggle>(transform, "FrontEdgeModeFollowBackEdge");
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
        if (sideSwatches == null || sideSwatches.Length == 0) {
            sideSwatches = CollectNamedButtons(transform, "SideSwatch", PresetColors.Length);
        }
        if (backEdgeSwatches == null || backEdgeSwatches.Length == 0) {
            backEdgeSwatches = CollectNamedButtons(transform, "BackEdgeSwatch", PresetColors.Length);
        }
        if (frontEdgeSwatches == null || frontEdgeSwatches.Length == 0) {
            frontEdgeSwatches = CollectNamedButtons(transform, "FrontEdgeSwatch", PresetColors.Length);
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
        if (currentBackEdgeMode == BackEdgeMode.FollowFront)
        {
            CardBackManager.ApplyBackEdgeColor(currentFrontEdgeColor);
        }
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
        if (currentFrontEdgeMode == FrontEdgeMode.FollowBackEdge)
        {
            CardBackManager.ApplyFrontEdgeColor(currentBackEdgeColor);
        }
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
        Color backPreview = CardBackManager.ResolveBackEdgeColor(currentBackEdgeMode, currentBackEdgeColor);
        Color frontPreview = CardBackManager.ResolveFrontEdgeColor(currentFrontEdgeMode, currentFrontEdgeColor);
        if (sideHexInput != null) sideHexInput.text = ColorUtility.ToHtmlStringRGB(currentSideColor);
        if (sidePreview != null) sidePreview.color = currentSideColor;
        if (backEdgeHexInput != null) backEdgeHexInput.text = ColorUtility.ToHtmlStringRGB(backPreview);
        if (backSidePreview != null) backSidePreview.color = backPreview;
        if (frontEdgeHexInput != null) frontEdgeHexInput.text = ColorUtility.ToHtmlStringRGB(frontPreview);
        if (frontSidePreview != null) frontSidePreview.color = frontPreview;

        if (backEdgeModeIndependent != null) backEdgeModeIndependent.isOn = currentBackEdgeMode == BackEdgeMode.Independent;
        if (backEdgeModeFollowBack != null) backEdgeModeFollowBack.isOn = currentBackEdgeMode == BackEdgeMode.FollowBack;
        if (backEdgeModeFollowFront != null) backEdgeModeFollowFront.isOn = currentBackEdgeMode == BackEdgeMode.FollowFront;
        if (frontEdgeModeIndependent != null) frontEdgeModeIndependent.isOn = currentFrontEdgeMode == FrontEdgeMode.Independent;
        if (frontEdgeModeFollowTableBg != null) frontEdgeModeFollowTableBg.isOn = currentFrontEdgeMode == FrontEdgeMode.FollowTableBg;
        if (frontEdgeModeFollowBackEdge != null) frontEdgeModeFollowBackEdge.isOn = currentFrontEdgeMode == FrontEdgeMode.FollowBackEdge;
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
        ApplyBackEdgeInteractable();
    }

    /// <summary>
    /// 背面边缘区交互状态：独立模式可编辑；跟随模式仅做视觉提示（变暗），
    /// 色块始终可点（点击自动切到独立模式并应用）。
    /// </summary>
    private void ApplyBackEdgeInteractable()
    {
        bool editable = currentBackEdgeMode == BackEdgeMode.Independent;
        if (backSidePreview != null) {
            backSidePreview.color = CardBackManager.ResolveBackEdgeColor(currentBackEdgeMode, currentBackEdgeColor);
        }
        if (backEdgeHexInput != null) backEdgeHexInput.interactable = editable;
        if (backEdgeHexApplyButton != null) backEdgeHexApplyButton.interactable = editable;

        if (backEdgeSectionGroup != null)
        {
            backEdgeSectionGroup.interactable = true;
            backEdgeSectionGroup.blocksRaycasts = true;
            backEdgeSectionGroup.alpha = editable ? 1f : 0.55f;
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
        ApplyFrontEdgeInteractable();
    }

    /// <summary>
    /// 正面边缘区交互状态：独立模式可编辑；跟随模式仅做视觉提示（变暗）。
    /// </summary>
    private void ApplyFrontEdgeInteractable()
    {
        bool editable = currentFrontEdgeMode == FrontEdgeMode.Independent;
        if (frontSidePreview != null) {
            frontSidePreview.color = CardBackManager.ResolveFrontEdgeColor(currentFrontEdgeMode, currentFrontEdgeColor);
        }
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

    /// <summary>牌背/牌面变化后刷新预览色（不改独立色存储）。</summary>
    public void RefreshPreviews()
    {
        if (!isActiveAndEnabled) return;
        SyncUIFromColor();
        ApplyBackEdgeInteractable();
        ApplyFrontEdgeInteractable();
    }

    /// <summary>选中的模式 Toggle 显示橙色，其余恢复默认色。</summary>
    private void UpdateModeToggleColors()
    {
        SetToggleColor(backEdgeModeIndependent, currentBackEdgeMode == BackEdgeMode.Independent);
        SetToggleColor(backEdgeModeFollowBack, currentBackEdgeMode == BackEdgeMode.FollowBack);
        SetToggleColor(backEdgeModeFollowFront, currentBackEdgeMode == BackEdgeMode.FollowFront);
        SetToggleColor(frontEdgeModeIndependent, currentFrontEdgeMode == FrontEdgeMode.Independent);
        SetToggleColor(frontEdgeModeFollowTableBg, currentFrontEdgeMode == FrontEdgeMode.FollowTableBg);
        SetToggleColor(frontEdgeModeFollowBackEdge, currentFrontEdgeMode == FrontEdgeMode.FollowBackEdge);
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
        if (MahjongObjectPool.Instance != null)
        {
            MahjongObjectPool.Instance.ForEachStandaloneMaterial(mat =>
            {
                if (mat != null) mat.SetTexture("_SideTex", copy);
            });
        }
        ShowTip("侧面贴图已应用");
    }
#endif
}
