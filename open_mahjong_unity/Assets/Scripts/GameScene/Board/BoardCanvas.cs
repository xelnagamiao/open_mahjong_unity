using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public partial class BoardCanvas : MonoBehaviour {
    [Header("游戏中心盘信息")]
    [SerializeField] private TMP_Text remiansTilesText;      // 剩余牌数文本
    [SerializeField] private TMP_Text CurrentRoundText;      // 当前回合文本

    [Header("玩家信息")]
    [SerializeField] private TMP_Text player_self_score;       // 玩家分数文本
    [SerializeField] private TMP_Text player_self_index;       // 玩家索引文本
    [SerializeField] private Image player_self_current_image;    // 玩家回合标记    

    [SerializeField] private TMP_Text player_left_score;         // 玩家分数文本
    [SerializeField] private TMP_Text player_left_index;         // 玩家索引文本
    [SerializeField] private Image player_left_current_image;      // 玩家回合标记

    [SerializeField] private TMP_Text player_top_score;          // 玩家分数文本
    [SerializeField] private TMP_Text player_top_index;          // 玩家索引文本
    [SerializeField] private Image player_top_current_image;       // 玩家回合标记

    [SerializeField] private TMP_Text player_right_score;        // 玩家分数文本
    [SerializeField] private TMP_Text player_right_index;        // 玩家索引文本
    [SerializeField] private Image player_right_current_image;     // 玩家回合标记

    private Dictionary<int, string> PositionToChineseCharacter = new Dictionary<int, string>(){
        {0, "东"},
        {1, "南"},
        {2, "西"},
        {3, "北"}
    };

    private static readonly Dictionary<int, string> CurrentRoundTextGB = new Dictionary<int, string>() {
        {1, "东风东"},
        {2, "东风南"},
        {3, "东风西"},
        {4, "东风北"},
        {5, "南风东"},
        {6, "南风南"},
        {7, "南风西"},
        {8, "南风北"},
        {9, "西风东"},
        {10, "西风南"},
        {11, "西风西"},
        {12, "西风北"},
        {13, "北风东"},
        {14, "北风南"},
        {15, "北风西"},
        {16, "北风北"},
    };

    
    public static BoardCanvas Instance { get; private set; }
    private Coroutine flashCoroutine; // 闪烁协程
    private Coroutine scoreDifferenceCoroutine; // 分差显示协程
    private Dictionary<TMP_Text, string> originalScores = new Dictionary<TMP_Text, string>(); // 运行时临时还原用（每次展示前由 baseline 复制）

    // 基准分数（永远保存“真实分数”，不允许被分差文本污染）
    private readonly Dictionary<TMP_Text, string> baselineScores = new Dictionary<TMP_Text, string>();

    private bool isShowingScoreDifference = false;

    private void Awake(){
        if (Instance != null && Instance != this){
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void InitializeBoardInfo(GameInfo gameInfo,Dictionary<int, string> indexToPosition){
        // 初始化玩家信息
        // 设置玩家位置、分数、索引(东南西北)、回合标记
        foreach (var player in gameInfo.players_info){
            if (indexToPosition[player.player_index] == "self"){ // 通过player_index确定玩家位置
                player_self_score.text = player.score.ToString(); // 设置玩家分数
                player_self_index.text = PositionToChineseCharacter[player.player_index]; // 设置玩家索引
                player_self_current_image.gameObject.SetActive(false); // 设置玩家回合标记
            } else if (indexToPosition[player.player_index] == "left"){
                player_left_score.text = player.score.ToString();
                player_left_index.text = PositionToChineseCharacter[player.player_index];
                player_left_current_image.gameObject.SetActive(false);
            } else if (indexToPosition[player.player_index] == "top"){
                player_top_score.text = player.score.ToString();
                player_top_index.text = PositionToChineseCharacter[player.player_index];
                player_top_current_image.gameObject.SetActive(false);
            } else if (indexToPosition[player.player_index] == "right"){
                player_right_score.text = player.score.ToString();
                player_right_index.text = PositionToChineseCharacter[player.player_index];
                player_right_current_image.gameObject.SetActive(false);
            }
        }

        // 设置剩余牌数
        remiansTilesText.text = $"余:{gameInfo.tile_count}"; 

        // 设置当前回合
        string roomType = gameInfo.room_type;
        Dictionary<int, string> roundMap = null;
        if (roomType == "guobiao") {
            roundMap = CurrentRoundTextGB;
        }

        if (roundMap != null && roundMap.TryGetValue(gameInfo.current_round, out string currentRoundStr)) {
            CurrentRoundText.text = currentRoundStr;
        } else {
            CurrentRoundText.text = "";
        }
    }

    // 显示玩家分数分差
    public void ShowScoreDifference() {
        // 如果当前正在显示分差，先强制恢复到基准分数，避免“分差文本”被当作原始分数保存
        if (scoreDifferenceCoroutine != null) {
            StopCoroutine(scoreDifferenceCoroutine);
            scoreDifferenceCoroutine = null;
        }
        RestoreBaselineScores();

        // 启动显示分差的协程（每次点击都重置 3 秒计时）
        scoreDifferenceCoroutine = StartCoroutine(ShowScoreDifferenceCoroutine());
    }

    private IEnumerator ShowScoreDifferenceCoroutine() {
        isShowingScoreDifference = true;

        // 1) 确保 baseline 已初始化（只在“正常分数显示状态”下更新）
        EnsureBaselineScores();

        // 2) 复制 baseline 到 originalScores（本次展示结束后用于恢复）
        originalScores.Clear();
        originalScores[player_self_score] = baselineScores[player_self_score];
        originalScores[player_left_score] = baselineScores[player_left_score];
        originalScores[player_top_score] = baselineScores[player_top_score];
        originalScores[player_right_score] = baselineScores[player_right_score];

        // 3) 基于 baseline 解析分数（避免解析到彩色标签/分差文本）
        int selfScore = ParseScore(originalScores[player_self_score]);
        int leftScore = ParseScore(originalScores[player_left_score]);
        int topScore = ParseScore(originalScores[player_top_score]);
        int rightScore = ParseScore(originalScores[player_right_score]);
        // 计算分差（玩家分数 - 其他玩家分数）
        int leftDiff = selfScore - leftScore;
        int topDiff = selfScore - topScore;
        int rightDiff = selfScore - rightScore;
        // 显示分差（玩家自己的分数显示为原始分数，其他位置显示分差）
        player_self_score.text = originalScores[player_self_score];
        player_left_score.text = FormatScoreDifference(leftDiff);
        player_top_score.text = FormatScoreDifference(topDiff);
        player_right_score.text = FormatScoreDifference(rightDiff);
        // 等待3秒
        yield return new WaitForSeconds(3f);
        // 恢复基准分数文本
        RestoreBaselineScores();
        isShowingScoreDifference = false;
        scoreDifferenceCoroutine = null; // 协程结束，清空引用
    }

    private void EnsureBaselineScores()
    {
        // 如果 baseline 为空，直接用当前文本初始化（此时应当是“真实分数”）
        if (!baselineScores.ContainsKey(player_self_score)) baselineScores[player_self_score] = player_self_score.text;
        if (!baselineScores.ContainsKey(player_left_score)) baselineScores[player_left_score] = player_left_score.text;
        if (!baselineScores.ContainsKey(player_top_score)) baselineScores[player_top_score] = player_top_score.text;
        if (!baselineScores.ContainsKey(player_right_score)) baselineScores[player_right_score] = player_right_score.text;

        // 如果当前不在展示分差，则允许刷新 baseline（例如服务器更新了分数）
        if (!isShowingScoreDifference)
        {
            baselineScores[player_self_score] = player_self_score.text;
            baselineScores[player_left_score] = player_left_score.text;
            baselineScores[player_top_score] = player_top_score.text;
            baselineScores[player_right_score] = player_right_score.text;
        }
    }

    private void RestoreBaselineScores()
    {
        // 如果 baseline 未初始化就先初始化
        EnsureBaselineScores();

        player_self_score.text = baselineScores[player_self_score];
        player_left_score.text = baselineScores[player_left_score];
        player_top_score.text = baselineScores[player_top_score];
        player_right_score.text = baselineScores[player_right_score];
    }

    // 解析分数文本（处理可能的非数字字符）
    private int ParseScore(string scoreText) {
        // 移除所有非数字字符（除了负号）
        string cleanText = Regex.Replace(scoreText, @"[^\d-]", "");
        if (int.TryParse(cleanText, out int score)) {
            return score;
        }
        return 0;
    }

    // 格式化分差显示（正数绿色+xx，负数红色-xx）
    private string FormatScoreDifference(int difference) {
        if (difference > 0) {
            return $"<color=#00ff00>+{difference}</color>"; // 正数显示绿色
        } else if (difference < 0) {
            return $"<color=#ff0000>{difference}</color>"; // 负数显示红色
        } else {
            return "<color=#ffffff>0</color>"; // 零显示白色
        }
    }
}


