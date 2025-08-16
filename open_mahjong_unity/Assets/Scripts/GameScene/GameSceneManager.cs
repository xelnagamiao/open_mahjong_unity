using System.Threading;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;



public class GameSceneManager : MonoBehaviour
{
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

    public List<string> allowActionList = new List<string>(); // 允许操作列表
    public int lastCutCardID; // 上一张切牌的ID

    // 玩家信息
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
    public void AskHandAction(int remaining_time,int deal_tile,int playerIndex,int remain_tiles,string[] action_list){
        string GetCardPlayer = indexToPosition[playerIndex];
        lastCutCardID = deal_tile;
        // 如果行动者是自己
        if (playerIndex == selfIndex){
            // 存储全部可用行动
            HashSet<string> AllowHandAction = new HashSet<string>(new string[] {"cut", "buhua", "hu", "angang", "jiagang"});
            foreach (string action in action_list){
                if (AllowHandAction.Contains(action)){
                    allowActionList.Add(action);
                }
            }
            // 如果有可用行动
            if (allowActionList.Count > 0){
                // 显示可用行动
                GameCanvas.Instance.SetActionButton(action_list);
                // 显示剩余时间
                GameCanvas.Instance.LoadingRemianTime(remaining_time,roomStepTime);
            }
            // 如果有摸牌行动
            if (action_list.Contains("deal")){
                GameCanvas.Instance.GetCard(deal_tile);
            }
        }
        // 如果行动者是他人
        else{
            if (this.remainTiles != remain_tiles){
                Game3DManager.Instance.GetCard3D(GetCardPlayer);}
        }
        // 显示行动者
        BoardCanvas.Instance.ShowCurrentPlayer(GetCardPlayer);
    }

    // 出牌后他家反馈操作
    public void AskOtherAction(int remaining_time,string[] action_list,int cut_tile){
        // 如果列表中有服务器提供的可用操作，则显示倒计时
        if (action_list.Length > 0){
            // 1.存储全部可用行动
            HashSet<string> AllowHandAction = new HashSet<string>(new string[] {"chi_left", "chi_mid", "chi_right", "peng", "gang","hu"});
            foreach (string action in action_list){
                if (AllowHandAction.Contains(action)){
                    allowActionList.Add(action);
                }
            }
            // 2.显示可用行动
            GameCanvas.Instance.SetActionButton(action_list);
            // 3.显示剩余时间
            GameCanvas.Instance.LoadingRemianTime(remaining_time,roomStepTime);
        }
    }


    // 执行操作
    public void DoAction(string[] action_list, int action_player, int? cut_tile, bool? cut_class, int? deal_tile, int? buhua_tile, int[] combination_mask) {
        string GetCardPlayer = indexToPosition[action_player];
        allowActionList = new List<string>{}; // 清空允许操作列表
        foreach (string action in action_list) {
            switch (action) {
                case "cut": // 切牌
                    if (GetCardPlayer == "self"){
                        if (allowActionList.Contains("cut")){
                            selfDiscardslist.Add(cut_tile.Value); // 存储弃牌
                            GameCanvas.Instance.ArrangeHandCards(); // 重新排列手牌
                            Game3DManager.Instance.CutCards(GetCardPlayer, cut_tile.Value, cut_class.Value); // 生成3D切牌
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
                        GameCanvas.Instance.RemoveCutCard(buhua_tile_id,false); // 补花手切
                    }
                    else if (GetCardPlayer == "left"){leftHuapaiList.Add(buhua_tile_id);}
                    else if (GetCardPlayer == "top"){topHuapaiList.Add(buhua_tile_id);}
                    else if (GetCardPlayer == "right"){rightHuapaiList.Add(buhua_tile_id);}
                    Game3DManager.Instance.BuhuaAnimation(GetCardPlayer, buhua_tile_id);
                    break;
                case "hu":
                    // 
                    break;
                case "chi_left": case"chi_mid": case"chi_right": case "angang": case "jiagang":
                    Game3DManager.Instance.ActionAnimation(GetCardPlayer, action, combination_mask);
                    break;
                default:
                    Debug.Log($"未知操作: {action}");
                    break;
            }
        }
        // 如果行动者是自己 则将摸牌区的卡牌加入手牌
        // if (GetCardPlayer == "self"){
            // GameCanvas.Instance.MoveAllGetCardsToHandCards();
        //}
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
        // 根据自身索引确定其他玩家位置
        if (selfIndex == 0)
        {
            indexToPosition[0] = "self";
            indexToPosition[1] = "left";
            indexToPosition[2] = "top";
            indexToPosition[3] = "right";
        }
        else if (selfIndex == 1)
        {
            indexToPosition[1] = "self";
            indexToPosition[2] = "left";
            indexToPosition[3] = "top";
            indexToPosition[0] = "right";
        }
        else if (selfIndex == 2)
        {
            indexToPosition[2] = "self";
            indexToPosition[3] = "left";
            indexToPosition[0] = "top";
            indexToPosition[1] = "right";
            
        }
        else if (selfIndex == 3)
        {
            indexToPosition[3] = "self";
            indexToPosition[0] = "left";
            indexToPosition[1] = "top";
            indexToPosition[2] = "right";
        }
        foreach (var player in gameInfo.players_info){
            if (indexToPosition[player.player_index] == "self"){ // 通过player_index确定玩家位置
                // 存储剩余时间
                selfRemainingTime = player.remaining_time;
                selfDiscardslist = player.discard_tiles.ToList(); // 存储弃牌列表
                selfCombinationList = player.combination_tiles.ToList(); // 存储组合牌列表
                selfHuapaiList = player.huapai_list.ToList(); // 存储花牌列表
            }
            else if (indexToPosition[player.player_index] == "left"){
                leftDiscardslist = player.discard_tiles.ToList(); // 存储弃牌列表
                leftCombinationList = player.combination_tiles.ToList(); // 存储组合牌列表
                leftHuapaiList = player.huapai_list.ToList(); // 存储花牌列表
            }
            else if (indexToPosition[player.player_index] == "top"){
                topDiscardslist = player.discard_tiles.ToList(); // 存储弃牌列表
                topCombinationList = player.combination_tiles.ToList(); // 存储组合牌列表
                topHuapaiList = player.huapai_list.ToList(); // 存储花牌列表
            }
            else if (indexToPosition[player.player_index] == "right"){
                rightDiscardslist = player.discard_tiles.ToList(); // 存储弃牌列表
                rightCombinationList = player.combination_tiles.ToList(); // 存储组合牌列表
                rightHuapaiList = player.huapai_list.ToList(); // 存储花牌列表
            }
        }
    }
}



