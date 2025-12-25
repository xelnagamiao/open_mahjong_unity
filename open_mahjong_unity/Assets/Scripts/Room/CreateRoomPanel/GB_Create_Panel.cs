using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class GB_Create_Panel : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private Toggle gameTime1Button; // 打一圈
    [SerializeField] private Toggle gameTime2Button; // 打两圈
    [SerializeField] private Toggle gameTime3Button; // 打三圈
    [SerializeField] private Toggle gameTime4Button; // 打四圈
    [SerializeField] private TMP_InputField roomNameInput; // 房间名
    [SerializeField] private Toggle tipsToggle; // 提示开关
    [SerializeField] private Toggle passwordToggle; // 密码开关
    [SerializeField] private TMP_Dropdown roundTimer; // 局时
    [SerializeField] private TMP_Dropdown stepTimer; // 步时
    [SerializeField] private TMP_InputField passwordInput; // 密码输入框
    [SerializeField] private Button closeButton; // 关闭按钮
    [SerializeField] private Button createButton; // 创建按钮

    private void Start()
    {
        // ClosePanel 方法关闭面板
        closeButton.onClick.AddListener(ClosePanel);
        // CreateRoom 方法创建房间
        createButton.onClick.AddListener(CreateRoom);
        // 设置密码输入框的初始状态为隐藏
        passwordInput.gameObject.SetActive(false);
        // 订阅密码开关事件
        passwordToggle.onValueChanged.AddListener(TogglePassword);
        // 初始化界面
        roundTimer.value = 2; // 20s
        stepTimer.value = 1; // 5s
        gameTime4Button.isOn = true; // 打四圈
        tipsToggle.isOn = false; // 提示关闭
        passwordToggle.isOn = false; // 密码关闭
        
        // 订阅创建房间响应事件    
        NetworkManager.Instance.CreateRoomResponse.AddListener(CreateRoomResponse);
    }

    // 关闭面板
    private void ClosePanel()
    {
        WindowsManager.Instance.SwitchWindow("menu");
    }

    // 创建房间
    private void CreateRoom()
    {
        var config = new GB_Create_RoomConfig
        {
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            Rule = "guobiao",
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn
        };

        // 验证配置
        if (!config.Validate(out string error, passwordToggle.isOn))
        {
            Debug.LogWarning(error);
            return;
        }

        // 发送创建房间请求
        NetworkManager.Instance.Create_GB_Room(config);
    }

    private int GetSelectedGameTime()
    {
        if (gameTime1Button.isOn) return 1;
        if (gameTime2Button.isOn) return 2;
        if (gameTime3Button.isOn) return 3;
        if (gameTime4Button.isOn) return 4;
        return 1; // 默认返回1
    }

    private int GetSelectedRoundTimer()
    {
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

    // 创建房间响应
    private void CreateRoomResponse(bool success, string message)
    {
        Debug.Log($"创建房间响应: {success}, {message}");
    }

    // 处理密码开关状态改变
    private void TogglePassword(bool isOn)
    {
        passwordInput.gameObject.SetActive(isOn);
    }
}
