using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// 统一创建房间面板。通过规则下拉状态字符串（guobiao / ricchi / qingque / classical）切换不同功能的显示与创建逻辑。
/// </summary>
public class CreatePanel : MonoBehaviour {
    /// <summary>规则状态：guobiao / ricchi / qingque / classical，与 chooseRule 下拉索引对应 0/1/2/3。</summary>
    private string _ruleState = "guobiao";

    [Header("Dropdown")]
    [SerializeField] private TMP_Dropdown chooseRule;
    [SerializeField] private TMP_Dropdown roundTimer;
    [SerializeField] private TMP_Dropdown stepTimer;
    [SerializeField] private TMP_Dropdown SubRuleDropdown;

    [Header("描述文本")]
    [SerializeField] private TMP_Text SubRuleDescriptionText;

    [Header("开关（顺序：GameRound 1-4 → Tips → Cuohe → Password → SetRandomSeed → TouristLimit → DefineHePaiLimit → AllowSpectator）")]
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

    [Header("面板")]
    [SerializeField] private GameObject SetRandomSeedPanel;
    [SerializeField] private GameObject PasswordPanel;



    
    [SerializeField] private GameObject InputHepaiLimitPlane;

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
        { "classical/standard", "本规则为根据《绘图麻雀牌谱》《想定宁波规则》等书籍文献资料汇总而成的，试图还原1920年代左右或以前的早期麻将样貌的麻将规则。相比现代规则，古典麻雀有番种体系简单、重刻杠幺九、未和牌家计分等特点，具有独特风味。" }
    };

    private void Start() {
        chooseRule.onValueChanged.AddListener(OnRuleDropdownChanged);
        closeButton.onClick.AddListener(ClosePanel);
        createButton.onClick.AddListener(CreateRoom);
        addRuleButton.onClick.AddListener(OnAddRuleClick);

        SetRandomSeedPanel.SetActive(false);
        PasswordPanel.SetActive(false);
        InputHepaiLimitPlane.SetActive(false);

        passwordToggle.onValueChanged.AddListener(TogglePassword);
        SetRandomSeedToggle.onValueChanged.AddListener(ToggleSetRandomSeed);
        CuoHeheToggle.onValueChanged.AddListener(ToggleCuoHehe);
        tipsToggle.onValueChanged.AddListener(ToggleTips);
        InputHepaiLimitToggle.onValueChanged.AddListener(ToggleInputHepaiLimit);
        SubRuleDropdown.onValueChanged.AddListener(OnSubRuleChanged);

        roomNameInput.text = GetDefaultRoomName();
        roundTimer.value = 2;  // 局时默认选第三个选项
        stepTimer.value = 1;   // 步时默认选第二个选项

        InitSubRuleDropdown();
        RefreshVisibility();
        RefreshSubRuleDescription();

        NetworkManager.Instance.CreateRoomResponse.AddListener(CreateRoomResponse);
    }

    private void OnRuleDropdownChanged(int selectedIndex) {
        _ruleState = selectedIndex switch {
            0 => "guobiao",
            1 => "ricchi",
            2 => "qingque",
            3 => "classical",
            _ => "guobiao"
        };
        RefreshVisibility();
        if (_ruleState == "guobiao") {
            OnSubRuleChanged(SubRuleDropdown.value);
        }
        RefreshSubRuleDescription();
    }

    /// <summary>按规则显示组件 </summary>
    private void RefreshVisibility() {
        bool isGuobiao = _ruleState == "guobiao";
        bool isXiaolin = isGuobiao && SubRuleDropdown.value == 1; // 仅小林隐藏错和等
        bool hideHepaiLimit = isGuobiao && (SubRuleDropdown.value == 1 || SubRuleDropdown.value == 2); // 小林、蓝十固定起和，不显示和牌限制

        SubRuleDropdown.gameObject.SetActive(isGuobiao);
        SubRuleDescriptionText.gameObject.SetActive(true);
        CuoHeheToggle.gameObject.SetActive(isGuobiao && !isXiaolin);
        InputHepaiLimitToggle.gameObject.SetActive(isGuobiao && !hideHepaiLimit);
        InputHepaiLimitPlane.SetActive(isGuobiao && !hideHepaiLimit && InputHepaiLimitToggle.isOn);
    }

    private string GetCurrentSubRuleKey() {
        if (_ruleState == "qingque") return "qingque/standard";
        if (_ruleState == "classical") return "classical/standard";
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
        if (_ruleState == "ricchi") {
            NotificationManager.Instance.ShowTip("create_room", false, "立直规则暂未开放");
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

    private static string GetDefaultRoomName() {
        string name = UserDataManager.Instance != null ? UserDataManager.Instance.Username : null;
        return (string.IsNullOrEmpty(name) ? "未知用户" : name) + "的游戏";
    }

    private void CreateGBRoom() {
        int hepaiLimit = 8;
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
            SubRule = subRule, // 固定发送子规则（guobiao/standard 或 guobiao/xiaolin）
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            CuoHe = CuoHeheToggle.isOn,
            HepaiLimit = hepaiLimit,
            TouristLimit = TouristLimitToggle.isOn,
            AllowSpectator = AllowSpectatorToggle.isOn,
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
            SubRule = "qingque/standard", // 固定发送，与国标一致由 NormalGameStateManager 收 sub_rule
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
        if (!isOn) HepaiLimitInput.text = "8";
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
