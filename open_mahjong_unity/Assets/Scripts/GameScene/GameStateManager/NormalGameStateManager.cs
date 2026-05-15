using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

[System.Serializable]
public class PlayerInfoClass
{
    public string username;
    public int userId;
    public int score;
    public int hand_tiles_count;
    public int[] hand_tiles;
    public List<int> discard_tiles;
    public List<int> discard_origin_tiles;
    public List<string> combination_tiles;
    public List<int> huapai_list;
    public int title_used;
    public int profile_used;
    public int character_used;
    public int voice_used;
    public List<string> score_history;
    public List<int> round_number_history;
    public int original_player_index;
    public string[] tag_list;
    /// <summary>立直规则：与 discard_tiles 同序的横置标记，用于他家鸣牌后续横、重连/牌谱重建复原立直横置弃牌。</summary>
    public List<bool> discard_riichi_flags = new List<bool>();
}


public class NormalGameStateManager : MonoBehaviour{
    public static NormalGameStateManager Instance { get; private set; }

    // 玩家位置信息 int[0,1,2,3] → string[self,left,top,right]
    public Dictionary<int, string> indexToPosition = new Dictionary<int, string>();

    // 房间信息
    public int roomId; // 房间ID
    public string gamestateId; // 游戏状态ID（用于发送游戏操作请求）
    public string roomType; // 房间类型（custom/match等）
    public string roomRule; // 房间规则（guobiao/qingque等）
    public string subRule;  // 子规则（guobiao/standard、guobiao/xiaolin、qingque/standard）
    public int hepaiLimit = 8; // 起和番限制（国标有效，服务器下发的 hepai_limit，默认8）
    public int selfIndex; // 自身位置 0东 1南 2西 3北
    public int roomStepTime; // 步时
    public int roomRoundTime; // 局时
    public int selfRemainingTime; // 剩余时间
    public int remainTiles; // 剩余牌数
    public int currentRound; // 当前轮数
    public bool tips; // 提示
    public bool isOpenCuoHe; // 是否开启错和
    public bool isSetRandomSeed; // 是否设置随机种子

    public List<string> allowActionList = new List<string>(); // 允许操作列表
    public int lastCutCardID; // 上一张切牌的ID
    public string CurrentPlayer; // 当前玩家字符串
    public List<int> selfHandTiles = new List<int>(); // 手牌列表

    /// <summary>当前是否在等待自己做出手牌/鸣牌操作（供回到主菜单时的红色提醒按钮判断）。</summary>
    public bool IsSelfActionRequired { get; private set; }
    /// <summary>对局是否处于进行中（InitializeGame 后置 true，结算/结束时置 false）。</summary>
    public bool IsGameActive { get; private set; }
    /// <summary>当前是否处于"实时观战"只读模式：接收完整 gamestate 广播，但所有发送动作的接口均早退。</summary>
    public bool IsRealtimeSpectator { get; private set; }

    // 玩家信息
    public Dictionary<string,PlayerInfoClass> player_to_info = new Dictionary<string,PlayerInfoClass>(); // 玩家信息

    // 上次摸牌类型
    public string lastDealTileType; // 上次摸牌类型

    // 立直麻将专属字段
    public int honba; // 本场棒数
    public int riichiSticks; // 场供立直棒数
    public List<int> doraIndicators = new List<int>(); // 初始宝牌指示牌
    public List<int> kanDoraIndicators = new List<int>(); // 杠宝牌指示牌
    public string hepaiWay; // 和牌方式 head_bump / multi_ron / three_ron_abort
    public bool redDora; // 是否启用赤宝牌
    public int dealerIndex; // 当前亲家索引

    // 调试用 于编辑器显示玩家信息列表
    [SerializeField]
    public List<PlayerInfoClass> playerInfoList = new List<PlayerInfoClass>(); // 玩家信息列表

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        player_to_info["self"] = new PlayerInfoClass();
        player_to_info["left"] = new PlayerInfoClass();
        player_to_info["top"] = new PlayerInfoClass();
        player_to_info["right"] = new PlayerInfoClass();
        // 调试用 显示玩家信息列表
        playerInfoList.Add(player_to_info["self"]);
        playerInfoList.Add(player_to_info["left"]);
        playerInfoList.Add(player_to_info["top"]);
        playerInfoList.Add(player_to_info["right"]);
    }

    // 初始化游戏
    public void InitializeGame(bool success, string message, GameInfo gameInfo){
        // 保存room_id（用于房间相关操作）
        UserDataManager.Instance.SetRoomId(gameInfo.room_id.ToString());
        // 保存gamestate_id
        UserDataManager.Instance.SetGamestateId(gameInfo.gamestate_id);

        gamestateId = gameInfo.gamestate_id;
        // 0.切换窗口
        WindowsManager.Instance.SwitchWindow("game"); // 切换到游戏场景

        Game3DManager.Instance.Clear3DTile(); // 清空3D手牌

        InitializeSetInfo(gameInfo); // 初始化对局数据
        GameCanvas.Instance.InitializeUIInfo(gameInfo,indexToPosition); // 初始化面板信息
        BoardCanvas.Instance.InitializeBoardInfo(gameInfo,indexToPosition); // 初始化桌面信息

        // 获取自己的手牌信息（从 PlayerInfo 中获取）
        PlayerInfo selfPlayerInfo = GetSelfPlayerInfo(gameInfo);
        int[] selfHandTilesArray = selfPlayerInfo.hand_tiles;
        
        // 初始化手牌区域
        GameCanvas.Instance.ChangeHandCards("InitHandCards",0,selfHandTilesArray,null);

        // 初始化他人手牌区域
        Game3DManager.Instance.Change3DTile("InitHandCards",0,0,null,false,null);

        // 初始化游戏开始UI（包括自动行为组件）
        GameSceneUIManager.Instance.InitGameStart();

        // InitGameStart内的HideGameRecord 会将鼠标输入置为 Idle，在此之后进入对局输入模式
        GameSceneMouseInputController.Instance.SetState(GameSceneMouseInputController.StateGame);

        // 根据对局信息生成他家已有的3D卡牌（弃牌、副露、花牌）
        GenerateOtherPlayers3DTiles(gameInfo);

        // 重连/初始化时：tag_list 中含 riichi/daburu_riichi 的玩家直接放置立直棒（无飞行动画）
        RestoreRiichiTenbous(gameInfo);

        IsGameActive = true;
        IsSelfActionRequired = false;
    }

    private void RestoreRiichiTenbous(GameInfo gameInfo){
        if (gameInfo == null || gameInfo.players_info == null) return;
        foreach (var player in gameInfo.players_info){
            if (player.tag_list == null || !indexToPosition.ContainsKey(player.player_index)) continue;
            for (int i = 0; i < player.tag_list.Length; i++){
                if (player.tag_list[i] == "riichi" || player.tag_list[i] == "daburu_riichi"){
                    Game3DManager.Instance.PlaceRiichiTenbouAt(indexToPosition[player.player_index]);
                    break;
                }
            }
        }
    }


    /// <summary>立直麻将：当前自家可立直切牌候选 {tile_id: [waiting_tile_id, ...]}。</summary>
    public Dictionary<int, int[]> selfRiichiCandidateCuts = new Dictionary<int, int[]>();
    /// <summary>立直麻将：当前自家本巡食替禁切牌列表（吃来源 + 两面搭子的筋）。</summary>
    public HashSet<int> selfForbiddenCutTiles = new HashSet<int>();

    // 询问手牌操作 手牌操作包括 切牌 补花 胡 暗杠 加杠
    public void AskHandAction(int remaining_time, int playerIndex, int remain_tiles, string[] action_list,
                              Dictionary<int, int[]> riichi_candidate_cuts = null, int[] forbidden_cut_tiles = null) {
        string GetCardPlayer = indexToPosition[playerIndex];
        // 立直麻将自家手牌可点状态依据：每次询问刷新
        selfRiichiCandidateCuts = riichi_candidate_cuts ?? new Dictionary<int, int[]>();
        selfForbiddenCutTiles = forbidden_cut_tiles != null
            ? new HashSet<int>(forbidden_cut_tiles)
            : new HashSet<int>();
        // 如果行动者是自己
        if (playerIndex == selfIndex){
            // 存储全部可用行动；riichi_cut 在 UI 上以「立直」按钮展示
            string[] AllowHandActionCheck = new string[] {"cut", "buhua", "hu_self" , "angang", "jiagang", "jiuzhongjiupai", "riichi_cut", "pass"};
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
    public void AskMingPaiAction(int remaining_time,string[] action_list,int cut_tile, Dictionary<string, int[][]> chi_candidates = null){
        // 立直麻将涉赤 5 的吃牌候选（键：方向，值：每条候选两张真实牌 ID）
        chiCandidates = chi_candidates ?? new Dictionary<string, int[][]>();
        // 询问鸣牌时不属于自家手牌行动阶段，清空立直/食替缓存防止旧值干扰可点状态
        selfRiichiCandidateCuts.Clear();
        selfForbiddenCutTiles.Clear();
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

    /// <summary>
    /// 当前一轮询问切牌后操作下发的吃牌候选（立直麻将赤宝牌场景）。
    /// </summary>
    public Dictionary<string, int[][]> chiCandidates = new Dictionary<string, int[][]>();

    // 执行行动
    public void DoAction(string[] action_list, int action_player, int? cut_tile, int? cut_tile_index, bool? cut_class, int? deal_tile, int? buhua_tile, int[] combination_mask,string combination_target, bool? is_riichi_horizontal = null, bool isClaim = false, bool isSilent = false) {
        string GetCardPlayer = indexToPosition[action_player]; // 获取执行操作的玩家位置
        bool isRiichiHorizontalCut = is_riichi_horizontal == true;
        if (isClaim) {
            // 战术鸣牌申请：仅播放发声+字体动画+战术倒计时面板，不改变任何游戏状态
            HandleTacticalClaim(GetCardPlayer, action_list);
            return;
        }
        if (isSilent) {
            // 战术鸣牌静默实际行为：申请阶段已发声/动画，本次仅同步状态
            TacticalCallPanel.Instance.HidePanel();
        }
        foreach (string action in action_list) {

            Debug.Log($"执行DoAction操作: {action} (silent={isSilent})");
            if (!isSilent) {
                SoundManager.Instance.PlayActionSound(GetCardPlayer, action); // 播放操作音效
                SoundManager.Instance.PlayPhysicsSound(action); // 播放物理音效
            }
            switch (action) { // action_list 实际上只会包含一个操作

                // 摸牌（普通摸牌 / 杠后摸牌 / 补花后摸牌）
                case "deal_tile":
                case "deal_gang_tile":
                case "deal_buhua_tile":
                    lastDealTileType = action;
                    remainTiles--; // 剩余牌数减少
                    if (GetCardPlayer == "self"){     // 添加手牌 显示手牌
                        selfHandTiles.Add(deal_tile.Value);
                        GameCanvas.Instance.ChangeHandCards("GetCard", deal_tile.Value, null, null);
                        Game3DManager.Instance.Change3DTile("GetCard", deal_tile.Value, 0, GetCardPlayer, false, null);
                    }
                    else{                             // 增加手牌 显示3D手牌
                        player_to_info[GetCardPlayer].hand_tiles_count++;
                        Game3DManager.Instance.Change3DTile("GetCard", deal_tile.Value, 0, GetCardPlayer, false, null);
                    }
                    break;
                
                // 切牌
                case "cut": 
                    lastCutCardID = cut_tile.Value; // 存储上次切牌的ID
                    player_to_info[GetCardPlayer].discard_tiles.Add(cut_tile.Value); // 存储弃牌
                    // 同步保存横置标记，用于他家鸣牌后下一张续横、重连/牌谱重建时还原立直横置弃牌
                    player_to_info[GetCardPlayer].discard_riichi_flags.Add(isRiichiHorizontalCut);
                    if (GetCardPlayer == "self"){
                        selfHandTiles.Remove(cut_tile.Value); // 删除手牌
                        Game3DManager.Instance.Change3DTile("Discard",cut_tile.Value,0,GetCardPlayer,cut_class.Value,null,isRiichiHorizontalCut); // 3D切牌行为
                        if (cut_class.Value){
                            GameCanvas.Instance.ChangeHandCards("RemoveGetCard",cut_tile.Value,null,null); // 2D摸切行为
                        }
                        else{
                            GameCanvas.Instance.ChangeHandCards("RemoveHandCard",cut_tile.Value,null,cut_tile_index.Value); // 2D手切行为
                        }
                    }
                    else{
                        player_to_info[GetCardPlayer].hand_tiles_count--; // 减少手牌
                        Game3DManager.Instance.Change3DTile("Discard",cut_tile.Value,0,GetCardPlayer,cut_class.Value,null,isRiichiHorizontalCut); // 3D切牌行为
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
                    if (!isSilent) GameCanvas.Instance.ShowActionDisplay(GetCardPlayer, "buhua"); // 显示操作文本
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
                            GameCanvas.Instance.ChangeHandCards("RemoveJiagangCard",tile_id,null,null); // 2D加杠行为
                        }
                        else{
                            player_to_info[GetCardPlayer].hand_tiles_count -= 1; // 减少手牌
                        }
                        Game3DManager.Instance.Change3DTile("jiagang",tile_id,1,GetCardPlayer,false,combination_mask); // 3D加杠行为
                        if (!isSilent) GameCanvas.Instance.ShowActionDisplay(GetCardPlayer, "jiagang"); // 显示操作文本
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
                                player_to_info[GetCardPlayer].hand_tiles_count -= 4; // 减少手牌
                            }
                        }
                        Game3DManager.Instance.Change3DTile(action,0,4,GetCardPlayer,false,combination_mask); // 3D暗杠行为
                        if (!isSilent) GameCanvas.Instance.ShowActionDisplay(GetCardPlayer, "angang"); // 显示操作文本
                    }
                    else if (action == "gang"){
                        // 杠情况下需要删除3张手牌（相对于暗杠少删一张）
                        player_to_info[GetCardPlayer].combination_tiles.Add(combination_target); // 存储组合牌
                        List<int> need_remove_list = combination_mask.Where(x => x > 10).ToList(); // 获取组合牌列表
                        need_remove_list.RemoveAt(need_remove_list.Count - 1); // 删除一张牌（杠相对于暗杠少删一张）
                        foreach (int tile_id in need_remove_list){
                            if (GetCardPlayer == "self"){
                                selfHandTiles.Remove(tile_id); // 删除手牌
                                GameCanvas.Instance.ChangeHandCards("RemoveCombinationCard",0,need_remove_list.ToArray(),null); // 2D手牌行为
                            }
                            else{
                                player_to_info[GetCardPlayer].hand_tiles_count -= 3; // 减少手牌（3张）
                            }
                        }
                        Game3DManager.Instance.Change3DTile(action,0,3,GetCardPlayer,false,combination_mask); // 3D杠行为
                        if (!isSilent) GameCanvas.Instance.ShowActionDisplay(GetCardPlayer, "gang"); // 显示操作文本
                    }
                    else{
                        // 正常情况 "chi_left" "chi_mid" "chi_right" "peng" "gang" 均为场地魔法 需要剔除上次切牌的ID
                        player_to_info[CurrentPlayer].discard_tiles.Remove(lastCutCardID); // 剔除上次切牌的ID
                        // 同步移除最后一张弃牌的横置标记，与服务器 discard_riichi_flags.pop(-1) 行为一致
                        if (player_to_info[CurrentPlayer].discard_riichi_flags.Count > 0){
                            player_to_info[CurrentPlayer].discard_riichi_flags.RemoveAt(player_to_info[CurrentPlayer].discard_riichi_flags.Count - 1);
                        }
                        player_to_info[CurrentPlayer].discard_origin_tiles.Add(lastCutCardID); // 添加上次切牌的理论弃牌

                        player_to_info[GetCardPlayer].combination_tiles.Add(combination_target); // 存储组合牌
                        List<int> need_remove_list = combination_mask.Where(x => x > 10).ToList(); // 获取组合牌列表
                        need_remove_list.Remove(lastCutCardID); // 剔除上次切牌的ID
                        foreach (int tile_id in need_remove_list){
                            if (GetCardPlayer == "self"){
                                selfHandTiles.Remove(tile_id); // 删除手牌
                            }
                            else{
                                player_to_info[GetCardPlayer].hand_tiles_count -= 1; // 减少手牌
                            }
                        }
                        if (GetCardPlayer == "self"){
                            GameCanvas.Instance.ChangeHandCards("RemoveCombinationCard",0,need_remove_list.ToArray(),null); // 2D手牌行为
                        }
                        Game3DManager.Instance.Change3DTile(action,0,need_remove_list.Count,GetCardPlayer,false,combination_mask); // 3D吃碰杠行为
                        if (!isSilent) GameCanvas.Instance.ShowActionDisplay(GetCardPlayer, action); // 显示操作文本
                    }
                    break;
                default:
                    Debug.Log($"未知操作: {action}");
                    break;
            }
        }
        player_to_info["self"].hand_tiles_count = selfHandTiles.Count;
        // 切换行动者
        SwitchCurrentPlayer(GetCardPlayer,"doAction",0);
    }

    // 回合结束 和牌 流局
    public void ShowResult(int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class, int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask, int? base_fu = null, string[] fu_fan_list = null, RiichiEndResultExtras riichiExtras = null, bool isSilent = false) {
        // 重置自身命令
        SwitchCurrentPlayer("None","ClearAction",0);
        // 隐藏和牌提示
        TipsBlock.Instance.HideTipsBlock();
        TipsContainer.Instance.HideTips();
        if (isSilent) {
            // 战术鸣牌：和牌字体动画/音效已在申请阶段播放，结算时直接关闭面板
            TacticalCallPanel.Instance.HidePanel();
        }
        // 显示结算结果
        if (hu_class == "liuju") {
            RoundEndFlowManager.Instance.PresentLiuju("流局");
        } else if (hu_class == "ryuukyoku") {
            foreach (var kvp in indexToPosition) {
                int idx = kvp.Key;
                string pos = kvp.Value;
                if (player_to_score != null && player_to_score.ContainsKey(idx) && player_to_info.ContainsKey(pos)) {
                    player_to_info[pos].score = player_to_score[idx];
                }
            }
            BoardCanvas.Instance.UpdatePlayerScores(player_to_score, indexToPosition);
            RoundEndFlowManager.Instance.PresentDrawWallLiujuAndPenalty("荒牌流局", player_to_score, riichiExtras);
        } else if (IsRiichiAbortHuClass(hu_class)) {
            RoundEndFlowManager.Instance.PresentRiichiAbortPenalty(player_to_score, riichiExtras);
        } else if (hu_class == "jiuzhongjiupai") {
            RoundEndFlowManager.Instance.PresentLiuju("九老峰回");
        } else {
            RoundEndFlowManager.Instance.PresentHuResultSequence(hepai_player_index, player_to_score, hu_score, hu_fan, hu_class, hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask, base_fu, fu_fan_list, riichiExtras, isSilent);
        }
        // 更新分数记录
        GameSceneUIManager.Instance.UpdateScoreRecord();
    }

    // 战术鸣牌：处理 is_claim 申请广播
    // 仅播放发声、字体动画并启动战术鸣牌面板倒计时，不修改任何游戏状态
    private void HandleTacticalClaim(string actor, string[] action_list) {
        TacticalCallPanel.Instance.ShowClaim();
        foreach (string action in action_list) {
            SoundManager.Instance.PlayActionSound(actor, action);
            SoundManager.Instance.PlayPhysicsSound(action);
            // ShowActionDisplay 已正确处理 hu_first/hu_second/hu_third/hu_self 并显示为「胡」
            GameCanvas.Instance.ShowActionDisplay(actor, action);
        }
    }

    // 判断日麻流局类 hu_class
    private static bool IsRiichiAbortHuClass(string hu_class) {
        return hu_class == "four_kan_abort"
            || hu_class == "four_wind_abort"
            || hu_class == "four_riichi_abort"
            || hu_class == "three_ron_abort";
    }

    // 数和尾结算
    public void ShowShuhewei(
        Dictionary<int, int> player_fu,
        Dictionary<int, int> player_to_score,
        Dictionary<int, int> score_changes,
        Dictionary<int, string[]> player_fan,
        Dictionary<int, string[]> player_fu_types,
        string hu_class,
        int? hepai_player_index
    ) {
        EndResultPanel.Instance.ClearEndResultPanel();
        if (!string.IsNullOrEmpty(hu_class) && hu_class != "liuju" && hu_class != "jiuzhongjiupai" && hepai_player_index.HasValue && indexToPosition.ContainsKey(hepai_player_index.Value)) {
            string huPos = indexToPosition[hepai_player_index.Value];
            GameCanvas.Instance.ShowActionDisplay(huPos, hu_class);
            SoundManager.Instance.PlayActionSound(huPos, hu_class);
        }
        // 更新计分板
        BoardCanvas.Instance.UpdatePlayerScores(player_to_score, indexToPosition);
        // 更新本地分数记录
        foreach (var kvp in player_to_score) {
            string pos = indexToPosition.ContainsKey(kvp.Key) ? indexToPosition[kvp.Key] : null;
            if (pos != null && player_to_info.ContainsKey(pos)) {
                player_to_info[pos].score = kvp.Value;
            }
        }
        RoundEndFlowManager.Instance.PresentShuhewei(player_fu, player_to_score, score_changes, player_fan, player_fu_types, indexToPosition, player_to_info);
    }

    // 执行换位
    public void HandleSwitchSeat(int current_round){
        // 显示换位动画
        GameSceneUIManager.Instance.ShowSwitchSeat(current_round);
    }

    // 刷新玩家标签列表 更新掉线,陪打等状态
    public void RefreshPlayerTagList(Dictionary<int, string[]> player_to_tag_list){
        // 更新所有玩家的标签列表
        foreach (var kvp in player_to_tag_list){
            int player_index = kvp.Key;
            string[] tag_list = kvp.Value;
            
            // 根据 player_index 找到对应的玩家位置
            if (indexToPosition.ContainsKey(player_index)){
                string position = indexToPosition[player_index];
                if (player_to_info.ContainsKey(position)){
                    player_to_info[position].tag_list = tag_list;
                    Debug.Log($"更新玩家 {position} (索引 {player_index}) 的标签列表: {string.Join(", ", tag_list)}");
                }
            }
        }
        
        // 更新 GameCanvas 中的玩家面板显示
        GameCanvas.Instance.UpdatePlayerTagList(player_to_tag_list);
    }

    // 游戏结束
    public void GameEnd(long game_random_seed, Dictionary<string, Dictionary<string, object>> player_final_data){
        // 重置自身命令
        SwitchCurrentPlayer("None","ClearAction",0);
        IsGameActive = false;
        IsSelfActionRequired = false;
        RoundEndFlowManager.Instance.PresentEndGame(game_random_seed, player_final_data);
    }

    // 切换玩家状态
    public void SwitchCurrentPlayer(string GetCardPlayer,string SwitchType,int remaining_time){
        
        // 询问手牌操作
        if (SwitchType == "askHandAction"){
            // 如果有人3d卡牌未排列 则排列 (仅在国标补花轮之后可能出现这样的问题)
            Game3DManager.Instance.CheckAndRearrangeAllPlayersHandCards();
            // 如果行动者是自己
            if (GetCardPlayer == "self"){
                // 显示可用行动 开启倒计时
                GameCanvas.Instance.ClearActionButton(); // 清空操作按钮 *有时候补花轮自己不补花，但是别人也不补，就出现两次按钮
                GameCanvas.Instance.SetActionButton(allowActionList);
                GameCanvas.Instance.LoadingRemianTime(remaining_time,roomStepTime);
                // 立直锁手 / 食替禁切：每次询问立刻刷新自家手牌的可点状态与变灰显示
                GameCanvas.Instance.RefreshHandTileSelectability();
                // 如果开启自动胡牌、自动补花或者自动出牌，则启动协程
                if (AutoAction.Instance.IsAutoHepai || AutoAction.Instance.IsAutoBuhua || AutoAction.Instance.IsAutoCut){
                    StartCoroutine(WaitAutoAction("AutoHandAction"));
                }
                // 询问操作时隐藏提示块
                TipsBlock.Instance.HideTipsBlock();
                TipsContainer.Instance.HideTips();
                IsSelfActionRequired = true;
                GameSceneMouseInputController.Instance.SetActionInputPhase(GameSceneMouseInputController.InputPhaseAskHand);
            }
            // 询问的不是自己的回合
            else{
                GameCanvas.Instance.ChangeHandCards("ReSetHandCards",0,null,null); // 重置手牌
                SwitchCurrentPlayer(GetCardPlayer,"ClearAction",0); // 重置自身命令
                IsSelfActionRequired = false;
            }
            // 只有askHandAction才会转移玩家位置
            BoardCanvas.Instance.ShowCurrentPlayer(GetCardPlayer, remainTiles); // 显示当前玩家
            CurrentPlayer = GetCardPlayer; // 存储当前玩家
        }

        // 询问鸣牌操作 鸣牌操作的操作方一定是"self"
        else if (SwitchType == "askMingPaiAction"){
            GameCanvas.Instance.SetActionButton(allowActionList);
            GameCanvas.Instance.LoadingRemianTime(remaining_time,roomStepTime);
            // 如果开启自动过牌或自动胡牌，则启动协程
            if (AutoAction.Instance.IsAutoPass || AutoAction.Instance.IsAutoHepai || AutoAction.Instance.IsAutoPassChi || AutoAction.Instance.IsAutoPassPeng || AutoAction.Instance.IsAutoPassGang){
                StartCoroutine(WaitAutoAction("AutoMingPaiAction"));
            }
            IsSelfActionRequired = true;
            GameSceneMouseInputController.Instance.SetActionInputPhase(GameSceneMouseInputController.InputPhaseAskOther);
        }

        // 执行行动
        else if (SwitchType == "doAction"){
            GameSceneMouseInputController.Instance.SetActionInputPhase(GameSceneMouseInputController.InputPhaseNone);
            Debug.Log($"doAction行动者: {GetCardPlayer}");
            // 如果行动者是自己
            if (GetCardPlayer == "self"){
                // 停止计时器
                GameCanvas.Instance.StopTimeRunning();
                // 清空允许操作列表
                allowActionList.Clear();
                // 清空按钮
                GameCanvas.Instance.ClearActionButton();
                // 切牌后退出立直选牌模式（超时被迫切牌时同样会走到这里），并清空食替禁切
                if (RiichiCutSelectionController.Instance != null) RiichiCutSelectionController.Instance.ExitRiichiCutMode();
                selfRiichiCandidateCuts.Clear();
                selfForbiddenCutTiles.Clear();
                // 立刻恢复手牌正常颜色，避免用户看到禁切灰色滞留到下一轮询问
                GameCanvas.Instance.RefreshHandTileSelectability();
                // 在自己执行操作以后计算听牌提示，如果有提示就显示右侧提示块
                if (tips){
                    TipsBlock.Instance.ShowTipsBlock(selfHandTiles, player_to_info["self"].combination_tiles);
                }
                IsSelfActionRequired = false;
            }
        }

        // 选择行动
        else if (SwitchType == "ClearAction"){
            // 停止计时器
            GameCanvas.Instance.StopTimeRunning();
            // 清空操作按钮
            GameCanvas.Instance.ClearActionButton();
            // 清空允许操作列表与立直/食替缓存
            allowActionList.Clear();
            selfRiichiCandidateCuts.Clear();
            selfForbiddenCutTiles.Clear();
            if (RiichiCutSelectionController.Instance != null) RiichiCutSelectionController.Instance.ExitRiichiCutMode();
            IsSelfActionRequired = false;
            GameSceneMouseInputController.Instance.SetActionInputPhase(GameSceneMouseInputController.InputPhaseNone);
        }

        // 时间耗尽
        else if (SwitchType == "TimeOut"){
            // 清空操作按钮
            GameCanvas.Instance.ClearActionButton();
            if (RiichiCutSelectionController.Instance != null) RiichiCutSelectionController.Instance.ExitRiichiCutMode();
            IsSelfActionRequired = false;
            GameSceneMouseInputController.Instance.SetActionInputPhase(GameSceneMouseInputController.InputPhaseNone);
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
            if (AutoAction.Instance.IsAutoHepai){
                // 如果允许操作列表中有和牌，则执行自动胡牌
                if (!string.IsNullOrEmpty(actualHupaiAction)){
                    yield return new WaitForSeconds(0.2f);
                    GameCanvas.Instance.ChooseAction(actualHupaiAction, 0);
                    yield return null;
                }
            }

            // 如果和牌列表为空（没有可用的和牌操作）
            if (string.IsNullOrEmpty(actualHupaiAction)){
                // 如果开启自动过牌，则直接pass
                if (AutoAction.Instance.IsAutoPass){
                    yield return new WaitForSeconds(0.2f); // (如果玩家后悔了，希望玩家手速够快)
                    GameCanvas.Instance.ChooseAction("pass", 0);
                    yield return null;
                }
                else{
                    // 按不吃/不碰/不杠筛选：不吃排除 chi_*，不碰排除 peng，不杠排除 gang
                    // 如果筛选后剩余操作列表为空（或只剩 pass），则自动pass
                    List<string> remaining = new List<string>(allowActionList);
                    if (AutoAction.Instance.IsAutoPassChi)
                        remaining.RemoveAll(a => a == "chi_left" || a == "chi_mid" || a == "chi_right");
                    if (AutoAction.Instance.IsAutoPassPeng)
                        remaining.RemoveAll(a => a == "peng");
                    if (AutoAction.Instance.IsAutoPassGang)
                        remaining.RemoveAll(a => a == "gang");
                    remaining.RemoveAll(a => a == "pass");
                    if (remaining.Count == 0){
                        yield return new WaitForSeconds(0.2f);
                        GameCanvas.Instance.ChooseAction("pass", 0);
                        yield return null;
                    }
                }
            }

            yield return null;
        }

        // 手牌操作自动执行
        else if (action == "AutoHandAction"){
            // 如果上次摸牌类型是杠牌，不执行任何自动操作
            if (lastDealTileType == "deal_gang_tile"){
                yield return null;
            }
            // 如果允许操作列表有hu_self
            if (allowActionList.Contains("hu_self")){
                // 如果开启自动胡牌，则执行自动胡牌
                if (AutoAction.Instance.IsAutoHepai){
                    yield return new WaitForSeconds(0.2f);
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
                if (AutoAction.Instance.IsAutoBuhua){
                    yield return new WaitForSeconds(0.2f);
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
                if (AutoAction.Instance.IsAutoCut){
                    yield return new WaitForSeconds(0.3f);
                    // 自动出牌 选择手牌中最近摸到的牌（列表的最后一张）
                    if (selfHandTiles != null && selfHandTiles.Count > 0) {
                        int lastTileId = selfHandTiles[selfHandTiles.Count - 1];
                        // 查找对应的 TileCard 并触发点击（优先摸切，否则手切）
                        bool success = GameCanvas.Instance.TriggerTileCardClick(lastTileId);
                        if (!success) {
                            Debug.LogWarning($"自动出牌失败：无法找到牌ID {lastTileId} 对应的 TileCard");
                        }
                    } else {
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

    // 生成其他玩家的3D卡牌（弃牌、副露、花牌）
    private void GenerateOtherPlayers3DTiles(GameInfo gameInfo){
        // 遍历所有玩家
        foreach (var player in gameInfo.players_info){
            if (!indexToPosition.ContainsKey(player.player_index)) continue;
            string position = indexToPosition[player.player_index];
            
            // 1. 生成弃牌（同时复原立直横置标记，重连/初始化时与服务器一致）
            if (player.discard_tiles != null && player.discard_tiles.Length > 0){
                bool[] flags = player.discard_riichi_flags;
                for (int idx = 0; idx < player.discard_tiles.Length; idx++){
                    int tileId = player.discard_tiles[idx];
                    bool horizontal = flags != null && idx < flags.Length && flags[idx];
                    Game3DManager.Instance.Change3DTile("SetDiscardWithoutAnimation", tileId, 0, position, false, null, horizontal);
                    Debug.Log($"生成弃牌: {tileId} horizontal={horizontal}");
                }
            }
            
            // 2. 生成花牌
            if (player.huapai_list != null && player.huapai_list.Length > 0){
                foreach (int tileId in player.huapai_list){
                    Game3DManager.Instance.Change3DTile("SetBuhuacardWithoutAnimation", tileId, 0, position, false, null);
                }
            }
            
            // 3. 生成副露（组合牌）
            // 直接遍历副露列表和掩码，调用 ActionAnimation 显示
            // 手牌数量已经反映了副露消耗，不需要再做移除操作
            if (player.combination_tiles != null && player.combination_tiles.Length > 0 &&
                player.combination_mask != null && player.combination_mask.Length > 0){
                
                // 遍历每个副露组合，直接使用二维数组中的每个子数组
                for (int i = 0; i < player.combination_tiles.Length && i < player.combination_mask.Length; i++){
                    string combinationStr = player.combination_tiles[i];
                    if (string.IsNullOrEmpty(combinationStr) || combinationStr.Length < 2) continue;
                    
                    int[] combinationMask = player.combination_mask[i];
                    if (combinationMask == null || combinationMask.Length == 0) continue;
                    
                    // 统计掩码中加杠牌（值为3）的数量
                    int jiagangCount = 0;
                    foreach (int value in combinationMask){
                        if (value == 3){
                            jiagangCount++;
                        }
                    }
                    
                    // 如果 combination_tiles 的字符串有 "k"（刻子/碰），传入 "peng"
                    if (combinationStr.Contains("k")){
                        Game3DManager.Instance.StartCoroutine(Game3DManager.Instance.ActionAnimationCoroutine(position, "peng", combinationMask, false));
                    }
                    // 如果 combination_mask 中有 "3"（加杠），说明是碰后加杠的情况
                    // 需要先调用 "peng" 再调用 "jiagang"，确保 pengToJiagangPosDict 正确缓存
                    else if (jiagangCount > 0){
                        // 先调用 peng，创建碰牌并缓存横置位置
                        Game3DManager.Instance.StartCoroutine(Game3DManager.Instance.ActionAnimationCoroutine(position, "peng", combinationMask, false));
                        // 再调用 jiagang，在缓存的位置上添加加杠牌
                        Game3DManager.Instance.StartCoroutine(Game3DManager.Instance.ActionAnimationCoroutine(position, "jiagang", combinationMask, false));
                    }
                    else{
                        Game3DManager.Instance.StartCoroutine(Game3DManager.Instance.ActionAnimationCoroutine(position, "None", combinationMask, false));
                    }
                }
            }
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
        // 清空弃牌横置标记列表（立直规则）
        player_to_info["self"].discard_riichi_flags = new List<bool>();
        player_to_info["left"].discard_riichi_flags = new List<bool>();
        player_to_info["top"].discard_riichi_flags = new List<bool>();
        player_to_info["right"].discard_riichi_flags = new List<bool>();
        // 清空理论弃牌列表
        player_to_info["self"].discard_origin_tiles = new List<int>();
        player_to_info["left"].discard_origin_tiles = new List<int>();
        player_to_info["top"].discard_origin_tiles = new List<int>();
        player_to_info["right"].discard_origin_tiles = new List<int>();
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
        roomType = gameInfo.room_type;
        roomRule = gameInfo.room_rule;
        subRule = gameInfo.sub_rule;
        hepaiLimit = gameInfo.hepai_limit > 0 ? gameInfo.hepai_limit : 8; // 起和番限制，国标提示用
        roomStepTime = gameInfo.step_time; // 存储步时
        roomRoundTime = gameInfo.round_time; // 存储局时
        remainTiles = gameInfo.tile_count; // 存储剩余牌数
        currentRound = gameInfo.current_round; // 存储当前轮数
        
        // 获取自己的手牌信息（从 PlayerInfo 中获取）
        PlayerInfo selfPlayerInfo = GetSelfPlayerInfo(gameInfo);
        if (selfPlayerInfo != null && selfPlayerInfo.hand_tiles != null){
            selfHandTiles = selfPlayerInfo.hand_tiles.ToList();
        } else {
            selfHandTiles = new List<int>();
        }
        player_to_info["self"].hand_tiles_count = selfHandTiles.Count;

        tips = gameInfo.tips; // 存储是否提示
        isOpenCuoHe = gameInfo.open_cuohe; // 存储是否开启错和
        isSetRandomSeed = gameInfo.isPlayerSetRandomSeed; // 存储是否设置随机种子

        // 立直麻将字段同步：服务端未下发时使用默认值
        honba = gameInfo.honba ?? 0;
        riichiSticks = gameInfo.riichi_sticks ?? 0;
        doraIndicators = gameInfo.dora_indicators != null ? new List<int>(gameInfo.dora_indicators) : new List<int>();
        kanDoraIndicators = gameInfo.kan_dora_indicators != null ? new List<int>(gameInfo.kan_dora_indicators) : new List<int>();
        hepaiWay = gameInfo.hepai_way ?? "head_bump";
        redDora = gameInfo.red_dora ?? false;
        dealerIndex = gameInfo.dealer_index ?? 0;
        if (isOpenCuoHe){
            Debug.Log("开启错和");
        }
        else{
            Debug.Log("关闭错和");
        }
        if (isSetRandomSeed){
            Debug.Log("设置随机种子");
        }
        else{
            Debug.Log("未设置随机种子");
        }
        // 根据自身索引确定其他玩家位置
        if (selfIndex == 0) {
            indexToPosition[0] = "self";
            indexToPosition[1] = "right";
            indexToPosition[2] = "top";
            indexToPosition[3] = "left";
        } else if (selfIndex == 1) {
            indexToPosition[1] = "self";
            indexToPosition[2] = "right";
            indexToPosition[3] = "top";
            indexToPosition[0] = "left";
        } else if (selfIndex == 2) {
            indexToPosition[2] = "self";
            indexToPosition[3] = "right";
            indexToPosition[0] = "top";
            indexToPosition[1] = "left";
        } else if (selfIndex == 3) {
            indexToPosition[3] = "self";
            indexToPosition[0] = "right";
            indexToPosition[1] = "top";
            indexToPosition[2] = "left";
        }
        foreach (var player in gameInfo.players_info){
            if (indexToPosition[player.player_index] == "self") { // 通过player_index确定玩家位置
                player_to_info["self"].username = player.username; // 存储用户名
                player_to_info["self"].userId = player.user_id; // 存储uid
                player_to_info["self"].score = player.score; // 存储分数
                // 存储剩余时间
                selfRemainingTime = player.remaining_time;
                player_to_info["self"].discard_tiles = player.discard_tiles.ToList(); // 存储弃牌列表
                player_to_info["self"].discard_riichi_flags = player.discard_riichi_flags != null ? new List<bool>(player.discard_riichi_flags) : new List<bool>();
                player_to_info["self"].discard_origin_tiles = player.discard_origin_tiles.ToList(); // 存储理论弃牌列表
                player_to_info["self"].combination_tiles = player.combination_tiles.ToList(); // 存储组合牌列表
                player_to_info["self"].huapai_list = player.huapai_list.ToList(); // 存储花牌列表
                player_to_info["self"].title_used = player.title_used; // 存储使用的称号ID
                player_to_info["self"].profile_used = player.profile_used; // 存储使用的头像ID
                player_to_info["self"].character_used = player.character_used; // 存储使用的角色ID
                player_to_info["self"].voice_used = player.voice_used; // 存储使用的音色ID
                player_to_info["self"].score_history = player.score_history.ToList(); // 存储分数历史变化列表
                player_to_info["self"].round_number_history = player.round_number_history != null ? player.round_number_history.ToList() : new List<int>(); // 存储每手对应局数
                player_to_info["self"].original_player_index = player.original_player_index; // 存储原始玩家索引
                player_to_info["self"].tag_list = player.tag_list; // 存储标签列表
            } else if (indexToPosition[player.player_index] == "right") {
                player_to_info["right"].username = player.username; // 存储用户名
                player_to_info["right"].score = player.score; // 存储分数
                player_to_info["right"].userId = player.user_id; // 存储uid
                player_to_info["right"].discard_tiles = player.discard_tiles.ToList(); // 存储弃牌列表
                player_to_info["right"].discard_riichi_flags = player.discard_riichi_flags != null ? new List<bool>(player.discard_riichi_flags) : new List<bool>();
                player_to_info["right"].discard_origin_tiles = player.discard_origin_tiles.ToList(); // 存储理论弃牌列表
                player_to_info["right"].combination_tiles = player.combination_tiles.ToList(); // 存储组合牌列表
                player_to_info["right"].huapai_list = player.huapai_list.ToList(); // 存储花牌列表
                player_to_info["right"].hand_tiles_count = player.hand_tiles_count; // 存储手牌数量
                player_to_info["right"].title_used = player.title_used; // 存储使用的称号ID
                player_to_info["right"].profile_used = player.profile_used; // 存储使用的头像ID
                player_to_info["right"].character_used = player.character_used; // 存储使用的角色ID
                player_to_info["right"].voice_used = player.voice_used; // 存储使用的音色ID
                player_to_info["right"].score_history = player.score_history.ToList(); // 存储分数历史变化列表
                player_to_info["right"].round_number_history = player.round_number_history != null ? player.round_number_history.ToList() : new List<int>(); // 存储每手对应局数
                player_to_info["right"].original_player_index = player.original_player_index; // 存储原始玩家索引
                player_to_info["right"].tag_list = player.tag_list; // 存储标签列表
            } else if (indexToPosition[player.player_index] == "top") {
                player_to_info["top"].username = player.username; // 存储用户名
                player_to_info["top"].score = player.score; // 存储分数
                player_to_info["top"].userId = player.user_id; // 存储uid
                player_to_info["top"].discard_tiles = player.discard_tiles.ToList(); // 存储弃牌列表
                player_to_info["top"].discard_riichi_flags = player.discard_riichi_flags != null ? new List<bool>(player.discard_riichi_flags) : new List<bool>();
                player_to_info["top"].discard_origin_tiles = player.discard_origin_tiles.ToList(); // 存储理论弃牌列表
                player_to_info["top"].combination_tiles = player.combination_tiles.ToList(); // 存储组合牌列表
                player_to_info["top"].huapai_list = player.huapai_list.ToList(); // 存储花牌列表
                player_to_info["top"].hand_tiles_count = player.hand_tiles_count; // 存储手牌数量
                player_to_info["top"].title_used = player.title_used; // 存储使用的称号ID
                player_to_info["top"].profile_used = player.profile_used; // 存储使用的头像ID
                player_to_info["top"].character_used = player.character_used; // 存储使用的角色ID
                player_to_info["top"].voice_used = player.voice_used; // 存储使用的音色ID
                player_to_info["top"].score_history = player.score_history.ToList(); // 存储分数历史变化列表
                player_to_info["top"].round_number_history = player.round_number_history != null ? player.round_number_history.ToList() : new List<int>(); // 存储每手对应局数
                player_to_info["top"].original_player_index = player.original_player_index; // 存储原始玩家索引
                player_to_info["top"].tag_list = player.tag_list; // 存储标签列表
            } else if (indexToPosition[player.player_index] == "left") {
                player_to_info["left"].username = player.username; // 存储用户名
                player_to_info["left"].score = player.score; // 存储分数
                player_to_info["left"].userId = player.user_id; // 存储uid
                player_to_info["left"].discard_tiles = player.discard_tiles.ToList(); // 存储弃牌列表
                player_to_info["left"].discard_riichi_flags = player.discard_riichi_flags != null ? new List<bool>(player.discard_riichi_flags) : new List<bool>();
                player_to_info["left"].discard_origin_tiles = player.discard_origin_tiles.ToList(); // 存储理论弃牌列表
                player_to_info["left"].combination_tiles = player.combination_tiles.ToList(); // 存储组合牌列表
                player_to_info["left"].huapai_list = player.huapai_list.ToList(); // 存储花牌列表
                player_to_info["left"].hand_tiles_count = player.hand_tiles_count; // 存储手牌数量
                player_to_info["left"].title_used = player.title_used; // 存储使用的称号ID
                player_to_info["left"].profile_used = player.profile_used; // 存储使用的头像ID
                player_to_info["left"].character_used = player.character_used; // 存储使用的角色ID
                player_to_info["left"].voice_used = player.voice_used; // 存储使用的音色ID
                player_to_info["left"].score_history = player.score_history.ToList(); // 存储分数历史变化列表
                player_to_info["left"].round_number_history = player.round_number_history != null ? player.round_number_history.ToList() : new List<int>(); // 存储每手对应局数
                player_to_info["left"].original_player_index = player.original_player_index; // 存储原始玩家索引
                player_to_info["left"].tag_list = player.tag_list; // 存储标签列表
            }
        }
    }

    // 获取自己的 PlayerInfo
    private PlayerInfo GetSelfPlayerInfo(GameInfo gameInfo){
        if (gameInfo.players_info == null) return null;
        
        int selfUserId = UserDataManager.Instance.UserId;
        foreach (var player in gameInfo.players_info){
            if (player.user_id == selfUserId){
                return player;
            }
        }
        return null;
    }

    /// <summary>
    /// 立直宣告广播处理：刷新玩家 tag_list、播放立直语音、立直棒从 outputPos 飞向 tenbouPos，
    /// 并按规则把场供立直棒 +1 同步到 RoundPanel；服务端的供托结算在 _commit_pending_riichi 与
    /// 和牌/抽水时处理，此处仅做客户端表现。
    /// </summary>
    public void OnRiichiDeclared(Dictionary<int, string[]> playerToTagList, int? riichiDeclaredPlayerIndex) {
        if (playerToTagList != null) {
            RefreshPlayerTagList(playerToTagList);
        }
        riichiSticks += 1;
        if (RoundPanel.Instance != null) {
            RoundPanel.Instance.RefreshRiichi(honba, riichiSticks, doraIndicators, kanDoraIndicators);
        }
        if (riichiDeclaredPlayerIndex.HasValue && indexToPosition.ContainsKey(riichiDeclaredPlayerIndex.Value)) {
            string pos = indexToPosition[riichiDeclaredPlayerIndex.Value];
            SoundManager.Instance.PlayRiichiVoice(pos);
            Game3DManager.Instance.PlayRiichiTenbouFlight(pos);
        }
    }

    /// <summary>
    /// 宝牌/杠宝牌翻开广播处理。
    /// </summary>
    public void OnDoraUpdated(GameInfo gameInfo) {
        if (gameInfo == null) return;
        OnDoraUpdated(gameInfo.dora_indicators, gameInfo.kan_dora_indicators);
    }

    public void OnDoraUpdated(int[] doraIndicatorsFromServer, int[] kanDoraIndicatorsFromServer) {
        if (doraIndicatorsFromServer != null) {
            doraIndicators = new List<int>(doraIndicatorsFromServer);
        }
        if (kanDoraIndicatorsFromServer != null) {
            kanDoraIndicators = new List<int>(kanDoraIndicatorsFromServer);
        }
        if (RoundPanel.Instance != null) {
            RoundPanel.Instance.RefreshRiichi(honba, riichiSticks, doraIndicators, kanDoraIndicators);
        }
    }

    /// <summary>
    /// 进入实时观战模式：客户端只渲染服务器推送的 gamestate，所有发送动作的接口（cut/action/riichi 等）均提前 return。
    /// 调用方应当先在 RealtimeRequestWaitPanel 收到 friend/realtime_started 后再调用此方法，
    /// 服务器随后会按 B 的座位转发完整 game_start + 后续广播。
    /// </summary>
    public void StartAsRealtimeSpectator(string gamestateId) {
        IsRealtimeSpectator = true;
        UserDataManager.Instance.SetGamestateId(gamestateId);
        if (ExitButtonManager.Instance != null) {
            ExitButtonManager.Instance.ShowForRealtimeSpectator();
        }
        SubscribeRealtimeEndEvents();
    }

    /// <summary>
    /// 退出实时观战模式（主动 ExitRealtime / 被 Kick / 游戏结束）：清空标志位与底层按钮显示，调用方负责切回主菜单。
    /// </summary>
    public void StopAsRealtimeSpectator() {
        IsRealtimeSpectator = false;
        if (ExitButtonManager.Instance != null) {
            ExitButtonManager.Instance.HideAll();
        }
        UnsubscribeRealtimeEndEvents();
    }

    private bool _realtimeEndSubscribed;
    private void SubscribeRealtimeEndEvents() {
        if (_realtimeEndSubscribed) return;
        if (FriendNetworkManager.Instance == null) return;
        FriendNetworkManager.Instance.OnRealtimeKicked += HandleRealtimeKicked;
        FriendNetworkManager.Instance.OnRealtimeEnded += HandleRealtimeEnded;
        _realtimeEndSubscribed = true;
    }
    private void UnsubscribeRealtimeEndEvents() {
        if (!_realtimeEndSubscribed) return;
        if (FriendNetworkManager.Instance != null) {
            FriendNetworkManager.Instance.OnRealtimeKicked -= HandleRealtimeKicked;
            FriendNetworkManager.Instance.OnRealtimeEnded -= HandleRealtimeEnded;
        }
        _realtimeEndSubscribed = false;
    }

    private void HandleRealtimeKicked(Response response) {
        if (!IsRealtimeSpectator) return;
        NotificationManager.Instance?.ShowTip("实时观战", false, response?.message ?? "您已被踢出实时观战");
        StopAsRealtimeSpectator();
        WindowsManager.Instance?.SwitchWindow("menu");
    }

    private void HandleRealtimeEnded(Response response) {
        if (!IsRealtimeSpectator) return;
        NotificationManager.Instance?.ShowTip("实时观战", true, response?.message ?? "被观战的对局已结束");
        StopAsRealtimeSpectator();
        WindowsManager.Instance?.SwitchWindow("menu");
    }
}



