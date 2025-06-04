using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RoomPanel : MonoBehaviour
{
    [SerializeField] private TMP_Text roomIdText; // 房间号
    [SerializeField] private TMP_Text roomnameText; // 房间名
    [SerializeField] private TMP_Text player_1; // 玩家1
    [SerializeField] private TMP_Text player_2; // 玩家2
    [SerializeField] private TMP_Text player_3; // 玩家3
    [SerializeField] private TMP_Text player_4; // 玩家4
    [SerializeField] private Button backButton; // 返回按钮
    [SerializeField] private Button startButton; // 开始按钮

    private string roomid;
    // Start is called before the first frame update
    void Start()
    {
        backButton.onClick.AddListener(BackButtonClicked);
        startButton.onClick.AddListener(StartButtonClicked);
    }

    private void OnEnable()
    {
        NetworkManager.Instance.GetRoomInfoResponse.AddListener(GetRoomInfoResponse);
    }

    private void GetRoomInfoResponse(bool success, string message, RoomInfo roomInfo)
    {
        // 设置房间号
        roomid = roomInfo.room_id;
        roomIdText.text = roomid;
        roomnameText.text = roomInfo.room_name;

        // 清空文本
        player_1.text = "";
        player_2.text = "";
        player_3.text = "";
        player_4.text = "";

        // 根据实际玩家数量设置文本
        for (int i = 0; i < roomInfo.player_list.Length && i < 4; i++)
        {
            switch (i)
            {
                case 0: player_1.text = roomInfo.player_list[i]; break;
                case 1: player_2.text = roomInfo.player_list[i]; break;
                case 2: player_3.text = roomInfo.player_list[i]; break;
                case 3: player_4.text = roomInfo.player_list[i]; break;
            }
        }

        // 禁用开始按钮 满4人后开启
        startButton.interactable = roomInfo.player_count == 4;
    }
    private void BackButtonClicked()
    {
        WindowsMannager.Instance.SwitchWindow("roomList");
        NetworkManager.Instance.LeaveRoom(roomid);

    }

    private void StartButtonClicked()
    {
        NetworkManager.Instance.StartGame(roomid);
    }




    // Update is called once per frame
    void Update()
    {
        
    }
}
