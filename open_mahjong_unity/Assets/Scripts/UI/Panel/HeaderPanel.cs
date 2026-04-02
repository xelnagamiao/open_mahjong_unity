using UnityEngine;
using UnityEngine.UI;

public class HeaderPanel : MonoBehaviour {
    public static HeaderPanel Instance { get; private set; }

    [SerializeField] private HeaderButton menuButton;
    [SerializeField] private HeaderButton roomButton;
    [SerializeField] private HeaderButton recordButton;
    [SerializeField] private HeaderButton playerDataButton;
    [SerializeField] private HeaderButton configButton;
    [SerializeField] private HeaderButton aboutUsButton;
    [SerializeField] private HeaderButton noticeButton;
    [SerializeField] private HeaderButton sceneConfigButton;
    [SerializeField] private HeaderButton spectatorButton;
    [SerializeField] private HeaderButton matchButton;
    [SerializeField] private HeaderButton backToGameButton;

    [Header("仅由 Panel 设置的 Stay 颜色（如：在房间里但未选房间页时房间按钮的颜色）")]
    [SerializeField] private Color playOnStayColor = new Color(0.6f, 0.85f, 1f);

    private string _currentWindowName;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start() {
        if (backToGameButton != null) {
            backToGameButton.Button.gameObject.SetActive(false);
        }

        if (matchButton != null) {
            matchButton.Button.onClick.AddListener(Match);
        }

        if (menuButton != null) menuButton.Button.onClick.AddListener(Menu);
        if (roomButton != null) roomButton.Button.onClick.AddListener(Room);
        if (recordButton != null) recordButton.Button.onClick.AddListener(Record);
        if (playerDataButton != null) playerDataButton.Button.onClick.AddListener(PlayerInfo);
        if (configButton != null) configButton.Button.onClick.AddListener(Config);
        if (aboutUsButton != null) aboutUsButton.Button.onClick.AddListener(AboutUs);
        if (noticeButton != null) noticeButton.Button.onClick.AddListener(Notice);
        if (sceneConfigButton != null) sceneConfigButton.Button.onClick.AddListener(SceneConfig);
        if (spectatorButton != null) spectatorButton.Button.onClick.AddListener(Spectator);
    }

    private void Menu() => WindowsManager.Instance.SwitchWindow("menu");
    private void Room() => WindowsManager.Instance.SwitchWindow("room");
    private void Record() {
        WindowsManager.Instance.SwitchWindow("record");
        DataNetworkManager.Instance?.GetRecordList();
    }
    private void PlayerInfo() => WindowsManager.Instance.SwitchWindow("player");
    private void Config() => WindowsManager.Instance.SwitchWindow("config");
    private void AboutUs() => WindowsManager.Instance.SwitchWindow("aboutUs");
    private void Notice() => WindowsManager.Instance.SwitchWindow("notice");
    private void SceneConfig() => WindowsManager.Instance.SwitchWindow("sceneConfig");
    private void Spectator() => WindowsManager.Instance.SwitchWindow("spectator");
    private void Match() => WindowsManager.Instance.SwitchWindow("match");

    /// <summary>
    /// 根据当前窗口更新导航栏按钮状态（由 WindowsManager.SwitchWindow 末尾调用）。Stay 颜色仅在此处设置。
    /// </summary>
    public void UpdateButtonState(string windowName) {
        _currentWindowName = windowName;
        RefreshButtonAppearance();
    }

    private void RefreshButtonAppearance() {
        bool inRoom = IsInRoom();
        menuButton?.SetState(_currentWindowName == "menu", false, default);
        roomButton?.SetState(_currentWindowName == "room", inRoom, playOnStayColor);
        recordButton?.SetState(_currentWindowName == "record", false, default);
        playerDataButton?.SetState(_currentWindowName == "player", false, default);
        configButton?.SetState(_currentWindowName == "config", false, default);
        aboutUsButton?.SetState(_currentWindowName == "aboutUs", false, default);
        noticeButton?.SetState(_currentWindowName == "notice", false, default);
        sceneConfigButton?.SetState(_currentWindowName == "sceneConfig", false, default);
        spectatorButton?.SetState(_currentWindowName == "spectator", false, default);
        matchButton?.SetState(_currentWindowName == "match", false, default);
    }

    private static bool IsInRoom() {
        return UserDataManager.Instance != null && UserDataManager.Instance.RoomId != UserDataManager.ROOM_ID_NONE;
    }
}
