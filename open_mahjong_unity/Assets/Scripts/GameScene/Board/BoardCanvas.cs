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
    private Dictionary<TMP_Text, string> originalScores = new Dictionary<TMP_Text, string>(); // 保存原始分数文本

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
        // 如果已有分差显示协程在运行，先停止它
        if (scoreDifferenceCoroutine != null) {
            StopCoroutine(scoreDifferenceCoroutine);
        }
        // 启动显示分差的协程，重置时间
        scoreDifferenceCoroutine = StartCoroutine(ShowScoreDifferenceCoroutine());
    }

    private IEnumerator ShowScoreDifferenceCoroutine() {
        // 保存原始分数文本
        originalScores.Clear();
        originalScores[player_self_score] = player_self_score.text;
        originalScores[player_left_score] = player_left_score.text;
        originalScores[player_top_score] = player_top_score.text;
        originalScores[player_right_score] = player_right_score.text;
        // 解析分数
        int selfScore = ParseScore(player_self_score.text);
        int leftScore = ParseScore(player_left_score.text);
        int topScore = ParseScore(player_top_score.text);
        int rightScore = ParseScore(player_right_score.text);
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
        // 恢复原始分数文本
        player_self_score.text = originalScores[player_self_score];
        player_left_score.text = originalScores[player_left_score];
        player_top_score.text = originalScores[player_top_score];
        player_right_score.text = originalScores[player_right_score];
        scoreDifferenceCoroutine = null; // 协程结束，清空引用
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


