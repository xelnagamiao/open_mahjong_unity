using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// 统一创建房间面板。通过规则下拉状态字符串（guobiao / riichi / qingque / classical）
/// 驱动配置项的显隐与默认值。
///
/// 设计要点：头部 <see cref="RuleConfigs"/> 为每条规则"全量"登记需要的配置项及默认值。
/// - 若某规则的字典里含有某个键 → 该配置项对此规则可见，并在切换到此规则时重置到默认值。
/// - 若某规则的字典里不含某个键 → 该配置项对此规则隐藏，发送房间配置时使用 <c>CreateRoom</c> 内的硬编码缺省值。
/// - 国标的错和/起和番仍受子规则二次收窄：小林规隐藏错和；蓝十隐藏起和番自定义；标准/K神/小林均可配置起和番（K神默认8、小林默认1）。
/// </summary>
public class CreatePanel : MonoBehaviour {
    // ===== 配置键：所有可能出现的配置项（含公共与差异） =====
    private const string CfgGameRound      = "game_round";       // 局数 1-4
    private const string CfgRoundTimer     = "round_timer";      // 局时下拉索引
    private const string CfgStepTimer      = "step_timer";       // 步时下拉索引
    private const string CfgTips           = "tips";             // 提示
    private const string CfgPassword       = "password";         // 密码开关
    private const string CfgRandomSeed     = "random_seed";      // 随机种子开关
    private const string CfgTouristLimit   = "tourist_limit";    // 游客限制
    private const string CfgAllowSpectator = "allow_spectator";  // 允许旁观
    private const string CfgSubRule        = "sub_rule";         // 子规则下拉索引（国标）
    private const string CfgCuohe          = "cuohe";            // 错和
    private const string CfgHepaiLimit     = "hepai_limit";      // 自定义起和番数（整数；Toggle 与 Input 成组显隐）
    private const string CfgRedDora        = "red_dora";         // 赤宝牌
    private const string CfgAllowKuikae    = "allow_kuikae";    // 禁止食替 Toggle：开=禁切（allow_kuikae 关）
    private const string CfgOpenXiru       = "open_xiru";       // 西入
    private const string CfgOpenTobi       = "open_tobi";       // 击飞
    private const string CfgHepaiWay       = "hepai_way";        // 和牌方式下拉索引
    private const string CfgTacticalCall   = "tactical_call";    // 战术鸣牌（国标 / 青雀 / 四川）
    private const string CfgBloodBattle    = "blood_battle";     // 血战到底（四川）

    /// <summary>
    /// 每条规则需要显示的全部配置项与默认值。
    /// 存在即可见、同时提供切换规则时应用的默认值；不存在即隐藏。
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, object>> RuleConfigs = new Dictionary<string, Dictionary<string, object>> {
        { "guobiao", new Dictionary<string, object> {
            { CfgGameRound,      4 }, // 默认半庄
            { CfgRoundTimer,     2 }, // 5 10 [20] 40 60
            { CfgStepTimer,      1 }, // 3 [5] 10 20 40
            { CfgTips,           true }, // 提示
            { CfgPassword,       false }, // 密码
            { CfgRandomSeed,     false }, // 随机种子
            { CfgTouristLimit,   false }, // 游客限制
            { CfgAllowSpectator, true }, // 允许旁观
            { CfgSubRule,        0 }, // 子规则下拉索引（国标）
            { CfgCuohe,          false }, // 错和
            { CfgHepaiLimit,     8 }, // 起和番数
            { CfgTacticalCall,   false }, // 战术鸣牌
        } },
        { "riichi", new Dictionary<string, object> {
            { CfgGameRound,      2 },
            { CfgRoundTimer,     2 },
            { CfgStepTimer,      1 },
            { CfgTips,           true },
            { CfgPassword,       false },
            { CfgRandomSeed,     false },
            { CfgTouristLimit,   false },
            { CfgAllowSpectator, true },
            { CfgSubRule,        0 }, // 子规则下拉索引（0=标准 1=浪涌）
            { CfgCuohe,          false },
            { CfgHepaiLimit,     1 },
            { CfgRedDora,        true }, // 赤宝牌：开
            { CfgAllowKuikae,    false }, // 禁止食替：开（allow_kuikae 关，标准日麻禁切）
            { CfgOpenXiru,       true }, // 西入：开
            { CfgOpenTobi,       true }, // 击飞：开
            { CfgHepaiWay,       0 }, // 0=多家和了 1=三家和了流局 2=头跳
        } },
        { "qingque", new Dictionary<string, object> {
            { CfgGameRound,      4 },
            { CfgRoundTimer,     2 },
            { CfgStepTimer,      1 },
            { CfgTips,           true },
            { CfgPassword,       false },
            { CfgRandomSeed,     false },
            { CfgTouristLimit,   false },
            { CfgAllowSpectator, true },
            { CfgTacticalCall,   false }, // 战术鸣牌
        } },
        { "classical", new Dictionary<string, object> {
            { CfgGameRound,      4 },
            { CfgRoundTimer,     2 },
            { CfgStepTimer,      1 },
            { CfgTips,           true },
            { CfgPassword,       false },
            { CfgRandomSeed,     false },
            { CfgTouristLimit,   false },
            { CfgAllowSpectator, true },
        } },
        { "sichuan", new Dictionary<string, object> {
            { CfgGameRound,      4 },
            { CfgRoundTimer,     2 },
            { CfgStepTimer,      1 },
            { CfgTips,           true },
            { CfgPassword,       false },
            { CfgRandomSeed,     false },
            { CfgTouristLimit,   false },
            { CfgAllowSpectator, true },
            { CfgTacticalCall,   false }, // 战术鸣牌
            { CfgBloodBattle,    true },  // 血战到底：默认开
        } },
    };

    /// <summary>规则状态：guobiao / riichi / qingque / classical，与 chooseRule 下拉索引对应 0/1/2/3。</summary>
    private string _ruleState = "guobiao";

    [Header("Dropdown")]
    [SerializeField] private TMP_Dropdown chooseRule;
    [SerializeField] private TMP_Dropdown roundTimer;
    [SerializeField] private TMP_Dropdown stepTimer;
    [SerializeField] private TMP_Dropdown SubRuleDropdown;

    [Header("描述文本")]
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

    [Header("面板")]
    [SerializeField] private GameObject SetRandomSeedPanel;
    [SerializeField] private GameObject PasswordPanel;
    [SerializeField] private GameObject InputHepaiLimitPlane;
    [SerializeField] private GameObject HepaiWayPanel;
    [SerializeField] private TMP_Dropdown HepaiWayDropdown;

    [Header("输入字段")]
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private TMP_InputField randomSeedInput;
    [SerializeField] private TMP_InputField HepaiLimitInput;

    [Header("按钮")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Button createButton;
    [SerializeField] private Button addRuleButton;

    private void Start() {
        chooseRule.onValueChanged.AddListener(OnRuleDropdownChanged);
        closeButton.onClick.AddListener(ClosePanel);
        createButton.onClick.AddListener(CreateRoom);
        addRuleButton.onClick.AddListener(OnAddRuleClick);

        SetRandomSeedPanel.SetActive(false);
        PasswordPanel.SetActive(false);
        InputHepaiLimitPlane.SetActive(false);

        HepaiWayDropdown.ClearOptions();
        HepaiWayDropdown.AddOptions(new List<string> { "允许多家和牌", "三家和了流局", "头跳" });

        passwordToggle.onValueChanged.AddListener(TogglePassword);
        SetRandomSeedToggle.onValueChanged.AddListener(ToggleSetRandomSeed);
        CuoHeheToggle.onValueChanged.AddListener(ToggleCuoHehe);
        tipsToggle.onValueChanged.AddListener(ToggleTips);
        InputHepaiLimitToggle.onValueChanged.AddListener(ToggleInputHepaiLimit);
        SubRuleDropdown.onValueChanged.AddListener(OnSubRuleChanged);

        roomNameInput.text = GetDefaultRoomName();

        EnsureRiichiOptionToggles();
        InitSubRuleDropdown();
        ApplyRuleDefaults(_ruleState);
        RefreshVisibility();
        RefreshSubRuleDescription();

        NetworkManager.Instance.CreateRoomResponse.AddListener(CreateRoomResponse);
    }

    private void OnRuleDropdownChanged(int selectedIndex) {
        _ruleState = selectedIndex switch {
            0 => "guobiao",
            1 => "riichi",
            2 => "qingque",
            3 => "classical",
            4 => "sichuan",
            _ => "guobiao"
        };
        bool hasSubRule = RuleConfigs[_ruleState].ContainsKey(CfgSubRule);
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

    /// <summary>遍历当前规则的配置默认值并下发到对应控件。</summary>
    private void ApplyRuleDefaults(string rule) {
        Dictionary<string, object> defaults = RuleConfigs[rule];
        foreach (KeyValuePair<string, object> kv in defaults) {
            SetConfigValue(kv.Key, kv.Value);
        }
    }

    private void SetConfigValue(string key, object value) {
        switch (key) {
            case CfgGameRound:      SelectGameTime((int)value); break;
            case CfgRoundTimer:     roundTimer.value = (int)value; break;
            case CfgStepTimer:      stepTimer.value = (int)value; break;
            case CfgTips:           tipsToggle.isOn = (bool)value; break;
            case CfgPassword:       passwordToggle.isOn = (bool)value; break;
            case CfgRandomSeed:     SetRandomSeedToggle.isOn = (bool)value; break;
            case CfgTouristLimit:   TouristLimitToggle.isOn = (bool)value; break;
            case CfgAllowSpectator: AllowSpectatorToggle.isOn = (bool)value; break;
            case CfgSubRule:        SubRuleDropdown.value = (int)value; break;
            case CfgCuohe:          CuoHeheToggle.isOn = (bool)value; break;
            case CfgHepaiLimit:
                // 切换规则时同步收起"自定义起和番"面板，避免前一条规则的开启状态带入当前规则
                InputHepaiLimitToggle.isOn = false;
                HepaiLimitInput.text = ((int)value).ToString();
                break;
            case CfgRedDora:        RedDoraToggle.isOn = (bool)value; break;
            case CfgAllowKuikae:    if (KuikaeToggle != null) KuikaeToggle.isOn = !(bool)value; break;
            case CfgOpenXiru:       if (XiruToggle != null) XiruToggle.isOn = (bool)value; break;
            case CfgOpenTobi:       if (TobiToggle != null) TobiToggle.isOn = (bool)value; break;
            case CfgHepaiWay:       HepaiWayDropdown.value = (int)value; break;
            case CfgTacticalCall:   TacticalCallToggle.isOn = (bool)value; break;
            case CfgBloodBattle:    if (BloodBattleToggle != null) BloodBattleToggle.isOn = (bool)value; break;
        }
    }

    private void SelectGameTime(int value) {
        gameTime1Button.isOn = value == 1;
        gameTime2Button.isOn = value == 2;
        gameTime3Button.isOn = value == 3;
        gameTime4Button.isOn = value == 4;
    }

    /// <summary>
    /// 根据 <see cref="RuleConfigs"/> 驱动配置项控件的显隐。
    /// 国标子规则对错和 / 起和番自定义做进一步收窄；浪涌子规则固定可食替，不暴露食替开关。
    /// </summary>
    private void RefreshVisibility() {
        Dictionary<string, object> visible = RuleConfigs[_ruleState];

        SubRuleDropdown.gameObject.SetActive(visible.ContainsKey(CfgSubRule));

        bool isXiaolin = _ruleState == "guobiao" && SubRuleDropdown.value == 1;
        bool isLanshi  = _ruleState == "guobiao" && SubRuleDropdown.value == 3;
        bool isLangyong = _ruleState == "riichi" && SubRuleDropdown.value == 1;

        // 小林规仍隐藏错和；K神/标准规可开错和
        bool showCuohe = visible.ContainsKey(CfgCuohe) && !isXiaolin;
        CuoHeheToggle.gameObject.SetActive(showCuohe);

        // 蓝十仍隐藏起和番自定义；标准/小林/K神均可改
        bool showHepaiLimit = visible.ContainsKey(CfgHepaiLimit) && !isLanshi;
        InputHepaiLimitToggle.gameObject.SetActive(showHepaiLimit);
        InputHepaiLimitPlane.SetActive(showHepaiLimit && InputHepaiLimitToggle.isOn);

        RedDoraToggle.gameObject.SetActive(visible.ContainsKey(CfgRedDora));
        if (KuikaeToggle != null) KuikaeToggle.gameObject.SetActive(visible.ContainsKey(CfgAllowKuikae) && !isLangyong);
        if (XiruToggle != null) XiruToggle.gameObject.SetActive(visible.ContainsKey(CfgOpenXiru));
        if (TobiToggle != null) TobiToggle.gameObject.SetActive(visible.ContainsKey(CfgOpenTobi));
        HepaiWayPanel.SetActive(visible.ContainsKey(CfgHepaiWay));
        TacticalCallToggle.gameObject.SetActive(visible.ContainsKey(CfgTacticalCall));
        if (BloodBattleToggle != null) BloodBattleToggle.gameObject.SetActive(visible.ContainsKey(CfgBloodBattle));
    }

    private string GetCurrentSubRuleKey() {
        if (_ruleState == "qingque") return "qingque/standard";
        if (_ruleState == "classical") return "classical/standard";
        if (_ruleState == "sichuan") return "sichuan/standard";
        if (_ruleState == "riichi") return GetSelectedRiichiSubRule();
        return GetSelectedSubRule();
    }

    private string GetSelectedRiichiSubRule() {
        return SubRuleDropdown.value == 1 ? "riichi/langyong" : "riichi/standard";
    }

    private void RefreshSubRuleDescription() {
        SubRuleDescriptionText.text = SubRuleDescriptionDictionary.GetDescription(GetCurrentSubRuleKey());
    }

    private void InitSubRuleDropdown() {
        PopulateSubRuleDropdown(_ruleState);
        OnSubRuleChanged(0);
    }

    /// <summary>按当前规则填充子规则下拉选项。仅含子规则的规则（国标 / 日麻）会用到。</summary>
    private void PopulateSubRuleDropdown(string rule) {
        SubRuleDropdown.ClearOptions();
        if (rule == "riichi") {
            SubRuleDropdown.AddOptions(new List<string> { "立直麻将(标准)", "浪涌麻将" });
        } else {
            // 国标：SubRuleDropdown.AddOptions(new List<string> { "标准规(新编MCR)", "国标麻将(小林改)", "国标麻将(蓝十改)" });
            SubRuleDropdown.AddOptions(new List<string> { "标准规(新编MCR)", "国标麻将(小林改)", "K神麻将" });
        }
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
            } else {
                HepaiLimitInput.text = "8";
            }
        }
        RefreshVisibility();
    }

    private string GetSelectedSubRule() {
        return SubRuleDropdown.value switch {
            1 => "guobiao/xiaolin",
            2 => "guobiao/kshen",
            3 => "guobiao/lanshi",
            _ => "guobiao/standard"
        };
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

    private void ClosePanel() {
        WindowsManager.Instance.SwitchWindow("menu");
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
        }
    }

    private void CreateRiichiRoom() {
        // HepaiWayDropdown 选项顺序：0=多家和，1=三家和了流局，2=头跳
        string hepaiWay = HepaiWayDropdown.value switch {
            0 => "multi_ron",
            1 => "three_ron_abort",
            2 => "head_bump",
            _ => "head_bump",
        };

        int hepaiLimit = (int)RuleConfigs["riichi"][CfgHepaiLimit];
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
            OpenXiru = XiruToggle != null ? XiruToggle.isOn : (bool)RuleConfigs["riichi"][CfgOpenXiru],
            OpenTobi = TobiToggle != null ? TobiToggle.isOn : (bool)RuleConfigs["riichi"][CfgOpenTobi],
            HepaiWay = hepaiWay,
        };

        if (!config.Validate(out string error, passwordToggle.isOn, SetRandomSeedToggle.isOn)) {
            Debug.LogWarning(error);
            NotificationManager.Instance.ShowTip("create_room", false, $"创建房间失败: {error}");
            return;
        }
        RoomNetworkManager.Instance.Create_Riichi_Room(config);
    }

    private static string GetDefaultRoomName() {
        string name = UserDataManager.Instance != null ? UserDataManager.Instance.Username : null;
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
            CuoHe = CuoHeheToggle.isOn,
            HepaiLimit = hepaiLimit,
            TouristLimit = TouristLimitToggle.isOn,
            AllowSpectator = AllowSpectatorToggle.isOn,
            TacticalCall = TacticalCallToggle.isOn,
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
        };

        if (!config.Validate(out string error, passwordToggle.isOn, SetRandomSeedToggle.isOn)) {
            Debug.LogWarning(error);
            NotificationManager.Instance.ShowTip("create_room", false, $"创建房间失败: {error}");
            return;
        }
        RoomNetworkManager.Instance.Create_Qingque_Room(config);
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
            : (bool)RuleConfigs["sichuan"][CfgBloodBattle];

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
        };

        if (!config.Validate(out string error, passwordToggle.isOn, SetRandomSeedToggle.isOn)) {
            Debug.LogWarning(error);
            NotificationManager.Instance.ShowTip("create_room", false, $"创建房间失败: {error}");
            return;
        }
        RoomNetworkManager.Instance.Create_Sichuan_Room(config);
    }

    private int GetSelectedGameTime() {
        if (gameTime1Button.isOn) return 1;
        if (gameTime2Button.isOn) return 2;
        if (gameTime3Button.isOn) return 3;
        if (gameTime4Button.isOn) return 4;
        return 1;
    }

    private int GetSelectedRoundTimer() {
        return roundTimer.value switch {
            0 => 5,
            1 => 10,
            2 => 20,
            3 => 40,
            4 => 60,
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

    private void CreateRoomResponse(bool success, string message) {
        Debug.Log($"创建房间响应: {success}, {message}");
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
                int fallback = (int)RuleConfigs[_ruleState].GetValueOrDefault(CfgHepaiLimit, 8);
                HepaiLimitInput.text = fallback.ToString();
            }
        }
    }

    private void ToggleCuoHehe(bool isOn) {
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
    }
}
