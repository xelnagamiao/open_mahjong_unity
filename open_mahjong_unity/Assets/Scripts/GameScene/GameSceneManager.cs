using System.Threading;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;



public class GameSceneManager : MonoBehaviour
{
    [SerializeField] private GameObject EndPanel;
    
    public static GameSceneManager Instance { get; private set; }

    // 玩家位置信息 int[0,1,2,3] → string[self,left,top,right]
    public Dictionary<int, string> indexToPosition = new Dictionary<int, string>();

    // 房间信息
    public int roomId; // 房间ID
    public int selfIndex; // 自身索引
    public int currentIndex; // 目前进行操作的索引
    public int roomStepTime; // 步时
    public int roomRoundTime; // 局时
    public int selfRemainingTime; // 剩余时间
    public int remainTiles; // 剩余牌数
    public bool tips; // 提示
    public List<int> handTiles = new List<int>(); // 手牌列表

    public List<string> allowActionList = new List<string>(); // 允许操作列表
    public int lastCutCardID; // 上一张切牌的ID

    // 玩家信息
    public string selfUserName; // 用户名
    public string leftUserName;
    public string topUserName;
    public string rightUserName;
    public int selfScore; // 分数
    public int leftScore;
    public int topScore;
    public int rightScore;

    public List<int> selfDiscardslist = new List<int>(); // 弃牌列表
    public List<int> leftDiscardslist = new List<int>();
    public List<int> topDiscardslist = new List<int>();
    public List<int> rightDiscardslist = new List<int>();

    public List<int> selfHuapaiList = new List<int>(); // 花牌列表
    public List<int> leftHuapaiList = new List<int>();
    public List<int> topHuapaiList = new List<int>();
    public List<int> rightHuapaiList = new List<int>();

    public List<string> selfCombinationList = new List<string>(); // 组合牌列表
    public List<string> leftCombinationList = new List<string>();
    public List<string> topCombinationList = new List<string>();
    public List<string> rightCombinationList = new List<string>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void InitializeGame(bool success, string message, GameInfo gameInfo){
        // 0.切换窗口
        WindowsManager.Instance.SwitchWindow("game");
        Game3DManager.Instance.Clear3DTile();
        EndPanel.GetComponent<EndPanel>().ClearEndPanel();
        EndPanel.SetActive(false);
        // 1.存储初始化信息
        InitializeSetInfo(gameInfo);
        // 2.初始化UI
        GameCanvas.Instance.InitializeUIInfo(gameInfo,indexToPosition);
        // 3.初始化面板
        BoardCanvas.Instance.InitializeBoardInfo(gameInfo,indexToPosition);
        // 4.初始化手牌区域 由于手牌信息必定是单独发送的，所以这里直接初始化
        GameCanvas.Instance.InitializeHandCards(gameInfo.self_hand_tiles);
        // 5.初始化他人手牌区域
        Game3DManager.Instance.InitializeOtherCards(gameInfo.players_info);
        // 6.如果当前玩家是自己,说明自己是庄家,进行摸牌
        if (selfIndex == currentIndex){
            GameCanvas.Instance.GetCard(gameInfo.self_hand_tiles[gameInfo.self_hand_tiles.Length - 1]);
            // 在这里可以添加向服务器传递加载完成方法
            // 亲家与闲家完成配牌以后等待服务器传递补花行为
        }
    }

    // 询问摸牌手牌操作 手牌操作包括 摸牌 切牌 补花 胡 暗杠 加杠
    public void AskHandAction(int remaining_time,int playerIndex,int remain_tiles,string[] action_list){
        string GetCardPlayer = indexToPosition[playerIndex];
        // 如果行动者是自己
        if (playerIndex == selfIndex){
            // 存储全部可用行动
            string[] AllowHandActionCheck = new string[] {"cut", "buhua", "hu_first", "hu_second", "hu_third", "angang", "jiagang","pass"};
            foreach (string action in action_list){
                if (AllowHandActionCheck.Contains(action)){
                    allowActionList.Add(action);
                }
            }
            // 如果有可用行动
            if (allowActionList.Count > 0){
                // 显示可用行动
                GameCanvas.Instance.SetActionButton(allowActionList);
                // 显示剩余时间
                GameCanvas.Instance.LoadingRemianTime(remaining_time,roomStepTime);
            }
        }
        // 显示行动者
        BoardCanvas.Instance.ShowCurrentPlayer(GetCardPlayer);
    }

    // 出牌后他家反馈操作
    public void AskOtherAction(int remaining_time,string[] action_list,int cut_tile){
        // 如果列表中有服务器提供的可用操作，则显示倒计时
        if (action_list.Length > 0){
            // 1.存储全部可用行动
            string[] AllowOtherActionCheck = new string[] {"chi_left", "chi_mid", "chi_right", "peng", "gang","hu_self","hu_first","hu_second","hu_third","pass"};
            foreach (string action in action_list){
                if (AllowOtherActionCheck.Contains(action)){
                    allowActionList.Add(action);
                }
            }
            // 2.显示可用行动
            GameCanvas.Instance.SetActionButton(allowActionList);
            // 3.显示剩余时间
            GameCanvas.Instance.LoadingRemianTime(remaining_time,roomStepTime);
        }
    }

    public void DoAction(string[] action_list, int action_player, int? cut_tile, bool? cut_class, int? deal_tile, int? buhua_tile, int[] combination_mask,string combination_target) {
        string GetCardPlayer = indexToPosition[action_player];
        foreach (string action in action_list) {
            Debug.Log($"执行DoAction操作: {action}");
            switch (action) { // action_list 实际上只会包含一个操作
                case "deal": // 摸牌
                    if (GetCardPlayer == "self"){
                        GameCanvas.Instance.GetCard(deal_tile.Value);
                    }
                    else{
                        Game3DManager.Instance.GetCard3D(GetCardPlayer);
                        BoardCanvas.Instance.ShowCurrentPlayer(GetCardPlayer);
                    }
                    break;
                case "cut": // 切牌
                    lastCutCardID = cut_tile.Value; // 存储切牌ID
                    if (GetCardPlayer == "self"){
                        Debug.Log($"是自己");
                        if (allowActionList.Contains("cut")){
                            handTiles.Remove(cut_tile.Value); // 删除手牌
                            selfDiscardslist.Add(cut_tile.Value); // 存储弃牌
                            Debug.Log($"重新排列手牌");
                            GameCanvas.Instance.ArrangeHandCards(); // 重新排列手牌
                            Debug.Log($"生成3D切牌");
                            Game3DManager.Instance.CutCards(GetCardPlayer, cut_tile.Value, cut_class.Value); // 生成3D切牌
                            GameCanvas.Instance.RemoveCanvasCard(cut_tile.Value,false); // 删除切牌
                        }
                    }
                    else if (GetCardPlayer == "left"){
                        leftDiscardslist.Add(cut_tile.Value); // 存储弃牌
                        Game3DManager.Instance.CutCards(GetCardPlayer, cut_tile.Value, cut_class.Value); // 生成3D切牌
                    } 
                    else if (GetCardPlayer == "top"){
                        topDiscardslist.Add(cut_tile.Value); // 存储弃牌
                        Game3DManager.Instance.CutCards(GetCardPlayer, cut_tile.Value, cut_class.Value); // 生成3D切牌
                    }
                    else if (GetCardPlayer == "right"){
                        rightDiscardslist.Add(cut_tile.Value); // 存储弃牌
                        Game3DManager.Instance.CutCards(GetCardPlayer, cut_tile.Value, cut_class.Value); // 生成3D切牌
                    }
                    break;
                case "buhua":
                    int buhua_tile_id = buhua_tile.Value;
                    if (GetCardPlayer == "self"){
                        selfHuapaiList.Add(buhua_tile_id);
                        handTiles.Remove(buhua_tile_id); // 删除手牌
                        GameCanvas.Instance.RemoveCanvasCard(buhua_tile_id,false); // 删除补花牌
                    }
                    else if (GetCardPlayer == "left"){leftHuapaiList.Add(buhua_tile_id);}
                    else if (GetCardPlayer == "top"){topHuapaiList.Add(buhua_tile_id);}
                    else if (GetCardPlayer == "right"){rightHuapaiList.Add(buhua_tile_id);
                    }
                    Game3DManager.Instance.BuhuaAnimation(GetCardPlayer, buhua_tile_id);
                    break;
                case "hu":
                    // 胡牌使用NetworkManager传参调用的ShowResult方法 此处为占位符
                    break;
                case "chi_left": case"chi_mid": case"chi_right": case "angang": case "jiagang":
                    Game3DManager.Instance.ActionAnimation(GetCardPlayer, action, combination_mask);
                    if (GetCardPlayer == "self"){
                        if (action == "jiagang"){
                            // 删除combination_target的首字符
                            string combination_target_str = combination_target.Substring(1);
                            selfCombinationList.Add($"g{combination_target_str}");
                            selfCombinationList.Remove(combination_target);
                        }
                        else{
                            selfCombinationList.Add(combination_target);
                        }
                    }
                    break;
                default:
                    Debug.Log($"未知操作: {action}");
                    break;
            }
        }
        // 行动终止清理
        if (GetCardPlayer == "self"){
            Debug.Log($"行动终止清理");
            // 将摸牌区的卡牌加入手牌
            if (allowActionList.Count > 0){
                GameCanvas.Instance.MoveAllGetCardsToHandCards();
            }
            // 停止计时器
            GameCanvas.Instance.StopTimeRunning();
            // 清空允许操作列表
            allowActionList = new List<string>{}; 
        }
    }

    public void ShowResult(int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class, int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask){
        // 显示结算结果
        EndPanel.SetActive(true);
        StartCoroutine(EndPanel.GetComponent<EndPanel>().ShowResult(hepai_player_index, player_to_score, hu_score, hu_fan, hu_class, hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask));
    }

    private void InitializeSetInfo(GameInfo gameInfo){
        // 如果gameinfo.username等于自己的username，则设置自身索引为gameinfo.player_index
        foreach (var player in gameInfo.players_info){
            if (player.username == Administrator.Instance.Username){
                selfIndex = player.player_index; // 存储自身索引
                break;
            }
        }
        roomId = gameInfo.room_id; // 存储房间ID
        currentIndex = gameInfo.current_player_index; // 存储当前玩家索引
        roomStepTime = gameInfo.step_time; // 存储步时
        roomRoundTime = gameInfo.round_time; // 存储局时
        tips = gameInfo.tips; // 存储是否提示
        remainTiles = gameInfo.tile_count; // 存储剩余牌数
        handTiles = gameInfo.self_hand_tiles.ToList(); // 存储手牌列表
        // 根据自身索引确定其他玩家位置
        if (selfIndex == 0)
        {
            indexToPosition[0] = "self";
            indexToPosition[1] = "right";
            indexToPosition[2] = "top";
            indexToPosition[3] = "left";
        }
        else if (selfIndex == 1)
        {
            indexToPosition[1] = "self";
            indexToPosition[2] = "right";
            indexToPosition[3] = "top";
            indexToPosition[0] = "left";
        }
        else if (selfIndex == 2)
        {
            indexToPosition[2] = "self";
            indexToPosition[3] = "right";
            indexToPosition[0] = "top";
            indexToPosition[1] = "left";
            
        }
        else if (selfIndex == 3)
        {
            indexToPosition[3] = "self";
            indexToPosition[0] = "right";
            indexToPosition[1] = "top";
            indexToPosition[2] = "left";
        }
        foreach (var player in gameInfo.players_info){
            if (indexToPosition[player.player_index] == "self"){ // 通过player_index确定玩家位置
                selfUserName = player.username; // 存储用户名
                selfScore = player.score; // 存储分数
                // 存储剩余时间
                selfRemainingTime = player.remaining_time;
                selfDiscardslist = player.discard_tiles.ToList(); // 存储弃牌列表
                selfCombinationList = player.combination_tiles.ToList(); // 存储组合牌列表
                selfHuapaiList = player.huapai_list.ToList(); // 存储花牌列表
            }
            else if (indexToPosition[player.player_index] == "right"){
                rightUserName = player.username; // 存储用户名
                rightScore = player.score; // 存储分数
                rightDiscardslist = player.discard_tiles.ToList(); // 存储弃牌列表
                rightCombinationList = player.combination_tiles.ToList(); // 存储组合牌列表
                rightHuapaiList = player.huapai_list.ToList(); // 存储花牌列表
            }
            else if (indexToPosition[player.player_index] == "top"){
                topUserName = player.username; // 存储用户名
                topScore = player.score; // 存储分数
                topDiscardslist = player.discard_tiles.ToList(); // 存储弃牌列表
                topCombinationList = player.combination_tiles.ToList(); // 存储组合牌列表
                topHuapaiList = player.huapai_list.ToList(); // 存储花牌列表
            }
            else if (indexToPosition[player.player_index] == "left"){
                leftUserName = player.username; // 存储用户名
                leftScore = player.score; // 存储分数
                leftDiscardslist = player.discard_tiles.ToList(); // 存储弃牌列表
                leftCombinationList = player.combination_tiles.ToList(); // 存储组合牌列表
                leftHuapaiList = player.huapai_list.ToList(); // 存储花牌列表
            }
        }
    }
}



