using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public partial class BoardCanvas : MonoBehaviour
{
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
    
    public static BoardCanvas Instance { get; private set; }
    private Coroutine flashCoroutine; // 闪烁协程

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void InitializeBoardInfo(GameInfo gameInfo,Dictionary<int, string> indexToPosition){
        foreach (var player in gameInfo.players_info){
            if (indexToPosition[player.player_index] == "self"){ // 通过player_index确定玩家位置
                player_self_score.text = player.score.ToString(); // 设置玩家分数
                player_self_index.text = PositionToChineseCharacter[player.player_index]; // 设置玩家索引
                player_self_current_image.gameObject.SetActive(false); // 设置玩家回合标记
            }
            else if (indexToPosition[player.player_index] == "left"){
                player_left_score.text = player.score.ToString();
                player_left_index.text = PositionToChineseCharacter[player.player_index];
                player_left_current_image.gameObject.SetActive(false);
            }
            else if (indexToPosition[player.player_index] == "top"){
                player_top_score.text = player.score.ToString();
                player_top_index.text = PositionToChineseCharacter[player.player_index];
                player_top_current_image.gameObject.SetActive(false);
            }
            else if (indexToPosition[player.player_index] == "right"){
                player_right_score.text = player.score.ToString();
                player_right_index.text = PositionToChineseCharacter[player.player_index];
                player_right_current_image.gameObject.SetActive(false);
            }
            remiansTilesText.text = $"余：{gameInfo.tile_count}"; // 设置剩余牌数
            string current_round_str = "";
            // 整除于4为0为东
            if (gameInfo.current_round-1 / 4 == 0){
                current_round_str = "东";
            }
            // 整除于4为1为南
            else if (gameInfo.current_round-1 / 4 == 1){
                current_round_str = "南";
            }
            // 整除于4为2为西
            else if (gameInfo.current_round-1 / 4 == 2){
                current_round_str = "西";
            }
            // 整除于4为3为北
            else if (gameInfo.current_round-1 / 4 == 3){
                current_round_str = "北";
            }
            // 整除4取余1为东
            if (gameInfo.current_round % 4 == 1){
                current_round_str = current_round_str + "一局";
            }
            // 整除4取余2为南
            else if (gameInfo.current_round % 4 == 2){
                current_round_str = current_round_str + "二局";
            }
            // 整除4取余3为西
            else if (gameInfo.current_round % 4 == 3){
                current_round_str = current_round_str + "三局";
            }
            // 整除4取余0为北
            else if (gameInfo.current_round % 4 == 0){
                current_round_str = current_round_str + "四局";
            }
            CurrentRoundText.text = $"{current_round_str}";
        }
    }
}


