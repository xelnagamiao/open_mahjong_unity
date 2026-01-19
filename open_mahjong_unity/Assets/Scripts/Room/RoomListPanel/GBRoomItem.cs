using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro; // Added TMPro namespace

// RoomItem 的作用是通过SetRoomInfo方法设置房间信息，并保存自己的房间号
// 监听JoinClick的事件，如果发生事件返回RoomPanel包含自己房间id的joinRoom调用

public class GBRoomItem : MonoBehaviour {

    [SerializeField] private TMP_Text roomName; // 房间名
    [SerializeField] private TMP_Text hostName; // 房主名
    [SerializeField] private TMP_Text playerCount; // 玩家人数
    [SerializeField] private TMP_Text roomID; // 房间号
    [SerializeField] private TMP_Text gameRound; // 游戏圈数
    [SerializeField] private Button joinButton; // 加入按钮
    [SerializeField] private TMP_Text hasPassword; // 是否有密码
    [SerializeField] private TMP_Text playRule; // 规则
    [SerializeField] private TMP_Text gameStatus; // 游戏状态（是否正在运行）
    [SerializeField] private TMP_Text fushiText; // 复式
    [SerializeField] private TMP_Text tipsText; // 提示
    [SerializeField] private TMP_Text cuoheText; // 错和

    private void Start() {
        // 监听房间元素的点击按钮事件
        joinButton.onClick.AddListener(JoinClick);
    }
    // 在创建RoomItem时，初始化房间号和是否有密码的布尔值 在点击按钮时，返回上一级至RoomListPanel
    private string roomId; // 房间号
    private bool needPassword; // 是否有密码

    // SetRoomInfo 会在RoomPanel的HandleRoomListResponse中调用
    public void SetRoomListInfo(string roomId,string roomName,string hostUserName,int playerCount,int gameTime,bool hasPassword,string room_type,bool isGameRunning,int randomSeed,bool tips,bool openCuohe) {
        this.roomId = roomId; // 保存房间号,在JoinClick中返回上一级
        this.needPassword = hasPassword; // 保存是否需要密码
        
        this.roomID.text = $"房间号:{roomId}";
        this.roomName.text = $"房间名:{roomName}";
        this.hostName.text = $"房主:{hostUserName}";
        this.playerCount.text = $"玩家数{playerCount}/4";
        
        // 显示游戏圈数
        if (gameTime == 1){
            this.gameRound.text = $"东风战";
        } else if(gameTime == 2){
            this.gameRound.text = $"东南战";
        } else if(gameTime == 3){
            this.gameRound.text = $"西南战";
        } else if(gameTime == 4){
            this.gameRound.text = $"全庄战";
        }
        
        // 显示规则
        if (room_type == "guobiao"){
            this.playRule.text = $"国标";
        } else {
            this.playRule.text = $"规则:未知";
        }
        
        // 显示游戏状态
        if (isGameRunning){
            this.gameStatus.text = "游戏中";
        } else {
            this.gameStatus.text = "等待中";
        }

        // 显示是否有密码
        if(hasPassword){
            this.hasPassword.text = $"密码:有";
        } else {
            this.hasPassword.text = $"密码:无";
        }

        // 显示复式、提示、错和
        fushiText.text = randomSeed == 0 ? "复式:关" : "复式:开";
        tipsText.text = tips ? "提示:开" : "提示:关";
        cuoheText.text = openCuohe ? "错和:开" : "错和:关";
        
        // 如果房间已满或游戏正在运行，禁用加入按钮
        joinButton.interactable = playerCount < 4 && !isGameRunning;
    }
    // 点击加入按钮时，直接调用密码面板
    private void JoinClick() {
        RoomListPanel.Instance.JoinClicked(roomId, needPassword);
    }


}
