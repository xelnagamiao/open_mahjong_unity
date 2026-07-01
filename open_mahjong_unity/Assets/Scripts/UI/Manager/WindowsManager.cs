using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WindowsManager : MonoBehaviour {
    [Header("mainCanvas")]
    [SerializeField] private GameObject mainCanvas;

    [Header("顶层窗口")]
    [SerializeField] private GameObject headerPanel; // 
    [SerializeField] private GameObject chatPanel; // 聊天窗口 保持窗口常开
    [SerializeField] private GameObject streamerModePanel; // 主播模式面板
    [SerializeField] private GameObject gamePanel; // 游戏窗口
    
    [Header("一级窗口")]
    [SerializeField] private GameObject loginPanel; // 登录窗口
    [SerializeField] private GameObject menuPanel; // 菜单界面窗口
    [SerializeField] private GameObject recordPanel; // 游戏记录窗口
    [SerializeField] private GameObject playerPanel; // 玩家数据窗口（DataPanel）
    [SerializeField] private GameObject configPanel; // 玩家配置窗口
    [SerializeField] private GameObject noticePanel; // 公告窗口
    [SerializeField] private GameObject aboutUsPanel; // 关于我们窗口
    [SerializeField] private GameObject roomRoot; // 房间根窗口（包含房间列表、房间、创建房间等子窗口）
    [SerializeField] private GameObject sceneConfigPanel; // 场景配置窗口
    [SerializeField] private GameObject spectatorPanel; // 观战窗口
    [SerializeField] private GameObject matchPanel; // 匹配窗口（段位匹配等）
    [SerializeField] private GameObject friendPanel; // 好友/关注窗口

    [Header("窗口切换动画")]
    [SerializeField] private float windowFadeDuration = 0.2f;

    /*
    windowsmanager管理所有的一级窗口 所有mainCanvas的一级窗口都应在windowsmanager中管理
    如果一级窗口例如createRoomPanel有多个创建不同规则的子窗口 从属createRoomPanel窗口本身管理
    同理 roomListPanel 进入密码房时可调用passwordInputPanel窗口 从属roomListPanel管理
    */

    public static WindowsManager Instance { get; private set; } // 单例
    
    private string currentWindow; // 当前所在窗口状态
    /// <summary>最近一次所在的大厅顶栏标签；进入 game/recordscene 时不改写，退出时直接切回此处。</summary>
    private string lastLobbyTab = "menu";
    private Coroutine _switchRoutine;

    private static bool IsLobbyTab(string window) {
        return window == "menu" || window == "room" || window == "record" || window == "player"
            || window == "config" || window == "notice" || window == "aboutUs" || window == "sceneConfig"
            || window == "spectator" || window == "match" || window == "friend";
    }

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        ApplyColdBootHiddenState(); // 未登录前关掉场景中的一级窗口，避免子面板 Start 协程读到空用户名等
        StreamerModeHelper.OnChanged += ApplyStreamerModePanels;
        SwitchWindow("login"); // 初始化窗口 游戏初始应当在mainCanvas中显示登录窗口
    }

    private void Start() {
        ApplyStreamerModePanels();
    }

    private void OnDestroy() {
        StreamerModeHelper.OnChanged -= ApplyStreamerModePanels;
    }

    public void ApplyStreamerModePanels() {
        bool showStreamerPanel = StreamerModeHelper.IsEnabled;
        GameObject chat = ResolveChatPanel();
        GameObject streamer = ResolveStreamerModePanel();
        if (chat != null) {
            chat.SetActive(!showStreamerPanel);
        }
        if (streamer != null) {
            streamer.SetActive(showStreamerPanel);
        }
    }

    private GameObject ResolveChatPanel() {
        if (chatPanel != null) {
            return chatPanel;
        }
        return ChatPanel.Instance != null ? ChatPanel.Instance.gameObject : null;
    }

    private GameObject ResolveStreamerModePanel() {
        if (streamerModePanel != null) {
            return streamerModePanel;
        }
        return StreamerModePanel.Instance != null ? StreamerModePanel.Instance.gameObject : null;
    }

    /// <summary>
    /// 断线软重置：立即回到仅显示登录界面的布局（无渐变动画）。
    /// </summary>
    public void ResetToLoginUI() {
        if (_switchRoutine != null) {
            StopCoroutine(_switchRoutine);
            _switchRoutine = null;
        }
        if (mainCanvas != null && !mainCanvas.activeSelf) {
            mainCanvas.SetActive(true);
        }
        EnsureLoginCanvasActive();
        ApplyColdBootHiddenState();
        EnsurePanelVisible(loginPanel);
        currentWindow = "login";
        lastLobbyTab = "menu";
        HeaderPanel.Instance?.UpdateButtonState("login");
        Debug.Log("[WindowsManager] 已重置到登录界面");
    }

    private void EnsureLoginCanvasActive() {
        if (loginPanel == null) return;
        foreach (Canvas canvas in loginPanel.GetComponentsInParent<Canvas>(true)) {
            if (canvas != null) canvas.gameObject.SetActive(true);
        }
    }

    private static void EnsurePanelVisible(GameObject go) {
        if (go == null) return;
        go.SetActive(true);
        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        if (cg == null) return;
        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;
    }

    /// <summary>
    /// 冷启动：除登录界面外的一级窗口一律 SetActive(false)，不依赖渐变协程，避免未登录时 room/创建房等子脚本提前启动。
    /// </summary>
    private void ApplyColdBootHiddenState() {
        void off(GameObject go) {
            if (go != null) go.SetActive(false);
        }
        off(headerPanel);
        off(menuPanel);
        off(recordPanel);
        off(playerPanel);
        off(configPanel);
        off(noticePanel);
        off(aboutUsPanel);
        off(roomRoot);
        off(sceneConfigPanel);
        off(spectatorPanel);
        off(matchPanel);
        off(friendPanel);
        off(gamePanel);
        loginPanel.SetActive(true);
    }

    // 切换窗口
    public void SwitchWindow(string targetWindow) {
        StartSwitchWindow(targetWindow, ensureHeader: false);
    }

    /// <summary>
    /// 真正离开 gamePanel（牌谱/观战/对局结束等）：先关闭游戏窗，再切到带 header 的目标页。
    /// 对局进行中挂后台浏览请继续用 SwitchWindow("menu")，不要调用本方法。
    /// </summary>
    public void ExitGameTo(string targetWindow) {
        if (gamePanel != null) gamePanel.SetActive(false);
        StartSwitchWindow(targetWindow, ensureHeader: true);
    }

    /// <summary>
    /// 离开 gamePanel 并切回进入对局/牌谱前所在的大厅标签（与游戏内「回到主菜单」同一套导航状态）。
    /// </summary>
    public void ExitGameToLastLobbyTab() {
        ExitGameTo(lastLobbyTab);
    }

    /// <summary>进入 game/recordscene 前最后一次选中的顶栏标签，默认 menu。</summary>
    public string GetLastLobbyTab() => lastLobbyTab;

    /// <summary>离开房间后：若退出目标仍是房间页，改为主菜单，避免对局结束误回房间。</summary>
    public void OnLeftRoom() {
        if (lastLobbyTab == "room") {
            lastLobbyTab = "menu";
        }
    }

    private void StartSwitchWindow(string targetWindow, bool ensureHeader) {
        if (_switchRoutine != null) {
            StopCoroutine(_switchRoutine);
            _switchRoutine = null;
            NormalizeCanvasGroups();
        }
        Debug.Log($"切换到{targetWindow}窗口");
        _switchRoutine = StartCoroutine(SwitchWindowRoutine(targetWindow, ensureHeader));
    }

    private IEnumerator SwitchWindowRoutine(string targetWindow, bool ensureHeader = false) {
        var wasActive = new HashSet<GameObject>(); // 当前激活窗口集合
        CollectCurrentlyActiveManaged(wasActive); // 读取 activeSelf 现况
        var willActive = new HashSet<GameObject>(wasActive); // 目标集合
        ApplySwitchSequenceToSet(willActive, targetWindow, ensureHeader); // 计算切换后的目标激活集合

        ApplyHeaderPanelInstant(wasActive, willActive); // 顶部栏即时切换
        if (IsLobbyTab(targetWindow)) {
            lastLobbyTab = targetWindow;
        }
        currentWindow = targetWindow; // 更新当前窗口状态
        HeaderPanel.Instance?.UpdateButtonState(targetWindow); // 即时刷新导航栏按钮
        if (targetWindow == "room"
            && UserDataManager.Instance != null
            && UserDataManager.Instance.RoomId == UserDataManager.ROOM_ID_NONE) {
            RoomWindowsManager.Instance?.SwitchRoomWindow("createRoom");
        }

        var fadeOut = new List<(GameObject go, CanvasGroup cg)>(); // 待渐隐面板
        var fadeIn = new List<(GameObject go, CanvasGroup cg)>(); // 待渐显面板
        foreach (var go in wasActive) {
            if (go == null || go == headerPanel) continue;
            if (!willActive.Contains(go)) fadeOut.Add((go, EnsureCanvasGroup(go))); // 从显示到隐藏
        }
        foreach (var go in willActive) {
            if (go == null || go == headerPanel) continue;
            if (!wasActive.Contains(go)) fadeIn.Add((go, EnsureCanvasGroup(go))); // 从隐藏到显示
        }

        WindowFadeTransition.PrepareFadeIn(fadeIn); // 统一淡入初态
        WindowFadeTransition.PrepareFadeOut(fadeOut); // 统一淡出初态
        if (fadeOut.Count == 0 && fadeIn.Count == 0) {
            if (targetWindow == "menu") RoomNetworkManager.Instance?.GetRoomList(showTipOnSuccess: false); // 仅主界面刷新房间列表
            _switchRoutine = null; // 清理协程引用
            yield break;
        }
        float duration = windowFadeDuration <= 0f ? 0f : windowFadeDuration; // 渐变时长
        yield return WindowFadeTransition.Fade(fadeOut, fadeIn, duration); // 执行渐隐渐显
        if (targetWindow == "menu") RoomNetworkManager.Instance?.GetRoomList(showTipOnSuccess: false); // 渐变完成后刷新列表
        _switchRoutine = null; // 协程结束
    }

    private void ApplyHeaderPanelInstant(HashSet<GameObject> was, HashSet<GameObject> will) {
        if (headerPanel == null) return;
        bool show = will.Contains(headerPanel);
        if (was.Contains(headerPanel) == show) return;
        headerPanel.SetActive(show);
        CanvasGroup cg = headerPanel.GetComponent<CanvasGroup>();
        if (cg != null) {
            cg.alpha = 1f;
            cg.interactable = show;
            cg.blocksRaycasts = show;
        }
    }

    /// <summary>
    /// 收集当前仍为激活的一级窗口（与其它代码改过的布局一致，只认 activeSelf）。
    /// </summary>
    private void CollectCurrentlyActiveManaged(HashSet<GameObject> dst) {
        dst.Clear();
        void addIfActive(GameObject go) {
            if (go != null && go.activeSelf) dst.Add(go);
        }
        addIfActive(loginPanel);
        addIfActive(headerPanel);
        addIfActive(menuPanel);
        addIfActive(recordPanel);
        addIfActive(playerPanel);
        addIfActive(configPanel);
        addIfActive(noticePanel);
        addIfActive(aboutUsPanel);
        addIfActive(roomRoot);
        addIfActive(sceneConfigPanel);
        addIfActive(spectatorPanel);
        addIfActive(matchPanel);
        addIfActive(friendPanel);
        addIfActive(gamePanel);
    }

    /// <summary>
    /// 与原先 SwitchWindow 里 SetActive 顺序等：在「当前激活集合」上做同样的关/开，得到切换后应激活的集合。
    /// </summary>
    private void ApplySwitchSequenceToSet(HashSet<GameObject> s, string targetWindow, bool ensureHeader = false) {
        void Off(GameObject go) {
            if (go != null) s.Remove(go);
        }
        void On(GameObject go) {
            if (go != null) s.Add(go);
        }
        Off(menuPanel);
        Off(recordPanel);
        Off(playerPanel);
        Off(configPanel);
        Off(noticePanel);
        Off(aboutUsPanel);
        Off(roomRoot);
        Off(sceneConfigPanel);
        Off(spectatorPanel);
        Off(matchPanel);
        Off(friendPanel);
        if (ensureHeader) {
            Off(gamePanel);
            if (targetWindow != "login" && targetWindow != "game" && targetWindow != "recordscene") {
                On(headerPanel);
            }
        }
        switch (targetWindow) {
            case "login":
                Off(gamePanel);
                On(loginPanel);
                break;
            case "menu":
                Off(loginPanel);
                On(headerPanel);
                On(menuPanel);
                break;
            case "room":
                On(headerPanel);
                On(roomRoot);
                break;
            case "game":
            case "recordscene":
                Off(menuPanel);
                Off(headerPanel);
                On(gamePanel);
                break;
            case "record":
                On(recordPanel);
                break;
            case "notice":
                On(noticePanel);
                break;
            case "aboutUs":
                On(aboutUsPanel);
                break;
            case "player":
                On(playerPanel);
                break;
            case "config":
                On(configPanel);
                break;
            case "sceneConfig":
                On(sceneConfigPanel);
                break;
            case "spectator":
                On(spectatorPanel);
                break;
            case "match":
                if (matchPanel != null) On(matchPanel);
                break;
            case "friend":
                if (friendPanel != null) On(friendPanel);
                break;
        }
    }

    private static CanvasGroup EnsureCanvasGroup(GameObject go) {
        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }

    private void NormalizeCanvasGroups() {
        void norm(GameObject go) {
            if (go == null) return;
            CanvasGroup cg = go.GetComponent<CanvasGroup>();
            if (cg == null) return;
            if (!go.activeSelf) return;
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }
        norm(loginPanel);
        norm(headerPanel);
        norm(menuPanel);
        norm(recordPanel);
        norm(playerPanel);
        norm(configPanel);
        norm(noticePanel);
        norm(aboutUsPanel);
        norm(roomRoot);
        norm(sceneConfigPanel);
        norm(spectatorPanel);
        norm(matchPanel);
        norm(friendPanel);
        norm(gamePanel);
    }

    /// <summary>
    /// 获取当前窗口标识（如 "menu"、"room"、"game" 等），供 NetworkManager 等判断是否在主菜单等。
    /// </summary>
    public string GetCurrentWindow() => currentWindow;

}
