using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HeaderPanel : MonoBehaviour {
    public static HeaderPanel Instance { get; private set; }

    [SerializeField] private Button MenuButton;
    [SerializeField] private Button RoomButton;
    [SerializeField] private Button RecordButton;
    [SerializeField] private Button playerDataButton;
    [SerializeField] private Button ConfigButton;
    [SerializeField] private Button AboutUsButton;
    [SerializeField] private Button NoticeButton;

    private Button currentSelectedButton; // 当前选中的按钮

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // Start is called before the first frame update
    void Start() {
        RoomButton.onClick.AddListener(Room);
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

    private void Room(){
        WindowsManager.Instance.SwitchWindow("room");
    }

    private void Record(){
        WindowsManager.Instance.SwitchWindow("record");
        NetworkManager.Instance.GetRecordList(); // 获取游戏记录
    }

    private void PlayerInfo(){
        WindowsManager.Instance.SwitchWindow("player");
    }

    // 更新按钮选中状态
    public void UpdateButtonState(string windowName) {
        // 取消之前选中按钮的状态
        if (currentSelectedButton != null) {
            currentSelectedButton.OnDeselect(null);
        }

        // 根据窗口名称选择对应的按钮
        Button targetButton = null;
        switch (windowName) {
            case "menu":
                targetButton = MenuButton;
                break;
            case "room":
                targetButton = RoomButton;
                break;
            case "record":
                targetButton = RecordButton;
                break;
            case "player":
                targetButton = playerDataButton;
                break;
            case "config":
                targetButton = ConfigButton;
                break;
            case "aboutUs":
                targetButton = AboutUsButton;
                break;
            case "notice":
                targetButton = NoticeButton;
                break;
        }

        // 设置新按钮为选中状态
        targetButton.Select();
        currentSelectedButton = targetButton;
    }

    // Update is called once per frame
    void Update() {
        
    }
}
