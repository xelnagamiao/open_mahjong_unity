using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro; // Added TMPro namespace

// RoomItem 的作用是通过SetRoomInfo方法设置房间信息，并保存自己的房间号
// 监听JoinClick的事件，如果发生事件返回RoomPanel包含自己房间id的joinRoom调用

public class GBRoomItem : MonoBehaviour
{

    [SerializeField] private TMP_Text roomName; // 房间名
    [SerializeField] private TMP_Text hostName; // 房主名
    [SerializeField] private TMP_Text playerCount; // 玩家人数
    [SerializeField] private TMP_Text roomID; // 房间号
    [SerializeField] private TMP_Text gameRound; // 游戏圈数
    [SerializeField] private Button joinButton; // 加入按钮
    [SerializeField] private TMP_Text hasPassword; // 是否有密码
    [SerializeField] private TMP_Text playRule; // 规则

    private void Start()
    {
        // 监听房间元素的点击按钮事件
        joinButton.onClick.AddListener(JoinClick);
    }
    // 在创建RoomItem时，初始化房间号和是否有密码的布尔值 在点击按钮时，返回上一级至RoomListPanel
    private string roomId; // 房间号
    private bool needPassword; // 是否有密码

    // SetRoomInfo 会在RoomPanel的HandleRoomListResponse中调用
    public void SetRoomListInfo(string roomId,string roomName,string hostName,int playerCount,int gameTime,bool hasPassword,string room_type)
    {
        this.roomId = roomId; // 保存房间号,在JoinClick中返回上一级
        this.needPassword = hasPassword;
        this.roomID.text = $"房间号: {roomId}";
        this.roomName.text = $"房间名: {roomName}";
        this.hostName.text = $"房主: {hostName}";
        this.playerCount.text = $"玩家数量{playerCount}/4";
        this.gameRound.text = $"圈数：{gameTime}";
        this.hasPassword.text = $"密码：{hasPassword}";
        this.playRule.text = $"规则：{room_type}";

        // 如果房间已满，禁用加入按钮
        joinButton.interactable = playerCount < 4;
    }
    // 定义房间加入点击事件 JoinClicked 等待RoomListPanel订阅
    public event System.Action<string, bool> JoinClicked;  

    // 点击加入按钮时，触发JoinClicked事件
    private void JoinClick()
    {
        JoinClicked?.Invoke(roomId, needPassword);  // 触发事件
    }


}
