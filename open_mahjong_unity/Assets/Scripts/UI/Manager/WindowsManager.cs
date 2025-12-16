using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;


public class WindowsManager : MonoBehaviour
{
    [Header("mainCanvas")]
    [SerializeField] private GameObject mainCanvas;

    [Header("顶层窗口")]
    [SerializeField] private GameObject headerPanel; // 
    [SerializeField] private GameObject chatPanel; // 聊天窗口 保持窗口常开
    [SerializeField] private GameObject gamePanel; // 游戏窗口
    
    [Header("一级窗口")]
    [SerializeField] private GameObject loginPanel; // 登录窗口
    [SerializeField] private GameObject menuPanel; // 菜单界面窗口
    [SerializeField] private GameObject recordPanel; // 游戏记录窗口
    [SerializeField] private GameObject playerPanel; // 玩家信息窗口
    [SerializeField] private GameObject configPanel; // 玩家配置窗口
    [SerializeField] private GameObject noticePanel; // 公告窗口
    [SerializeField] private GameObject aboutUsPanel; // 关于我们窗口
    [SerializeField] private GameObject roomRoot; // 房间根窗口（包含房间列表、房间、创建房间等子窗口）
    

    [Header("二级窗口")]


    [Header("窗口元素")]
    [SerializeField] private GameObject playerInfoPanelPrefab; // 玩家信息面板预制体

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
    }

    // 切换窗口
    public void SwitchWindow(string targetWindow)
    {
        Debug.Log($"切换到{targetWindow}窗口");

        menuPanel.SetActive(false);
        recordPanel.SetActive(false);
        playerPanel.SetActive(false);
        configPanel.SetActive(false);
        noticePanel.SetActive(false);
        aboutUsPanel.SetActive(false);
        roomRoot.SetActive(false);
        switch (targetWindow)
        {
            // login 登录界面
            case "login":
                gamePanel.SetActive(false);
                loginPanel.SetActive(true);
                break;
            // menu 主界面
            case "menu": // 关闭登录界面 打开主界面
                loginPanel.SetActive(false);
                menuPanel.SetActive(true); // 主界面
                break;
            // room 房间界面
            case "room":
                roomRoot.SetActive(true);
                break;
            // game 游戏界面 下属的窗口
            case "game":
                menuPanel.SetActive(false);
                gamePanel.SetActive(true); // 游戏界面
                break;
            case "record":
                recordPanel.SetActive(true); // 游戏记录界面
                break;
            case "notice":
                noticePanel.SetActive(true); // 公告界面
                break;
            case "aboutUs":
                aboutUsPanel.SetActive(true); // 关于我们界面
                break;
            case "player":
                playerPanel.SetActive(true);
                break;
            case "config":
                configPanel.SetActive(true);
                break;
        }
    }


    public void OpenPlayerInfoPanel(bool success, string message, PlayerInfoResponse playerInfo)
    {
        if (success && playerInfo != null)
        {
            // 在 mainCanvas 下创建玩家信息面板
            GameObject playerInfoPanelObject = Instantiate(playerInfoPanelPrefab, mainCanvas.transform);
            PlayerInfoPanel playerInfoPanel = playerInfoPanelObject.GetComponent<PlayerInfoPanel>();
            playerInfoPanel.ShowPlayerInfo(playerInfo);
        }
        else
        {
            Debug.LogError($"获取玩家信息失败: {message}");
        }
    }
    
}