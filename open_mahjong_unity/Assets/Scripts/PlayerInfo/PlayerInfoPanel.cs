using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;


public class PlayerInfoPanel : MonoBehaviour
{
    // 用户信息
    [SerializeField] private TMP_Text usernameText; // 用户名
    [SerializeField] private TMP_Text useridText; // 用户ID
    [SerializeField] private TMP_Text titleText; // 头衔
    [SerializeField] private Image profileImage; // 头像
    [SerializeField] private Button copyUseridButton; // 复制用户ID按钮

    // 切换规则按钮
    [SerializeField] private Button ShowGBRuleButtom;
    [SerializeField] private Button ShowJPRuleButtom;
    [SerializeField] private Button ShowOtherRuleButtom;

    [SerializeField] private Transform RecordEntryContainer;
    [SerializeField] private GameObject PlayerInfoEntryPrefab;
    [SerializeField] private GameObject PlayerInfoDataTextPrefab;
    [SerializeField] private GameObject PlayerInfoDataLayoutGroupPrefab;

    private string CurrentShowRule = "GB";
    private PlayerStatsInfo[] GBstats;
    private PlayerStatsInfo[] JPstats;
    private PlayerStatsInfo[] Otherstats;
    
    // 保存每个规则的汇总统计数据
    private PlayerStatsInfo GBTotalStats;
    private PlayerStatsInfo JPTotalStats;
    private PlayerStatsInfo OtherTotalStats;

    // Start is called before the first frame update
    void Start()
    {
        copyUseridButton.onClick.AddListener(OnCopyUseridButtonClick);

        ShowGBRuleButtom.onClick.AddListener(() => OnSwitchRuleButtonClick("GB"));
        ShowJPRuleButtom.onClick.AddListener(() => OnSwitchRuleButtonClick("JP"));
        ShowOtherRuleButtom.onClick.AddListener(() => OnSwitchRuleButtonClick("Other"));
    }


    // 显示玩家信息
    public void ShowPlayerInfo(PlayerInfoResponse playerInfo)
    {
        if (playerInfo == null)
        {
            Debug.LogError("PlayerInfoResponse 为 null");
            return;
        }

        // 显示用户名和用户ID
        usernameText.text = playerInfo.user_settings.username ?? "未知用户";
        useridText.text = playerInfo.user_id.ToString();
        titleText.text = playerInfo.user_settings.title_id.ToString();
        profileImage.sprite = Resources.Load<Sprite>($"image/Profiles/{playerInfo.user_settings.profile_image_id}");


        // 保存规则数据
        GBstats = playerInfo.gb_stats;
        JPstats = playerInfo.jp_stats;
        Otherstats = null;

        // 保存汇总数据
        if (GBstats != null && GBstats.Length > 0){
            GBTotalStats = CreateTotalStats(GBstats, "GB");
        }
        if (JPstats != null && JPstats.Length > 0){
            JPTotalStats = CreateTotalStats(JPstats, "JP");
        }
        if (Otherstats != null && Otherstats.Length > 0){
            OtherTotalStats = CreateTotalStats(Otherstats, "Other");
        }

        // 显示当前规则的统计数据
        OnSwitchRuleButtonClick(CurrentShowRule);
    }

    // 切换规则
    private void OnSwitchRuleButtonClick(string rule)
    {
        CurrentShowRule = rule;
        
        // 清空容器
        ClearRecordEntryContainer();
        
        PlayerStatsInfo[] statsToShow = null;
        if (rule == "GB" && GBstats != null){
            statsToShow = GBstats;
        }
        else if (rule == "JP" && JPstats != null){
            statsToShow = JPstats;
        }
        else if (rule == "Other" && Otherstats != null){
            statsToShow = Otherstats;
        }
        else{}
        
        // 创建汇总统计数据
        PlayerStatsInfo totalStats = CreateTotalStats(statsToShow, rule);
        
        // 在头部添加汇总条目
        GameObject totalEntryObject = Instantiate(PlayerInfoEntryPrefab, RecordEntryContainer);
        PlayerInfoEntry totalEntry = totalEntryObject.GetComponent<PlayerInfoEntry>();
        totalEntry.SetPlayerInfoEntry("total", this, totalStats);

        // 显示分支模式条目
        foreach (var stat in statsToShow)
        {
            GameObject playerInfoEntryObject = Instantiate(PlayerInfoEntryPrefab, RecordEntryContainer);
            PlayerInfoEntry playerInfoEntry = playerInfoEntryObject.GetComponent<PlayerInfoEntry>();
            playerInfoEntry.SetPlayerInfoEntry("mode", this, stat);
        }

        // 在尾部添加番种条目
        GameObject fanStatsEntryObject = Instantiate(PlayerInfoEntryPrefab, RecordEntryContainer);
        PlayerInfoEntry fanStatsEntry = fanStatsEntryObject.GetComponent<PlayerInfoEntry>();
        fanStatsEntry.SetPlayerInfoEntry("fanStats", this, totalStats);

    }
    
    // 清空容器
    private void ClearRecordEntryContainer()
    {
        foreach (Transform child in RecordEntryContainer)
        {
            Destroy(child.gameObject);
        }
    }
    
    // 创建汇总统计数据（将所有模式的番数相加）
    private PlayerStatsInfo CreateTotalStats(PlayerStatsInfo[] statsArray, string rule)
    {
        PlayerStatsInfo totalStats = new PlayerStatsInfo
        {
            rule = rule,
            mode = "总计",
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
            fan_stats = new Dictionary<string, int>()
        };
        
        // 汇总所有模式的基础统计数据
        foreach (var stat in statsArray)
        {
            if (stat == null) continue;
            
            totalStats.total_games = (totalStats.total_games ?? 0) + (stat.total_games ?? 0);
            totalStats.total_rounds = (totalStats.total_rounds ?? 0) + (stat.total_rounds ?? 0);
            totalStats.win_count = (totalStats.win_count ?? 0) + (stat.win_count ?? 0);
            totalStats.self_draw_count = (totalStats.self_draw_count ?? 0) + (stat.self_draw_count ?? 0);
            totalStats.deal_in_count = (totalStats.deal_in_count ?? 0) + (stat.deal_in_count ?? 0);
            totalStats.total_fan_score = (totalStats.total_fan_score ?? 0) + (stat.total_fan_score ?? 0);
            totalStats.total_win_turn = (totalStats.total_win_turn ?? 0) + (stat.total_win_turn ?? 0);
            totalStats.total_fangchong_score = (totalStats.total_fangchong_score ?? 0) + (stat.total_fangchong_score ?? 0);
            totalStats.first_place_count = (totalStats.first_place_count ?? 0) + (stat.first_place_count ?? 0);
            totalStats.second_place_count = (totalStats.second_place_count ?? 0) + (stat.second_place_count ?? 0);
            totalStats.third_place_count = (totalStats.third_place_count ?? 0) + (stat.third_place_count ?? 0);
            totalStats.fourth_place_count = (totalStats.fourth_place_count ?? 0) + (stat.fourth_place_count ?? 0);
            
            // 汇总番种统计数据
            if (stat.fan_stats != null)
            {
                foreach (var fanPair in stat.fan_stats)
                {
                    if (totalStats.fan_stats.ContainsKey(fanPair.Key))
                    {
                        totalStats.fan_stats[fanPair.Key] += fanPair.Value;
                    }
                    else
                    {
                        totalStats.fan_stats[fanPair.Key] = fanPair.Value;
                    }
                }
            }
        }
        
        return totalStats;
    }

    // 复制 Userid
    private void OnCopyUseridButtonClick(){
        TextEditor textEditor = new TextEditor();
        textEditor.text = useridText.text;
        textEditor.SelectAll();
        textEditor.Copy();
    }

    // 显示数据
    public void ShowStatsData(string statsCase, PlayerStatsInfo playerStatsInfo, Transform entryTransform)
    {
        if (playerStatsInfo == null || entryTransform == null)
        {
            Debug.LogError("ShowStatsData: playerStatsInfo 或 entryTransform 为 null");
            return;
        }

        // 检查是否已经存在数据布局组（避免重复创建）
        int entryIndex = entryTransform.GetSiblingIndex();
        if (entryIndex + 1 < RecordEntryContainer.childCount)
        {
            Transform nextChild = RecordEntryContainer.GetChild(entryIndex + 1);
            Debug.Log("entryIndex: " + entryIndex);
            Debug.Log("nextChild: " + nextChild.name);
            // 如果下一个子物体已经是数据布局组，则删除而不是创建
            if (nextChild.name.Contains("DataLayoutGroup"))
            {
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
        if (statsCase == "total" || statsCase == "mode")
        {
            // 显示对局统计
            ShowGameStats(layoutGroupTransform, playerStatsInfo);
        }
        else if (statsCase == "fanStats")
        {
            // 显示番数统计
            ShowFanStats(layoutGroupTransform, playerStatsInfo);
        }
    }

    // 显示对局统计数据
    private void ShowGameStats(Transform parent, PlayerStatsInfo stats)
    {
        if (stats == null) return;

        // 对局统计字段列表
        List<KeyValuePair<string, int?>> gameStatsList = new List<KeyValuePair<string, int?>>
        {
            new KeyValuePair<string, int?>("总对局数", stats.total_games),
            new KeyValuePair<string, int?>("累计回合数", stats.total_rounds),
            new KeyValuePair<string, int?>("和牌次数", stats.win_count),
            new KeyValuePair<string, int?>("自摸次数", stats.self_draw_count),
            new KeyValuePair<string, int?>("放铳次数", stats.deal_in_count),
            new KeyValuePair<string, int?>("累计番数", stats.total_fan_score),
            new KeyValuePair<string, int?>("累计和巡", stats.total_win_turn),
            new KeyValuePair<string, int?>("累计放铳分", stats.total_fangchong_score),
            new KeyValuePair<string, int?>("一位次数", stats.first_place_count),
            new KeyValuePair<string, int?>("二位次数", stats.second_place_count),
            new KeyValuePair<string, int?>("三位次数", stats.third_place_count),
            new KeyValuePair<string, int?>("四位次数", stats.fourth_place_count)
        };

        // 为每个统计项创建文本预制体
        foreach (var statPair in gameStatsList)
        {
            if (statPair.Value.HasValue)
            {
                CreateDataText(parent, statPair.Key, statPair.Value.Value.ToString());
            }
        }
    }

    // 显示番数统计数据
    private void ShowFanStats(Transform parent, PlayerStatsInfo stats)
    {
        if (stats == null || stats.fan_stats == null || stats.fan_stats.Count == 0)
        {
            CreateDataText(parent, "暂无番种数据", "");
            return;
        }

        // 为每个番种创建文本预制体
        foreach (var fanPair in stats.fan_stats)
        {
            CreateDataText(parent, fanPair.Key, fanPair.Value.ToString());
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
