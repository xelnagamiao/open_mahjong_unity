using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;


public class WindowsManager : MonoBehaviour
{
    [Header("mainCanvas")]
    [SerializeField] private GameObject mainCanvas;

    [Header("mainCanvas管理一级窗口")]
    [SerializeField] private GameObject loginPanel; // 登录窗口
    [SerializeField] private GameObject mainPanel; // 主界面窗口
    [SerializeField] private GameObject roomListPanel; // 房间列表窗口
    [SerializeField] private GameObject roomPanel; // 房间窗口
    [SerializeField] private GameObject createRoomPanel; // 创建房间窗口
    [SerializeField] private GameObject gamePanel; // 游戏窗口
    [SerializeField] private GameObject chatPanel; // 聊天窗口 保持窗口常开
    [SerializeField] private GameObject recordPanel; // 游戏记录窗口
    [SerializeField] private GameObject playerPanel; // 玩家信息窗口
    [SerializeField] private GameObject playerConfigPanel; // 玩家配置窗口
    [SerializeField] private GameObject gameConfigPanel; // 游戏配置窗口
    [SerializeField] private GameObject PlayerInfoPanelPrefab; // 玩家信息面板预制体

    /*
    windowsmanager管理所有的一级窗口 所有mainCanvas的一级窗口都应在windowsmanager中管理
    如果一级窗口例如createRoomPanel有多个创建不同规则的子窗口 从属createRoomPanel窗口本身管理
    同理 roomListPanel 进入密码房时可调用passwordInputPanel窗口 从属roomListPanel管理
    */

    public static WindowsManager Instance { get; private set; } // 单例
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
        SwitchWindow("login"); // 初始化窗口 游戏初始应当在mainCanvas中显示登录窗口
        playerPanel.SetActive(false);
        gameConfigPanel.SetActive(false);
        playerConfigPanel.SetActive(false);
    }

    public void OpenPlayerInfoPanel()
    {
        PlayerPanel.Instance.gameObject.SetActive(true);
        mainPanel.SetActive(false);
    }

    public void ClosePlayerInfoPanel()
    {
        PlayerPanel.Instance.gameObject.SetActive(false);
        mainPanel.SetActive(true);
    }

    // 切换窗口
    public void SwitchWindow(string targetWindow)
    {
        Debug.Log($"切换到{targetWindow}窗口");
        switch (targetWindow)
        {
            // login 登录界面
            case "login":

                loginPanel.SetActive(true);
                mainPanel.SetActive(false);
                roomListPanel.SetActive(false);
                roomPanel.SetActive(false);
                createRoomPanel.SetActive(false);
                gamePanel.SetActive(false);
                recordPanel.SetActive(false);
                break;
            // main 主界面 下属的窗口
            case "main": // 关闭登录界面 打开主界面
                loginPanel.SetActive(false);
                mainPanel.SetActive(true); // 主界面
                roomListPanel.SetActive(false);
                roomPanel.SetActive(false);
                createRoomPanel.SetActive(false);
                gamePanel.SetActive(false);
                recordPanel.SetActive(false);
                break;
            // 三个roomroot下属的窗口
            case "roomList": // 打开房间界面 关闭其他界面
                loginPanel.SetActive(false);
                mainPanel.SetActive(false);
                roomListPanel.SetActive(true); // 房间列表界面
                roomPanel.SetActive(false);
                createRoomPanel.SetActive(false);
                gamePanel.SetActive(false);
                recordPanel.SetActive(false);
                break;
            case "room":
                loginPanel.SetActive(false);
                mainPanel.SetActive(false);
                roomListPanel.SetActive(false);
                roomListPanel.SetActive(false); 
                createRoomPanel.SetActive(false);
                roomPanel.SetActive(true); // 房间界面
                recordPanel.SetActive(false);
                break;
            case "createRoom":
                loginPanel.SetActive(false);
                mainPanel.SetActive(false);
                roomListPanel.SetActive(false);
                roomPanel.SetActive(false);
                roomListPanel.SetActive(false); 
                createRoomPanel.SetActive(true); // 创建房间界面
                roomPanel.SetActive(false);
                recordPanel.SetActive(false);
                break;
            // game 游戏界面 下属的窗口
            case "game":
                loginPanel.SetActive(false);
                mainPanel.SetActive(false);
                roomListPanel.SetActive(false);
                roomPanel.SetActive(false);
                createRoomPanel.SetActive(false);
                gamePanel.SetActive(true); // 游戏界面
                recordPanel.SetActive(false);
                break;
            case "record":
                loginPanel.SetActive(false);
                mainPanel.SetActive(false);
                roomListPanel.SetActive(false);
                roomPanel.SetActive(false);
                createRoomPanel.SetActive(false);
                gamePanel.SetActive(false);
                recordPanel.SetActive(true); // 游戏记录界面
                break;
        }
    }


    public void OpenPlayerInfoPanel(bool success, string message, PlayerInfoResponse playerInfo)
    {
        if (success && playerInfo != null)
        {
            // 在 mainCanvas 下创建玩家信息面板
            GameObject playerInfoPanelObject = Instantiate(PlayerInfoPanelPrefab, mainCanvas.transform);
            PlayerInfoPanel playerInfoPanel = playerInfoPanelObject.GetComponent<PlayerInfoPanel>();
            playerInfoPanel.ShowPlayerInfo(playerInfo);
        }
        else
        {
            Debug.LogError($"获取玩家信息失败: {message}");
        }
    }
    
}