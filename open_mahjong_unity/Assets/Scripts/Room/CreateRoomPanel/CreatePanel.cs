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
/// - 国标的错和/起和番仍受子规则（小林/蓝十）的二次收窄。
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
    private const string CfgHepaiWay       = "hepai_way";        // 和牌方式下拉索引
    private const string CfgTacticalCall   = "tactical_call";    // 战术鸣牌（国标 / 青雀）

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
            { CfgCuohe,          false },
            { CfgHepaiLimit,     1 },
            { CfgRedDora,        true }, // 赤宝牌：开
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
    [SerializeField] private Toggle TacticalCallToggle;

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

    private static readonly Dictionary<string, string> SubRuleDescriptions = new Dictionary<string, string> {
        { "qingque/standard", "青雀是由莫莫柴编写的一款麻雀规则，旨在寻求一种在传统麻将行牌规则框架内的做大、抢和、兜牌防守三者平衡的麻雀游戏，同时试图为各类和牌提供基于美感和难度评估的赋分参照；如在测试中发现设计问题或有任何建议，可以联系规则制定人莫莫柴Q1107574，提交bug可在群906497522提交" },
        { "guobiao/standard", "国标麻将源于国家体育总局于1998年11月出台的《中国竞技麻将比赛规则(试行)》、是中国唯一由官方确立的竞技麻将规则；本平台参照Natsuki编著的新编MCR撰写运行逻辑，已通过所有牌例验证，如发现测试过程中出现了不符合国标麻将规则预期的行为，请向Q群906497522反馈。" },
        { "guobiao/xiaolin", "小林改版国标麻将，对国标麻将进行了番数平衡，还处于测试版，取消了8番起胡和底分，改为点和得分x2，自摸番三。非竞技规则，只为娱乐。" },
        { "guobiao/lanshi", "蓝十改版的国标麻将规则，对国标麻将的番种表进行了全面的修改，并根据番种的难度调整了评分，5分起和，授受制为半全铳半分付。如在测试中发现设计问题或有任何建议，可以联系规则制定人蓝十QQ1002094810。" },
        { "classical/standard", "本规则为根据《绘图麻雀牌谱》《想定宁波规则》等书籍文献资料汇总而成的，试图还原1920年代左右或以前的早期麻将样貌的麻将规则。相比现代规则，古典麻雀有番种体系简单、重刻杠幺九、未和牌家计分等特点，具有独特风味。" },
        { "riichi/standard", "立直麻将（日麻）标准规则：起和默认 1 番，起始 25000 点，立直棒 1000 分由和牌者收取，本场棒按 300/场结算。可在此配置赤宝牌、自定义起和番数、错和惩罚（9000/家重打）以及和牌方式（头跳 / 允许多家和 / 三家和流局）。" }
    };

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
            _ => "guobiao"
        };
        ApplyRuleDefaults(_ruleState);
        RefreshVisibility();
        if (_ruleState == "guobiao") {
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
            case CfgHepaiWay:       HepaiWayDropdown.value = (int)value; break;
            case CfgTacticalCall:   TacticalCallToggle.isOn = (bool)value; break;
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
    /// 国标的子规则（小林/蓝十）对错和/起和番自定义做进一步收窄。
    /// </summary>
    private void RefreshVisibility() {
        Dictionary<string, object> visible = RuleConfigs[_ruleState];

        SubRuleDropdown.gameObject.SetActive(visible.ContainsKey(CfgSubRule));

        // 国标子规则对错和 / 起和番自定义做进一步收窄
        bool isXiaolin = _ruleState == "guobiao" && SubRuleDropdown.value == 1;
        bool isLanshi  = _ruleState == "guobiao" && SubRuleDropdown.value == 2;

        bool showCuohe = visible.ContainsKey(CfgCuohe) && !isXiaolin;
        CuoHeheToggle.gameObject.SetActive(showCuohe);

        bool showHepaiLimit = visible.ContainsKey(CfgHepaiLimit) && !isXiaolin && !isLanshi;
        InputHepaiLimitToggle.gameObject.SetActive(showHepaiLimit);
        InputHepaiLimitPlane.SetActive(showHepaiLimit && InputHepaiLimitToggle.isOn);

        RedDoraToggle.gameObject.SetActive(visible.ContainsKey(CfgRedDora));
        HepaiWayPanel.SetActive(visible.ContainsKey(CfgHepaiWay));
        TacticalCallToggle.gameObject.SetActive(visible.ContainsKey(CfgTacticalCall));
    }

    private string GetCurrentSubRuleKey() {
        if (_ruleState == "qingque") return "qingque/standard";
        if (_ruleState == "classical") return "classical/standard";
        if (_ruleState == "riichi") return "riichi/standard";
        return GetSelectedSubRule();
    }

    private void RefreshSubRuleDescription() {
        if (SubRuleDescriptions.TryGetValue(GetCurrentSubRuleKey(), out string desc)) {
            SubRuleDescriptionText.text = desc;
        }
    }

    private void InitSubRuleDropdown() {
        SubRuleDropdown.ClearOptions();
        // SubRuleDropdown.AddOptions(new List<string> { "标准规(新编MCR)", "国标麻将(小林改)", "国标麻将(蓝十改)" });
        SubRuleDropdown.AddOptions(new List<string> { "标准规(新编MCR)", "国标麻将(小林改)" });
        SubRuleDropdown.value = 0;
        OnSubRuleChanged(0);
    }

    private void OnSubRuleChanged(int index) {
        bool isXiaolin = (index == 1);
        bool isLanshi = (index == 2);
        RefreshSubRuleDescription();
        if (isXiaolin) {
            InputHepaiLimitToggle.isOn = false;
            HepaiLimitInput.text = "1";
            CuoHeheToggle.onValueChanged.RemoveListener(ToggleCuoHehe);
            CuoHeheToggle.isOn = false;
            CuoHeheToggle.onValueChanged.AddListener(ToggleCuoHehe);
        } else {
            if (isLanshi) {
                InputHepaiLimitToggle.isOn = false;
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
            2 => "guobiao/lanshi",
            _ => "guobiao/standard"
        };
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
            SubRule = "riichi/standard",
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            TouristLimit = TouristLimitToggle.isOn,
            AllowSpectator = AllowSpectatorToggle.isOn,
            CuoHe = CuoHeheToggle.isOn,
            HepaiLimit = hepaiLimit,
            RedDora = RedDoraToggle.isOn,
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

    private void CreateGBRoom() {
        int hepaiLimit = (int)RuleConfigs["guobiao"][CfgHepaiLimit];
        string subRule = GetSelectedSubRule();
        if (subRule == "guobiao/xiaolin") {
            hepaiLimit = 1;
        } else if (subRule == "guobiao/lanshi") {
            hepaiLimit = 5; // 蓝十默认 5 分起和
        }
        if (subRule != "guobiao/xiaolin" && InputHepaiLimitToggle.isOn) {
            if (int.TryParse(HepaiLimitInput.text.Trim(), out int inputLimit))
                hepaiLimit = Mathf.Clamp(inputLimit, 1, 64);
        }

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
            int fallback = (int)RuleConfigs[_ruleState].GetValueOrDefault(CfgHepaiLimit, 8);
            HepaiLimitInput.text = fallback.ToString();
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
