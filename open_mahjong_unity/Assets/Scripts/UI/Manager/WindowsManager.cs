using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;


public class WindowsManager : MonoBehaviour {
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
    [SerializeField] private GameObject sceneConfigPanel; // 场景配置窗口
    [SerializeField] private GameObject spectatorPanel; // 观战窗口

    /*
    windowsmanager管理所有的一级窗口 所有mainCanvas的一级窗口都应在windowsmanager中管理
    如果一级窗口例如createRoomPanel有多个创建不同规则的子窗口 从属createRoomPanel窗口本身管理
    同理 roomListPanel 进入密码房时可调用passwordInputPanel窗口 从属roomListPanel管理
    */

    public static WindowsManager Instance { get; private set; } // 单例
    
    private string currentWindow; // 当前所在窗口状态
    
    private void Awake() {
        if (Instance == null) {
            Instance = this;
        } else {
            Destroy(gameObject);
        }
        SwitchWindow("login"); // 初始化窗口 游戏初始应当在mainCanvas中显示登录窗口
    }

    // 切换窗口
    public void SwitchWindow(string targetWindow) {
        // 如果在game状态下收到切换到room的请求，直接返回(在对局中如果房间状态更新，不自动切换到房间列表)
        if (currentWindow == "game" && targetWindow == "room") {
            return;
        }

        Debug.Log($"切换到{targetWindow}窗口");

        menuPanel.SetActive(false);
        recordPanel.SetActive(false);
        playerPanel.SetActive(false);
        configPanel.SetActive(false);
        noticePanel.SetActive(false);
        aboutUsPanel.SetActive(false);
        roomRoot.SetActive(false);
        sceneConfigPanel.SetActive(false);
        spectatorPanel.SetActive(false);
        switch (targetWindow) {
            // login 登录界面
            case "login":
                gamePanel.SetActive(false);
                loginPanel.SetActive(true);
                break;
            // menu 主界面
            case "menu": // 关闭登录界面 打开主界面
                loginPanel.SetActive(false);
                headerPanel.SetActive(true);
                menuPanel.SetActive(true); // 主界面
                RoomNetworkManager.Instance?.GetRoomList(showTipOnSuccess: false);
                break;
            // room 房间界面
            case "room":
                roomRoot.SetActive(true);
                break;
            // game 游戏界面 下属的窗口
            case "game":
            case "recordscene":
                menuPanel.SetActive(false);
                headerPanel.SetActive(false);
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
            case "sceneConfig":
                sceneConfigPanel.SetActive(true);
                break;
            case "spectator":
                spectatorPanel.SetActive(true);
                break;
        }

        // 更新当前窗口状态
        currentWindow = targetWindow;
        // 由 HeaderPanel 统一根据当前窗口更新导航栏按钮图片
        HeaderPanel.Instance?.UpdateButtonState(targetWindow);
    }

    /// <summary>
    /// 获取当前窗口标识（如 "menu"、"room"、"game" 等），供 NetworkManager 等判断是否在主菜单等。
    /// </summary>
    public string GetCurrentWindow() => currentWindow;

}