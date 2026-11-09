using System.Collections.Generic;
using System.Linq;
using Salasasa.DataLab;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayerInfoPanel : MonoBehaviour {
    [SerializeField] private PanelPopupTransition panelPopup;
    [SerializeField] private PlayerInfoAppearance appearance;
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
    [Header("规则评分")]
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private Slider rankProgressBar;
    [SerializeField] private TMP_Text rankScoreText;
    [SerializeField] private Button rankSwitchButton;
    [Header("规则与场次")]
    [SerializeField] private Button[] ruleButtons;
    [SerializeField] private TMP_Dropdown otherRulesDropdown;
    [SerializeField] private TMP_Dropdown mainRulesDropdown;
    [SerializeField] private TMP_Dropdown subRulesDropdown;
    [SerializeField] private TextAsset ruleCatalog;
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

    private static readonly string[] PrimaryRules = PlayerInfoRuleCatalog.PrimaryRules;
    private readonly List<KeyValuePair<string, string>> otherRules = new List<KeyValuePair<string, string>>();
    [System.NonSerialized] private List<DataRule> customRules;
    [System.NonSerialized] private string[] mainRuleKeys;
    [System.NonSerialized] private List<DataRule> subRules;
    public static PlayerInfoPanel Instance { get; private set; }
    public string CurrentRule { get; private set; } = "guobiao";
    public bool ShowingRanked { get; private set; } = true;
    private string customRule = "guobiao";
    private int currentUserId;
    public int CurrentUserId => currentUserId;
    public bool IsOpen => panelPopup ? panelPopup.IsOpen : gameObject.activeInHierarchy;
    private int currentProfileImageId = 1;
    private int currentAvatarFrameId;
    private string currentUsername;
    private int currentTitleId;
    private bool missingUsername;
    private bool shownOnce;
    private bool initialized;
    private string recentRequestId;
    private PlayerRecentRecordsResponse recentRecords;
    [SerializeField] private TMP_Text emptyWinText;
    private Dictionary<string,RuleRating> ratings;
    [SerializeField] private GameObject rankPolicyButton;
    private string rankRule="guobiao";
    public void CycleRankRule() => SelectRule(RankedRules.Next(rankRule));

    private void RefreshRating() {
        var rating = RankedRules.Get(ratings, rankRule);
        bool grade = RankedRules.IsGrade(rankRule);
        // 沿用原版三行段位区，短规则名给段位和分数保留完整的显示宽度。
        string name = RankedRules.Name(rankRule).Replace("麻将", "");
        rankText.text = name + " · " + (grade ? rating.rank_name : "Elo");
        rankScoreText.text = RankedRules.ScoreCaption(rating);
        rankProgressBar.gameObject.SetActive(grade);
        rankProgressBar.value = RankedRules.Progress(rating);
    }

    private void Awake() {
        Instance = this;
        InitializeControls();
    }

    private void InitializeControls() {
        if (initialized) return;
        initialized = true;
        rankSwitchButton.onClick.AddListener(CycleRankRule);
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
        mainRulesDropdown.onValueChanged.AddListener(SelectMainRule);
        subRulesDropdown.onValueChanged.AddListener(SelectSubRule);
        RefreshRuleControls();
    }

    public void RefreshRuleControls() {
        if (customRules == null) {
            customRules = DataRule.Read(ruleCatalog.text).Where(r => PlayerInfoRuleCatalog.Supports(r.key)).ToList();
            string[] first = { "guobiao", "riichi", "sichuan", "qingque", "hongque" };
            var families = customRules.Select(r => r.family).Distinct().ToArray();
            mainRuleKeys = first.Where(families.Contains).Concat(families.Where(f => !first.Contains(f))).ToArray();
        }
        otherRules.Clear();
        otherRules.AddRange(PlayerInfoRuleCatalog.OtherRules(true));
        var options = new List<TMP_Dropdown.OptionData> { new TMP_Dropdown.OptionData(PlatformText.Get("其他规则")) };
        foreach (var rule in otherRules) {
            string label = rule.Key switch {
                "riichi_sanma" => "立直三人",
                "sichuan_xueliu_exchange" => "血流换三",
                "hongque" => "虹雀",
                _ => rule.Value
            };
            options.Add(new TMP_Dropdown.OptionData(PlatformText.Get(label)));
        }
        otherRulesDropdown.ClearOptions();
        otherRulesDropdown.AddOptions(options);
        otherRulesDropdown.SetValueWithoutNotify(otherRules.FindIndex(r => r.Key == CurrentRule) + 1);

        string family = PlayerInfoRuleCatalog.SourceRule(CurrentRule);
        mainRulesDropdown.ClearOptions();
        mainRulesDropdown.AddOptions(mainRuleKeys.Select(key => PlatformText.Get(customRules.First(r => r.family == key).familyLabel)).ToList());
        mainRulesDropdown.SetValueWithoutNotify(System.Array.IndexOf(mainRuleKeys, family));
        subRules = customRules.Where(r => r.family == family).ToList();
        subRulesDropdown.ClearOptions();
        subRulesDropdown.AddOptions(subRules.Select(r => PlatformText.Get(r.subRuleLabel)).ToList());
        subRulesDropdown.SetValueWithoutNotify(subRules.FindIndex(r => r.key == CurrentRule));

        for (int i = 0; i < ruleButtons.Length; i++) {
            ruleButtons[i].gameObject.SetActive(ShowingRanked || PrimaryRules[i] != "qingque");
            var rect = (RectTransform)ruleButtons[i].transform;
            rect.anchoredPosition = new Vector2(!ShowingRanked && PrimaryRules[i] == "sichuan" ? 256 : i * 128, 0);
            rect.sizeDelta = new Vector2(ShowingRanked && PrimaryRules[i] == "sichuan" ? 192 : 120, 44);
        }
        otherRulesDropdown.gameObject.SetActive(ShowingRanked);
        mainRulesDropdown.gameObject.SetActive(!ShowingRanked);
        subRulesDropdown.gameObject.SetActive(!ShowingRanked);
    }

    private void SelectMainRule(int index) {
        if (index < 0 || index >= mainRuleKeys.Length) return;
        SelectRule(customRules.First(r => r.family == mainRuleKeys[index]).key);
    }

    private void SelectSubRule(int index) {
        if (index >= 0 && index < subRules.Count) SelectRule(subRules[index].key);
    }

    private void HideRuleDropdowns() {
        otherRulesDropdown.Hide();
        if (mainRulesDropdown) mainRulesDropdown.Hide();
        if (subRulesDropdown) subRulesDropdown.Hide();
    }

    private void Start() {
        if (!shownOnce) gameObject.SetActive(false);
    }

    private void OnEnable() {
        GameSettings.AppearanceChanged += RefreshAppearance;
        ConfigManager.OnLanguageChanged += RefreshLanguage;
        RefreshAppearance(null);
    }

    private void OnDisable() {
        FriendRelationCache.OnChanged -= RefreshFriendActionButton;
        GameSettings.AppearanceChanged -= RefreshAppearance;
        ConfigManager.OnLanguageChanged -= RefreshLanguage;
    }

    private void OnDestroy() {
        FriendRelationCache.OnChanged -= RefreshFriendActionButton;
        GameSettings.AppearanceChanged -= RefreshAppearance;
        ConfigManager.OnLanguageChanged -= RefreshLanguage;
        if (Instance == this) Instance = null;
    }

    public void ShowRequestedPlayerInfo(PlayerInfoResponse playerInfo, string loadedRule = null, RuleStatsResponse loadedStats = null) {
        if (playerInfo != null && ProfileOnClick.AcceptPlayerInfoResponse(playerInfo.user_id))
            ShowPlayerInfo(playerInfo, loadedRule, loadedStats);
    }

    public void ShowPlayerInfo(PlayerInfoResponse playerInfo, string loadedRule = null, RuleStatsResponse loadedStats = null) {
        if (playerInfo == null) return;
        InitializeControls(); // 兼容未激活面板先收到玩家信息的情况。
        Instance = this;
        ApplyPlayerInfo(playerInfo);
        recentRecords = null;
        recentRequestId = System.Guid.NewGuid().ToString("N");
        CurrentRule = ProfileOnClick.RequestedRule;
        customRule = CurrentRule;
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
        missingUsername = string.IsNullOrEmpty(settings?.username);
        currentUsername = missingUsername ? "未知用户" : settings.username;
        usernameText.text = missingUsername ? PlatformText.Get(currentUsername) : currentUsername;
        useridText.text = currentUserId.ToString();
        currentTitleId = settings?.title_id ?? 0;
        titleText.text = ConfigManager.GetTitleText(currentTitleId);
        currentProfileImageId = settings?.profile_image_id ?? 1;
        currentAvatarFrameId = settings?.avatar_frame_id ?? 0;
        RefreshAppearance(null);
        ratings=playerInfo.ratings ?? new Dictionary<string,RuleRating>();
        if(!ratings.ContainsKey("guobiao"))ratings["guobiao"]=new RuleRating{rule="guobiao",system="grade",rank_name=playerInfo.guobiao_rank??"10级",rank_score=playerInfo.guobiao_score};
        rankRule=ProfileOnClick.RequestedRule;RefreshRating();
    }

    private void RefreshLanguage() {
        if (missingUsername) usernameText.text = PlatformText.Get("未知用户");
        titleText.text = ConfigManager.GetTitleText(currentTitleId);
        HideRuleDropdowns();
        RefreshRuleControls();
        RefreshDesign();
    }

    private void RefreshAppearance(InventoryAppearance appearance) {
        if (currentUserId == 0 || profileImage == null) return;
        if (appearance != null) {
            if (appearance.user_id != currentUserId) return;
            currentProfileImageId = appearance.profile_image_id;
            currentAvatarFrameId = appearance.avatar_frame_id;
        } else {
            // A hidden self profile may have missed equipment broadcasts.
            var user = UserDataManager.Instance;
            if (user != null && user.UserId == currentUserId && user.Inventory?.appearance != null) {
                currentProfileImageId = user.ProfileImageId;
                currentAvatarFrameId = user.AvatarFrameId;
            }
        }
        profileImage.sprite = ConfigManager.GetProfileSprite(currentProfileImageId);
        AvatarFrameGraphic.Apply(profileImage, currentAvatarFrameId);
    }

    public void SelectRule(int index) {
        if (index < 0 || index >= PrimaryRules.Length) return;
        SelectRule(PrimaryRules[index]);
    }

    public void SelectRule(string rule) {
        if (ShowingRanked && !RankedRules.Supports(rule)) return;
        if (!PlayerInfoRuleCatalog.Supports(rule)) return;
        CurrentRule = rule;
        if (!ShowingRanked) customRule = rule;
        RefreshRecords();
    }

    private void SelectOtherRule(int index) {
        if (index > 0 && index <= otherRules.Count) SelectRule(otherRules[index - 1].Key);
    }

    public void SelectCategory(bool ranked) {
        if (ShowingRanked == ranked) return;
        HideRuleDropdowns();
        ShowingRanked = ranked;
        CurrentRule = ranked ? rankRule : customRule;
        RefreshRecords();
    }

    private void RefreshRecords() {
        if (ShowingRanked && RankedRules.Supports(CurrentRule)) rankRule = CurrentRule;
        RefreshRating();
        modeToggleContainer.SetActive(true);
        RefreshRuleControls();
        RefreshDesign();
        rankPolicyButton?.SetActive(false);
        statistics.Show(CurrentRule, ShowingRanked, currentUserId);
        RefreshRecentRecords();
    }

    private void RefreshRecentRecords() {
        PlayerRecentCategory category = null;
        if (recentRecords?.rules != null) {
            if (recentRecords.rules.TryGetValue(CurrentRule, out var rule))
                rule.TryGetValue(ShowingRanked ? "match" : "custom", out category);
        }
        var win = CurrentRule == "guobiao" ? category?.big_win : null;
        winLabel.text = PlayerInfoStatsFormatter.WinCaption(win);
        handView.SetRuleHand(CurrentRule, win?.concealed_tiles, win?.melds);
        if (emptyWinText != null) emptyWinText.gameObject.SetActive(win == null);
        var placements = new List<int>();
        int participants = PlayerInfoRuleCatalog.PlayerCount(CurrentRule);
        if (category?.placements != null) {
            foreach (var entry in category.placements)
                if (entry != null && entry.rank >= 1 && entry.rank <= participants) placements.Add(entry.rank);
        }
        trendChart.SetPlacements(placements.ToArray(), participants);
    }

    public void OnRecentRecordsReceived(bool success, string message, PlayerRecentRecordsResponse response) {
        // 同一玩家重新打开也使用新请求标识，迟到回复不能覆盖这次面板。
        if (response == null || response.user_id != currentUserId || response.request_id != recentRequestId) return;
        recentRecords = success ? response : null;
        RefreshRecentRecords();
        if (!success && gameObject.activeInHierarchy)
            NotificationManager.Instance?.ShowTip("获取数据", false, message ?? "获取玩家近期记录失败");
    }

    public void RefreshDesign() {
        if (!appearance) return;
        HideRuleDropdowns();
        appearance.PaintCategory(rankModeButton, rankModeButton.GetComponentInChildren<TMP_Text>(true), rankedTabIndicator, ShowingRanked);
        appearance.PaintCategory(customModeButton, customModeButton.GetComponentInChildren<TMP_Text>(true), customTabIndicator, !ShowingRanked);
        for (int i = 0; i < ruleButtons.Length; i++)
            SetTabSelected(ruleButtons[i], ruleButtons[i].GetComponentInChildren<TMP_Text>(true), PrimaryRules[i] == CurrentRule);
        SetTabSelected(otherRulesDropdown, otherRulesDropdown.captionText, otherRules.FindIndex(r => r.Key == CurrentRule) >= 0);
        if (mainRulesDropdown) SetTabSelected(mainRulesDropdown, mainRulesDropdown.captionText, false);
        if (subRulesDropdown) SetTabSelected(subRulesDropdown, subRulesDropdown.captionText, false);
    }

    private void SetTabSelected(Selectable control, TMP_Text label, bool selected) {
        if (!appearance) return;
        appearance.PaintTab(control, label, selected);
        if (control is TMP_Dropdown dropdown) {
            var arrow = dropdown.transform.Find("Arrow");
            if (arrow != null)
                foreach (var image in arrow.GetComponentsInChildren<Image>(true))
                    if (image.name.Contains("Stroke")) image.color = label.color;
        }
    }

    private void CopyUserId() {
        GUIUtility.systemCopyBuffer = currentUserId.ToString();
        NotificationManager.Instance?.ShowTip("复制", true, "已复制用户ID");
    }

    public void Close() {
        ProfileOnClick.CancelPlayerInfo(currentUserId);
        if (panelPopup != null) panelPopup.Hide();
        else gameObject.SetActive(false);
    }

    public void OnRankedStatsReceived(Response response) {
        statistics.Receive(response.rating_rule+"_rank",response.success,response.message,response.rule_stats);
    }

    public void OnRuleStatsReceived(Response response) => statistics.ReceiveFiltered(response);

    private void RefreshFriendActionButton() {
        bool self = UserDataManager.Instance != null && currentUserId == UserDataManager.Instance.UserId;
        friendActionButton.gameObject.SetActive(!self);
        // Title equipment is intentionally accessible only through the chat command.
        changeTitleButton.gameObject.SetActive(false);
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
}
