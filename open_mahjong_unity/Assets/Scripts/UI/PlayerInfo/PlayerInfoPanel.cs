using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayerInfoPanel : MonoBehaviour {
    [SerializeField] private PanelPopupTransition panelPopup;
    [Header("玩家信息")]
    [SerializeField] private TMP_Text usernameText;
    [SerializeField] private TMP_Text useridText;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private Image profileImage;
    [SerializeField] private Button copyUseridButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button friendActionButton;
    [SerializeField] private TMP_Text friendActionButtonText;
    [SerializeField] private Button changeTitleButton;
    [Header("国标段位")]
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private Slider rankProgressBar;
    [SerializeField] private TMP_Text rankScoreText;
    [SerializeField] private Button rankSwitchButton; // 预留段位切换，暂不绑定点击事件。
    [Header("规则与场次")]
    [SerializeField] private Button[] ruleButtons;
    [SerializeField] private TMP_Dropdown otherRulesDropdown;
    [SerializeField] private PlayerInfoStatistics statistics;
    [SerializeField] private GameObject modeToggleContainer;
    [SerializeField] private Button customModeButton;
    [SerializeField] private Button rankModeButton;
    [SerializeField] private Image rankedTabIndicator;
    [SerializeField] private Image customTabIndicator;
    [Header("最近记录")]
    [SerializeField] private RectTransform recentSection;
    [SerializeField] private RectTransform trendSection;
    [SerializeField] private RectTransform rulesSection;
    [SerializeField] private TMP_Text winLabel;
    [SerializeField] private PlayerInfoHandView handView;
    [SerializeField] private PlayerInfoTrendChart trendChart;

    private static readonly string[] PrimaryRules = { "guobiao", "riichi", "qingque" };
    private readonly List<RuleManifest> otherRules = new List<RuleManifest>();
    public static PlayerInfoPanel Instance { get; private set; }
    public string CurrentRule { get; private set; } = "guobiao";
    public bool ShowingRanked { get; private set; } = true;
    private string customRule = "guobiao";
    private int currentUserId;
    private string currentUsername;
    private bool shownOnce;
    private bool initialized;
    private string recentRequestId;
    private PlayerRecentRecordsResponse recentRecords;
    private TMP_Text emptyWinText;

    private void Awake() {
        Instance = this;
        InitializeControls();
    }

    private void InitializeControls() {
        if (initialized) return;
        initialized = true;
        // 番名和总番数共用标题右侧的一行，长列表适当缩字号，不挤占牌面及统计区域。
        var captionRect = winLabel.rectTransform;
        captionRect.anchorMin = new Vector2(0, 1);
        captionRect.anchorMax = new Vector2(1, 1);
        captionRect.pivot = new Vector2(.5f, 1);
        captionRect.anchoredPosition = new Vector2(76, 0);
        captionRect.sizeDelta = new Vector2(-152, 32);
        winLabel.textWrappingMode = TextWrappingModes.NoWrap;
        winLabel.fontSizeMax = winLabel.fontSize;
        winLabel.fontSizeMin = 14;
        winLabel.enableAutoSizing = true;
        if (panelPopup == null) panelPopup = GetComponent<PanelPopupTransition>();
        copyUseridButton.onClick.AddListener(CopyUserId);
        closeButton.onClick.AddListener(Close);
        friendActionButton.onClick.AddListener(OnFriendActionButtonClick);
        rankModeButton.onClick.AddListener(() => SelectCategory(true));
        customModeButton.onClick.AddListener(() => SelectCategory(false));
        for (int i = 0; i < ruleButtons.Length; i++) {
            int index = i;
            ruleButtons[i].onClick.AddListener(() => SelectRule(index));
        }
        otherRulesDropdown.onValueChanged.AddListener(SelectOtherRule);
        PopulateOtherRules();
    }

    private void PopulateOtherRules() {
        otherRules.Clear();
        var options = new List<TMP_Dropdown.OptionData>();
        foreach (var manifest in RuleRegistry.Ordered) {
            if (System.Array.IndexOf(PrimaryRules, manifest.RuleId) >= 0) continue;
            otherRules.Add(manifest);
            options.Add(new TMP_Dropdown.OptionData(manifest.LobbyName ?? manifest.DisplayName));
        }
        otherRulesDropdown.ClearOptions();
        otherRulesDropdown.AddOptions(options);
        otherRulesDropdown.SetValueWithoutNotify(-1);
    }

    private void Start() {
        if (!shownOnce) gameObject.SetActive(false);
    }

    private void OnDisable() => FriendRelationCache.OnChanged -= RefreshFriendActionButton;

    private void OnDestroy() {
        FriendRelationCache.OnChanged -= RefreshFriendActionButton;
        if (Instance == this) Instance = null;
    }

    public void ShowPlayerInfo(PlayerInfoResponse playerInfo, string loadedRule = null, RuleStatsResponse loadedStats = null) {
        if (playerInfo == null) return;
        InitializeControls(); // 兼容未激活面板先收到玩家信息的情况。
        Instance = this;
        ApplyPlayerInfo(playerInfo);
        recentRecords = null;
        recentRequestId = System.Guid.NewGuid().ToString("N");
        CurrentRule = customRule = "guobiao";
        ShowingRanked = true;
        statistics.ResetUser(currentUserId, loadedRule, loadedStats);
        RefreshRecords();
        DataNetworkManager.Instance?.GetPlayerRecentRecords(currentUserId, recentRequestId);
        FriendRelationCache.OnChanged -= RefreshFriendActionButton;
        FriendRelationCache.OnChanged += RefreshFriendActionButton;
        RefreshFriendActionButton();
        FriendNetworkManager.Instance?.ListFriends();
        shownOnce = true;
        if (panelPopup != null) panelPopup.Show();
        else gameObject.SetActive(true);
    }

    private void ApplyPlayerInfo(PlayerInfoResponse playerInfo) {
        currentUserId = playerInfo.user_id;
        var settings = playerInfo.user_settings;
        currentUsername = string.IsNullOrEmpty(settings?.username) ? "未知用户" : settings.username;
        usernameText.text = currentUsername;
        useridText.text = currentUserId.ToString();
        titleText.text = ConfigManager.GetTitleText(settings?.title_id ?? 0);
        profileImage.sprite = Resources.Load<Sprite>($"image/Profiles/{settings?.profile_image_id ?? 1}")
            ?? Resources.Load<Sprite>("image/Profiles/1");
        string rank = string.IsNullOrEmpty(playerInfo.guobiao_rank) ? "10级" : playerInfo.guobiao_rank;
        float score = RankLevelConfig.NormalizeScore(rank, playerInfo.guobiao_score);
        var (_, _, promoteScore) = RankConfig.RankTable[RankConfig.GetRankIndex(rank)];
        // 目前服务器只提供国标段位，切换规则时保留明确的规则名称。
        rankText.text = "国标 · " + rank;
        rankProgressBar.value = promoteScore > 0 ? Mathf.Clamp01(score / promoteScore) : 0;
        rankScoreText.text = $"{score:F2}/{promoteScore}";
    }

    public void SelectRule(int index) {
        if (index < 0 || index >= PrimaryRules.Length) return;
        SelectRule(PrimaryRules[index]);
    }

    public void SelectRule(string rule) {
        if (ShowingRanked && rule != "guobiao") return;
        if (RuleRegistry.Resolve(rule, rule) == null) return;
        CurrentRule = rule;
        if (!ShowingRanked) customRule = rule;
        RefreshRecords();
    }

    private void SelectOtherRule(int index) {
        if (index >= 0 && index < otherRules.Count) SelectRule(otherRules[index].RuleId);
    }

    public void SelectCategory(bool ranked) {
        if (ShowingRanked == ranked) return;
        otherRulesDropdown.Hide();
        ShowingRanked = ranked;
        CurrentRule = ranked ? "guobiao" : customRule;
        RefreshRecords();
    }

    private void RefreshRecords() {
        modeToggleContainer.SetActive(true);
        SetCategorySelected(rankModeButton, rankedTabIndicator, ShowingRanked);
        SetCategorySelected(customModeButton, customTabIndicator, !ShowingRanked);
        for (int i = 0; i < ruleButtons.Length; i++) {
            ruleButtons[i].gameObject.SetActive(!ShowingRanked || i == 0);
            SetTabSelected(ruleButtons[i], ruleButtons[i].GetComponentInChildren<TMP_Text>(true), PrimaryRules[i] == CurrentRule);
        }
        otherRulesDropdown.gameObject.SetActive(!ShowingRanked);
        int otherIndex = otherRules.FindIndex(r => r.RuleId == CurrentRule);
        otherRulesDropdown.SetValueWithoutNotify(otherIndex);
        SetTabSelected(otherRulesDropdown, otherRulesDropdown.captionText, otherIndex >= 0);
        statistics.Show(CurrentRule, ShowingRanked, currentUserId);
        rulesSection.anchoredPosition = new Vector2(24, -212);
        recentSection.anchoredPosition = new Vector2(24, -276);
        RefreshRecentRecords();
    }

    private void RefreshRecentRecords() {
        PlayerRecentCategory category = null;
        if (recentRecords?.rules != null && recentRecords.rules.TryGetValue(CurrentRule, out var rule))
            rule.TryGetValue(ShowingRanked ? "match" : "custom", out category);
        var win = CurrentRule == "guobiao" ? category?.big_win : null;
        winLabel.text = PlayerInfoStatsFormatter.WinCaption(win);
        handView.SetHand(win?.concealed_tiles, win?.melds);
        if (win == null && emptyWinText == null) {
            // 沿用番数文字的字体和颜色，空状态放在牌面区域内。
            emptyWinText = Instantiate(winLabel, handView.transform, false);
            emptyWinText.name = "NoRecentWin";
            emptyWinText.text = "无最近大和";
            emptyWinText.enableAutoSizing = false;
            emptyWinText.fontSize = winLabel.fontSizeMax;
            emptyWinText.alignment = TextAlignmentOptions.MidlineLeft;
            emptyWinText.raycastTarget = false;
            var rect = emptyWinText.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        if (emptyWinText != null) emptyWinText.gameObject.SetActive(win == null);
        var placements = new List<int>();
        if (category?.placements != null) {
            foreach (var entry in category.placements)
                if (entry != null && entry.rank >= 1 && entry.rank <= 4) placements.Add(entry.rank);
        }
        trendChart.SetPlacements(placements.ToArray());
    }

    public void OnRecentRecordsReceived(bool success, string message, PlayerRecentRecordsResponse response) {
        // 同一玩家重新打开也使用新请求标识，迟到回复不能覆盖这次面板。
        if (response == null || response.user_id != currentUserId || response.request_id != recentRequestId) return;
        recentRecords = success ? response : null;
        RefreshRecentRecords();
        if (!success && gameObject.activeInHierarchy)
            NotificationManager.Instance?.ShowTip("获取数据", false, message ?? "获取玩家近期记录失败");
    }

    private static readonly Color TabIdleFill = new Color(.215f, .235f, .285f);
    private static readonly Color TabIdleText = new Color(.94f, .95f, .98f);
    private static readonly Color TabSelectedText = new Color(.12f, .14f, .18f);

    private void SetCategorySelected(Button button, Image indicator, bool selected) {
        var colors = button.colors;
        colors.normalColor = colors.selectedColor = Color.clear;
        colors.highlightedColor = new Color(1, 1, 1, .035f);
        colors.pressedColor = new Color(1, 1, 1, .02f);
        button.colors = colors;
        button.interactable = true;
        button.GetComponentInChildren<TMP_Text>(true).color = selected
            ? copyUseridButton.colors.normalColor : new Color(.79f, .83f, .9f);
        indicator.gameObject.SetActive(selected);
    }

    private void SetTabSelected(Selectable control, TMP_Text label, bool selected) {
        var colors = control.colors;
        colors.normalColor = colors.selectedColor = selected ? copyUseridButton.colors.normalColor : TabIdleFill;
        colors.highlightedColor = Color.Lerp(colors.normalColor, Color.white, .08f);
        colors.pressedColor = Color.Lerp(colors.normalColor, Color.black, .1f);
        control.colors = colors;
        control.interactable = true;
        label.color = selected ? TabSelectedText : TabIdleText;
    }

    private void CopyUserId() {
        GUIUtility.systemCopyBuffer = currentUserId.ToString();
        NotificationManager.Instance?.ShowTip("复制", true, "已复制用户ID");
    }

    private void Close() {
        if (panelPopup != null) panelPopup.Hide();
        else gameObject.SetActive(false);
    }

    private void RefreshFriendActionButton() {
        bool self = UserDataManager.Instance != null && currentUserId == UserDataManager.Instance.UserId;
        friendActionButton.gameObject.SetActive(!self);
        changeTitleButton.gameObject.SetActive(self);
        friendActionButton.interactable = !self;
        friendActionButtonText.text = FriendRelationCache.IsFriend(currentUserId) ? "移除好友" : "添加好友";
    }

    private void OnFriendActionButtonClick() {
        if (UserDataManager.Instance != null && currentUserId == UserDataManager.Instance.UserId) return;
        if (FriendRelationCache.IsFriend(currentUserId)) {
            FriendPanel.Instance?.ShowDeleteFriendConfirm(currentUserId, currentUsername);
        } else {
            FriendNetworkManager.Instance?.RequestFriend(currentUserId);
        }
    }

    public void OnGuobiaoStatsReceived(bool success, string message, RuleStatsResponse stats) => statistics.Receive("guobiao", success, message, stats);
    public void OnRiichiStatsReceived(bool success, string message, RuleStatsResponse stats) => statistics.Receive("riichi", success, message, stats);
    public void OnQingqueStatsReceived(bool success, string message, RuleStatsResponse stats) => statistics.Receive("qingque", success, message, stats);
    public void OnClassicalStatsReceived(bool success, string message, RuleStatsResponse stats) => statistics.Receive("classical", success, message, stats);
    public void OnJiandanStatsReceived(bool success, string message, RuleStatsResponse stats) => statistics.Receive("jiandan", success, message, stats);
}
