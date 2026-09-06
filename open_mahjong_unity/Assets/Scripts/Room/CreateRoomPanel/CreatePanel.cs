using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// 统一创建房间面板。规则目录与建房默认值来自 RuleRegistry（各族 Manifest.LobbySubRules / CreateRoomDefaults）。
/// 存在即可见、同时提供切换规则时应用的默认值；不存在即隐藏。
///
/// 国标的错和/起和番仍受子规则二次收窄：小林规隐藏错和；蓝十隐藏起和番自定义；标准/K神/小林均可配置起和番（K神默认8、小林默认1）。
/// </summary>
public partial class CreatePanel : MonoBehaviour {
    private static readonly int[] ChangshaBirdCountOptions = { 0, 1, 2, 4 };

    /// <summary>当前规则建房默认值，来自 RuleManifest.CreateRoomDefaults。</summary>
    private Dictionary<string, object> DefaultsOf(string rule) {
        Dictionary<string, object> defaults = RuleRegistry.Resolve(rule)?.CreateRoomDefaults;
        return defaults ?? _emptyDefaults;
    }

    private static readonly Dictionary<string, object> _emptyDefaults = new Dictionary<string, object>();

    private bool TryGetDefaults(string rule, out Dictionary<string, object> config) {
        config = RuleRegistry.Resolve(rule)?.CreateRoomDefaults;
        return config != null;
    }

    /// <summary>规则状态：与 RuleManifest.RuleId 一致。</summary>
    private string _ruleState = "guobiao";
    private string _venueEventId;

    [Header("Dropdown")]
    [SerializeField] private TMP_Dropdown chooseRule;
    [SerializeField] private TMP_Dropdown roundTimer;
    [SerializeField] private TMP_Dropdown stepTimer;
    [SerializeField] private TMP_Dropdown SubRuleDropdown;

    [Header("描述文本")]
    [SerializeField] private TMP_Text SubRuleText;
    [SerializeField] private TMP_Text SubRuleDescriptionText;

    [Header("开关")]
    [SerializeField] private Toggle gameTime1Button;
    [SerializeField] private Toggle gameTime2Button;
    [SerializeField] private Toggle gameTime3Button;
    [SerializeField] private Toggle gameTime4Button;
    [SerializeField] private Toggle tipsToggle;
    [SerializeField] private Toggle CuoHeheToggle;
    [SerializeField] private Toggle passwordToggle;
    [SerializeField] private Toggle SetRandomSeedToggle;
    [SerializeField] private Toggle TouristLimitToggle;
    [SerializeField] private Toggle InputHepaiLimitToggle;
    [SerializeField] private Toggle AllowSpectatorToggle;
    [SerializeField] private Toggle RedDoraToggle;
    [SerializeField] private Toggle KuikaeToggle;
    [SerializeField] private Toggle XiruToggle;
    [SerializeField] private Toggle TobiToggle;
    [SerializeField] private Toggle TacticalCallToggle;
    [SerializeField] private Toggle BloodBattleToggle;
    [SerializeField] private Toggle ChangshaInitialSiXiToggle;
    [SerializeField] private Toggle ChangshaInitialBanBanHuToggle;
    [SerializeField] private Toggle ChangshaInitialQueYiSeToggle;
    [SerializeField] private Toggle ChangshaInitialLiuLiuShunToggle;
    [SerializeField] private Toggle ChangshaInitialSanTongToggle;
    [SerializeField] private Toggle ChangshaDealerBirdToggle;
    [SerializeField] private Toggle ChangshaBaseScoreNoDealerToggle;

    [Header("面板")]
    [SerializeField] private GameObject SetRandomSeedPanel;
    [SerializeField] private GameObject PasswordPanel;
    [SerializeField] private GameObject InputHepaiLimitPlane;
    [SerializeField] private GameObject HepaiWayPanel;
    [SerializeField] private TMP_Dropdown HepaiWayDropdown;
    [SerializeField] private GameObject CuoheTypePanel;
    [SerializeField] private TMP_Dropdown CuoheTypeDropdown;
    [SerializeField] private GameObject ChangshaOpenKongPanel;
    [SerializeField] private TMP_Dropdown ChangshaOpenKongDropdown;
    [SerializeField] private GameObject ChangshaBirdCountPanel;
    [SerializeField] private TMP_Dropdown ChangshaBirdCountDropdown;
    [SerializeField] private GameObject ChangshaScoreRow;
    [SerializeField] private GameObject ChangshaSmallHuScorePanel;
    [SerializeField] private GameObject ChangshaBigHuScorePanel;
    [SerializeField] private TMP_InputField ChangshaSmallHuScoreInput;
    [SerializeField] private TMP_InputField ChangshaBigHuScoreInput;

    [Header("输入字段")]
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private TMP_InputField randomSeedInput;
    [SerializeField] private TMP_InputField HepaiLimitInput;

    [Header("按钮")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Button createButton;
    [SerializeField] private Button addRuleButton;
    [SerializeField] private Button DetailedConfigButton;

    private bool _gameRoundLabelsCached;
    private string[] _defaultGameRoundLabels;
    private int _roomNameBoundUserId = int.MinValue;
    private static readonly List<CreatePanel> LivePanels = new List<CreatePanel>();

    private void EnsureRuleDropdownOptions() {
        if (chooseRule == null) return;
        IReadOnlyList<CreateRoomRuleTextConfig> configs = CreateRoomRuleTextConfigCatalog.Rules;
        bool needsRefresh = chooseRule.options.Count != configs.Count;
        if (!needsRefresh) {
            for (int i = 0; i < configs.Count; i++) {
                if (chooseRule.options[i].text == configs[i].DisplayName) continue;
                needsRefresh = true;
                break;
            }
        }
        if (!needsRefresh) return;

        chooseRule.ClearOptions();
        var options = new List<string>();
        foreach (CreateRoomRuleTextConfig config in configs) options.Add(config.DisplayName);
        chooseRule.AddOptions(options);
        chooseRule.RefreshShownValue();
    }

    private void Awake() {
        if (!LivePanels.Contains(this)) LivePanels.Add(this);
    }

    private void OnDestroy() {
        LivePanels.Remove(this);
    }

    private void Start() {
        BindDetailedConfigControls("taiwan");
        EnsureRuleDropdownOptions();
        chooseRule.onValueChanged.AddListener(OnRuleDropdownChanged);
        closeButton.onClick.AddListener(ClosePanel);
        createButton.onClick.AddListener(CreateRoom);
        addRuleButton.onClick.AddListener(OnAddRuleClick);
        if (DetailedConfigButton != null) {
            DetailedConfigButton.onClick.AddListener(
                () => ShowDetailedConfigPanel(_ruleState));
        }

        SetRandomSeedPanel.SetActive(false);
        PasswordPanel.SetActive(false);
        InputHepaiLimitPlane.SetActive(false);
        if (CuoheTypePanel != null) CuoheTypePanel.SetActive(false);

        if (chooseRule != null && chooseRule.options.Count > 0) {
            _ruleState = CreateRoomRuleTextConfigCatalog.GetRule(chooseRule.value).Rule;
        }
        RebuildHepaiWayOptions();

        passwordToggle.onValueChanged.AddListener(TogglePassword);
        SetRandomSeedToggle.onValueChanged.AddListener(ToggleSetRandomSeed);
        CuoHeheToggle.onValueChanged.AddListener(ToggleCuoHehe);
        tipsToggle.onValueChanged.AddListener(ToggleTips);
        InputHepaiLimitToggle.onValueChanged.AddListener(ToggleInputHepaiLimit);
        SubRuleDropdown.onValueChanged.AddListener(OnSubRuleChanged);

        ApplyDefaultRoomNameForCurrentUser();

        EnsureRiichiOptionToggles();
        EnsureCuoheTypePanel();
        InitCuoheTypeDropdown();
        EnsureChangshaOptionControls();
        InitSubRuleDropdown();
        ApplyRuleDefaults(_ruleState);
        RefreshVisibility();
        RefreshSubRuleDescription();
    }

    private void OnEnable() {
        ApplyDefaultRoomNameForCurrentUser();
    }

    private void OnDisable() {
        CancelDetailedConfigChanges();
    }

    /// <summary>登出/换账号后清掉默认房间名草稿，下次打开按当前用户重填。</summary>
    public static void ResetAllSessionCaches() {
        for (int i = LivePanels.Count - 1; i >= 0; i--) {
            CreatePanel panel = LivePanels[i];
            if (panel == null) {
                LivePanels.RemoveAt(i);
                continue;
            }
            panel.ResetSessionCaches();
        }
    }

    public void ResetSessionCaches() {
        _roomNameBoundUserId = int.MinValue;
        if (roomNameInput != null) roomNameInput.text = "";
    }

    /// <summary>账号变了或输入为空时，用「用户名的游戏」填默认房间名；同一账号下保留用户改过的草稿。</summary>
    private void ApplyDefaultRoomNameForCurrentUser() {
        if (roomNameInput == null) return;
        int userId = UserDataManager.Instance != null ? UserDataManager.Instance.UserId : 0;
        bool userChanged = userId != _roomNameBoundUserId;
        _roomNameBoundUserId = userId;
        if (userChanged || string.IsNullOrWhiteSpace(roomNameInput.text)) {
            roomNameInput.text = GetDefaultRoomName();
        }
    }

    private void OnRuleDropdownChanged(int selectedIndex) {
        _ruleState = CreateRoomRuleTextConfigCatalog.GetRule(selectedIndex).Rule;
        RebuildHepaiWayOptions();
        bool hasSubRule = DefaultsOf(_ruleState).ContainsKey(CreateRoomKeys.SubRule);
        if (hasSubRule) {
            PopulateSubRuleDropdown(_ruleState);
        }
        ApplyRuleDefaults(_ruleState);
        RefreshVisibility();
        if (hasSubRule) {
            OnSubRuleChanged(SubRuleDropdown.value);
        }
        RefreshSubRuleDescription();
    }

    /// <summary>虹雀只开放“多家和 / 头跳”两项，日麻额外提供三家和了流局。</summary>
    private void RebuildHepaiWayOptions() {
        if (HepaiWayDropdown == null) return;
        HepaiWayDropdown.ClearOptions();
        string[] options = RuleRegistry.Resolve(_ruleState)?.HepaiWayOptions;
        if (options != null && options.Length > 0) {
            HepaiWayDropdown.AddOptions(new List<string>(options));
        } else {
            HepaiWayDropdown.AddOptions(new List<string> { "允许多家和牌", "三家和了流局", "头跳" });
        }
    }

    /// <summary>遍历当前规则的配置默认值并下发到对应控件。</summary>
    private void ApplyRuleDefaults(string rule) {
        Dictionary<string, object> defaults = DefaultsOf(rule);
        foreach (KeyValuePair<string, object> kv in defaults) {
            SetConfigValue(kv.Key, kv.Value);
        }
    }

    private void SetConfigValue(string key, object value) {
        switch (key) {
            case CreateRoomKeys.GameRound:      SelectGameTime((int)value); break;
            case CreateRoomKeys.RoundTimer:     roundTimer.value = (int)value; break;
            case CreateRoomKeys.StepTimer:      stepTimer.value = (int)value; break;
            case CreateRoomKeys.Tips:           tipsToggle.isOn = (bool)value; break;
            case CreateRoomKeys.Password:       passwordToggle.isOn = (bool)value; break;
            case CreateRoomKeys.RandomSeed:     SetRandomSeedToggle.isOn = (bool)value; break;
            case CreateRoomKeys.TouristLimit:   TouristLimitToggle.isOn = (bool)value; break;
            case CreateRoomKeys.AllowSpectator: AllowSpectatorToggle.isOn = (bool)value; break;
            case CreateRoomKeys.SubRule:        SubRuleDropdown.value = (int)value; break;
            case CreateRoomKeys.Cuohe:          CuoHeheToggle.isOn = (bool)value; break;
            case CreateRoomKeys.CuoheType:
                if (CuoheTypeDropdown != null) CuoheTypeDropdown.value = (int)value;
                break;
            case CreateRoomKeys.HepaiLimit:
                // 切换规则时同步收起"自定义起和番"面板，避免前一条规则的开启状态带入当前规则
                InputHepaiLimitToggle.isOn = false;
                HepaiLimitInput.text = ((int)value).ToString();
                break;
            case CreateRoomKeys.RedDora:        RedDoraToggle.isOn = (bool)value; break;
            case CreateRoomKeys.AllowKuikae:    if (KuikaeToggle != null) KuikaeToggle.isOn = !(bool)value; break;
            case CreateRoomKeys.OpenXiru:       if (XiruToggle != null) XiruToggle.isOn = (bool)value; break;
            case CreateRoomKeys.OpenTobi:       if (TobiToggle != null) TobiToggle.isOn = (bool)value; break;
            case CreateRoomKeys.HepaiWay:
                HepaiWayDropdown.value = (int)value;
                HepaiWayDropdown.RefreshShownValue();
                break;
            case CreateRoomKeys.TacticalCall:   TacticalCallToggle.isOn = (bool)value; break;
            case CreateRoomKeys.BloodBattle:    if (BloodBattleToggle != null) BloodBattleToggle.isOn = (bool)value; break;
            case CreateRoomKeys.CsOpenKongCount: SetChangshaOpenKongCount((int)value); break;
            case CreateRoomKeys.CsInitialSiXi:   if (ChangshaInitialSiXiToggle != null) ChangshaInitialSiXiToggle.isOn = (bool)value; break;
            case CreateRoomKeys.CsInitialBanBanHu: if (ChangshaInitialBanBanHuToggle != null) ChangshaInitialBanBanHuToggle.isOn = (bool)value; break;
            case CreateRoomKeys.CsInitialQueYiSe: if (ChangshaInitialQueYiSeToggle != null) ChangshaInitialQueYiSeToggle.isOn = (bool)value; break;
            case CreateRoomKeys.CsInitialLiuLiuShun: if (ChangshaInitialLiuLiuShunToggle != null) ChangshaInitialLiuLiuShunToggle.isOn = (bool)value; break;
            case CreateRoomKeys.CsInitialSanTong: if (ChangshaInitialSanTongToggle != null) ChangshaInitialSanTongToggle.isOn = (bool)value; break;
            case CreateRoomKeys.CsBirdCount:    SetChangshaBirdCount((int)value); break;
            case CreateRoomKeys.CsDealerBird:   if (ChangshaDealerBirdToggle != null) ChangshaDealerBirdToggle.isOn = (bool)value; break;
            case CreateRoomKeys.CsBaseScoreNoDealer:
                if (ChangshaBaseScoreNoDealerToggle != null) ChangshaBaseScoreNoDealerToggle.isOn = (bool)value;
                break;
            case CreateRoomKeys.CsSmallHuScore:
                if (ChangshaSmallHuScoreInput != null) ChangshaSmallHuScoreInput.text = ((int)value).ToString();
                break;
            case CreateRoomKeys.CsBigHuScore:
                if (ChangshaBigHuScoreInput != null) ChangshaBigHuScoreInput.text = ((int)value).ToString();
                break;
        }
    }

    private void SelectGameTime(int value) {
        gameTime1Button.isOn = value == 1;
        gameTime2Button.isOn = value == 2;
        gameTime3Button.isOn = value == 3;
        gameTime4Button.isOn = value == 4;
    }

    /// <summary>
    /// 根据 CreateRoomDefaults 驱动配置项控件的显隐。
    /// 国标子规则对错和 / 起和番自定义做进一步收窄；浪涌子规则固定可食替，不暴露食替开关。
    /// </summary>
    private void RefreshVisibility() {
        Dictionary<string, object> visible = DefaultsOf(_ruleState);

        bool isXiaolin = _ruleState == "guobiao" && SubRuleDropdown.value == 1;
        bool isLanshi  = _ruleState == "guobiao" && SubRuleDropdown.value == 3;
        bool isLangyong = _ruleState == "riichi" && SubRuleDropdown.value == 1;

        // 蓝十改固定启用“错和扣 40 分”，不暴露可变开关。
        bool showCuohe = visible.ContainsKey(CreateRoomKeys.Cuohe) && !isXiaolin && !isLanshi;
        CuoHeheToggle.gameObject.SetActive(showCuohe);

        // 蓝十仍隐藏起和番自定义；标准/小林/K神均可改
        bool showHepaiLimit = visible.ContainsKey(CreateRoomKeys.HepaiLimit) && !isLanshi;
        InputHepaiLimitToggle.gameObject.SetActive(showHepaiLimit);
        InputHepaiLimitPlane.SetActive(showHepaiLimit && InputHepaiLimitToggle.isOn);

        RedDoraToggle.gameObject.SetActive(visible.ContainsKey(CreateRoomKeys.RedDora));
        if (KuikaeToggle != null) KuikaeToggle.gameObject.SetActive(visible.ContainsKey(CreateRoomKeys.AllowKuikae) && !isLangyong);
        if (XiruToggle != null) XiruToggle.gameObject.SetActive(visible.ContainsKey(CreateRoomKeys.OpenXiru));
        if (TobiToggle != null) TobiToggle.gameObject.SetActive(visible.ContainsKey(CreateRoomKeys.OpenTobi));
        HepaiWayPanel.SetActive(visible.ContainsKey(CreateRoomKeys.HepaiWay));
        TacticalCallToggle.gameObject.SetActive(visible.ContainsKey(CreateRoomKeys.TacticalCall));
        if (BloodBattleToggle != null) BloodBattleToggle.gameObject.SetActive(visible.ContainsKey(CreateRoomKeys.BloodBattle));
        SetChangshaOptionsVisible(DefaultsOf(_ruleState).ContainsKey(CreateRoomKeys.CsBirdCount));
        ApplyGameRoundDisplayForRule();
        RefreshCuoheTypePanelVisibility();
        RefreshDetailedConfigEntry();
        RebuildCreateRoomLayoutHierarchy();
    }

    private void RebuildCreateRoomLayoutHierarchy() {
        Transform body = transform.Find("Create_Panel");
        if (body != null) {
            // ToggleContainer owns a GridLayoutGroup + ContentSizeFitter. Its
            // height must be settled before Content's VerticalLayoutGroup can
            // place it below RuleDescribePanel. Rebuilding from Create_Panel
            // skips that inner fitter and leaves Content using the old height.
            Transform toggleContainer = body.Find("Scroll View/Viewport/Content/ToggleContainer");
            if (toggleContainer != null) {
                LayoutHierarchyRebuilder.RebuildUpwards(toggleContainer, transform);
            } else {
                LayoutHierarchyRebuilder.RebuildUpwards(body, transform);
            }
        }
        Transform header = transform.Find("HeaderPanel");
        if (header != null) {
            LayoutHierarchyRebuilder.RebuildUpwards(header, transform);
        }
    }

    private string GetSelectedRiichiSubRule() {
        return CreateRoomRuleTextConfigCatalog.GetRule("riichi")
            .GetSubRule(SubRuleDropdown.value).Key;
    }

    private void RefreshSubRuleDescription() {
        CreateRoomSubRuleTextConfig subRule = CreateRoomRuleTextConfigCatalog
            .GetRule(_ruleState)
            .GetSubRule(SubRuleDropdown.value);
        SubRuleDescriptionText.text = subRule != null ? subRule.Description : "";
        SubRuleDescriptionText.ForceMeshUpdate();
        LayoutHierarchyRebuilder.RebuildUpwards(SubRuleDescriptionText.rectTransform, transform);
    }

    private void InitSubRuleDropdown() {
        PopulateSubRuleDropdown(_ruleState);
        OnSubRuleChanged(0);
    }

    /// <summary>按当前规则填充子规则下拉选项。仅含子规则的规则（国标 / 日麻）会用到。</summary>
    private void PopulateSubRuleDropdown(string rule) {
        SubRuleDropdown.ClearOptions();
        var options = new List<string>();
        foreach (CreateRoomSubRuleTextConfig subRule in CreateRoomRuleTextConfigCatalog.GetRule(rule).SubRules) {
            options.Add(subRule.DisplayName);
        }
        SubRuleDropdown.AddOptions(options);
        SubRuleDropdown.value = 0;
    }

    private void OnSubRuleChanged(int index) {
        RefreshSubRuleDescription();
        // 日麻子规则（标准 / 浪涌）不涉及起和番/错和的二次收窄，仅国标需处理。
        if (_ruleState == "guobiao") {
            bool isXiaolin = (index == 1);
            bool isKshen = (index == 2);
            bool isLanshi = (index == 3);
            InputHepaiLimitToggle.isOn = false;
            if (isXiaolin) {
                HepaiLimitInput.text = "1";
                CuoHeheToggle.onValueChanged.RemoveListener(ToggleCuoHehe);
                CuoHeheToggle.isOn = false;
                CuoHeheToggle.onValueChanged.AddListener(ToggleCuoHehe);
            } else if (isKshen) {
                HepaiLimitInput.text = "8";
            } else if (isLanshi) {
                HepaiLimitInput.text = "5";
                CuoHeheToggle.onValueChanged.RemoveListener(ToggleCuoHehe);
                CuoHeheToggle.isOn = true;
                CuoHeheToggle.onValueChanged.AddListener(ToggleCuoHehe);
            } else {
                HepaiLimitInput.text = "8";
            }
        }
        RefreshVisibility();
    }

    private string GetSelectedSubRule() {
        return CreateRoomRuleTextConfigCatalog.GetRule("guobiao")
            .GetSubRule(SubRuleDropdown.value).Key;
    }

    /// <summary>运行时从赤宝牌开关克隆日麻专属选项，避免场景内重复手工挂接。</summary>
    private void EnsureRiichiOptionToggles() {
        if (RedDoraToggle == null) return;
        KuikaeToggle = EnsureClonedToggle(RedDoraToggle, KuikaeToggle, "UseKuikae", "禁止食替", true);
        Toggle xiruTemplate = KuikaeToggle != null ? KuikaeToggle : RedDoraToggle;
        XiruToggle = EnsureClonedToggle(xiruTemplate, XiruToggle, "UseXiru", "西入", true);
        Toggle tobiTemplate = XiruToggle != null ? XiruToggle : xiruTemplate;
        TobiToggle = EnsureClonedToggle(tobiTemplate, TobiToggle, "UseTobi", "击飞", true);
    }

    private static Toggle EnsureClonedToggle(Toggle template, Toggle existing, string goName, string labelText, bool defaultOn) {
        if (existing != null) return existing;
        if (template == null) return null;
        var clone = Instantiate(template, template.transform.parent);
        clone.name = goName;
        clone.isOn = defaultOn;
        var label = clone.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = labelText;
        return clone;
    }

    private void InitCuoheTypeDropdown() {
        if (CuoheTypeDropdown == null) return;
        CuoheTypeDropdown.ClearOptions();
        CuoheTypeDropdown.AddOptions(new List<string> {
            "-30/+10",
            "-40/0",
        });
        CuoheTypeDropdown.value = 0;
    }

    /// <summary>运行时从和牌方式面板克隆错和形式面板，避免场景内重复手工挂接。</summary>
    private void EnsureCuoheTypePanel() {
        if (CuoheTypePanel != null && CuoheTypeDropdown != null) return;
        if (HepaiWayPanel == null || HepaiWayDropdown == null) return;

        Transform parent = InputHepaiLimitPlane != null
            ? InputHepaiLimitPlane.transform.parent
            : HepaiWayPanel.transform.parent;
        CuoheTypePanel = Instantiate(HepaiWayPanel, parent);
        CuoheTypePanel.name = "CuoheTypePanel";
        CuoheTypePanel.SetActive(false);
        CuoheTypeDropdown = CuoheTypePanel.GetComponentInChildren<TMP_Dropdown>(true);
        foreach (TMP_Text label in CuoheTypePanel.GetComponentsInChildren<TMP_Text>(true)) {
            if (label.GetComponentInParent<TMP_Dropdown>() != null) continue;
            label.text = "错和形式";
            break;
        }
    }

    private void EnsureChangshaOptionControls() {
        Toggle toggleTemplate = TacticalCallToggle != null ? TacticalCallToggle : RedDoraToggle;
        ChangshaInitialSiXiToggle = EnsureClonedToggle(toggleTemplate, ChangshaInitialSiXiToggle, "ChangshaInitialSiXi", "四喜", true);
        Toggle lastToggle = ChangshaInitialSiXiToggle != null ? ChangshaInitialSiXiToggle : toggleTemplate;
        ChangshaInitialBanBanHuToggle = EnsureClonedToggle(lastToggle, ChangshaInitialBanBanHuToggle, "ChangshaInitialBanBanHu", "板板胡", true);
        lastToggle = ChangshaInitialBanBanHuToggle != null ? ChangshaInitialBanBanHuToggle : lastToggle;
        ChangshaInitialQueYiSeToggle = EnsureClonedToggle(lastToggle, ChangshaInitialQueYiSeToggle, "ChangshaInitialQueYiSe", "缺一色", true);
        lastToggle = ChangshaInitialQueYiSeToggle != null ? ChangshaInitialQueYiSeToggle : lastToggle;
        ChangshaInitialLiuLiuShunToggle = EnsureClonedToggle(lastToggle, ChangshaInitialLiuLiuShunToggle, "ChangshaInitialLiuLiuShun", "六六顺", true);
        lastToggle = ChangshaInitialLiuLiuShunToggle != null ? ChangshaInitialLiuLiuShunToggle : lastToggle;
        ChangshaInitialSanTongToggle = EnsureClonedToggle(lastToggle, ChangshaInitialSanTongToggle, "ChangshaInitialSanTong", "三同", true);
        lastToggle = ChangshaInitialSanTongToggle != null ? ChangshaInitialSanTongToggle : lastToggle;
        ChangshaDealerBirdToggle = EnsureClonedToggle(lastToggle, ChangshaDealerBirdToggle, "ChangshaDealerBird", "定庄扎鸟", true);

        ChangshaOpenKongPanel = EnsureClonedDropdownPanel(HepaiWayPanel, ChangshaOpenKongPanel, "ChangshaOpenKongPanel", "开杠张数");
        ChangshaOpenKongDropdown = ChangshaOpenKongPanel != null
            ? ChangshaOpenKongPanel.GetComponentInChildren<TMP_Dropdown>(true)
            : null;
        if (ChangshaOpenKongDropdown != null) {
            ChangshaOpenKongDropdown.ClearOptions();
            ChangshaOpenKongDropdown.AddOptions(new List<string> { "1张", "2张", "3张", "4张" });
            SetChangshaOpenKongCount(2);
        }

        GameObject birdTemplate = ChangshaOpenKongPanel != null ? ChangshaOpenKongPanel : HepaiWayPanel;
        ChangshaBirdCountPanel = EnsureClonedDropdownPanel(birdTemplate, ChangshaBirdCountPanel, "ChangshaBirdCountPanel", "扎鸟张数");
        ChangshaBirdCountDropdown = ChangshaBirdCountPanel != null
            ? ChangshaBirdCountPanel.GetComponentInChildren<TMP_Dropdown>(true)
            : null;
        if (ChangshaBirdCountDropdown != null) {
            ChangshaBirdCountDropdown.ClearOptions();
            ChangshaBirdCountDropdown.AddOptions(new List<string> { "不扎鸟", "1鸟", "2鸟", "4鸟" });
            SetChangshaBirdCount(2);
        }

        EnsureChangshaScoreControls();

        SetChangshaOptionsVisible(false);
    }

    private void EnsureChangshaScoreControls() {
        // 长沙计分模式及自定义分值独占一行，避免与开杠、扎鸟配置挤在同一行。
        Transform parent = InputHepaiLimitPlane != null
            ? InputHepaiLimitPlane.transform.parent
            : transform;
        if (ChangshaScoreRow == null) {
            ChangshaScoreRow = new GameObject("ChangshaScoreRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            ChangshaScoreRow.transform.SetParent(parent, false);
            HorizontalLayoutGroup layout = ChangshaScoreRow.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            ChangshaScoreRow.GetComponent<LayoutElement>().minHeight = 44f;
        }

        Toggle toggleTemplate = ChangshaDealerBirdToggle != null ? ChangshaDealerBirdToggle : TacticalCallToggle;
        ChangshaBaseScoreNoDealerToggle = EnsureClonedToggle(
            toggleTemplate,
            ChangshaBaseScoreNoDealerToggle,
            "ChangshaBaseScoreNoDealer",
            "不区分庄闲",
            false);
        if (ChangshaBaseScoreNoDealerToggle != null) {
            ChangshaBaseScoreNoDealerToggle.onValueChanged.RemoveListener(OnChangshaBaseScoreModeChanged);
            ChangshaBaseScoreNoDealerToggle.onValueChanged.AddListener(OnChangshaBaseScoreModeChanged);
        }

        ChangshaSmallHuScorePanel = EnsureChangshaScoreInputPanel(
            ChangshaSmallHuScorePanel,
            "ChangshaSmallHuScorePanel",
            "小胡分数",
            out ChangshaSmallHuScoreInput);
        ChangshaBigHuScorePanel = EnsureChangshaScoreInputPanel(
            ChangshaBigHuScorePanel,
            "ChangshaBigHuScorePanel",
            "大胡分数",
            out ChangshaBigHuScoreInput);
        if (ChangshaSmallHuScoreInput != null) ChangshaSmallHuScoreInput.text = "2";
        if (ChangshaBigHuScoreInput != null) ChangshaBigHuScoreInput.text = "8";
        RefreshChangshaScoreInputVisibility();
    }

    private GameObject EnsureChangshaScoreInputPanel(
        GameObject existing,
        string objectName,
        string labelText,
        out TMP_InputField input) {
        if (existing == null && InputHepaiLimitPlane != null) {
            existing = Instantiate(InputHepaiLimitPlane, ChangshaScoreRow.transform);
            existing.name = objectName;
            foreach (TMP_Text label in existing.GetComponentsInChildren<TMP_Text>(true)) {
                if (label.GetComponentInParent<TMP_InputField>() != null) continue;
                label.text = labelText;
                break;
            }
        }
        input = existing != null ? existing.GetComponentInChildren<TMP_InputField>(true) : null;
        if (input != null) input.contentType = TMP_InputField.ContentType.IntegerNumber;
        return existing;
    }

    private void OnChangshaBaseScoreModeChanged(bool _) {
        RefreshChangshaScoreInputVisibility();
    }

    private void RefreshChangshaScoreInputVisibility() {
        bool visible = _ruleState == "changsha"
            && ChangshaBaseScoreNoDealerToggle != null
            && ChangshaBaseScoreNoDealerToggle.isOn;
        if (ChangshaSmallHuScorePanel != null) ChangshaSmallHuScorePanel.SetActive(visible);
        if (ChangshaBigHuScorePanel != null) ChangshaBigHuScorePanel.SetActive(visible);
    }

    private GameObject EnsureClonedDropdownPanel(GameObject template, GameObject existing, string goName, string labelText) {
        if (existing != null) return existing;
        if (template == null) return null;
        GameObject clone = Instantiate(template, template.transform.parent);
        clone.name = goName;
        SetPanelLabel(clone, labelText);
        clone.SetActive(false);
        return clone;
    }

    private static void SetPanelLabel(GameObject panel, string labelText) {
        if (panel == null) return;
        foreach (TMP_Text label in panel.GetComponentsInChildren<TMP_Text>(true)) {
            if (label.GetComponentInParent<TMP_Dropdown>() != null) continue;
            label.text = labelText;
            return;
        }
    }

    private void SetChangshaOptionsVisible(bool visible) {
        if (ChangshaOpenKongPanel != null) ChangshaOpenKongPanel.SetActive(visible);
        if (ChangshaBirdCountPanel != null) ChangshaBirdCountPanel.SetActive(visible);
        SetToggleVisible(ChangshaInitialSiXiToggle, visible);
        SetToggleVisible(ChangshaInitialBanBanHuToggle, visible);
        SetToggleVisible(ChangshaInitialQueYiSeToggle, visible);
        SetToggleVisible(ChangshaInitialLiuLiuShunToggle, visible);
        SetToggleVisible(ChangshaInitialSanTongToggle, visible);
        SetToggleVisible(ChangshaDealerBirdToggle, visible);
        SetToggleVisible(ChangshaBaseScoreNoDealerToggle, visible);
        if (ChangshaScoreRow != null) ChangshaScoreRow.SetActive(visible);
        RefreshChangshaScoreInputVisibility();
    }

    private static void SetToggleVisible(Toggle toggle, bool visible) {
        if (toggle != null) toggle.gameObject.SetActive(visible);
    }

    private void SetChangshaOpenKongCount(int count) {
        if (ChangshaOpenKongDropdown == null) return;
        ChangshaOpenKongDropdown.value = Mathf.Clamp(count, 1, 4) - 1;
        ChangshaOpenKongDropdown.RefreshShownValue();
    }

    private int GetChangshaOpenKongCount() {
        return ChangshaOpenKongDropdown != null
            ? Mathf.Clamp(ChangshaOpenKongDropdown.value + 1, 1, 4)
            : 2;
    }

    private void SetChangshaBirdCount(int count) {
        if (ChangshaBirdCountDropdown == null) return;
        int index = Array.IndexOf(ChangshaBirdCountOptions, count);
        ChangshaBirdCountDropdown.value = index >= 0 ? index : 2;
        ChangshaBirdCountDropdown.RefreshShownValue();
    }

    private int GetChangshaBirdCount() {
        if (ChangshaBirdCountDropdown == null) return 2;
        int index = Mathf.Clamp(ChangshaBirdCountDropdown.value, 0, ChangshaBirdCountOptions.Length - 1);
        return ChangshaBirdCountOptions[index];
    }

    private void CacheDefaultGameRoundLabels() {
        if (_gameRoundLabelsCached) return;
        _defaultGameRoundLabels = new[] {
            GetToggleLabelText(gameTime1Button),
            GetToggleLabelText(gameTime2Button),
            GetToggleLabelText(gameTime3Button),
            GetToggleLabelText(gameTime4Button),
        };
        _gameRoundLabelsCached = true;
    }

    private void ApplyGameRoundDisplayForRule() {
        CacheDefaultGameRoundLabels();
        bool isChangsha = _ruleState == "changsha";
        if (isChangsha && gameTime3Button != null && gameTime3Button.isOn) {
            SelectGameTime(4);
        }

        SetToggleLabel(gameTime1Button, isChangsha ? "4局" : _defaultGameRoundLabels[0]);
        SetToggleLabel(gameTime2Button, isChangsha ? "8局" : _defaultGameRoundLabels[1]);
        SetToggleLabel(gameTime3Button, _defaultGameRoundLabels[2]);
        SetToggleLabel(gameTime4Button, isChangsha ? "16局" : _defaultGameRoundLabels[3]);
        if (gameTime3Button != null) gameTime3Button.gameObject.SetActive(!isChangsha);
    }

    private static string GetToggleLabelText(Toggle toggle) {
        TMP_Text label = GetToggleLabel(toggle);
        return label != null ? label.text : "";
    }

    private static void SetToggleLabel(Toggle toggle, string text) {
        TMP_Text label = GetToggleLabel(toggle);
        if (label != null) label.text = text;
    }

    private static TMP_Text GetToggleLabel(Toggle toggle) {
        return toggle != null ? toggle.GetComponentInChildren<TMP_Text>(true) : null;
    }

    public bool IsVenueMode => !string.IsNullOrEmpty(_venueEventId);

    public void OpenForVenue(string eventId) {
        _venueEventId = string.IsNullOrEmpty(eventId) ? null : eventId;
        ApplyDefaultRoomNameForCurrentUser();
    }

    public void CloseVenueMode() {
        _venueEventId = null;
        if (gameObject.activeSelf) gameObject.SetActive(false);
        WindowFadeTransition.Normalize(gameObject);
    }

    private void ClosePanel() {
        if (IsVenueMode) {
            if (EventDetailPanel.Instance != null) {
                EventDetailPanel.Instance.CloseVenueCreateFaded();
            } else {
                CloseVenueMode();
            }
            return;
        }
        WindowsManager.Instance.DismissCreateRoomPanel();
    }

    private void OnAddRuleClick() {
        WindowsManager.Instance.SwitchWindow("aboutUs");
    }

    private void CreateRoom() {
        if (_ruleState == "riichi") {
            CreateRiichiRoom();
            return;
        }

        if (_ruleState == "guobiao") {
            CreateGBRoom();
            return;
        }

        if (_ruleState == "qingque") {
            CreateQingqueRoom();
            return;
        }

        if (_ruleState == "classical") {
            CreateClassicalRoom();
            return;
        }

        if (_ruleState == "sichuan") {
            CreateSichuanRoom();
            return;
        }

        if (_ruleState == "jiandan") {
            CreateJiandanRoom();
            return;
        }

        if (_ruleState == "changsha") {
            CreateChangshaRoom();
            return;
        }

        if (_ruleState == "taiwan") {
            CreateTaiwanRoom();
            return;
        }

        if (_ruleState == "hongque") {
            CreateHongqueRoom();
            return;
        }
    }

    private void CreateRiichiRoom() {
        // HepaiWayDropdown 选项顺序：0=多家和，1=三家和了流局，2=头跳
        string hepaiWay = HepaiWayDropdown.value switch {
            0 => "multi_ron",
            1 => "three_ron_abort",
            2 => "head_bump",
            _ => "multi_ron",
        };

        int hepaiLimit = (int)DefaultsOf("riichi")[CreateRoomKeys.HepaiLimit];
        if (InputHepaiLimitToggle.isOn && int.TryParse(HepaiLimitInput.text.Trim(), out int parsed)) {
            hepaiLimit = Mathf.Clamp(parsed, 1, 64);
        }

        var config = new Riichi_Create_RoomConfig {
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            Rule = "riichi",
            SubRule = GetSelectedRiichiSubRule(),
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            TouristLimit = TouristLimitToggle.isOn,
            AllowSpectator = AllowSpectatorToggle.isOn,
            CuoHe = CuoHeheToggle.isOn,
            HepaiLimit = hepaiLimit,
            RedDora = RedDoraToggle.isOn,
            AllowKuikae = KuikaeToggle != null && !KuikaeToggle.isOn,
            OpenXiru = XiruToggle != null ? XiruToggle.isOn : (bool)DefaultsOf("riichi")[CreateRoomKeys.OpenXiru],
            OpenTobi = TobiToggle != null ? TobiToggle.isOn : (bool)DefaultsOf("riichi")[CreateRoomKeys.OpenTobi],
            HepaiWay = hepaiWay,
            EventId = _venueEventId,
        };

        if (!config.Validate(out string error, passwordToggle.isOn, SetRandomSeedToggle.isOn)) {
            Debug.LogWarning(error);
            NotificationManager.Instance.ShowTip("create_room", false, $"创建房间失败: {error}");
            return;
        }
        RoomNetworkManager.Instance.Create_Riichi_Room(config);
    }

    private static string GetDefaultRoomName() {
        string name = UserDataManager.Instance.Username;
        return (string.IsNullOrEmpty(name) ? "未知用户" : name) + "的游戏";
    }

    private int GetGuobiaoSubRuleDefaultHepaiLimit(string subRule) {
        return subRule switch {
            "guobiao/xiaolin" => 1,
            "guobiao/kshen" => 8,
            "guobiao/lanshi" => 5,
            _ => 8
        };
    }

    private int ResolveGuobiaoHepaiLimit(string subRule) {
        int hepaiLimit = GetGuobiaoSubRuleDefaultHepaiLimit(subRule);
        if (InputHepaiLimitToggle.isOn && int.TryParse(HepaiLimitInput.text.Trim(), out int inputLimit))
            hepaiLimit = Mathf.Clamp(inputLimit, 1, 64);
        return hepaiLimit;
    }

    private void CreateGBRoom() {
        string subRule = GetSelectedSubRule();
        int hepaiLimit = ResolveGuobiaoHepaiLimit(subRule);

        var config = new GB_Create_RoomConfig {
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            Rule = "guobiao",
            SubRule = subRule,
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            CuoHe = subRule == "guobiao/lanshi" || CuoHeheToggle.isOn,
            CuoheType = subRule == "guobiao/lanshi" ? 1 : GetSelectedCuoheType(),
            HepaiLimit = hepaiLimit,
            TouristLimit = TouristLimitToggle.isOn,
            AllowSpectator = AllowSpectatorToggle.isOn,
            TacticalCall = TacticalCallToggle.isOn,
            EventId = _venueEventId,
        };

        if (!config.Validate(out string error, passwordToggle.isOn, SetRandomSeedToggle.isOn)) {
            Debug.LogWarning(error);
            NotificationManager.Instance.ShowTip("create_room", false, $"创建房间失败: {error}");
            return;
        }
        RoomNetworkManager.Instance.Create_GB_Room(config);
    }

    private void CreateQingqueRoom() {
        var config = new Qingque_Create_RoomConfig {
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            Rule = "qingque",
            SubRule = "qingque/standard",
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            TouristLimit = TouristLimitToggle.isOn,
            AllowSpectator = AllowSpectatorToggle.isOn,
            TacticalCall = TacticalCallToggle.isOn,
            EventId = _venueEventId,
        };

        if (!config.Validate(out string error, passwordToggle.isOn, SetRandomSeedToggle.isOn)) {
            Debug.LogWarning(error);
            NotificationManager.Instance.ShowTip("create_room", false, $"创建房间失败: {error}");
            return;
        }
        RoomNetworkManager.Instance.Create_Qingque_Room(config);
    }

    private void CreateTaiwanRoom() {
        var config = new Taiwan_Create_RoomConfig {
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            Rule = "taiwan",
            SubRule = "taiwan/standard",
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            TouristLimit = TouristLimitToggle.isOn,
            AllowSpectator = AllowSpectatorToggle.isOn,
            CuoHe = CuoHeheToggle.isOn,
            CuoheType = GetSelectedCuoheType(),
            DetailedConfig = BuildDetailedConfigValues("taiwan"),
            EventId = _venueEventId,
        };

        if (!config.Validate(out string error, passwordToggle.isOn, SetRandomSeedToggle.isOn)) {
            Debug.LogWarning(error);
            NotificationManager.Instance.ShowTip("create_room", false, $"创建房间失败: {error}");
            return;
        }
        RoomNetworkManager.Instance.Create_Taiwan_Room(config);
    }

    private void CreateClassicalRoom() {
        var config = new Qingque_Create_RoomConfig {
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            Rule = "classical",
            SubRule = "classical/standard",
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            TouristLimit = TouristLimitToggle.isOn,
            AllowSpectator = AllowSpectatorToggle.isOn,
            EventId = _venueEventId,
        };

        if (!config.Validate(out string error, passwordToggle.isOn, SetRandomSeedToggle.isOn)) {
            Debug.LogWarning(error);
            NotificationManager.Instance.ShowTip("create_room", false, $"创建房间失败: {error}");
            return;
        }
        RoomNetworkManager.Instance.Create_Classical_Room(config);
    }

    private void CreateSichuanRoom() {
        bool bloodBattle = BloodBattleToggle != null
            ? BloodBattleToggle.isOn
            : (bool)DefaultsOf("sichuan")[CreateRoomKeys.BloodBattle];

        var config = new Sichuan_Create_RoomConfig {
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            Rule = "sichuan",
            SubRule = "sichuan/standard",
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            TouristLimit = TouristLimitToggle.isOn,
            AllowSpectator = AllowSpectatorToggle.isOn,
            TacticalCall = TacticalCallToggle.isOn,
            BloodBattle = bloodBattle,
            EventId = _venueEventId,
        };

        if (!config.Validate(out string error, passwordToggle.isOn, SetRandomSeedToggle.isOn)) {
            Debug.LogWarning(error);
            NotificationManager.Instance.ShowTip("create_room", false, $"创建房间失败: {error}");
            return;
        }
        RoomNetworkManager.Instance.Create_Sichuan_Room(config);
    }

    private void CreateJiandanRoom() {
        var config = new Jiandan_Create_RoomConfig {
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            Rule = "jiandan",
            SubRule = "jiandan/standard",
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            TouristLimit = TouristLimitToggle.isOn,
            AllowSpectator = AllowSpectatorToggle.isOn,
            TacticalCall = false,
            EventId = _venueEventId,
        };

        if (!config.Validate(out string error, passwordToggle.isOn, SetRandomSeedToggle.isOn)) {
            Debug.LogWarning(error);
            NotificationManager.Instance.ShowTip("create_room", false, $"创建房间失败: {error}");
            return;
        }
        RoomNetworkManager.Instance.Create_Jiandan_Room(config);
    }

    private void CreateHongqueRoom() {
        // 虹雀和牌方式：0=允许多家和（multi_ron），1=头跳（head_bump）
        string hepaiWay = HepaiWayDropdown.value switch {
            1 => "head_bump",
            _ => "multi_ron",
        };
        var config = new Jiandan_Create_RoomConfig {
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            Rule = "hongque",
            SubRule = "hongque/v1.6",
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            TouristLimit = TouristLimitToggle.isOn,
            AllowSpectator = false,
            TacticalCall = false,
            HepaiWay = hepaiWay,
            EventId = _venueEventId,
        };
        if (!config.Validate(out string error, passwordToggle.isOn, SetRandomSeedToggle.isOn)) {
            NotificationManager.Instance.ShowTip("create_room", false, $"创建房间失败: {error}");
            return;
        }
        RoomNetworkManager.Instance.Create_Hongque_Room(config);
    }

    private void CreateChangshaRoom() {
        var config = new Changsha_Create_RoomConfig {
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            Rule = "changsha",
            SubRule = "changsha/classic_double_bird",
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            TouristLimit = TouristLimitToggle.isOn,
            AllowSpectator = AllowSpectatorToggle.isOn,
            TacticalCall = TacticalCallToggle.isOn,
            OpenKongReplacementCount = GetChangshaOpenKongCount(),
            InitialHuSiXi = ChangshaInitialSiXiToggle == null || ChangshaInitialSiXiToggle.isOn,
            InitialHuBanBanHu = ChangshaInitialBanBanHuToggle == null || ChangshaInitialBanBanHuToggle.isOn,
            InitialHuQueYiSe = ChangshaInitialQueYiSeToggle == null || ChangshaInitialQueYiSeToggle.isOn,
            InitialHuLiuLiuShun = ChangshaInitialLiuLiuShunToggle == null || ChangshaInitialLiuLiuShunToggle.isOn,
            InitialHuSanTong = ChangshaInitialSanTongToggle == null || ChangshaInitialSanTongToggle.isOn,
            BirdCount = GetChangshaBirdCount(),
            DealerBird = ChangshaDealerBirdToggle == null || ChangshaDealerBirdToggle.isOn,
            BaseScoreNoDealer = ChangshaBaseScoreNoDealerToggle != null && ChangshaBaseScoreNoDealerToggle.isOn,
            SmallHuScore = ReadChangshaScore(ChangshaSmallHuScoreInput, 2),
            BigHuScore = ReadChangshaScore(ChangshaBigHuScoreInput, 8),
            EventId = _venueEventId,
        };

        if (!config.Validate(out string error, passwordToggle.isOn, SetRandomSeedToggle.isOn)) {
            Debug.LogWarning(error);
            NotificationManager.Instance.ShowTip("create_room", false, $"创建房间失败: {error}");
            return;
        }
        RoomNetworkManager.Instance.Create_Changsha_Room(config);
    }

    private static int ReadChangshaScore(TMP_InputField input, int fallback) {
        if (input == null) return fallback;
        return int.TryParse(input.text, out int value) ? value : 0;
    }

    private int GetSelectedGameTime() {
        if (_ruleState == "changsha") {
            if (gameTime1Button.isOn) return 1;
            if (gameTime2Button.isOn) return 2;
            return 4;
        }
        if (gameTime1Button.isOn) return 1;
        if (gameTime2Button.isOn) return 2;
        if (gameTime3Button.isOn) return 3;
        if (gameTime4Button.isOn) return 4;
        return 1;
    }

    private int GetSelectedRoundTimer() {
        return roundTimer.value switch {
            0 => 0,
            1 => 5,
            2 => 10,
            3 => 20,
            4 => 40,
            5 => 60,
            _ => 20
        };
    }

    private int GetSelectedStepTimer() {
        return stepTimer.value switch {
            0 => 3,
            1 => 5,
            2 => 10,
            3 => 20,
            4 => 40,
            _ => 5
        };
    }

    private void TogglePassword(bool isOn) {
        PasswordPanel.SetActive(isOn);
    }

    private void ToggleSetRandomSeed(bool isOn) {
        SetRandomSeedPanel.SetActive(isOn);
    }

    private void ToggleInputHepaiLimit(bool isOn) {
        InputHepaiLimitPlane.SetActive(isOn);
        if (!isOn) {
            if (_ruleState == "guobiao")
                HepaiLimitInput.text = GetGuobiaoSubRuleDefaultHepaiLimit(GetSelectedSubRule()).ToString();
            else {
                object fallbackValue;
                int fallback = TryGetDefaults(_ruleState, out var config)
                    && config.TryGetValue(CreateRoomKeys.HepaiLimit, out fallbackValue)
                    ? Convert.ToInt32(fallbackValue)
                    : 8;
                HepaiLimitInput.text = fallback.ToString();
            }
        }
    }

    private int GetSelectedCuoheType() {
        if (CuoheTypeDropdown == null) return 0;
        return Mathf.Clamp(CuoheTypeDropdown.value, 0, 1);
    }

    private void RefreshCuoheTypePanelVisibility() {
        if (CuoheTypePanel == null) return;
        bool isXiaolin = _ruleState == "guobiao" && SubRuleDropdown.value == 1;
        bool isLanshi = _ruleState == "guobiao" && SubRuleDropdown.value == 3;
        bool showPanel = TryGetDefaults(_ruleState, out var config)
            && config.ContainsKey(CreateRoomKeys.CuoheType)
            && !isXiaolin
            && !isLanshi
            && CuoHeheToggle.isOn;
        CuoheTypePanel.SetActive(showPanel);
    }

    private void ToggleCuoHehe(bool isOn) {
        RefreshCuoheTypePanelVisibility();
        if (!isOn) return;
        tipsToggle.onValueChanged.RemoveListener(ToggleTips);
        tipsToggle.isOn = false;
        tipsToggle.onValueChanged.AddListener(ToggleTips);
    }

    private void ToggleTips(bool isOn) {
        if (!isOn) return;
        CuoHeheToggle.onValueChanged.RemoveListener(ToggleCuoHehe);
        CuoHeheToggle.isOn = false;
        CuoHeheToggle.onValueChanged.AddListener(ToggleCuoHehe);
        RefreshCuoheTypePanelVisibility();
    }
}
