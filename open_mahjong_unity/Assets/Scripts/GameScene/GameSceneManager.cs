using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;



public class GameSceneManager : MonoBehaviour
{
    public static GameSceneManager Instance { get; private set; }

    // 玩家位置信息 int[0,1,2,3] → string[self,left,top,right]
    public Dictionary<int, string> indexToPosition = new Dictionary<int, string>();

    // 房间信息
    public int roomId; // 房间ID
    public int selfIndex; // 自身位置 0东 1南 2西 3北
    public int roomStepTime; // 步时
    public int roomRoundTime; // 局时
    public int selfRemainingTime; // 剩余时间
    public int remainTiles; // 剩余牌数
    public bool tips; // 提示

    [Header("自动操作配置")]

    public bool isAutoArrangeHandCards = true; // 是否自动排列手牌
    public bool isAutoHepai = false; // 是否自动胡牌
    public bool isAutoCut = false; // 是否自动出牌
    public bool isAutoPass = false; // 是否自动过牌
    public bool isAutoBuhua = false; // 是否自动补花


    public List<string> allowActionList = new List<string>(); // 允许操作列表
    public int lastCutCardID; // 上一张切牌的ID

    // 玩家信息
    public Dictionary<string,PlayerInfo> player_to_info = new Dictionary<string,PlayerInfo>(); // 玩家信息
    public List<int> selfHandTiles = new List<int>(); // 手牌列表
    public class PlayerInfo{
        public string username;
        public int userId;
        public int score;
        public int hand_tiles_count;
        public int[] hand_tiles;
        public List<int> discard_tiles;
        public List<string> combination_tiles;
        public List<int> huapai_list;
        public int title_used;      // 使用的称号ID
        public int profile_used;    // 使用的头像ID
        public int character_used;  // 使用的角色ID
        public int voice_used;      // 使用的音色ID
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        player_to_info["self"] = new PlayerInfo();
        player_to_info["left"] = new PlayerInfo();
        player_to_info["top"] = new PlayerInfo();
        player_to_info["right"] = new PlayerInfo();
    }



    // 初始化游戏
    public void InitializeGame(bool success, string message, GameInfo gameInfo){
        // 0.切换窗口
        WindowsManager.Instance.SwitchWindow("game"); // 切换到游戏场景
        
        EndResultPanel.Instance.ClearEndResultPanel(); // 清空和牌结算面板
        EndGamePanel.Instance.ClearEndGamePanel(); // 清空游戏结束面板
        SwitchSeatPanel.Instance.ClearSwitchSeatPanel(); // 清空换位面板
        EndLiujuPanel.Instance.ClearEndLiujuPanel(); // 清空流局面板
        StartGamePanel.Instance.ClearStartGamePanel(); // 清空开始游戏面板
        GameRecordManager.Instance.HideGameRecord(); // 隐藏游戏记录

        Game3DManager.Instance.Clear3DTile(); // 清空3D手牌

        InitializeSetInfo(gameInfo); // 初始化对局数据
        GameCanvas.Instance.InitializeUIInfo(gameInfo,indexToPosition); // 初始化面板信息
        BoardCanvas.Instance.InitializeBoardInfo(gameInfo,indexToPosition); // 初始化桌面信息

        // 初始化手牌区域 由于手牌信息必定是单独发送的，所以这里直接初始化
        GameCanvas.Instance.ChangeHandCards("InitHandCards",0,gameInfo.self_hand_tiles,null);
        // 如果自己的手牌有14张，则摸最后一张牌
        if (gameInfo.self_hand_tiles.Length == 14){
            GameCanvas.Instance.ChangeHandCards("GetCard",gameInfo.self_hand_tiles[gameInfo.self_hand_tiles.Length - 1],null,null);
            // 在这里可以添加向服务器传递加载完成方法
            // 亲家与闲家完成配牌以后等待服务器传递补花行为
        }

        // 初始化他人手牌区域
        Game3DManager.Instance.Change3DTile("InitHandCards",0,0,null,false,null);
    }

    // 询问手牌操作 手牌操作包括 切牌 补花 胡 暗杠 加杠
    public void AskHandAction(int remaining_time,int playerIndex,int remain_tiles,string[] action_list){
        string GetCardPlayer = indexToPosition[playerIndex];
        // 如果行动者是自己
        if (playerIndex == selfIndex){
            // 存储全部可用行动
            string[] AllowHandActionCheck = new string[] {"cut", "buhua", "hu_self" , "angang", "jiagang","pass"};
            foreach (string action in action_list){
                if (AllowHandActionCheck.Contains(action)){
                    allowActionList.Add(action);
                }
            }
        }
        // 切换行动者
        SwitchCurrentPlayer(GetCardPlayer,"askHandAction",remaining_time);
    }

    // 询问鸣牌操作 鸣牌操作包括 吃 碰 杠 胡 跳过
    public void AskMingPaiAction(int remaining_time,string[] action_list,int cut_tile){
        // 如果列表中有服务器提供的可用操作，则显示倒计时
        if (action_list.Length > 0){
            // 1.存储全部可用行动
            string[] AllowOtherActionCheck = new string[] {"chi_left", "chi_mid", "chi_right", "peng", "gang","hu_first","hu_second","hu_third","pass"};
            foreach (string action in action_list){
                if (AllowOtherActionCheck.Contains(action)){
                    allowActionList.Add(action);
                }
            }
            // 2.切换行动者
            SwitchCurrentPlayer("self","askMingPaiAction",remaining_time);
        }
    }

    // 执行行动
    public void DoAction(string[] action_list, int action_player, int? cut_tile, int? cut_tile_index, bool? cut_class, int? deal_tile, int? buhua_tile, int[] combination_mask,string combination_target) {
        string GetCardPlayer = indexToPosition[action_player]; // 获取执行操作的玩家位置
        foreach (string action in action_list) {

            Debug.Log($"执行DoAction操作: {action}");
            SoundManager.Instance.PlayActionSound(GetCardPlayer, action); // 播放操作音效
            SoundManager.Instance.PlayPhysicsSound(action); // 播放物理音效
            switch (action) { // action_list 实际上只会包含一个操作

                // 摸牌
                case "deal": 
                    remainTiles--; // 剩余牌数减少
                    if (GetCardPlayer == "self"){     // 添加手牌 显示手牌
                        selfHandTiles.Add(deal_tile.Value);
                        GameCanvas.Instance.ChangeHandCards("GetCard",deal_tile.Value,null,null);
                    }
                    else{                             // 增加手牌 显示3D手牌
                        player_to_info[GetCardPlayer].hand_tiles_count++;
                        Game3DManager.Instance.Change3DTile("GetCard",deal_tile.Value,0,GetCardPlayer,false,null);
                    }
                    break;
                
                // 切牌
                case "cut": 
                    lastCutCardID = cut_tile.Value; // 存储上次切牌的ID
                    player_to_info[GetCardPlayer].discard_tiles.Add(cut_tile.Value); // 存储弃牌
                    if (GetCardPlayer == "self"){
                        if (allowActionList.Contains("cut")){
                            selfHandTiles.Remove(cut_tile.Value); // 删除手牌
                            Game3DManager.Instance.Change3DTile("Discard",cut_tile.Value,0,GetCardPlayer,cut_class.Value,null); // 3D切牌行为
                            if (cut_class.Value){
                                GameCanvas.Instance.ChangeHandCards("RemoveGetCard",cut_tile.Value,null,null); // 2D摸切行为
                            }
                            else{
                                GameCanvas.Instance.ChangeHandCards("RemoveHandCard",cut_tile.Value,null,cut_tile_index.Value); // 2D手切行为
                            }
                        }
                    }
                    else{
                        player_to_info[GetCardPlayer].hand_tiles_count--; // 减少手牌
                        Game3DManager.Instance.Change3DTile("Discard",cut_tile.Value,0,GetCardPlayer,cut_class.Value,null); // 3D切牌行为
                    } 
                    break;

                // 补花
                case "buhua":
                    int buhua_tile_id = buhua_tile.Value;
                    player_to_info[GetCardPlayer].huapai_list.Add(buhua_tile_id); // 存储花牌
                    if (GetCardPlayer == "self"){
                        selfHandTiles.Remove(buhua_tile_id); // 删除手牌
                        GameCanvas.Instance.ChangeHandCards("RemoveBuhuaCard",buhua_tile_id,null,null); // 2D补花行为
                    }
                    else{
                        player_to_info[GetCardPlayer].hand_tiles_count--; // 减少手牌
                    }
                    Game3DManager.Instance.Change3DTile("Buhua",buhua_tile_id,0,GetCardPlayer,false,null); // 3D补花行为
                    GameCanvas.Instance.ShowActionDisplay(GetCardPlayer, "buhua"); // 显示操作文本
                    break;

                // 胡牌使用NetworkManager传参调用的ShowResult方法 此处为占位符
                case "hu":
                    break;

                // 吃碰杠
                case "chi_left": case"chi_mid": case"chi_right": case "angang": case "jiagang": case "peng": case "gang":
                    if (action == "jiagang"){
                        // 加杠情况下收到的combination_target是原本刻子的字符串
                        // 替换combination_target的首字符改为加杠字符串
                        string combination_target_str = combination_target.Substring(1);
                        player_to_info[GetCardPlayer].combination_tiles.Add($"g{combination_target_str}"); // 存储组合牌
                        player_to_info[GetCardPlayer].combination_tiles.Remove(combination_target); // 删除刻子组合牌
                        int tile_id = int.Parse(combination_target_str);
                        if (GetCardPlayer == "self"){
                            selfHandTiles.Remove(tile_id); // 删除手牌
                            GameCanvas.Instance.ChangeHandCards("RemoveHandCard",tile_id,null,null); // 2D手牌行为
                        }
                        else{
                            player_to_info[GetCardPlayer].hand_tiles_count--; // 减少手牌
                        }
                        Game3DManager.Instance.Change3DTile("jiagang",tile_id,1,GetCardPlayer,false,combination_mask); // 3D加杠行为
                        GameCanvas.Instance.ShowActionDisplay(GetCardPlayer, "jiagang"); // 显示操作文本
                    }
                    else if (action == "angang"){
                        // 暗杠情况下需要删除完整手牌
                        player_to_info[GetCardPlayer].combination_tiles.Add(combination_target); // 存储组合牌
                        List<int> need_remove_list = combination_mask.Where(x => x > 10).ToList(); // 获取组合牌列表
                        foreach (int tile_id in need_remove_list){
                            if (GetCardPlayer == "self"){
                                selfHandTiles.Remove(tile_id); // 删除手牌
                                GameCanvas.Instance.ChangeHandCards("RemoveCombinationCard",0,need_remove_list.ToArray(),null); // 2D手牌行为
                            }
                            else{
                                player_to_info[GetCardPlayer].hand_tiles_count--; // 减少手牌
                            }
                        }
                        Game3DManager.Instance.Change3DTile(action,0,4,GetCardPlayer,false,combination_mask); // 3D暗杠行为
                        GameCanvas.Instance.ShowActionDisplay(GetCardPlayer, "angang"); // 显示操作文本
                    }
                    else{
                        // 正常情况 "chi_left" "chi_mid" "chi_right" "peng" "gang" 均为场地魔法 需要剔除上次切牌的ID
                        player_to_info[GetCardPlayer].combination_tiles.Add(combination_target); // 存储组合牌
                        List<int> need_remove_list = combination_mask.Where(x => x > 10).ToList(); // 获取组合牌列表
                        need_remove_list.Remove(lastCutCardID); // 剔除上次切牌的ID
                        foreach (int tile_id in need_remove_list){
                            if (GetCardPlayer == "self"){
                                selfHandTiles.Remove(tile_id); // 删除手牌
                            }
                            else{
                                player_to_info[GetCardPlayer].hand_tiles_count--; // 减少手牌
                            }
                        }
                        if (GetCardPlayer == "self"){
                            GameCanvas.Instance.ChangeHandCards("RemoveCombinationCard",0,need_remove_list.ToArray(),null); // 2D手牌行为
                        }
                        Game3DManager.Instance.Change3DTile(action,0,need_remove_list.Count,GetCardPlayer,false,combination_mask); // 3D吃碰杠行为
                        GameCanvas.Instance.ShowActionDisplay(GetCardPlayer, action); // 显示操作文本
                    }
                    break;
                default:
                    Debug.Log($"未知操作: {action}");
                    break;
            }
        }
        // 切换行动者
        SwitchCurrentPlayer(GetCardPlayer,"doAction",0);
    }

    // 回合结束 和牌 流局
    public void ShowResult(int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class, int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask){
        // 重置自身命令
        SwitchCurrentPlayer("None","ClearAction",0);
        // 显示结算结果
        if (hu_class != "liuju"){
            GameCanvas.Instance.ShowActionDisplay(indexToPosition[hepai_player_index], hu_class); // 显示操作文本
            SoundManager.Instance.PlayActionSound(indexToPosition[hepai_player_index], hu_class); // 播放操作音效
            StartCoroutine(EndResultPanel.Instance.ShowResult(hepai_player_index, player_to_score, hu_score, hu_fan, hu_class, hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask));
        }
        else{
            // 流局情况下，显示流局文本
            EndLiujuPanel.Instance.ShowLiujuPanel();
        }
    }

    // 执行换位
    public void HandleSwitchSeat(int current_round){
        // 显示换位动画
        StartCoroutine(SwitchSeatPanel.Instance.ShowSwitchSeatPanel(current_round));
    }

    // 游戏结束
    public void GameEnd(long game_random_seed, Dictionary<string, Dictionary<string, object>> player_final_data){
        // 重置自身命令
        SwitchCurrentPlayer("None","ClearAction",0);
        // 显示游戏结束结果
        EndGamePanel.Instance.ShowGameEndPanel(game_random_seed, player_final_data);
    }

    public void SwitchCurrentPlayer(string GetCardPlayer,string SwitchType,int remaining_time){
        
        // 询问手牌操作
        if (SwitchType == "askHandAction"){
            // 如果行动者是自己
            if (GetCardPlayer == "self"){
                // 显示可用行动 开启倒计时
                GameCanvas.Instance.ClearActionButton(); // 清空操作按钮 *有时候补花轮自己不补花，但是别人也不补，就出现两次按钮
                GameCanvas.Instance.SetActionButton(allowActionList);
                GameCanvas.Instance.LoadingRemianTime(remaining_time,roomStepTime);
                // 如果开启自动胡牌、自动补花或者自动出牌，则启动协程
                if (isAutoHepai || isAutoBuhua || isAutoCut){
                    StartCoroutine(WaitAutoAction("AutoHandAction"));
                }
            }
            // 询问的不是自己的回合
            else{
                GameCanvas.Instance.ChangeHandCards("ReSetHandCards",0,null,null); // 重置手牌
                SwitchCurrentPlayer(GetCardPlayer,"ClearAction",0); // 重置自身命令
            }
            BoardCanvas.Instance.ShowCurrentPlayer(GetCardPlayer); // 显示当前玩家
        }

        // 询问鸣牌操作 鸣牌操作的操作方一定是"self"
        else if (SwitchType == "askMingPaiAction"){
            GameCanvas.Instance.SetActionButton(allowActionList);
            GameCanvas.Instance.LoadingRemianTime(remaining_time,roomStepTime);
            // 如果开启自动过牌和自动胡牌，则启动协程
            if (isAutoPass && isAutoHepai){
                StartCoroutine(WaitAutoAction("AutoMingPaiAction"));
            }
        }

        // 执行行动
        else if (SwitchType == "doAction"){
            Debug.Log($"doAction行动者: {GetCardPlayer}");
            // 如果行动者是自己
            if (GetCardPlayer == "self"){
                // 停止计时器
                GameCanvas.Instance.StopTimeRunning();
                // 清空允许操作列表
                allowActionList.Clear();
                // 清空按钮
                GameCanvas.Instance.ClearActionButton();
            }
        }

        // 选择行动
        else if (SwitchType == "ClearAction"){
            // 停止计时器
            GameCanvas.Instance.StopTimeRunning();
            // 清空操作按钮
            GameCanvas.Instance.ClearActionButton();
            // 清空允许操作列表
            allowActionList.Clear();
        }

        // 时间耗尽
        else if (SwitchType == "TimeOut"){
            // 清空操作按钮
            GameCanvas.Instance.ClearActionButton();
        }
    }

    // 等待自动操作
    private IEnumerator WaitAutoAction(string action){

        // 鸣牌操作自动执行
        if (action == "AutoMingPaiAction"){
            // 和牌不属于不吃碰杠
            List<string> allowHupaiAction = new List<string>{"hu_first","hu_second","hu_third"};

            // 从允许操作列表中找到实际存在的和牌操作
            string actualHupaiAction = allowActionList.FirstOrDefault(a => allowHupaiAction.Contains(a));

            // 如果开启自动胡牌，则判断
            if (isAutoHepai){
                // 如果允许操作列表中有和牌，则执行自动胡牌
                if (!string.IsNullOrEmpty(actualHupaiAction)){
                    yield return new WaitForSeconds(0.3f);
                    GameCanvas.Instance.ChooseAction(actualHupaiAction, 0);
                    yield return null;
                }
            }
            // 如果开启自动过牌，则执行自动过牌
            if (isAutoPass){
                yield return new WaitForSeconds(0.3f); // (如果玩家后悔了，希望玩家手速够快)
                GameCanvas.Instance.ChooseAction("pass", 0);
            }
            yield return null;

        }

        // 手牌操作自动执行
        else if (action == "AutoHandAction"){
            // 如果允许操作列表有hu_self
            if (allowActionList.Contains("hu_self")){
                // 如果开启自动胡牌，则执行自动胡牌
                if (isAutoHepai){
                    yield return new WaitForSeconds(0.3f);
                    GameCanvas.Instance.ChooseAction("hu_self", 0);
                    yield return null;
                }
                // 如果没有开启，转到玩家操作
                else{
                    yield return null;
                }
            }
            
            // 如果允许操作列表有buhua
            if (allowActionList.Contains("buhua")){
                // 如果开启自动补花，则执行自动补花
                if (isAutoBuhua){
                    yield return new WaitForSeconds(0.3f);
                    GameCanvas.Instance.ChooseAction("buhua", 0);
                    yield return null;
                }
                // 如果没有开启，转到玩家操作
                else{
                    yield return null;
                }
            }

            List<string> allowActionWithoutCut = new List<string>{"angang","jiagang","hu_self","buhua"};
            // 如果允许操作列表有除去cut的其他操作 则转到玩家操作
            if (allowActionWithoutCut.Any(allowActionList.Contains)){
                yield return null;
            }
            // 如果没有，则执行自动出牌
            else{
                if (isAutoCut){
                    yield return new WaitForSeconds(0.3f);
                    // 自动出牌 选择手牌中最近摸到的牌（列表的最后一张）
                    if (selfHandTiles != null && selfHandTiles.Count > 0)
                    {
                        int lastTileId = selfHandTiles[selfHandTiles.Count - 1];
                        // 查找对应的 TileCard 并触发点击（优先摸切，否则手切）
                        bool success = GameCanvas.Instance.TriggerTileCardClick(lastTileId);
                        if (!success)
                        {
                            Debug.LogWarning($"自动出牌失败：无法找到牌ID {lastTileId} 对应的 TileCard");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("自动出牌失败：手牌列表为空");
                    }
                }
                else{
                    yield return null;
                }
            }
        }

        else{
            Debug.LogWarning($"未知操作: {action}");
        }
    }

    // 设置游戏信息
    private void InitializeSetInfo(GameInfo gameInfo){
        // 清空操作列表
        allowActionList = new List<string>();
        // 清空弃牌列表
        player_to_info["self"].discard_tiles = new List<int>();
        player_to_info["left"].discard_tiles = new List<int>();
        player_to_info["top"].discard_tiles = new List<int>();
        player_to_info["right"].discard_tiles = new List<int>();
        // 清空花牌列表
        player_to_info["self"].huapai_list = new List<int>();
        player_to_info["left"].huapai_list = new List<int>();
        player_to_info["top"].huapai_list = new List<int>();
        player_to_info["right"].huapai_list = new List<int>();
        // 清空组合牌列表
        player_to_info["self"].combination_tiles = new List<string>();
        player_to_info["left"].combination_tiles = new List<string>();
        player_to_info["top"].combination_tiles = new List<string>();
        player_to_info["right"].combination_tiles = new List<string>();

        // 如果gameinfo.user_id等于自己的user_id，则设置自身索引为gameinfo.player_index
        foreach (var player in gameInfo.players_info){
            if (player.user_id == UserDataManager.Instance.UserId){
                selfIndex = player.player_index; // 存储自身索引
                break;
            }
        }
        roomId = gameInfo.room_id; // 存储房间ID
        roomStepTime = gameInfo.step_time; // 存储步时
        roomRoundTime = gameInfo.round_time; // 存储局时
        tips = gameInfo.tips; // 存储是否提示
        remainTiles = gameInfo.tile_count; // 存储剩余牌数
        selfHandTiles = gameInfo.self_hand_tiles.ToList(); // 存储手牌列表
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
                player_to_info["self"].username = player.username; // 存储用户名
                player_to_info["self"].userId = player.user_id; // 存储uid
                player_to_info["self"].score = player.score; // 存储分数
                // 存储剩余时间
                selfRemainingTime = player.remaining_time;
                player_to_info["self"].discard_tiles = player.discard_tiles.ToList(); // 存储弃牌列表
                player_to_info["self"].combination_tiles = player.combination_tiles.ToList(); // 存储组合牌列表
                player_to_info["self"].huapai_list = player.huapai_list.ToList(); // 存储花牌列表
                player_to_info["self"].title_used = player.title_used; // 存储使用的称号ID
                player_to_info["self"].profile_used = player.profile_used; // 存储使用的头像ID
                player_to_info["self"].character_used = player.character_used; // 存储使用的角色ID
                player_to_info["self"].voice_used = player.voice_used; // 存储使用的音色ID
            }
            else if (indexToPosition[player.player_index] == "right"){
                player_to_info["right"].username = player.username; // 存储用户名
                player_to_info["right"].score = player.score; // 存储分数
                player_to_info["right"].userId = player.user_id; // 存储uid
                player_to_info["right"].discard_tiles = player.discard_tiles.ToList(); // 存储弃牌列表
                player_to_info["right"].combination_tiles = player.combination_tiles.ToList(); // 存储组合牌列表
                player_to_info["right"].huapai_list = player.huapai_list.ToList(); // 存储花牌列表
                player_to_info["right"].hand_tiles_count = player.hand_tiles_count; // 存储手牌数量
                player_to_info["right"].title_used = player.title_used; // 存储使用的称号ID
                player_to_info["right"].profile_used = player.profile_used; // 存储使用的头像ID
                player_to_info["right"].character_used = player.character_used; // 存储使用的角色ID
                player_to_info["right"].voice_used = player.voice_used; // 存储使用的音色ID
            }
            else if (indexToPosition[player.player_index] == "top"){
                player_to_info["top"].username = player.username; // 存储用户名
                player_to_info["top"].score = player.score; // 存储分数
                player_to_info["top"].userId = player.user_id; // 存储uid
                player_to_info["top"].discard_tiles = player.discard_tiles.ToList(); // 存储弃牌列表
                player_to_info["top"].combination_tiles = player.combination_tiles.ToList(); // 存储组合牌列表
                player_to_info["top"].huapai_list = player.huapai_list.ToList(); // 存储花牌列表
                player_to_info["top"].hand_tiles_count = player.hand_tiles_count; // 存储手牌数量
                player_to_info["top"].title_used = player.title_used; // 存储使用的称号ID
                player_to_info["top"].profile_used = player.profile_used; // 存储使用的头像ID
                player_to_info["top"].character_used = player.character_used; // 存储使用的角色ID
                player_to_info["top"].voice_used = player.voice_used; // 存储使用的音色ID
            }
            else if (indexToPosition[player.player_index] == "left"){
                player_to_info["left"].username = player.username; // 存储用户名
                player_to_info["left"].score = player.score; // 存储分数
                player_to_info["left"].userId = player.user_id; // 存储uid
                player_to_info["left"].discard_tiles = player.discard_tiles.ToList(); // 存储弃牌列表
                player_to_info["left"].combination_tiles = player.combination_tiles.ToList(); // 存储组合牌列表
                player_to_info["left"].huapai_list = player.huapai_list.ToList(); // 存储花牌列表
                player_to_info["left"].hand_tiles_count = player.hand_tiles_count; // 存储手牌数量
                player_to_info["left"].title_used = player.title_used; // 存储使用的称号ID
                player_to_info["left"].profile_used = player.profile_used; // 存储使用的头像ID
                player_to_info["left"].character_used = player.character_used; // 存储使用的角色ID
                player_to_info["left"].voice_used = player.voice_used; // 存储使用的音色ID
            }
        }
    }
}



