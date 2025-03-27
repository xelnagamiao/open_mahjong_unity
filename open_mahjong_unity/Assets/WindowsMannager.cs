using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;


public class WindowsMannager : MonoBehaviour
{
    private Camera MainCamera;
    [SerializeField] private GameObject mainCanvas;
    [Header("mainCanvas管理三个窗口")]
    [SerializeField] private GameObject loginPanel;
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject roomRoot;
    [Header("roomRoot管理四个子窗口,passwordInput开关通过CreateRoom管理")]
    [SerializeField] private GameObject roomListPanel;
    [SerializeField] private GameObject roomPanel;
    [SerializeField] private GameObject createRoomPanel;
    [SerializeField] private GameObject passwordInput;
    [Header("GameCanvas")]
    [SerializeField] private GameObject gameCanvas;

    public static WindowsMannager Instance { get; private set; } // 单例模式 外部只读 内部可写
    // 定义SwitchWindowsEvent 是一个特殊的unityevent事件类 可以携带一个string参数
    public class SwitchWindowsEvent : UnityEvent<string>{}  
    // 实例化一个SwitchWindowsEvent 建立一个事件响应实例 GetWindowsSwitchResponse 用于接收事件 外部通过invoke调用
    public SwitchWindowsEvent GetWindowsSwitchResponse = new SwitchWindowsEvent(); 
    
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
        GetWindowsSwitchResponse.AddListener(OnSwitchWindow); // 监听GetWindowsSwitchResponse事件
        WindowsMannager.Instance.GetWindowsSwitchResponse.Invoke("login"); // 游戏初始化登录
    }

    private void OnSwitchWindow(string targetWindow)
    {
        Debug.Log($"切换到{targetWindow}窗口");
        switch (targetWindow)
        {
            // main 主界面 下属的窗口
            case "main": // 关闭登录界面 打开主界面
                mainPanel.SetActive(true);
                loginPanel.SetActive(false);
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
            // login 相当于初始化方法
            case "login":
                gameCanvas.SetActive(false);
                mainPanel.SetActive(false);
                roomRoot.SetActive(false);
                loginPanel.SetActive(true);
                break;
            // game 游戏界面 下属的窗口
            case "game":
                gameCanvas.SetActive(true);
                mainPanel.SetActive(false);
                roomRoot.SetActive(false);
                break;
        }
    }
}
