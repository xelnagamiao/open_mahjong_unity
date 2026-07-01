using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;


public class PlayerInfoPanel : MonoBehaviour {
    [SerializeField] private PanelPopupTransition panelPopup;

    // 用户信息
    [SerializeField] private TMP_Text usernameText;
    [SerializeField] private TMP_Text useridText;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private Image profileImage;
    [SerializeField] private Button copyUseridButton;
    [SerializeField] private Button closeButton;

    [Header("好友操作")]
    [SerializeField] private Button friendActionButton;
    [SerializeField] private TMP_Text friendActionButtonText;

    [Header("段位信息")]
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private Slider rankProgressBar;
    [SerializeField] private TMP_Text rankScoreText;

    // 切换规则按钮
    [SerializeField] private Button ShowGBRuleButtom;
    [SerializeField] private Button ShowJPRuleButtom;
    [SerializeField] private Button ShowOtherRuleButtom;

    [Header("国标场次切换（仅国标显示）")]
    [SerializeField] private GameObject modeToggleContainer;   // 容纳 天梯/自定义 两个按钮的容器
    [SerializeField] private Button customModeButton;          // 自定义对局
    [SerializeField] private Button rankModeButton;            // 天梯对局

    [SerializeField] private Transform RecordEntryContainer;
    [SerializeField] private GameObject PlayerInfoEntryPrefab;
    [SerializeField] private GameObject PlayerInfoDataTextPrefab;
    [SerializeField] private GameObject PlayerInfoDataLayoutGroupPrefab;

    public static PlayerInfoPanel Instance { get; private set; }

    private string CurrentShowRule = "guobiao";
    private string currentGuobiaoCategory = "rank"; // 国标场次分类：custom=自定义 / rank=天梯（默认天梯）
    private int currentUserId; // 当前显示的用户ID
    private PlayerStatsInfo[] guobiaoStats;
    private PlayerStatsInfo[] riichiStats;
    private PlayerStatsInfo[] qingqueStats;
    private PlayerStatsInfo[] classicalStats;
    
    // 保存汇总番种统计数据（由服务器返回）
    private Dictionary<string, int> guobiaoTotalFanStats;
    private Dictionary<string, int> guobiaoRankedFanStats;
    private Dictionary<string, int> riichiTotalFanStats;
    private Dictionary<string, int> qingqueTotalFanStats;
    private Dictionary<string, int> classicalTotalFanStats;

    private bool _shownOnce;
    private string _currentUsername;

    private void Awake() {
        Instance = this;
        if (panelPopup == null) {
            panelPopup = GetComponent<PanelPopupTransition>();
        }
        copyUseridButton.onClick.AddListener(OnCopyUseridButtonClick);
        if (closeButton != null) {
            closeButton.onClick.AddListener(OnCloseButtonClick);
        }
        if (friendActionButton != null) {
            friendActionButton.onClick.AddListener(OnFriendActionButtonClick);
        }
        ShowGBRuleButtom.onClick.AddListener(() => OnSwitchRuleButtonClick("guobiao"));
        ShowJPRuleButtom.onClick.AddListener(() => OnSwitchRuleButtonClick("riichi"));
        ShowOtherRuleButtom.onClick.AddListener(() => OnSwitchRuleButtonClick("Other"));
        if (customModeButton != null) {
            customModeButton.onClick.AddListener(() => OnSwitchGuobiaoCategoryButtonClick("custom"));
        }
        if (rankModeButton != null) {
            rankModeButton.onClick.AddListener(() => OnSwitchGuobiaoCategoryButtonClick("rank"));
        }
        // 脚本挂载时即设定默认选中态（天梯），避免 prefab 初始按钮态与默认分类不一致
        UpdateModeToggleSelection();
    }

    private void Start() {
        if (!_shownOnce) {
            gameObject.SetActive(false);
        }
    }

    private void OnDestroy() {
        UnsubscribeFriendCache();
        if (Instance == this) {
            Instance = null;
        }
    }


    // 显示玩家信息（仅显示用户信息，默认加载国标数据）
    public void ShowPlayerInfo(PlayerInfoResponse playerInfo){
        if (playerInfo == null){
            Debug.LogError("PlayerInfoResponse 为 null");
            return;
        }

        UnsubscribeFriendCache();
        currentUserId = playerInfo.user_id;
        _currentUsername = playerInfo.user_settings.username ?? "未知用户";

        // 显示用户名和用户ID
        usernameText.text = _currentUsername;
        useridText.text = playerInfo.user_id.ToString();
        titleText.text = ConfigManager.GetTitleText(playerInfo.user_settings.title_id);
        
        profileImage.sprite = Resources.Load<Sprite>($"image/Profiles/{playerInfo.user_settings.profile_image_id}");

        // 段位信息
        string rank = playerInfo.guobiao_rank ?? "10级";
        float score = playerInfo.guobiao_score;
        int idx = RankConfig.GetRankIndex(rank);
        var (_, startScore, promoteScore) = RankConfig.RankTable[idx];
        if (rankText != null) rankText.text = rank;
        if (rankProgressBar != null) {
            float range = promoteScore - startScore;
            rankProgressBar.value = range > 0 ? (score - startScore) / range : 0;
        }
        if (rankScoreText != null) rankScoreText.text = $"{score:F1}/{promoteScore}";

        // 清空之前的数据
        guobiaoStats = null;
        riichiStats = null;
        qingqueStats = null;
        classicalStats = null;
        guobiaoTotalFanStats = null;
        riichiTotalFanStats = null;
        qingqueTotalFanStats = null;
        classicalTotalFanStats = null;
        guobiaoRankedFanStats = null;

        // 默认加载国标数据
        CurrentShowRule = "guobiao";
        currentGuobiaoCategory = "rank";
        UpdateModeToggleVisibility();
        ClearRecordEntryContainer();
        DataNetworkManager.Instance?.GetGuobiaoStats(currentUserId.ToString());
        FriendRelationCache.OnChanged += RefreshFriendActionButton;
        RefreshFriendActionButton();
        FriendNetworkManager.Instance?.ListFriends();
        _shownOnce = true;
        if (panelPopup != null) {
            panelPopup.Show();
        } else {
            gameObject.SetActive(true);
        }
    }

    private void RefreshFriendActionButton() {
        if (friendActionButton == null) return;
        if (currentUserId == UserDataManager.Instance.UserId) {
            friendActionButton.gameObject.SetActive(false);
            return;
        }
        friendActionButton.gameObject.SetActive(true);
        bool isFriend = FriendRelationCache.IsFriend(currentUserId);
        if (friendActionButtonText != null) {
            friendActionButtonText.text = isFriend ? "移除好友" : "添加好友";
        }
    }

    private void OnFriendActionButtonClick() {
        if (currentUserId == UserDataManager.Instance.UserId) return;
        if (FriendRelationCache.IsFriend(currentUserId)) {
            FriendPanel.Instance?.ShowDeleteFriendConfirm(currentUserId, _currentUsername);
            return;
        }
        FriendNetworkManager.Instance?.RequestFriend(currentUserId);
    }

    private void UnsubscribeFriendCache() {
        FriendRelationCache.OnChanged -= RefreshFriendActionButton;
    }

    // 接收国标统计数据
    public void OnGuobiaoStatsReceived(bool success, string message, RuleStatsResponse ruleStats){
        if (!success || ruleStats == null){
            NotificationManager.Instance.ShowTip("获取数据", false, message ?? "获取国标统计数据失败");
            return;
        }

        // 保存国标数据
        guobiaoStats = ruleStats.history_stats ?? new PlayerStatsInfo[0];
        guobiaoTotalFanStats = ruleStats.total_fan_stats;
        guobiaoRankedFanStats = ruleStats.ranked_fan_stats;

        // 如果当前显示的是国标，则刷新显示
        if (CurrentShowRule == "guobiao"){
            RefreshCurrentRuleDisplay();
        }
    }

    // 接收立直统计数据
    public void OnRiichiStatsReceived(bool success, string message, RuleStatsResponse ruleStats){
        if (!success || ruleStats == null){
            NotificationManager.Instance.ShowTip("获取数据", false, message ?? "获取立直统计数据失败");
            return;
        }

        // 保存立直数据
        riichiStats = ruleStats.history_stats ?? new PlayerStatsInfo[0];
        riichiTotalFanStats = ruleStats.total_fan_stats;

        // 如果当前显示的是立直，则刷新显示
        if (CurrentShowRule == "riichi"){
            RefreshCurrentRuleDisplay();
        }
    }

    // 接收青雀统计数据
    public void OnQingqueStatsReceived(bool success, string message, RuleStatsResponse ruleStats){
        if (!success || ruleStats == null){
            NotificationManager.Instance.ShowTip("获取数据", false, message ?? "获取青雀统计数据失败");
            return;
        }

        qingqueStats = ruleStats.history_stats ?? new PlayerStatsInfo[0];
        qingqueTotalFanStats = ruleStats.total_fan_stats;

        if (CurrentShowRule == "Other"){
            RefreshCurrentRuleDisplay();
        }
    }

    // 接收古典麻将统计数据
    public void OnClassicalStatsReceived(bool success, string message, RuleStatsResponse ruleStats) {
        if (!success || ruleStats == null) {
            NotificationManager.Instance.ShowTip("获取数据", false, message ?? "获取古典麻将统计数据失败");
            return;
        }

        classicalStats = ruleStats.history_stats ?? new PlayerStatsInfo[0];
        classicalTotalFanStats = ruleStats.total_fan_stats;

        if (CurrentShowRule == "Other") {
            RefreshCurrentRuleDisplay();
        }
    }

    // 切换国标场次分类（自定义 / 天梯）
    private void OnSwitchGuobiaoCategoryButtonClick(string category){
        if (CurrentShowRule != "guobiao") return;
        if (category == currentGuobiaoCategory) return;
        currentGuobiaoCategory = category;
        UpdateModeToggleSelection();
        RefreshCurrentRuleDisplay();
    }

    // 仅国标显示 天梯/自定义 切换容器
    private void UpdateModeToggleVisibility(){
        bool show = (CurrentShowRule == "guobiao") && (modeToggleContainer != null);
        if (modeToggleContainer != null){
            modeToggleContainer.SetActive(show);
        }
        UpdateModeToggleSelection();
    }

    // 更新两个按钮的选中态（选中者置灰）
    private void UpdateModeToggleSelection(){
        if (customModeButton != null) customModeButton.interactable = (currentGuobiaoCategory != "custom");
        if (rankModeButton != null) rankModeButton.interactable = (currentGuobiaoCategory != "rank");
    }

    // 切换规则
    private void OnSwitchRuleButtonClick(string rule){
        CurrentShowRule = rule;
        UpdateModeToggleVisibility();
        
        // 如果数据不存在（null 或未初始化），则请求数据
        if (rule == "guobiao" && guobiaoStats == null){
            DataNetworkManager.Instance?.GetGuobiaoStats(currentUserId.ToString());
            return;
        }
        else if (rule == "riichi" && riichiStats == null){
            DataNetworkManager.Instance?.GetRiichiStats(currentUserId.ToString());
            return;
        }
        else if (rule == "Other" && (qingqueStats == null || classicalStats == null)){
            if (qingqueStats == null) DataNetworkManager.Instance?.GetQingqueStats(currentUserId.ToString());
            if (classicalStats == null) DataNetworkManager.Instance?.GetClassicalStats(currentUserId.ToString());
            return;
        }
        
        // 刷新显示（即使数据为空数组也会显示所有模式）
        RefreshCurrentRuleDisplay();
    }

    // 刷新当前规则的显示
    private void RefreshCurrentRuleDisplay(){
        // 清空容器
        ClearRecordEntryContainer();
        
        Dictionary<string, int> totalFanStats = null;
        
        if (CurrentShowRule == "guobiao"){
            // 国标麻将：4 种局制，按场次分类（自定义=4/4 等，天梯=4/4_rank 等）
            bool isRank = (currentGuobiaoCategory == "rank");
            string suffix = isRank ? "_rank" : "";
            string[] guobiaoModes = {
                "4/4" + suffix,
                "3/4" + suffix,
                "2/4" + suffix,
                "1/4" + suffix
            };
            
            // 创建字典以便快速查找统计数据
            Dictionary<string, PlayerStatsInfo> statsDict = new Dictionary<string, PlayerStatsInfo>();
            if (guobiaoStats != null){
                foreach (var stat in guobiaoStats){
                    if (stat?.mode != null){
                        statsDict[stat.mode] = stat;
        }
                }
            }
            
            // 按固定顺序显示所有模式
            for (int i = 0; i < guobiaoModes.Length; i++){
                string mode = guobiaoModes[i];
                statsDict.TryGetValue(mode, out PlayerStatsInfo stat);
        
                // 如果没有数据，创建空数据
                if (stat == null){
                    stat = new PlayerStatsInfo{
                        rule = "guobiao",
                        mode = mode,
                        total_games = 0,
                        total_rounds = 0,
                        win_count = 0,
                        self_draw_count = 0,
                        deal_in_count = 0,
                        total_fan_score = 0,
                        total_win_turn = 0,
                        total_fangchong_score = 0,
                        first_place_count = 0,
                        second_place_count = 0,
                        third_place_count = 0,
                        fourth_place_count = 0,
                        fulu_round_count = 0,
                        cuohe_count = 0,
                        total_round_score = 0
                    };
                }
                
            GameObject playerInfoEntryObject = Instantiate(PlayerInfoEntryPrefab, RecordEntryContainer);
            PlayerInfoEntry playerInfoEntry = playerInfoEntryObject.GetComponent<PlayerInfoEntry>();
            playerInfoEntry.SetPlayerInfoEntry("mode", this, stat);
        }

            totalFanStats = guobiaoTotalFanStats;
        }
        else if (CurrentShowRule == "riichi"){
            // 立直麻将：显示2个固定模式
            string[] riichiModes = {"2/4", "1/4"};
            
            // 创建字典以便快速查找统计数据
            Dictionary<string, PlayerStatsInfo> statsDict = new Dictionary<string, PlayerStatsInfo>();
            if (riichiStats != null){
                foreach (var stat in riichiStats){
                    if (stat?.mode != null){
                        statsDict[stat.mode] = stat;
        }
                }
    }
    
            // 按固定顺序显示所有模式
            for (int i = 0; i < riichiModes.Length; i++){
                string mode = riichiModes[i];
                statsDict.TryGetValue(mode, out PlayerStatsInfo stat);
                
                // 如果没有数据，创建空数据
                if (stat == null){
                    stat = new PlayerStatsInfo{
                        rule = "riichi",
                        mode = mode,
            total_games = 0,
            total_rounds = 0,
            win_count = 0,
            self_draw_count = 0,
            deal_in_count = 0,
            total_fan_score = 0,
            total_win_turn = 0,
            total_fangchong_score = 0,
            first_place_count = 0,
            second_place_count = 0,
            third_place_count = 0,
                        fourth_place_count = 0,
                        fulu_round_count = 0
                    };
                }
                
                GameObject playerInfoEntryObject = Instantiate(PlayerInfoEntryPrefab, RecordEntryContainer);
                PlayerInfoEntry playerInfoEntry = playerInfoEntryObject.GetComponent<PlayerInfoEntry>();
                playerInfoEntry.SetPlayerInfoEntry("mode", this, stat);
            }
            
            totalFanStats = riichiTotalFanStats;
        }
        else if (CurrentShowRule == "Other"){
            // 青雀麻将：显示4个固定模式
            string[] qingqueModes = {"4/4", "3/4", "2/4", "1/4"};
            
            Dictionary<string, PlayerStatsInfo> statsDict = new Dictionary<string, PlayerStatsInfo>();
            if (qingqueStats != null){
                foreach (var stat in qingqueStats){
                    if (stat?.mode != null){
                        statsDict[stat.mode] = stat;
                    }
                }
            }
            
            for (int i = 0; i < qingqueModes.Length; i++){
                string mode = qingqueModes[i];
                statsDict.TryGetValue(mode, out PlayerStatsInfo stat);
                
                if (stat == null){
                    stat = new PlayerStatsInfo{
                        rule = "qingque",
                        mode = mode,
                        total_games = 0,
                        total_rounds = 0,
                        win_count = 0,
                        self_draw_count = 0,
                        deal_in_count = 0,
                        total_fan_score = 0,
                        total_win_turn = 0,
                        total_fangchong_score = 0,
                        first_place_count = 0,
                        second_place_count = 0,
                        third_place_count = 0,
                        fourth_place_count = 0,
                        fulu_round_count = 0
                    };
                }
                
                GameObject playerInfoEntryObject = Instantiate(PlayerInfoEntryPrefab, RecordEntryContainer);
                PlayerInfoEntry playerInfoEntry = playerInfoEntryObject.GetComponent<PlayerInfoEntry>();
                playerInfoEntry.SetPlayerInfoEntry("mode", this, stat);
            }

            totalFanStats = qingqueTotalFanStats;

            // 古典麻将数据（同在 Other 标签下）
            string[] classicalModes = {"4/4", "3/4", "2/4", "1/4"};

            Dictionary<string, PlayerStatsInfo> classicalStatsDict = new Dictionary<string, PlayerStatsInfo>();
            if (classicalStats != null) {
                foreach (var stat in classicalStats) {
                    if (stat?.mode != null) {
                        classicalStatsDict[stat.mode] = stat;
                    }
                }
            }

            for (int i = 0; i < classicalModes.Length; i++) {
                string mode = classicalModes[i];
                classicalStatsDict.TryGetValue(mode, out PlayerStatsInfo stat);

                if (stat == null) {
                    stat = new PlayerStatsInfo {
                        rule = "classical",
                        mode = mode,
                        total_games = 0,
                        total_rounds = 0,
                        win_count = 0,
                        self_draw_count = 0,
                        deal_in_count = 0,
                        total_fan_score = 0,
                        total_win_turn = 0,
                        total_fangchong_score = 0,
                        first_place_count = 0,
                        second_place_count = 0,
                        third_place_count = 0,
                        fourth_place_count = 0,
                        fulu_round_count = 0
                    };
                }

                GameObject classicalEntryObject = Instantiate(PlayerInfoEntryPrefab, RecordEntryContainer);
                PlayerInfoEntry classicalEntry = classicalEntryObject.GetComponent<PlayerInfoEntry>();
                classicalEntry.SetPlayerInfoEntry("mode", this, stat);
            }
        }

        // 在尾部显示番数总计
        // Other 标签实际对应青雀规则
        string fanStatsRule = CurrentShowRule == "Other" ? "qingque" : CurrentShowRule;
        if (CurrentShowRule == "guobiao") {
            // 国标：番数总计跟随当前场次分类，标签带（天梯）/（自定义）
            Dictionary<string, int> guobiaoFan = (currentGuobiaoCategory == "rank")
                ? guobiaoRankedFanStats
                : guobiaoTotalFanStats;
            string fanLabel = (currentGuobiaoCategory == "rank")
                ? "国标番数总计（天梯）"
                : "国标番数总计（自定义）";
            if (guobiaoFan != null) {
                AppendFanStatsEntry(fanStatsRule, fanLabel, guobiaoFan);
            }
        }
        else if (totalFanStats != null) {
            AppendFanStatsEntry(fanStatsRule, null, totalFanStats);
        }
    }

    // 追加一行番种统计条目（label 为 null 时使用规则默认标签，如"国标番数总计"）
    private void AppendFanStatsEntry(string rule, string label, Dictionary<string, int> fanStats) {
        PlayerStatsInfo fanStatsInfo = new PlayerStatsInfo{
            rule = rule,
            mode = label,
            fan_stats = fanStats
        };
        GameObject fanStatsEntryObject = Instantiate(PlayerInfoEntryPrefab, RecordEntryContainer);
        PlayerInfoEntry fanStatsEntry = fanStatsEntryObject.GetComponent<PlayerInfoEntry>();
        fanStatsEntry.SetPlayerInfoEntry("fanStats", this, fanStatsInfo);
    }
    
    // 清空容器
    private void ClearRecordEntryContainer(){
        foreach (Transform child in RecordEntryContainer){
            Destroy(child.gameObject);
                    }
                }
    

    // 复制 Userid
    private void OnCopyUseridButtonClick(){
        ClipboardUtility.Copy(useridText.text);
        NotificationManager.Instance.ShowTip("用户", true, $"已复制用户ID: {useridText.text}");
    }

    private void OnCloseButtonClick() {
        UnsubscribeFriendCache();
        if (panelPopup != null) {
            panelPopup.Hide();
        } else {
            gameObject.SetActive(false);
        }
    }

    // 显示数据
    public void ShowStatsData(string statsCase, PlayerStatsInfo playerStatsInfo, Transform entryTransform){
        if (playerStatsInfo == null || entryTransform == null){
            return;
        }

        // 检查是否已经存在数据布局组（避免重复创建）
        int entryIndex = entryTransform.GetSiblingIndex();
        if (entryIndex + 1 < RecordEntryContainer.childCount){
            Transform nextChild = RecordEntryContainer.GetChild(entryIndex + 1);
            // 如果下一个子物体已经是数据布局组，则删除而不是创建
            if (nextChild != null && nextChild.name.Contains("DataLayoutGroup")){
                Destroy(nextChild.gameObject);
                return;
            }
        }

        // 在条目下方创建数据布局组
        GameObject layoutGroupObject = Instantiate(PlayerInfoDataLayoutGroupPrefab, RecordEntryContainer);
        layoutGroupObject.transform.SetSiblingIndex(entryIndex + 1);
        
        // 获取布局组的 Transform（作为父物体）
        Transform layoutGroupTransform = layoutGroupObject.transform;
        
        // 根据数据类型显示不同的内容
        if (statsCase == "mode"){
            // 显示对局统计
            ShowGameStats(layoutGroupTransform, playerStatsInfo);
        }
        else if (statsCase == "fanStats"){
            // 显示番数统计
            ShowFanStats(layoutGroupTransform, playerStatsInfo);
        }
    }

    // 显示对局统计数据
    private void ShowGameStats(Transform parent, PlayerStatsInfo stats){
        if (stats == null || parent == null) return;

        int? totalGames = stats.total_games ?? 0;
        int? totalRounds = stats.total_rounds ?? 0;
        int? winCount = stats.win_count ?? 0;
        int? dealInCount = stats.deal_in_count ?? 0;

        // 对局统计字段列表（包含计算后的率）
        List<KeyValuePair<string, string>> gameStatsList = new List<KeyValuePair<string, string>>();

        // 总对局数
        gameStatsList.Add(new KeyValuePair<string, string>("总对局数", stats.total_games?.ToString() ?? "0"));

        // 累计回合数
        gameStatsList.Add(new KeyValuePair<string, string>("累计回合数", stats.total_rounds?.ToString() ?? "0"));

        // 平均顺位（1~4 位加权平均）
        if (totalGames > 0) {
            int rankWeighted = (stats.first_place_count ?? 0) * 1
                + (stats.second_place_count ?? 0) * 2
                + (stats.third_place_count ?? 0) * 3
                + (stats.fourth_place_count ?? 0) * 4;
            float avgRank = (float)rankWeighted / totalGames.Value;
            gameStatsList.Add(new KeyValuePair<string, string>("平均顺位", $"{avgRank:F2}"));
        }
        else {
            gameStatsList.Add(new KeyValuePair<string, string>("平均顺位", "0.00"));
        }

        // 副露率（副露局数 / 累计回合数）
        int? fuluRounds = stats.fulu_round_count ?? 0;
        if (totalRounds > 0) {
            float fuluRate = (float)fuluRounds.Value / totalRounds.Value * 100f;
            gameStatsList.Add(new KeyValuePair<string, string>("副露率", $"{fuluRate:F2}%"));
        }
        else {
            gameStatsList.Add(new KeyValuePair<string, string>("副露率", "0.00%"));
        }

        // 和牌率（和牌次数 / 总小局数）
        if (stats.win_count.HasValue && totalRounds > 0){
            float winRate = (float)stats.win_count.Value / totalRounds.Value * 100f;
            gameStatsList.Add(new KeyValuePair<string, string>("和牌率", $"{winRate:F2}%"));
        }
        else{
            gameStatsList.Add(new KeyValuePair<string, string>("和牌率", "0.00%"));
        }

        // 错和率（错和次数 / 小局数），仅国标
        if (stats.rule == "guobiao") {
            int? cuoheCount = stats.cuohe_count ?? 0;
            if (totalRounds > 0) {
                float cuoheRate = (float)cuoheCount.Value / totalRounds.Value * 100f;
                gameStatsList.Add(new KeyValuePair<string, string>("错和率", $"{cuoheRate:F2}%"));
            }
            else {
                gameStatsList.Add(new KeyValuePair<string, string>("错和率", "0.00%"));
            }
        }

        // 自摸率（自摸次数 / 和牌次数）
        if (stats.self_draw_count.HasValue && winCount > 0){
            float selfDrawRate = (float)stats.self_draw_count.Value / winCount.Value * 100f;
            gameStatsList.Add(new KeyValuePair<string, string>("自摸率", $"{selfDrawRate:F2}%"));
        }
        else{
            gameStatsList.Add(new KeyValuePair<string, string>("自摸率", "0.00%"));
        }

        // 放铳率（放铳次数 / 总小局数）
        if (stats.deal_in_count.HasValue && totalRounds > 0){
            float dealInRate = (float)stats.deal_in_count.Value / totalRounds.Value * 100f;
            gameStatsList.Add(new KeyValuePair<string, string>("放铳率", $"{dealInRate:F2}%"));
        }
        else{
            gameStatsList.Add(new KeyValuePair<string, string>("放铳率", "0.00%"));
        }

        // 平均和番（累计番数 / 和牌次数）
        if (stats.total_fan_score.HasValue && winCount > 0){
            float avgFanScore = (float)stats.total_fan_score.Value / winCount.Value;
            gameStatsList.Add(new KeyValuePair<string, string>("平均和番", $"{avgFanScore:F2}"));
        }
        else{
            gameStatsList.Add(new KeyValuePair<string, string>("平均和番", "0.00"));
        }

        // 平均和巡（累计和巡 / 和牌次数）
        if (stats.total_win_turn.HasValue && winCount > 0){
            float avgWinTurn = (float)stats.total_win_turn.Value / winCount.Value;
            gameStatsList.Add(new KeyValuePair<string, string>("平均和巡", $"{avgWinTurn:F2}"));
        }
        else{
            gameStatsList.Add(new KeyValuePair<string, string>("平均和巡", "0.00"));
        }

        // 平均铳番（累计放铳分 / 放铳次数）
        if (stats.total_fangchong_score.HasValue && dealInCount > 0){
            float avgFangchongScore = (float)stats.total_fangchong_score.Value / dealInCount.Value;
            gameStatsList.Add(new KeyValuePair<string, string>("平均铳番", $"{avgFangchongScore:F2}"));
        }
        else{
            gameStatsList.Add(new KeyValuePair<string, string>("平均铳番", "0.00"));
        }

        // 局均点（累计小局净得分 / 对局数），仅国标，可为负；与 Web 端口径一致
        if (stats.rule == "guobiao") {
            int? totalRoundScore = stats.total_round_score ?? 0;
            if (totalGames > 0) {
                float avgRoundScore = (float)totalRoundScore.Value / totalGames.Value;
                gameStatsList.Add(new KeyValuePair<string, string>("局均点", $"{avgRoundScore:F2}"));
            }
            else {
                gameStatsList.Add(new KeyValuePair<string, string>("局均点", "0.00"));
            }
        }

        // 一位率（一位次数 / 总对局数）
        if (stats.first_place_count.HasValue && totalGames > 0){
            float firstPlaceRate = (float)stats.first_place_count.Value / totalGames.Value * 100f;
            gameStatsList.Add(new KeyValuePair<string, string>("一位率", $"{firstPlaceRate:F2}%"));
        }
        else{
            gameStatsList.Add(new KeyValuePair<string, string>("一位率", "0.00%"));
        }

        // 二位率（二位次数 / 总对局数）
        if (stats.second_place_count.HasValue && totalGames > 0){
            float secondPlaceRate = (float)stats.second_place_count.Value / totalGames.Value * 100f;
            gameStatsList.Add(new KeyValuePair<string, string>("二位率", $"{secondPlaceRate:F2}%"));
        }
        else{
            gameStatsList.Add(new KeyValuePair<string, string>("二位率", "0.00%"));
        }

        // 三位率（三位次数 / 总对局数）
        if (stats.third_place_count.HasValue && totalGames > 0){
            float thirdPlaceRate = (float)stats.third_place_count.Value / totalGames.Value * 100f;
            gameStatsList.Add(new KeyValuePair<string, string>("三位率", $"{thirdPlaceRate:F2}%"));
        }
        else{
            gameStatsList.Add(new KeyValuePair<string, string>("三位率", "0.00%"));
        }

        // 四位率（四位次数 / 总对局数）
        if (stats.fourth_place_count.HasValue && totalGames > 0){
            float fourthPlaceRate = (float)stats.fourth_place_count.Value / totalGames.Value * 100f;
            gameStatsList.Add(new KeyValuePair<string, string>("四位率", $"{fourthPlaceRate:F2}%"));
        }
        else{
            gameStatsList.Add(new KeyValuePair<string, string>("四位率", "0.00%"));
        }

        // 为每个统计项创建文本预制体
        foreach (var statPair in gameStatsList){
            CreateDataText(parent, statPair.Key, statPair.Value);
        }
    }

    // 显示番数统计数据
    private void ShowFanStats(Transform parent, PlayerStatsInfo stats){
        if (parent == null) return;
        
        // 根据规则选择对应的番种翻译字典
        Dictionary<string, string> currentFanDict = RankConfig.GuobiaoFanTranslation;
        if (stats?.rule == "qingque") {
            currentFanDict = RankConfig.QingqueFanTranslation;
        } else if (stats?.rule == "riichi") {
            currentFanDict = RankConfig.RiichiFanTranslation;
        }

        // 显示所有番种，包括值为0的
        foreach (var fanPair in currentFanDict){
            string fanName = fanPair.Value; // 中文名称
            int fanValue = 0;
            
            // 如果统计数据中有该番种，获取其值
            if (stats?.fan_stats != null && stats.fan_stats.ContainsKey(fanPair.Key)){
                fanValue = stats.fan_stats[fanPair.Key];
            }
            
            CreateDataText(parent, fanName, fanValue.ToString());
        }
    }

    // 创建数据文本预制体
    private void CreateDataText(Transform parent, string label, string value)
    {
        if (PlayerInfoDataTextPrefab == null)
        {
            Debug.LogError("PlayerInfoDataTextPrefab 未设置");
            return;
        }

        GameObject textObject = Instantiate(PlayerInfoDataTextPrefab, parent);
        TMP_Text textComponent = textObject.GetComponent<TMP_Text>();
        
        if (textComponent != null)
        {
            textComponent.text = $"{label}: {value}";
        }
        else
        {
            Debug.LogWarning($"PlayerInfoDataTextPrefab 上未找到 TMP_Text 组件");
        }
    }

}
