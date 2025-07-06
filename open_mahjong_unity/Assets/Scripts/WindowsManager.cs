using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;


public class WindowsManager : MonoBehaviour
{
    private Camera MainCamera;
    [Header("Canvas")]
    [SerializeField] private GameObject gameCanvas;
    [SerializeField] private GameObject mainCanvas;
    [Header("mainCanvas管理三个窗口")]
    [SerializeField] private GameObject loginRoot;
    [SerializeField] private GameObject mainRoot;
    [SerializeField] private GameObject roomRoot;
    [Header("roomRoot管理四个子窗口,passwordInput开关通过CreateRoom管理")]
    [SerializeField] private GameObject roomListPanel;
    [SerializeField] private GameObject roomPanel;
    [SerializeField] private GameObject createRoomPanel;
    [SerializeField] private GameObject passwordInput;


    public static WindowsManager Instance { get; private set; } // 单例模式 外部只读 内部可写
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        WindowsInit();
    }

    // 切换窗口
    public void SwitchWindow(string targetWindow)
    {
        Debug.Log($"切换到{targetWindow}窗口");
        switch (targetWindow)
        {
            // main 主界面 下属的窗口
            case "main": // 关闭登录界面 打开主界面
                mainRoot.SetActive(true);
                roomRoot.SetActive(false);
                break;
            // 三个roomroot下属的窗口
            case "roomList": // 打开房间界面 关闭其他界面
                roomRoot.SetActive(true);  // 房间根
                roomListPanel.SetActive(true); // 房间列表界面
                createRoomPanel.SetActive(false);
                roomPanel.SetActive(false);
                passwordInput.SetActive(false); // 在初次激活roomRoot时关闭密码输入框
                break;
            case "room":
                roomRoot.SetActive(true); // 房间根
                roomListPanel.SetActive(false); 
                createRoomPanel.SetActive(false);
                roomPanel.SetActive(true); // 房间内部界面
                break;
            case "createRoom":
                roomRoot.SetActive(true); // 房间根
                roomListPanel.SetActive(false); 
                createRoomPanel.SetActive(true); // 创建房间界面
                roomPanel.SetActive(false);
                break;
            // game 游戏界面 下属的窗口
            case "game":
                gameCanvas.SetActive(true);
                mainRoot.SetActive(false);
                roomRoot.SetActive(false);
                break;
        }
    }

    // 初始化窗口
    private void WindowsInit(){
        loginRoot.SetActive(true);
        mainRoot.SetActive(false);
        roomRoot.SetActive(false);
        gameCanvas.SetActive(false);
    }

    // 登录完成
    public void LoginSuccess(){
        mainRoot.SetActive(true);
        Destroy(loginRoot);
    }

}

