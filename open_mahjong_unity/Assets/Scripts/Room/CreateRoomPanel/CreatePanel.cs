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
    [SerializeField] private Toggle countTipsToggle;
    [SerializeField] private Toggle pointerTipsToggle;
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
    [SerializeField] private Toggle ClaimProtectionToggle;
    [SerializeField] private Toggle BloodBattleToggle;
    [SerializeField] private Toggle ChangshaInitialSiXiToggle;
    [SerializeField] private Toggle ChangshaInitialBanBanHuToggle;
    [SerializeField] private Toggle ChangshaInitialQueYiSeToggle;
    [SerializeField] private Toggle ChangshaInitialLiuLiuShunToggle;
    [SerializeField] private Toggle ChangshaInitialSanTongToggle;
    [SerializeField] private Toggle ChangshaDealerBirdToggle;
    [SerializeField] private Toggle ChangshaBaseScoreNoDealerToggle;
    [SerializeField] private Toggle WallWanToggle;
    [SerializeField] private Toggle WallTongToggle;
    [SerializeField] private Toggle WallSuoToggle;
    [SerializeField] private Toggle WallWindsToggle;
    [SerializeField] private Toggle WallDragonsToggle;
    [SerializeField] private Toggle WallFlowersToggle;

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
    [SerializeField] private GameObject RiichiStartingScorePanel;
    [SerializeField] private TMP_InputField RiichiStartingScoreInput;
    [SerializeField] private Toggle DuplicateWallToggle;
    [SerializeField] private GameObject DuplicateWallPanel;
    [SerializeField] private TMP_InputField DuplicateKeyInput;
    [SerializeField] private Toggle GuobiaoFlowersToggle;
    [SerializeField] private UnityEngine.UI.Toggle TianDiRenHeToggle;
    [SerializeField] private TMP_Text GuobiaoFlowersLabel;

    private string SelectedDuplicateKey => _ruleState == "guobiao" && DuplicateWallToggle != null && DuplicateWallToggle.isOn
        ? DuplicateKeyInput?.text.Trim() ?? "" : "";

    [Header("按钮")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Button createButton;
    [SerializeField] private Button addRuleButton;
    [SerializeField] private Button DetailedConfigButton;

    private bool _gameRoundLabelsCached;
    private bool _duplicateWasEnabled;
    private bool _spectatorBeforeDuplicate;
    private string[] _defaultGameRoundLabels;
    private int _roomNameBoundUserId = int.MinValue;
    private static readonly List<CreatePanel> LivePanels = new List<CreatePanel>();
    public static bool IsAnyCreationPanelOpen => LivePanels.Exists(panel => panel && panel.isActiveAndEnabled);

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
        BindFixedCreateControls();
        BindDetailedConfigControls("taiwan");
        BindDetailedConfigControls("hongkong");
        BindDetailedConfigControls("riichi");
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

        InitSubRuleDropdown();
        ApplyRuleDefaults(_ruleState);
        RefreshVisibility();
        RefreshSubRuleDescription();
        InitializeRoomPresentation();
        ApplyDefaultRoomPreset();
    }

    private void OnEnable() {
        ApplyDefaultRoomNameForCurrentUser();
        ShowRoomHelp("");
        WindowsManager.Instance?.ApplyStreamerModePanels();
    }

    private void OnDisable() {
        CancelDetailedConfigChanges();
        WindowsManager.Instance?.ApplyStreamerModePanels();
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
        if (DuplicateWallToggle != null) DuplicateWallToggle.isOn = false;
        if (DuplicateKeyInput != null) DuplicateKeyInput.text = "";
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
        PopulateSubRuleDropdown(_ruleState);
        ApplyRuleDefaults(_ruleState);
        RefreshVisibility();
        if (hasSubRule) {
            OnSubRuleChanged(SubRuleDropdown.value);
        }
        RefreshSubRuleDescription();
        ApplyDefaultRoomPreset();
        ShowRoomHelp("");
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
            case GuangdongMilRules.MinimumScoreKey: if (GuangdongMinimumScoreToggle) GuangdongMinimumScoreToggle.isOn = (bool)value; break;
            case YixingRuleBootstrap.SevenPairsKey: if (YixingSevenPairsToggle) YixingSevenPairsToggle.isOn = (bool)value; break;
            case CreateRoomKeys.GameRound:      SelectGameTime((int)value); break;
            case CreateRoomKeys.RoundTimer:     roundTimer.value = (int)value; break;
            case CreateRoomKeys.StepTimer:      stepTimer.value = (int)value; break;
            case CreateRoomKeys.PointerTips: pointerTipsToggle.isOn = (bool)value; break;
            case CreateRoomKeys.CountTips:      countTipsToggle.isOn = (bool)value; break;
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
            case CreateRoomKeys.StartingScore:
                if (RiichiStartingScoreInput != null) RiichiStartingScoreInput.text = ((int)value).ToString();
                break;
            case CreateRoomKeys.RedDora:        RedDoraToggle.isOn = (bool)value; break;
            case CreateRoomKeys.AllowKuikae:    if (KuikaeToggle != null) KuikaeToggle.isOn = !(bool)value; break;
            case CreateRoomKeys.OpenXiru:       if (XiruToggle != null) XiruToggle.isOn = (bool)value; break;
            case CreateRoomKeys.OpenTobi:       if (TobiToggle != null) TobiToggle.isOn = (bool)value; break;
            case CreateRoomKeys.HepaiWay:
                HepaiWayDropdown.value = (int)value;
                HepaiWayDropdown.RefreshShownValue();
                break;
            case CreateRoomKeys.ClaimProtection: ClaimProtectionToggle.isOn = (bool)value; break;
            case CreateRoomKeys.TacticalCall:   TacticalCallToggle.isOn = (bool)value; break;
            case CreateRoomKeys.UseFlowers:
                if (GuobiaoFlowersToggle != null) GuobiaoFlowersToggle.SetIsOnWithoutNotify((bool)value);
                break;
            case CreateRoomKeys.TianDiRenHe:
                if (TianDiRenHeToggle != null) TianDiRenHeToggle.SetIsOnWithoutNotify((bool)value);
                break;
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
            case CreateRoomKeys.WallWan: if (WallWanToggle != null) WallWanToggle.isOn = (bool)value; break;
            case CreateRoomKeys.WallTong: if (WallTongToggle != null) WallTongToggle.isOn = (bool)value; break;
            case CreateRoomKeys.WallSuo: if (WallSuoToggle != null) WallSuoToggle.isOn = (bool)value; break;
            case CreateRoomKeys.WallWinds: if (WallWindsToggle != null) WallWindsToggle.isOn = (bool)value; break;
            case CreateRoomKeys.WallDragons: if (WallDragonsToggle != null) WallDragonsToggle.isOn = (bool)value; break;
            case CreateRoomKeys.WallFlowers: if (WallFlowersToggle != null) WallFlowersToggle.isOn = (bool)value; break;
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
        if (RiichiStartingScorePanel != null) RiichiStartingScorePanel.SetActive(visible.ContainsKey(CreateRoomKeys.StartingScore));

        bool isBloodBattle = _ruleState == "guobiao" && GetSelectedSubRule() == GuobiaoGameState.BloodBattleSubRule;
        bool isSichuanXueLiu = _ruleState == "sichuan" && SichuanLobby.IsXueliu(GetSelectedSubRule());
        bool isXiaolin = _ruleState == "guobiao" && SubRuleDropdown.value == 1;
        bool isLanshi  = _ruleState == "guobiao" && SubRuleDropdown.value == 3;
        bool isLangyong = _ruleState == "riichi" && SubRuleDropdown.value == 1;

        // 蓝十改固定启用“错和扣 40 分”，不暴露可变开关。
        bool showCuohe = visible.ContainsKey(CreateRoomKeys.Cuohe) && !isXiaolin && !isLanshi && !isBloodBattle;
        CuoHeheToggle.gameObject.SetActive(showCuohe);

        // 蓝十仍隐藏起和番自定义；标准/小林/K神均可改
        bool showHepaiLimit = visible.ContainsKey(CreateRoomKeys.HepaiLimit) && !isLanshi && !isBloodBattle
            && (_ruleState != "shanghai" || IsShanghaiQiaoma);
        RefreshHepaiLimitCaption();
        InputHepaiLimitToggle.gameObject.SetActive(showHepaiLimit);
        InputHepaiLimitPlane.SetActive(showHepaiLimit && InputHepaiLimitToggle.isOn && !IsShanghaiQiaoma);

        RedDoraToggle.gameObject.SetActive(visible.ContainsKey(CreateRoomKeys.RedDora));
        if (KuikaeToggle != null) KuikaeToggle.gameObject.SetActive(visible.ContainsKey(CreateRoomKeys.AllowKuikae) && !isLangyong);
        if (XiruToggle != null) XiruToggle.gameObject.SetActive(visible.ContainsKey(CreateRoomKeys.OpenXiru));
        if (TobiToggle != null) TobiToggle.gameObject.SetActive(visible.ContainsKey(CreateRoomKeys.OpenTobi));
        HepaiWayPanel.SetActive(visible.ContainsKey(CreateRoomKeys.HepaiWay));
        ClaimProtectionToggle.gameObject.SetActive(visible.ContainsKey(CreateRoomKeys.ClaimProtection));
        TacticalCallToggle.gameObject.SetActive(visible.ContainsKey(CreateRoomKeys.TacticalCall) && !isBloodBattle);
        if (BloodBattleToggle != null) {
            BloodBattleToggle.gameObject.SetActive(
                visible.ContainsKey(CreateRoomKeys.BloodBattle) && !isSichuanXueLiu);
        }
        SetChangshaOptionsVisible(DefaultsOf(_ruleState).ContainsKey(CreateRoomKeys.CsBirdCount));
        SetWallOptionsVisible(visible.ContainsKey(CreateRoomKeys.WallWan));
        if (YixingSevenPairsToggle) YixingSevenPairsToggle.gameObject.SetActive(visible.ContainsKey(YixingRuleBootstrap.SevenPairsKey));
        RefreshGuangdongControls();
        SetCommonCreateControlsVisible(visible);
        ApplyGameRoundDisplayForRule();
        RefreshCuoheTypePanelVisibility();
        RefreshDetailedConfigEntry();
        RebuildCreateRoomLayoutHierarchy();
        RefreshRoomPresentation();
    }

    private void RebuildCreateRoomLayoutHierarchy() {
        if (RoomContent) LayoutRebuilder.MarkLayoutForRebuild(RoomContent);
    }

    private string GetSelectedRiichiSubRule() {
        return CreateRoomRuleTextConfigCatalog.GetRule("riichi")
            .GetSubRule(SubRuleDropdown.value).Key;
    }

    private void RefreshSubRuleDescription() {
        CreateRoomSubRuleTextConfig subRule = CreateRoomRuleTextConfigCatalog
            .GetRule(_ruleState)
            .GetSubRule(SubRuleDropdown.value);
        createButton.interactable = true;
        SubRuleDescriptionText.text = subRule != null ? subRule.Description : "";
        if (_ruleState == "hongkong") SubRuleDescriptionText.text = HongKong_Create_RoomConfig.SourceDescription(GetSelectedSubRule(),BuildDetailedConfigValues("hongkong"));
        SubRuleDescriptionText.ForceMeshUpdate();
        ReflowRoomPresentation();
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
        RefreshHongKongMainControls();
        RefreshGuangdongControls();
        RefreshSubRuleDescription();
        if (_ruleState == "riichi" && RiichiStartingScoreInput != null) {
            string selectedSubRule = GetSelectedRiichiSubRule();
            if (selectedSubRule == "riichi/sanma") ApplyRiichiPreset(5);
            else if (selectedRiichiPresetIndex >= 4) {
                ApplyRiichiPreset(1);
                SubRuleDropdown.SetValueWithoutNotify(index);
            }
            RiichiStartingScoreInput.text = selectedSubRule == "riichi/sanma" ? "35000" : selectedSubRule == "riichi/langyong" ? "50000" : "25000";
        }
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
        CreateRoomRuleTextConfig rule = CreateRoomRuleTextConfigCatalog.GetRule(_ruleState);
        int index = SubRuleDropdown != null ? SubRuleDropdown.value : 0;
        CreateRoomSubRuleTextConfig subRule = rule.GetSubRule(index);
        return subRule?.Key ?? RuleRegistry.Resolve(_ruleState)?.DefaultSubRule ?? (_ruleState + "/standard");
    }

    /// <summary>运行时从赤宝牌开关克隆日麻专属选项，避免场景内重复手工挂接。</summary>
    private void SetWallOptionsVisible(bool visible) {
        SetToggleVisible(WallWanToggle, visible);
        SetToggleVisible(WallTongToggle, visible);
        SetToggleVisible(WallSuoToggle, visible);
        SetToggleVisible(WallWindsToggle, visible);
        SetToggleVisible(WallDragonsToggle, visible);
        SetToggleVisible(WallFlowersToggle, visible);
    }

    private void SetCommonCreateControlsVisible(Dictionary<string, object> visible) {
        if (TianDiRenHeToggle != null) {
            string subRule = GetSelectedSubRule();
            bool supported = _ruleState == "guobiao" && (subRule == "guobiao/standard" || subRule == GuobiaoGameState.SanmaSubRule || subRule == GuobiaoGameState.BloodBattleSubRule);
            TianDiRenHeToggle.gameObject.SetActive(supported);
            if (!supported) TianDiRenHeToggle.SetIsOnWithoutNotify(false);
        }
        if (DuplicateWallToggle != null) {
            bool supported = _ruleState == "guobiao" && GetSelectedSubRule() != GuobiaoGameState.SanmaSubRule;
            DuplicateWallToggle.gameObject.SetActive(supported);
            if (!supported) {
                DuplicateWallToggle.isOn = false;
                if (DuplicateKeyInput != null) DuplicateKeyInput.text = "";
                if (DuplicateWallPanel != null) DuplicateWallPanel.SetActive(false);
            }
        }
        bool showRound = visible.ContainsKey(CreateRoomKeys.GameRound);
        if (gameTime1Button != null) gameTime1Button.gameObject.SetActive(showRound);
        if (gameTime2Button != null) gameTime2Button.gameObject.SetActive(showRound);
        if (gameTime3Button != null) gameTime3Button.gameObject.SetActive(showRound && _ruleState != "changsha");
        if (gameTime4Button != null) gameTime4Button.gameObject.SetActive(showRound);
        if (roundTimer != null) roundTimer.gameObject.SetActive(visible.ContainsKey(CreateRoomKeys.RoundTimer));
        if (stepTimer != null) stepTimer.gameObject.SetActive(visible.ContainsKey(CreateRoomKeys.StepTimer));
        if (tipsToggle != null) tipsToggle.gameObject.SetActive(visible.ContainsKey(CreateRoomKeys.Tips));
        if (pointerTipsToggle != null) pointerTipsToggle.gameObject.SetActive(visible.ContainsKey(CreateRoomKeys.PointerTips));
        if (countTipsToggle != null) countTipsToggle.gameObject.SetActive(visible.ContainsKey(CreateRoomKeys.CountTips));
        if (AllowSpectatorToggle != null) AllowSpectatorToggle.gameObject.SetActive(visible.ContainsKey(CreateRoomKeys.AllowSpectator));
        if (TouristLimitToggle != null) TouristLimitToggle.gameObject.SetActive(visible.ContainsKey(CreateRoomKeys.TouristLimit));
        RefreshDuplicateWallOptions();
    }

    private void RefreshDuplicateWallOptions() {
        bool enabled = _ruleState == "guobiao" && DuplicateWallToggle != null && DuplicateWallToggle.isOn;
        if (AllowSpectatorToggle != null) {
            if (enabled && !_duplicateWasEnabled) _spectatorBeforeDuplicate = AllowSpectatorToggle.isOn;
            if (!enabled && _duplicateWasEnabled && _ruleState == "guobiao") AllowSpectatorToggle.isOn = _spectatorBeforeDuplicate;
        }
        _duplicateWasEnabled = enabled;
        if (GuobiaoFlowersToggle != null) {
            bool isLanshi = _ruleState == "guobiao" && GetSelectedSubRule() == "guobiao/lanshi";
            GuobiaoFlowersToggle.gameObject.SetActive(_ruleState == "guobiao");
            GuobiaoFlowersToggle.interactable = !enabled && !isLanshi;
            if (isLanshi) GuobiaoFlowersToggle.SetIsOnWithoutNotify(false);
            if (GuobiaoFlowersLabel != null) {
                GuobiaoFlowersLabel.text = enabled ? "花牌\n<size=65%>跟随复式设置</size>"
                    : isLanshi ? "花牌\n<size=65%>蓝十改无花</size>" : "花牌";
            }
        }
        if (AllowSpectatorToggle != null) {
            AllowSpectatorToggle.interactable = !enabled;
            if (enabled) AllowSpectatorToggle.isOn = false;
        }
        foreach (Toggle round in new[] { gameTime1Button, gameTime2Button, gameTime3Button, gameTime4Button }) {
            if (round != null) round.interactable = !enabled;
        }
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
        bool showRound = DefaultsOf(_ruleState).ContainsKey(CreateRoomKeys.GameRound);
        bool isChangsha = _ruleState == "changsha";
        bool usesFixedHandCount = _ruleState == "shanghai" || _ruleState == "zhongyong" || _ruleState == "guangdong" || _ruleState == GuizhouGameState.RuleId || _ruleState == HongzhongGameState.RuleId || _ruleState == HangzhouGameState.RuleId;
        if (!showRound) return;
        if (isChangsha && gameTime3Button != null && gameTime3Button.isOn) {
            SelectGameTime(4);
        }

        SetToggleLabel(gameTime1Button, (isChangsha || usesFixedHandCount) ? "4局" : _defaultGameRoundLabels[0]);
        SetToggleLabel(gameTime2Button, (isChangsha || usesFixedHandCount) ? "8局" : _defaultGameRoundLabels[1]);
        SetToggleLabel(gameTime3Button, usesFixedHandCount ? "12局" : _defaultGameRoundLabels[2]);
        SetToggleLabel(gameTime4Button, (isChangsha || usesFixedHandCount) ? "16局" : _defaultGameRoundLabels[3]);
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
        if (_ruleState == HangzhouGameState.RuleId) { CreateHangzhouRoom(); return; }
        if (_ruleState == "guangdong") { CreateTuidaoRoom(); return; }
        if (_ruleState == "shanghai" && GetSelectedSubRule() != "shanghai/qiaoma" && GetSelectedSubRule() != "shanghai/qinghunpeng") {
            NotificationManager.Instance.ShowTip("create_room", false, "不支持的上海麻将子规则");
            return;
        }
        if (_ruleState == "changchun") { CreateChangchunRoom(); return; }
        if (_ruleState == "shanxi") {
            CreateShanxiRoom();
            return;
        }
        if (_ruleState == "shanghai") {
            CreateShanghaiRoom();
            return;
        }
        int minimumLimit = _ruleState == "sichuan" ? 0 : 1;
        if (InputHepaiLimitToggle.gameObject.activeSelf && InputHepaiLimitToggle.isOn
            && (!int.TryParse(HepaiLimitInput.text.Trim(), out int limit) || limit < minimumLimit || limit > 64)) {
            HepaiLimitInput.ActivateInputField();
            NotificationManager.Instance.ShowTip("create_room", false, $"起和番须为{minimumLimit}–64的整数");
            return;
        }
        if (_ruleState == "guobiao" && DuplicateWallToggle != null && DuplicateWallToggle.isOn && string.IsNullOrEmpty(SelectedDuplicateKey)) {
            NotificationManager.Instance.ShowTip("create_room", false, "请填写在网站账户面板创建的复式密钥");
            return;
        }
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

        if (_ruleState == "jiandan" || _ruleState == "zhongyong") {
            CreateJiandanRoom();
            return;
        }

        if (_ruleState == "changsha") {
            CreateChangshaRoom();
            return;
        }

        if (_ruleState == WenzhouGameState.RuleId) {
            CreateWenzhouRoom(); return;
        }
        if (_ruleState == HongzhongGameState.RuleId) {
            CreateHongzhongRoom(); return;
        }
        if (_ruleState == YixingGameState.RuleId) {
            CreateYixingRoom(); return;
        }
        if (_ruleState == GuizhouGameState.RuleId) {
            CreateGuizhouRoom();
            return;
        }

        if (_ruleState == "hongkong") {
            CreateHongKongRoom();
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

        if (_ruleState == "free") {
            CreateFreeRoom();
            return;
        }
    }

    private void CreateRiichiRoom() {
        if (RiichiStartingScoreInput == null || !int.TryParse(RiichiStartingScoreInput.text.Trim(), out int startingScore)) {
            NotificationManager.Instance.ShowTip("create_room", false, "请填写有效的起始点数");
            return;
        }
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
            ClaimProtection = DefaultsOf(_ruleState).ContainsKey(CreateRoomKeys.ClaimProtection) && ClaimProtectionToggle.isOn,
            DetailedConfig = BuildDetailedConfigValues("riichi"),
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            Rule = "riichi",
            SubRule = GetSelectedRiichiSubRule(),
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            CountTips = countTipsToggle.isOn,
            PointerTips = pointerTipsToggle.isOn,
            TouristLimit = TouristLimitToggle.isOn,
            AllowSpectator = AllowSpectatorToggle.isOn,
            CuoHe = CuoHeheToggle.isOn,
            HepaiLimit = hepaiLimit,
            RedDora = RedDoraToggle.isOn,
            StartingScore = startingScore,
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
        if (subRule == "guobiao/lanshi") return 5;
        int hepaiLimit = GetGuobiaoSubRuleDefaultHepaiLimit(subRule);
        if (InputHepaiLimitToggle.isOn && int.TryParse(HepaiLimitInput.text.Trim(), out int inputLimit))
            hepaiLimit = Mathf.Clamp(inputLimit, 1, 64);
        return hepaiLimit;
    }

    private void CreateGBRoom() {
        string subRule = GetSelectedSubRule();
        int hepaiLimit = ResolveGuobiaoHepaiLimit(subRule);

        var config = new GB_Create_RoomConfig {
            ClaimProtection = DefaultsOf(_ruleState).ContainsKey(CreateRoomKeys.ClaimProtection) && ClaimProtectionToggle.isOn,
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            DuplicateKey = SelectedDuplicateKey,
            UseFlowers = subRule != "guobiao/lanshi" && (GuobiaoFlowersToggle == null || GuobiaoFlowersToggle.isOn),
            TianDiRenHe = (subRule == "guobiao/standard" || subRule == GuobiaoGameState.SanmaSubRule || subRule == GuobiaoGameState.BloodBattleSubRule) && TianDiRenHeToggle != null && TianDiRenHeToggle.isOn,
            Rule = "guobiao",
            SubRule = subRule,
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            CountTips = countTipsToggle.isOn,
            PointerTips = pointerTipsToggle.isOn,
            CuoHe = subRule != "guobiao/blood_battle" && (subRule == "guobiao/lanshi" || CuoHeheToggle.isOn),
            CuoheType = subRule == "guobiao/lanshi" ? 1 : GetSelectedCuoheType(),
            HepaiLimit = subRule == "guobiao/blood_battle" ? 8 : hepaiLimit,
            TouristLimit = TouristLimitToggle.isOn,
            AllowSpectator = AllowSpectatorToggle.isOn,
            TacticalCall = subRule != "guobiao/blood_battle" && TacticalCallToggle.isOn,
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
            ClaimProtection = DefaultsOf(_ruleState).ContainsKey(CreateRoomKeys.ClaimProtection) && ClaimProtectionToggle.isOn,
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            Rule = "qingque",
            SubRule = "qingque/standard",
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            CountTips = countTipsToggle.isOn,
            PointerTips = pointerTipsToggle.isOn,
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
            CountTips = countTipsToggle.isOn,
            PointerTips = pointerTipsToggle.isOn,
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
            ClaimProtection = DefaultsOf(_ruleState).ContainsKey(CreateRoomKeys.ClaimProtection) && ClaimProtectionToggle.isOn,
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            Rule = "classical",
            SubRule = "classical/standard",
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            CountTips = countTipsToggle.isOn,
            PointerTips = pointerTipsToggle.isOn,
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

    private bool IsShanghaiQiaoma => _ruleState == "shanghai" && GetSelectedSubRule() == "shanghai/qiaoma";

    private void CreateTuidaoRoom() {
        var config = new Qingque_Create_RoomConfig {
            TacticalCall = TacticalCallToggle != null && TacticalCallToggle.isOn,
            ClaimProtection = DefaultsOf(_ruleState).ContainsKey(CreateRoomKeys.ClaimProtection) && ClaimProtectionToggle.isOn,
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            Rule = "guangdong",
            SubRule = GetSelectedSubRule(),
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            CountTips = countTipsToggle.isOn,
            PointerTips = pointerTipsToggle.isOn,
            TouristLimit = TouristLimitToggle.isOn,
            AllowSpectator = AllowSpectatorToggle.isOn,
            EventId = _venueEventId,
        };

        if (!config.Validate(out string error, passwordToggle.isOn, SetRandomSeedToggle.isOn)) {
            Debug.LogWarning(error);
            NotificationManager.Instance.ShowTip("create_room", false, $"创建房间失败: {error}");
            return;
        }
        RoomNetworkManager.Instance.Create_Guangdong_Room(config, GuangdongMinimumScoreToggle == null || GuangdongMinimumScoreToggle.isOn);
    }

    private void CreateShanghaiRoom() {
        var config = new Qingque_Create_RoomConfig {
            ClaimProtection = DefaultsOf(_ruleState).ContainsKey(CreateRoomKeys.ClaimProtection) && ClaimProtectionToggle.isOn,
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            Rule = "shanghai",
            SubRule = GetSelectedSubRule(),
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            CountTips = countTipsToggle.isOn,
            PointerTips = pointerTipsToggle.isOn,
            TouristLimit = TouristLimitToggle.isOn,
            AllowSpectator = AllowSpectatorToggle.isOn,
            EventId = _venueEventId,
        };

        if (!config.Validate(out string error, passwordToggle.isOn, SetRandomSeedToggle.isOn)) {
            Debug.LogWarning(error);
            NotificationManager.Instance.ShowTip("create_room", false, $"创建房间失败: {error}");
            return;
        }
        RoomNetworkManager.Instance.Create_Shanghai_Room(config, IsShanghaiQiaoma && InputHepaiLimitToggle.isOn);
    }

    private void CreateSichuanRoom() {
        string subRule = GetSelectedSubRule();
        int hepaiLimit = (int)DefaultsOf("sichuan")[CreateRoomKeys.HepaiLimit];
        if (InputHepaiLimitToggle.isOn && int.TryParse(HepaiLimitInput.text.Trim(), out int parsed)) {
            hepaiLimit = parsed;
        }
        bool bloodBattle = SichuanLobby.IsXueliu(subRule) ? false : BloodBattleToggle != null
            ? BloodBattleToggle.isOn
            : (bool)DefaultsOf("sichuan")[CreateRoomKeys.BloodBattle];

        var config = new Sichuan_Create_RoomConfig {
            ClaimProtection = DefaultsOf(_ruleState).ContainsKey(CreateRoomKeys.ClaimProtection) && ClaimProtectionToggle.isOn,
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            Rule = "sichuan",
            SubRule = subRule,
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            CountTips = countTipsToggle.isOn,
            PointerTips = pointerTipsToggle.isOn,
            TouristLimit = TouristLimitToggle.isOn,
            AllowSpectator = AllowSpectatorToggle.isOn,
            TacticalCall = TacticalCallToggle.isOn,
            BloodBattle = bloodBattle,
            HepaiLimit = hepaiLimit,
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
            ClaimProtection = DefaultsOf(_ruleState).ContainsKey(CreateRoomKeys.ClaimProtection) && ClaimProtectionToggle.isOn,
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            Rule = _ruleState,
            SubRule = GetSelectedSubRule(),
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            CountTips = countTipsToggle.isOn,
            PointerTips = pointerTipsToggle.isOn,
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
            ClaimProtection = DefaultsOf(_ruleState).ContainsKey(CreateRoomKeys.ClaimProtection) && ClaimProtectionToggle.isOn,
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            Rule = "hongque",
            SubRule = "hongque/v1.6",
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            CountTips = countTipsToggle.isOn,
            PointerTips = pointerTipsToggle.isOn,
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
            ClaimProtection = DefaultsOf(_ruleState).ContainsKey(CreateRoomKeys.ClaimProtection) && ClaimProtectionToggle.isOn,
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            Rule = "changsha",
            SubRule = "changsha/classic_double_bird",
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            CountTips = countTipsToggle.isOn,
            PointerTips = pointerTipsToggle.isOn,
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

    private void CreateFreeRoom() {
        var config = new Free_Create_RoomConfig {
            RoomName = roomNameInput.text.Trim(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            TouristLimit = false,
            PointerTips = pointerTipsToggle.isOn,
            WallWan = WallWanToggle == null || WallWanToggle.isOn,
            WallTong = WallTongToggle == null || WallTongToggle.isOn,
            WallSuo = WallSuoToggle == null || WallSuoToggle.isOn,
            WallWinds = WallWindsToggle == null || WallWindsToggle.isOn,
            WallDragons = WallDragonsToggle == null || WallDragonsToggle.isOn,
            WallFlowers = WallFlowersToggle == null || WallFlowersToggle.isOn,
        };
        if (!config.Validate(out string error, passwordToggle.isOn, SetRandomSeedToggle.isOn)) {
            Debug.LogWarning(error);
            NotificationManager.Instance.ShowTip("create_room", false, $"创建房间失败: {error}");
            return;
        }
        RoomNetworkManager.Instance.Create_Free_Room(config);
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
            2 => 8,
            3 => 10,
            4 => 20,
            5 => 40,
            _ => 5
        };
    }

    private void TogglePassword(bool isOn) {
        PasswordPanel.SetActive(isOn);
    }

    private void ToggleSetRandomSeed(bool isOn) {
        if (isOn && DuplicateWallToggle != null) DuplicateWallToggle.isOn = false;
        SetRandomSeedPanel.SetActive(isOn);
    }

    private void ToggleInputHepaiLimit(bool isOn) {
        InputHepaiLimitPlane.SetActive(isOn && _ruleState != "shanghai");
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
        if (CuoheTypeDropdown != null && CuoheTypeDropdown.options.Count > 0) {
            CuoheTypeDropdown.options[0].text = _ruleState == "guobiao" && GetSelectedSubRule() == GuobiaoGameState.SanmaSubRule ? "-20/+10" : "-30/+10";
            CuoheTypeDropdown.RefreshShownValue();
        }
        bool isBloodBattle = _ruleState == "guobiao" && GetSelectedSubRule() == GuobiaoGameState.BloodBattleSubRule;
        bool isXiaolin = _ruleState == "guobiao" && SubRuleDropdown.value == 1;
        bool isLanshi = _ruleState == "guobiao" && SubRuleDropdown.value == 3;
        bool showPanel = TryGetDefaults(_ruleState, out var config)
            && config.ContainsKey(CreateRoomKeys.CuoheType)
            && !isXiaolin
            && !isLanshi
            && !isBloodBattle && CuoHeheToggle.isOn;
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
