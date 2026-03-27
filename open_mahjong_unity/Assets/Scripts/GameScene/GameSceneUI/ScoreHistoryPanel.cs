using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class ScoreHistoryPanel : MonoBehaviour
{
    public static ScoreHistoryPanel Instance { get; private set; }
    [SerializeField] private GameObject Tmp_Text_Prefab;
    [SerializeField] private GameObject RoundIndexContainer;
    [SerializeField] private TMP_Text player0UserName;
    [SerializeField] private Transform player0RoundScoreContainer;
    [SerializeField] private Transform player0GameScoreContainer;
    [SerializeField] private TMP_Text player1UserName;
    [SerializeField] private Transform player1RoundScoreContainer;
    [SerializeField] private Transform player1GameScoreContainer;
    [SerializeField] private TMP_Text player2UserName;
    [SerializeField] private Transform player2RoundScoreContainer;
    [SerializeField] private Transform player2GameScoreContainer;
    [SerializeField] private TMP_Text player3UserName;
    [SerializeField] private Transform player3RoundScoreContainer;
    [SerializeField] private Transform player3GameScoreContainer;
    [SerializeField] private Button closeButton; // 关闭计分板按钮

    // 规则到局数标签字典的映射（统一使用 RoundTextDictionary）
    private static readonly Dictionary<string, Dictionary<int, string>> RuleToRoundMap = new Dictionary<string, Dictionary<int, string>> {
        { "guobiao", RoundTextDictionary.CurrentRoundTextGB },
        { "qingque", RoundTextDictionary.CurrentRoundTextQingque },
        { "riichi", RoundTextDictionary.CurrentRoundTextRiichi },
        { "classical", RoundTextDictionary.CurrentRoundTextClassical },
    };

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 关闭分数记录面板：
    /// - 依次清空各个 Container 的子元素（不删除 Container 本身）
    /// - 将自身 SetActive(false)
    /// </summary>
    public void Close()
    {
        if (RoundIndexContainer != null)
        {
            ClearContainer(RoundIndexContainer.transform);
        }

        ClearContainer(player0RoundScoreContainer);
        ClearContainer(player0GameScoreContainer);
        ClearContainer(player1RoundScoreContainer);
        ClearContainer(player1GameScoreContainer);
        ClearContainer(player2RoundScoreContainer);
        ClearContainer(player2GameScoreContainer);
        ClearContainer(player3RoundScoreContainer);
        ClearContainer(player3GameScoreContainer);

        gameObject.SetActive(false);
    }

    /// <summary>
    /// 更新分数记录显示。由调用方传入规则与 player_to_info（含 score_history、original_player_index）。
    /// </summary>
    /// <param name="rule">规则类型（如 guobiao、qingque）</param>
    /// <param name="player_to_info">四家玩家数据（可任意顺序，内部按 original_player_index 排序）</param>
    public void UpdateScoreRecord(string rule, IReadOnlyDictionary<string, PlayerInfoClass> player_to_info)
    {
        if (player_to_info == null || player_to_info.Count < 4) return;

        var sorted = new List<PlayerInfoClass>(player_to_info.Values);
        sorted.Sort((a, b) => a.original_player_index.CompareTo(b.original_player_index));

        InitializeScoreRecord(rule,
            sorted[0].original_player_index, sorted[0].username, sorted[0].score_history ?? new List<string>(),
            sorted[1].original_player_index, sorted[1].username, sorted[1].score_history ?? new List<string>(),
            sorted[2].original_player_index, sorted[2].username, sorted[2].score_history ?? new List<string>(),
            sorted[3].original_player_index, sorted[3].username, sorted[3].score_history ?? new List<string>());
    }

    /// <summary>
    /// 初始化分数记录显示
    /// </summary>
    /// <param name="rule">规则类型（如"GB"）</param>
    /// <param name="originIndex0">玩家0的原始索引</param>
    /// <param name="username0">玩家0的用户名</param>
    /// <param name="scoreHistory0">玩家0的分数历史</param>
    /// <param name="originIndex1">玩家1的原始索引</param>
    /// <param name="username1">玩家1的用户名</param>
    /// <param name="scoreHistory1">玩家1的分数历史</param>
    /// <param name="originIndex2">玩家2的原始索引</param>
    /// <param name="username2">玩家2的用户名</param>
    /// <param name="scoreHistory2">玩家2的分数历史</param>
    /// <param name="originIndex3">玩家3的原始索引</param>
    /// <param name="username3">玩家3的用户名</param>
    /// <param name="scoreHistory3">玩家3的分数历史</param>
    public void InitializeScoreRecord(
        string rule,
        int originIndex0, string username0, List<string> scoreHistory0,
        int originIndex1, string username1, List<string> scoreHistory1,
        int originIndex2, string username2, List<string> scoreHistory2,
        int originIndex3, string username3, List<string> scoreHistory3)
    {
        // 清空之前的局数索引容器
        if (RoundIndexContainer != null)
        {
            ClearContainer(RoundIndexContainer.transform);
        }
        
        // 子规则（如 guobiao/xiaolin、qingque/standard）映射为基础规则以查局数标签
        string baseRule = rule ?? "";
        foreach (var kv in RuleToRoundMap) {
            if (baseRule.StartsWith(kv.Key)) { baseRule = kv.Key; break; }
        }

        if (!RuleToRoundMap.TryGetValue(baseRule, out Dictionary<int, string> roundMap)) {
            Debug.LogError($"未知的规则类型: {rule}");
            return;
        }

        // 在RoundIndexContainer中生成局数标签文本（按 key 顺序 1~16）
        var sortedKeys = new List<int>(roundMap.Keys);
        sortedKeys.Sort();
        foreach (int key in sortedKeys) {
            GameObject textObj = Instantiate(Tmp_Text_Prefab, RoundIndexContainer.transform);
            TMP_Text text = textObj.GetComponent<TMP_Text>();
            if (text != null) {
                text.text = roundMap[key];
            }
        }
        
        // 按照originalIndex的顺序组织玩家数据（originIndex应该已经是从0-3排序的）
        var players = new List<(int originIndex, string username, List<string> scoreHistory, TMP_Text userNameText, Transform roundScoreContainer, Transform gameScoreContainer)>
        {
            (originIndex0, username0, scoreHistory0 ?? new List<string>(), player0UserName, player0RoundScoreContainer, player0GameScoreContainer),
            (originIndex1, username1, scoreHistory1 ?? new List<string>(), player1UserName, player1RoundScoreContainer, player1GameScoreContainer),
            (originIndex2, username2, scoreHistory2 ?? new List<string>(), player2UserName, player2RoundScoreContainer, player2GameScoreContainer),
            (originIndex3, username3, scoreHistory3 ?? new List<string>(), player3UserName, player3RoundScoreContainer, player3GameScoreContainer)
        };
        
        // 按照originIndex排序
        players.Sort((a, b) => a.originIndex.CompareTo(b.originIndex));
        
        // 为每个玩家设置数据
        foreach (var player in players)
        {
            // 设置用户名
            if (player.userNameText != null)
            {
                player.userNameText.text = player.username;
            }
            
            // 清空容器
            ClearContainer(player.roundScoreContainer);
            ClearContainer(player.gameScoreContainer);
            
            // 累计分数（用于GameScoreContainer）
            int cumulativeScore = 0;
            
            // 显示每局分数变化
            for (int i = 0; i < player.scoreHistory.Count; i++)
            {
                string scoreChange = player.scoreHistory[i];
                
                // 解析分数变化字符串（格式：+24、-08、0）
                int scoreValue = 0;
                if (scoreChange.StartsWith("+"))
                {
                    int.TryParse(scoreChange.Substring(1), out scoreValue);
                }
                else if (scoreChange.StartsWith("-"))
                {
                    int.TryParse(scoreChange.Substring(1), out scoreValue);
                    scoreValue = -scoreValue;
                }
                else
                {
                    int.TryParse(scoreChange, out scoreValue);
                }
                
                // 在RoundScoreContainer中显示分数变化（带颜色）
                // 显示时去掉前导 0：例如 "-08" -> "-8"
                string displayScoreChange = scoreChange;
                if (scoreChange.StartsWith("+") || scoreChange.StartsWith("-"))
                {
                    // 保留符号，数值部分按 int 解析再转回字符串，自动去掉前导 0
                    if (int.TryParse(scoreChange.Substring(1), out int absValue))
                    {
                        displayScoreChange = (scoreChange.StartsWith("+") ? "+" : "-") + absValue.ToString();
                    }
                }
                GameObject roundScoreObj = Instantiate(Tmp_Text_Prefab, player.roundScoreContainer.transform);
                TMP_Text roundScoreText = roundScoreObj.GetComponent<TMP_Text>();
                if (roundScoreText != null)
                {
                    if (scoreValue > 0)
                    {
                        roundScoreText.text = $"<color=green>{displayScoreChange}</color>";
                    }
                    else if (scoreValue < 0)
                    {
                        roundScoreText.text = $"<color=red>{displayScoreChange}</color>";
                    }
                    else
                    {
                        roundScoreText.text = displayScoreChange;
                    }
                }
                
                // 累计分数并显示在GameScoreContainer中（总计栏目一律使用白色）
                cumulativeScore += scoreValue;
                GameObject gameScoreObj = Instantiate(Tmp_Text_Prefab, player.gameScoreContainer.transform);
                TMP_Text gameScoreText = gameScoreObj.GetComponent<TMP_Text>();
                if (gameScoreText != null)
                {
                    if (cumulativeScore > 0)
                    {
                        gameScoreText.text = $"+{cumulativeScore}";
                    }
                    else if (cumulativeScore < 0)
                    {
                        gameScoreText.text = cumulativeScore.ToString();
                    }
                    else
                    {
                        gameScoreText.text = "0";
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// 清空容器中的所有子对象
    /// </summary>
    private void ClearContainer(Transform container)
    {
        if (container == null) return;

        // 只销毁 container 的子对象，不会影响 container 自身
        for (int i = container.childCount - 1; i >= 0; i--)
        {
            Transform child = container.GetChild(i);
            if (child != null)
            {
                Destroy(child.gameObject);
            }
        }
    }
}
