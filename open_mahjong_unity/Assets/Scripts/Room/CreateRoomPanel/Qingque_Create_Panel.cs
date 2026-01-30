using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 青雀13 创建房间面板。
/// 基于 GB_Create_Panel 改造，去掉“错和”选项，其余逻辑保持一致。
/// </summary>
public class Qingque_Create_Panel : MonoBehaviour {
    [Header("UI Elements")]
    [SerializeField] private Toggle gameTime1Button; // 打一圈
    [SerializeField] private Toggle gameTime2Button; // 打两圈
    [SerializeField] private Toggle gameTime3Button; // 打三圈
    [SerializeField] private Toggle gameTime4Button; // 打四圈
    [SerializeField] private TMP_InputField roomNameInput; // 房间名
    [SerializeField] private Toggle tipsToggle; // 提示开关
    [SerializeField] private Toggle passwordToggle; // 密码开关
    [SerializeField] private Toggle SetRandomSeedToggle; // 复式开关（随机种子）
    [SerializeField] private TMP_InputField passwordInput; // 密码输入框
    [SerializeField] private TMP_InputField randomSeedInput; // 随机种子输入框
    [SerializeField] private GameObject SetRandomSeedPanel; // 设置随机种子面板
    [SerializeField] private GameObject PasswordPanel; // 设置密码面板
    [SerializeField] private TMP_Dropdown roundTimer; // 局时
    [SerializeField] private TMP_Dropdown stepTimer; // 步时

    [SerializeField] private Button closeButton; // 关闭按钮
    [SerializeField] private Button createButton; // 创建按钮

    private void Start() {
        closeButton.onClick.AddListener(ClosePanel);
        createButton.onClick.AddListener(CreateRoom);

        SetRandomSeedPanel.SetActive(false);
        PasswordPanel.SetActive(false);

        passwordToggle.onValueChanged.AddListener(TogglePassword);
        SetRandomSeedToggle.onValueChanged.AddListener(ToggleSetRandomSeed);

        roundTimer.value = 2; // 20s
        stepTimer.value = 1; // 5s
        gameTime4Button.isOn = true; // 打四圈
        tipsToggle.isOn = false; // 提示默认关闭
        passwordToggle.isOn = false; // 密码默认关闭

        // 如果服务器也有青雀专用创建响应事件，可以在这里订阅
        // NetworkManager.Instance.CreateQingqueRoomResponse.AddListener(CreateRoomResponse);
    }

    // 关闭面板
    private void ClosePanel() {
        WindowsManager.Instance.SwitchWindow("menu");
    }

    // 创建房间
    private void CreateRoom() {
        var config = new Qingque_Create_RoomConfig {
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            Rule = "qingque",
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn
        };

        // 验证配置
        if (!config.Validate(out string error, passwordToggle.isOn, SetRandomSeedToggle.isOn)) {
            Debug.LogWarning(error);
            NotificationManager.Instance.ShowTip("create_room", false, $"创建房间失败: {error}");
            return;
        }

        // 发送创建房间请求
        if (RoomNetworkManager.Instance != null) {
            // 如果有专门的青雀创建方法，替换为相应接口
            RoomNetworkManager.Instance.Create_Qingque_Room(config);
        }
    }

    private int GetSelectedGameTime() {
        if (gameTime1Button.isOn) return 1;
        if (gameTime2Button.isOn) return 2;
        if (gameTime3Button.isOn) return 3;
        if (gameTime4Button.isOn) return 4;
        return 1; // 默认返回1
    }

    private int GetSelectedRoundTimer() {
        switch (roundTimer.value){
            case 0: return 5;
            case 1: return 10;
            case 2: return 20;
            case 3: return 40;
            case 4: return 60;
        }
        return 20;
    }

    private int GetSelectedStepTimer(){
        switch (stepTimer.value){
            case 0: return 3;
            case 1: return 5;
            case 2: return 10;
            case 3: return 20;
            case 4: return 40;
        }
        return 5;
    }

    // 创建房间响应（如需）
    private void CreateRoomResponse(bool success, string message) {
        Debug.Log($"创建青雀房间响应: {success}, {message}");
    }

    // 处理密码开关状态改变
    private void TogglePassword(bool isOn) {
        PasswordPanel.SetActive(isOn);
    }

    // 处理随机种子面板开关
    private void ToggleSetRandomSeed(bool isOn) {
        SetRandomSeedPanel.SetActive(isOn);
    }
}


