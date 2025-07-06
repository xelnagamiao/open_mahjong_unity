using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RoomListPanel : MonoBehaviour
{
    public static RoomListPanel Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private Button createButton;      // 创建房间按钮
    [SerializeField] private Button refreshButton;     // 刷新按钮
    [SerializeField] private Transform roomListContent; // 房间列表容器
    [SerializeField] private GameObject roomItemPrefab; // 房间预制体
    [SerializeField] private Button backButton; // 返回按钮

    [Header("Password Input")]
    [SerializeField] private GameObject passwordInputPanel; // 密码输入面板
    [SerializeField] private TMP_InputField passwordInput; // 密码输入框
    [SerializeField] private Button passwordInputAdmit; // 密码输入按钮
    [SerializeField] private Button passwordInputCancel; // 密码输入取消按钮

    // GameObject列表 存储当前房间列表 用于CreateRoomList和ClearRoomList
    private List<GameObject> currentRoomItems = new List<GameObject>();
    private string roomId;
    private void Start()
    {
        // 初始化按钮监听 1.显示房间面板 2.刷新房间列表 3.返回主面板
        createButton.onClick.AddListener(OpenCreatePanel);
        refreshButton.onClick.AddListener(RefreshRoomList);
        backButton.onClick.AddListener(BackButtonClicked);
        // 订阅密码输入按钮
        passwordInputAdmit.onClick.AddListener(PasswordInputAdmit);
        passwordInputCancel.onClick.AddListener(PasswordInputCancel);
        // 订阅GetRoomListResponse事件
        NetworkManager.Instance.GetRoomListResponse.AddListener(GetRoomListResponse);
    }

    // 1.点击创建房间按钮 打开房间面板
    private void OpenCreatePanel()
    {
        WindowsManager.Instance.SwitchWindow("createRoom");
    }

    // 3.刷新房间列表 调用NetworkManager的GetRoomList方法
    public void RefreshRoomList()
    {
        // ClearRoomList 清空当前列表
        foreach (var item in currentRoomItems)
        {
            Destroy(item);
        }
        // 请求新的房间列表
        NetworkManager.Instance.GetRoomList();
    }
    // 返回主面板
    private void BackButtonClicked()
    {
        WindowsManager.Instance.SwitchWindow("main");
    }
    // 4.获取房间列表响应
    private void GetRoomListResponse(bool success, string message, RoomData[] room_List)
    {
        if (!success)
        {
            Debug.LogError($"获取房间列表失败: {message}");
            return;
        }

        // 清理现有房间列表
        foreach (var item in currentRoomItems)
        {
            Destroy(item);
        }
        currentRoomItems.Clear();

        // 直接使用 roomList 数组
        if (room_List != null)
        {
            foreach (var roomData in room_List)
            {
                // instantiate 是python的实例化语法 将roomItemPrefab实例化 并添加到roomListContent下
                GameObject roomItem = Instantiate(roomItemPrefab, roomListContent);
                roomItem.SetActive(true);
                currentRoomItems.Add(roomItem);

                // 获取RoomItem组件 订阅RoomItem的JoinClicked事件
                var roomItemComponent = roomItem.GetComponent<RoomItem>();
                roomItemComponent.JoinClicked += JoinClicked;

                // 初始化房间信息
                roomItemComponent.SetRoomListInfo(
                    roomId: roomData.room_id,
                    roomName: roomData.room_name,
                    hostName: roomData.host_name,
                    playerCount: roomData.player_count,
                    gameTime: roomData.game_time,
                    hasPassword: roomData.has_password
                );
            }
        }
    }
    // 接收到房间预制体的加入房间点击事件和预制体的roomid,needPassword
    // 如果需要密码 则打开密码输入面板 并保存roomId 否则直接调用joinRoom
    public void JoinClicked(string roomId, bool needPassword)
    {
        if (needPassword)
        {
            passwordInputPanel.SetActive(true);
            this.roomId = roomId;
        }
        else
        {
            NetworkManager.Instance.JoinRoom(roomId, "");
        }
    }
    // 密码输入面板点击确认调用joinRoom
    private void PasswordInputAdmit()
    {
        if (string.IsNullOrEmpty(passwordInput.text))
        {
            Debug.LogError("密码不能为空");
            return;
        }
        passwordInputPanel.SetActive(false);
        NetworkManager.Instance.JoinRoom(roomId, passwordInput.text);
    }
    // 密码输入面板点击取消关闭密码输入面板
    private void PasswordInputCancel()
    {
        passwordInputPanel.SetActive(false);
    }
}

