using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
    [SerializeField] private HeaderButton friendButton;
    [SerializeField] private HeaderButton backToGameButton;
    [Tooltip("「正在对局中」的红色提醒按钮所使用的底色")]
    [SerializeField] private Color backToGameTintColor = new Color(0.92f, 0.34f, 0.34f);
    [Header("轮到自己操作时的提醒闪烁")]
    [Tooltip("回到主菜单后轮到自己操作，按钮在 turnFlashColorA 与 turnFlashColorB 之间渐变循环")]
    [SerializeField] private Color turnFlashColorA = new Color(0.94f, 0.32f, 0.32f);
    [SerializeField] private Color turnFlashColorB = new Color(1.00f, 0.78f, 0.20f);
    [Tooltip("一次完整渐变周期所需秒数")]
    [SerializeField] private float turnFlashCycleSeconds = 1.0f;

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
        if (UserDataManager.Instance != null) {
            UserDataManager.Instance.OnRoomIdChanged += RefreshButtonAppearance;
        }
        if (backToGameButton != null) {
            backToGameButton.Button.gameObject.SetActive(false);
            backToGameButton.Button.onClick.AddListener(BackToGame);
            ApplyBackToGameTint();
        }

        if (matchButton != null) {
            matchButton.Button.onClick.AddListener(Match);
        }

        if (menuButton != null) menuButton.Button.onClick.AddListener(Menu);
        if (roomButton != null) roomButton.Button.onClick.AddListener(Room);
        if (recordButton != null) recordButton.Button.onClick.AddListener(Record);
        if (playerDataButton != null) playerDataButton.Button.onClick.AddListener(PlayerData);
        if (configButton != null) configButton.Button.onClick.AddListener(Config);
        if (aboutUsButton != null) aboutUsButton.Button.onClick.AddListener(AboutUs);
        if (noticeButton != null) noticeButton.Button.onClick.AddListener(Notice);
        if (sceneConfigButton != null) sceneConfigButton.Button.onClick.AddListener(SceneConfig);
        if (spectatorButton != null) spectatorButton.Button.onClick.AddListener(Spectator);
        if (friendButton != null) friendButton.Button.onClick.AddListener(Friend);

        ConfigManager.OnLanguageChanged += RefreshHeaderLabels;
        RefreshHeaderLabels();
    }

    private void OnDestroy() {
        ConfigManager.OnLanguageChanged -= RefreshHeaderLabels;
        if (UserDataManager.Instance != null) {
            UserDataManager.Instance.OnRoomIdChanged -= RefreshButtonAppearance;
        }
    }

    /// <summary>
    /// 控制「正在对局中」红色返回按钮的显示状态。游戏中允许玩家在主菜单浏览时随时回到对局。
    /// </summary>
    public void SetBackToGameVisible(bool visible) {
        if (backToGameButton == null) return;
        backToGameButton.Button.gameObject.SetActive(visible);
        if (visible) ApplyBackToGameTint();
    }

    private void ApplyBackToGameTint() {
        if (backToGameButton == null) return;
        backToGameButton.SetState(false, true, backToGameTintColor);
    }

    private void Update() {
        if (backToGameButton == null) return;
        if (!backToGameButton.Button.gameObject.activeInHierarchy) return;
        bool inGameWindow = _currentWindowName == "game";
        bool needFlash = !inGameWindow
            && NormalGameStateManager.Instance != null
            && NormalGameStateManager.Instance.IsGameActive
            && NormalGameStateManager.Instance.IsSelfActionRequired;
        if (needFlash) {
            float t = Mathf.PingPong(Time.unscaledTime / Mathf.Max(0.05f, turnFlashCycleSeconds * 0.5f), 1f);
            backToGameButton.SetState(false, true, Color.Lerp(turnFlashColorA, turnFlashColorB, t));
        } else {
            ApplyBackToGameTint();
        }
    }

    private void BackToGame() {
        SetBackToGameVisible(false);
        WindowsManager.Instance.SwitchWindow("game");
    }

    private void Menu() => WindowsManager.Instance.SwitchWindow("menu");
    private void Room() {
        if (!LobbyStateGuard.IsInRoom && LobbyStateGuard.BlockIfInMatchQueueForRoom()) {
            return;
        }
        WindowsManager.Instance.SwitchWindow("room");
    }
    private void Record() {
        WindowsManager.Instance.SwitchWindow("record");
        DataNetworkManager.Instance?.GetRecordList();
    }
    private void PlayerData() {
        WindowsManager.Instance.SwitchWindow("player");
        DataNetworkManager.Instance?.GetLeaderboard();
    }
    private void Config() => WindowsManager.Instance.SwitchWindow("config");
    private void AboutUs() => WindowsManager.Instance.SwitchWindow("aboutUs");
    private void Notice() => WindowsManager.Instance.SwitchWindow("notice");
    private void SceneConfig() => WindowsManager.Instance.SwitchWindow("sceneConfig");
    private void Spectator() => WindowsManager.Instance.SwitchWindow("spectator");
    private void Match() {
        if (UserDataManager.Instance != null && UserDataManager.Instance.IsTourist) {
            NotificationManager.Instance?.ShowTip("匹配", false, "游客无法进行排位匹配，请先注册账号");
            return;
        }
        if (LobbyStateGuard.BlockIfInRoomForMatch()) {
            return;
        }
        WindowsManager.Instance.SwitchWindow("match");
    }

    /// <summary>
    /// 游客账户隐藏排位匹配入口；登录成功后由 NetworkManager 调用。
    /// </summary>
    public void RefreshMatchButtonVisibility() {
        if (matchButton == null) return;
        bool visible = UserDataManager.Instance == null || !UserDataManager.Instance.IsTourist;
        matchButton.gameObject.SetActive(visible);
    }
    private void Friend() {
        WindowsManager.Instance.SwitchWindow("friend");
        FriendNetworkManager.Instance?.ListAllFriendPanels();
    }

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
        friendButton?.SetState(_currentWindowName == "friend", false, default);
    }

    private static bool IsInRoom() {
        return UserDataManager.Instance != null && UserDataManager.Instance.RoomId != UserDataManager.ROOM_ID_NONE;
    }

    private void RefreshHeaderLabels() {
        SetButtonLabel(menuButton, HeaderNavItem.Menu);
        SetButtonLabel(roomButton, HeaderNavItem.Room);
        SetButtonLabel(friendButton, HeaderNavItem.Friend);
        SetButtonLabel(spectatorButton, HeaderNavItem.Spectator);
        SetButtonLabel(playerDataButton, HeaderNavItem.PlayerData);
        SetButtonLabel(recordButton, HeaderNavItem.Record);
        SetButtonLabel(matchButton, HeaderNavItem.Match);
        SetButtonLabel(sceneConfigButton, HeaderNavItem.SceneConfig);
        SetButtonLabel(aboutUsButton, HeaderNavItem.AboutUs);
        SetButtonLabel(noticeButton, HeaderNavItem.Notice);
        SetButtonLabel(configButton, HeaderNavItem.Config);
        SetButtonLabel(backToGameButton, HeaderNavItem.BackToGame);
    }

    private static void SetButtonLabel(HeaderButton button, HeaderNavItem item) {
        if (button == null) {
            return;
        }
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label == null) {
            return;
        }
        label.text = AppLanguageTexts.GetHeaderNavLabel(item);
    }
}
