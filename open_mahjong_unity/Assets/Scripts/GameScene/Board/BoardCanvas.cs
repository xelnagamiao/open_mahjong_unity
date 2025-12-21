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
        }

        // 设置剩余牌数
        remiansTilesText.text = $"余:{gameInfo.tile_count}"; 

        // 设置当前回合
        string current_round_str = "";
        if (gameInfo.current_round == 1){
            current_round_str = "东一局";
        }
        else if (gameInfo.current_round == 2){
            current_round_str = "东二局";
        }
        else if (gameInfo.current_round == 3){
            current_round_str = "东三局";
        }
        else if (gameInfo.current_round == 4){
            current_round_str = "东四局";
        }
        else if (gameInfo.current_round == 5){
            current_round_str = "南一局";
        }
        else if (gameInfo.current_round == 6){
            current_round_str = "南二局";
        }
        else if (gameInfo.current_round == 7){
            current_round_str = "南三局";
        }
        else if (gameInfo.current_round == 8){
            current_round_str = "南四局";
        }
        else if (gameInfo.current_round == 9){
            current_round_str = "西一局";
        }
        else if (gameInfo.current_round == 10){
            current_round_str = "西二局";
        }
        else if (gameInfo.current_round == 11){
            current_round_str = "西三局";
        }
        else if (gameInfo.current_round == 12){
            current_round_str = "西四局";
        }
        else if (gameInfo.current_round == 13){
            current_round_str = "北一局";
        }
        else if (gameInfo.current_round == 14){
            current_round_str = "北二局";
        }
        else if (gameInfo.current_round == 15){
            current_round_str = "北三局";
        }
        else if (gameInfo.current_round == 16){
            current_round_str = "北四局";
        }
        CurrentRoundText.text = $"{current_round_str}";
    }
}


