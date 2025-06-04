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
        
        // 订阅创建房间响应事件    
        NetworkManager.Instance.CreateRoomResponse.AddListener(CreateRoomResponse);
    }

    // 关闭面板
    private void ClosePanel()
    {
        WindowsMannager.Instance.SwitchWindow("roomList");
    }
    // 创建房间
    private void CreateRoom()
    {
        // 获取房间名 并去除两端的空白字符
        string roomName = roomNameInput.text.Trim();
        // 如果用户名为空 则设置状态文本为"用户名不能为空" 并跳出
        if (string.IsNullOrEmpty(roomName))
        {
            Debug.LogWarning("房间名不能为空");
            return; // 当break使用
        }
        else{  
            if (passwordToggle.isOn)
            {
                string password = passwordInput.text.Trim();
                if (string.IsNullOrEmpty(password))
                {
                    Debug.LogWarning("密码不能为空");
                    return;
                }
                else{
                    int gameTime = GetSelectedGameTime(); // 获取选择的打圈数
                    NetworkManager.Instance.CreateRoom(roomName, gameTime, password);
                }
            }
            else{
                int gameTime = GetSelectedGameTime(); // 获取选择的打圈数
                NetworkManager.Instance.CreateRoom(roomName, gameTime, "");
            }
        }
    }
    private int GetSelectedGameTime()
    {
        if (gameTime1Button.isOn) return 1;
        if (gameTime2Button.isOn) return 2;
        if (gameTime3Button.isOn) return 3;
        if (gameTime4Button.isOn) return 4;
        return 1; // 默认返回1
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
