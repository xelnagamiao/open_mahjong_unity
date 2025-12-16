using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HeaderPanel : MonoBehaviour
{
    [SerializeField] private Button MenuButton;
    [SerializeField] private Button RoomListButton;
    [SerializeField] private Button RecordButton;
    [SerializeField] private Button playerDataButton;
    [SerializeField] private Button ConfigButton;
    [SerializeField] private Button AboutUsButton;
    [SerializeField] private Button NoticeButton;
    // Start is called before the first frame update
    void Start()
    {
        RoomListButton.onClick.AddListener(RoomList);
        RecordButton.onClick.AddListener(Record);
        playerDataButton.onClick.AddListener(PlayerInfo);
        ConfigButton.onClick.AddListener(Config);
        AboutUsButton.onClick.AddListener(AboutUs);
        NoticeButton.onClick.AddListener(Notice);
        MenuButton.onClick.AddListener(Menu);
    }

    private void Menu(){
        WindowsManager.Instance.SwitchWindow("menu");
    }

    private void Config(){
        WindowsManager.Instance.SwitchWindow("config");
    }

    private void AboutUs(){
        WindowsManager.Instance.SwitchWindow("aboutUs");
    }

    private void Notice(){
        WindowsManager.Instance.SwitchWindow("notice");
    }

    private void RoomList(){
        WindowsManager.Instance.SwitchWindow("room");
        RoomListPanel.Instance.RefreshRoomList(); // 刷新房间列表
    }

    private void Record(){
        WindowsManager.Instance.SwitchWindow("record");
        NetworkManager.Instance.GetRecordList(); // 获取游戏记录
    }

    private void PlayerInfo(){
        WindowsManager.Instance.SwitchWindow("player");
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
