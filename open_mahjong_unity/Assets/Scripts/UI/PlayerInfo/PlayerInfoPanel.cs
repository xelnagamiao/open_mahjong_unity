using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;


public class PlayerInfoPanel : MonoBehaviour{
    // 番种英文到中文的翻译字典
    private static Dictionary<string, string> fanTranslationDict = new Dictionary<string, string>{
        {"dasixi", "大四喜"},
        {"dasanyuan", "大三元"},
        {"lvyise", "绿一色"},
        {"jiulianbaodeng", "九莲宝灯"},
        {"sigang", "四杠"},
        {"sangang", "三杠"},
        {"lianqidui", "连七对"},
        {"shisanyao", "十三幺"},
        {"qingyaojiu", "清幺九"},
        {"xiaosixi", "小四喜"},
        {"xiaosanyuan", "小三元"},
        {"ziyise", "字一色"},
        {"sianke", "四暗刻"},
        {"yiseshuanglonghui", "一色双龙会"},
        {"yisesitongshun", "一色四同顺"},
        {"yisesijiegao", "一色四节高"},
        {"yisesibugao", "一色四步高"},
        {"hunyaojiu", "混幺九"},
        {"qiduizi", "七对子"},
        {"qixingbukao", "七星不靠"},
        {"quanshuangke", "全双刻"},
        {"qingyise", "清一色"},
        {"yisesantongshun", "一色三同顺"},
        {"yisesanjiegao", "一色三节高"},
        {"quanda", "全大"},
        {"quanzhong", "全中"},
        {"quanxiao", "全小"},
        {"qinglong", "清龙"},
        {"sanseshuanglonghui", "三色双龙会"},
        {"yisesanbugao", "一色三步高"},
        {"quandaiwu", "全带五"},
        {"santongke", "三同刻"},
        {"sananke", "三暗刻"},
        {"quanbukao", "全不靠"},
        {"zuhelong", "组合龙"},
        {"dayuwu", "大于五"},
        {"xiaoyuwu", "小于五"},
        {"sanfengke", "三风刻"},
        {"hualong", "花龙"},
        {"tuibudao", "推不倒"},
        {"sansesantongshun", "三色三同顺"},
        {"sansesanjiegao", "三色三节高"},
        {"wufanhe", "无番和"},
        {"miaoshouhuichun", "妙手回春"},
        {"haidilaoyue", "海底捞月"},
        {"gangshangkaihua", "杠上开花"},
        {"qiangganghe", "抢杠和"},
        {"pengpenghe", "碰碰和"},
        {"hunyise", "混一色"},
        {"sansesanbugao", "三色三步高"},
        {"wumenqi", "五门齐"},
        {"quanqiuren", "全求人"},
        {"shuangangang", "双暗杠"},
        {"shuangjianke", "双箭刻"},
        {"quandaiyao", "全带幺"},
        {"buqiuren", "不求人"},
        {"shuangminggang", "双明杠"},
        {"hejuezhang", "和绝张"},
        {"jianke", "箭刻"},
        {"quanfengke", "圈风刻"},
        {"menfengke", "门风刻"},
        {"menqianqing", "门前清"},
        {"pinghe", "平和"},
        {"siguiyi", "四归一"},
        {"shuangtongke", "双同刻"},
        {"shuanganke", "双暗刻"},
        {"angang", "暗杠"},
        {"duanyao", "断幺"},
        {"yibangao", "一般高"},
        {"xixiangfeng", "喜相逢"},
        {"lianliu", "连六"},
        {"laoshaofu", "老少副"},
        {"yaojiuke", "幺九刻"},
        {"minggang", "明杠"},
        {"queyimen", "缺一门"},
        {"wuzi", "无字"},
        {"bianzhang", "边张"},
        {"qianzhang", "嵌张"},
        {"dandiaojiang", "单钓将"},
        {"zimo", "自摸"},
        {"huapai", "花牌"},
        {"mingangang", "明暗杠"}
    };

    // 青雀番种英文到中文的翻译字典
    private static Dictionary<string, string> qingqueFanTranslationDict = new Dictionary<string, string>{
        {"hepai", "和牌"}, {"tianhe", "天和"}, {"dihe", "地和"}, {"lingshangkaihua", "岭上开花"},
        {"haidilaoyue", "海底捞月"}, {"hedilaoyue", "河底捞鱼"}, {"qianggang", "抢杠"},
        {"qidui", "七对"}, {"menqianqing", "门前清"}, {"siangang", "四暗杠"},
        {"sanangang", "三暗杠"}, {"shuangangang", "双暗杠"}, {"angang", "暗杠"},
        {"sigang", "四杠"}, {"sangang", "三杠"}, {"shuanggang", "双杠"},
        {"sianke", "四暗刻"}, {"sananke", "三暗刻"}, {"duiduihe", "对对和"},
        {"shiergui", "十二归"}, {"bagui", "八归"}, {"sandiedui", "三叠对"},
        {"erdiedui", "二叠对"}, {"diedui", "叠对"}, {"ziyise", "字一色"},
        {"dasixi", "大四喜"}, {"xiaosixi", "小四喜"}, {"sixidui", "四喜对"},
        {"fengpaisanke", "风牌三刻"}, {"fengpaiqidui", "风牌七对"}, {"fengpailiudui", "风牌六对"},
        {"fengpaiwudui", "风牌五对"}, {"fengpaisidui", "风牌四对"}, {"dasanyuan", "大三元"},
        {"xiaosanyuan", "小三元"}, {"sanyuanliudui", "三元六对"}, {"sanyuandui", "三元对"},
        {"fanpaisike", "番牌四刻"}, {"fanpaisanke", "番牌三刻"}, {"fanpaierke", "番牌二刻"},
        {"fanpaike", "番牌刻"}, {"fanpaiqidui", "番牌七对"}, {"fanpailiudui", "番牌六对"},
        {"fanpaiwudui", "番牌五对"}, {"fanpaisifu", "番牌四副"}, {"fanpaisanfu", "番牌三副"},
        {"fanpaierfu", "番牌二副"}, {"fanpai", "番牌"}, {"qingyaojiu", "清幺九"},
        {"hunyaojiu", "混幺九"}, {"qingdaiyao", "清带幺"}, {"hundaiyao", "混带幺"},
        {"jiulianbaodeng", "九莲宝灯"}, {"qingyise", "清一色"}, {"hunyise", "混一色"},
        {"wumenqi", "五门齐"}, {"hunyishu", "混一数"}, {"ershu", "二数"},
        {"erju", "二聚"}, {"sanju", "三聚"}, {"siju", "四聚"},
        {"lianshu", "连数"}, {"jianshu", "间数"}, {"jingshu", "镜数"},
        {"yingshu", "映数"}, {"mantingfang", "满庭芳"}, {"sitongshun", "四同顺"},
        {"santongshun", "三同顺"}, {"erbangao", "二般高"}, {"yibangao", "一般高"},
        {"silianke", "四连刻"}, {"sanlianke", "三连刻"}, {"sibugao", "四步高"},
        {"sanbugao", "三步高"}, {"silianhuan", "四连环"}, {"sanlianhuan", "三连环"},
        {"yiqiguantong", "一气贯通"}, {"qiliandui", "七连对"}, {"liuliandui", "六连对"},
        {"wuliandui", "五连对"}, {"siliandui", "四连对"}, {"sansetongke", "三色同刻"},
        {"sansetongshun", "三色同顺"}, {"sanseedui", "三色二对"}, {"sansetongdui", "三色同对"},
        {"sanselianke", "三色连刻"}, {"sanseguantong", "三色贯通"}, {"jingtong", "镜同"},
        {"jingtongsandui", "镜同三对"}, {"jingtongerdui", "镜同二对"}, {"shuanglonghui", "双龙会"}
    };

    // 用户信息
    [SerializeField] private TMP_Text usernameText; // 用户名
    [SerializeField] private TMP_Text useridText; // 用户ID
    [SerializeField] private TMP_Text titleText; // 头衔
    [SerializeField] private Image profileImage; // 头像
    [SerializeField] private Button copyUseridButton; // 复制用户ID按钮
    [SerializeField] private Button closeButton; // 关闭按钮

    // 切换规则按钮
    [SerializeField] private Button ShowGBRuleButtom;
    [SerializeField] private Button ShowJPRuleButtom;
    [SerializeField] private Button ShowOtherRuleButtom;

    [SerializeField] private Transform RecordEntryContainer;
    [SerializeField] private GameObject PlayerInfoEntryPrefab;
    [SerializeField] private GameObject PlayerInfoDataTextPrefab;
    [SerializeField] private GameObject PlayerInfoDataLayoutGroupPrefab;

    public static PlayerInfoPanel Instance { get; private set; }
    
    private string CurrentShowRule = "guobiao";
    private int currentUserId; // 当前显示的用户ID
    private PlayerStatsInfo[] guobiaoStats;
    private PlayerStatsInfo[] riichiStats;
    private PlayerStatsInfo[] qingqueStats;
    private PlayerStatsInfo[] classicalStats;
    
    // 保存汇总番种统计数据（由服务器返回）
    private Dictionary<string, int> guobiaoTotalFanStats;
    private Dictionary<string, int> riichiTotalFanStats;
    private Dictionary<string, int> qingqueTotalFanStats;
    private Dictionary<string, int> classicalTotalFanStats;

    // Start is called before the first frame update
    void Start(){
        Instance = this;
        copyUseridButton.onClick.AddListener(OnCopyUseridButtonClick);
        
        if (closeButton != null){
            closeButton.onClick.AddListener(OnCloseButtonClick);
        }

        ShowGBRuleButtom.onClick.AddListener(() => OnSwitchRuleButtonClick("guobiao"));
        ShowJPRuleButtom.onClick.AddListener(() => OnSwitchRuleButtonClick("riichi"));
        ShowOtherRuleButtom.onClick.AddListener(() => OnSwitchRuleButtonClick("Other"));

        gameObject.SetActive(false);
    }

    void OnDestroy(){
        if (Instance == this){
            Instance = null;
        }
    }


    // 显示玩家信息（仅显示用户信息，默认加载国标数据）
    public void ShowPlayerInfo(PlayerInfoResponse playerInfo){
        if (playerInfo == null){
            Debug.LogError("PlayerInfoResponse 为 null");
            return;
        }

        // 保存当前用户ID
        currentUserId = playerInfo.user_id;

        // 显示用户名和用户ID
        usernameText.text = playerInfo.user_settings.username ?? "未知用户";
        useridText.text = playerInfo.user_id.ToString();
        titleText.text = ConfigManager.GetTitleText(playerInfo.user_settings.title_id);
        
        profileImage.sprite = Resources.Load<Sprite>($"image/Profiles/{playerInfo.user_settings.profile_image_id}");

        // 清空之前的数据
        guobiaoStats = null;
        riichiStats = null;
        qingqueStats = null;
        classicalStats = null;
        guobiaoTotalFanStats = null;
        riichiTotalFanStats = null;
        qingqueTotalFanStats = null;
        classicalTotalFanStats = null;

        // 默认加载国标数据
        CurrentShowRule = "guobiao";
        ClearRecordEntryContainer();
        DataNetworkManager.Instance?.GetGuobiaoStats(currentUserId.ToString());
    }

    // 接收国标统计数据
    public void OnGuobiaoStatsReceived(bool success, string message, RuleStatsResponse ruleStats){
        if (!success || ruleStats == null){
            NotificationManager.Instance.ShowTip("获取数据", false, message ?? "获取国标统计数据失败");
            return;
        }

        // 确保面板处于显示状态
        if (!gameObject.activeSelf){
            gameObject.SetActive(true);
        }

        // 保存国标数据
        guobiaoStats = ruleStats.history_stats ?? new PlayerStatsInfo[0];
        guobiaoTotalFanStats = ruleStats.total_fan_stats;

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

        // 确保面板处于显示状态
        if (!gameObject.activeSelf){
            gameObject.SetActive(true);
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

        if (!gameObject.activeSelf){
            gameObject.SetActive(true);
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

        if (!gameObject.activeSelf) {
            gameObject.SetActive(true);
        }

        classicalStats = ruleStats.history_stats ?? new PlayerStatsInfo[0];
        classicalTotalFanStats = ruleStats.total_fan_stats;

        if (CurrentShowRule == "Other") {
            RefreshCurrentRuleDisplay();
        }
    }

    // 切换规则
    private void OnSwitchRuleButtonClick(string rule){
        CurrentShowRule = rule;
        
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
            // 国标麻将：显示4个固定模式
            string[] guobiaoModes = {"4/4", "3/4", "2/4", "1/4"};
            
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
                        fourth_place_count = 0
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
                        fourth_place_count = 0
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
                        fourth_place_count = 0
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
                        fourth_place_count = 0
                    };
                }

                GameObject classicalEntryObject = Instantiate(PlayerInfoEntryPrefab, RecordEntryContainer);
                PlayerInfoEntry classicalEntry = classicalEntryObject.GetComponent<PlayerInfoEntry>();
                classicalEntry.SetPlayerInfoEntry("mode", this, stat);
            }
        }

        // 在尾部显示番数总计（如果存在）
        if (totalFanStats != null) {
            // Other 标签实际对应青雀规则
            string fanStatsRule = CurrentShowRule == "Other" ? "qingque" : CurrentShowRule;
            PlayerStatsInfo fanStatsInfo = new PlayerStatsInfo{
                rule = fanStatsRule,
                mode = "总计",
                fan_stats = totalFanStats
            };
            GameObject fanStatsEntryObject = Instantiate(PlayerInfoEntryPrefab, RecordEntryContainer);
            PlayerInfoEntry fanStatsEntry = fanStatsEntryObject.GetComponent<PlayerInfoEntry>();
            fanStatsEntry.SetPlayerInfoEntry("fanStats", this, fanStatsInfo);
        }
    }
    
    // 清空容器
    private void ClearRecordEntryContainer(){
        foreach (Transform child in RecordEntryContainer){
            Destroy(child.gameObject);
                    }
                }
    

    // 复制 Userid
    private void OnCopyUseridButtonClick(){
        TextEditor textEditor = new TextEditor();
        textEditor.text = useridText.text;
        textEditor.SelectAll();
        textEditor.Copy();
    }

    // 关闭按钮点击事件
    private void OnCloseButtonClick(){
        // 仅隐藏面板，不销毁对象，便于下次再次显示
        gameObject.SetActive(false);
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

        // 和牌率（和牌次数 / 总小局数）
        if (stats.win_count.HasValue && totalRounds > 0){
            float winRate = (float)stats.win_count.Value / totalRounds.Value * 100f;
            gameStatsList.Add(new KeyValuePair<string, string>("和牌率", $"{winRate:F2}%"));
        }
        else{
            gameStatsList.Add(new KeyValuePair<string, string>("和牌率", "0.00%"));
        }

        // 自摸率（自摸次数 / 总小局数）
        if (stats.self_draw_count.HasValue && totalRounds > 0){
            float selfDrawRate = (float)stats.self_draw_count.Value / totalRounds.Value * 100f;
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
        Dictionary<string, string> currentFanDict = fanTranslationDict;
        if (stats?.rule == "qingque"){
            currentFanDict = qingqueFanTranslationDict;
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
