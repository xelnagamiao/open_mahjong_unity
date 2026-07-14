using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class NormalGameStateManager {
    // 初始化游戏
    public void InitializeGame(bool success, string message, GameInfo gameInfo){
        ClearPendingCuoheContinue();
        ClearPendingSichuanContinue();
        lastAskHandPlayerIndex = -1;
        // 新对局（gamestate_id 变化）才清空本地结算快照；同一场对局的下一局 game_start 不应清空，
        // 否则会抹掉已累积的主番快照（国标每局都会广播 game_start），导致计分板主番列整列变 —。
        // 重连时若本地快照行数与服务端 score_history 不一致，仍清空以免与分值行错位。
        bool isNewMatch = string.IsNullOrEmpty(gamestateId) || gamestateId != gameInfo.gamestate_id;
        if (isNewMatch) {
            ClearRoundSettlementHistory();
        }
        if (!IsRealtimeSpectator) {
            UserDataManager.Instance.SetRoomId(gameInfo.room_id.ToString());
        }
        UserDataManager.Instance.SetGamestateId(gameInfo.gamestate_id);

        gamestateId = gameInfo.gamestate_id;
        // 0.切换窗口
        MatchStateManager.Instance.StopQueueing();
        MatchNetworkManager.Instance.ResetMatchLock();
        MatchQueueingPanel.Instance?.HideImmediately();
        MatchFoundedPanel.Instance?.StopCountdownAndHide();
        WindowsManager.Instance.SwitchWindow("game"); // 切换到游戏场景

        Game3DManager.Instance.Clear3DTile(); // 清空3D手牌

        InitializeSetInfo(gameInfo, isNewMatch); // 初始化对局数据
        GameCanvas.Instance.InitializeUIInfo(gameInfo,indexToPosition); // 初始化面板信息
        BoardCanvas.Instance.InitializeBoardInfo(gameInfo,indexToPosition); // 初始化桌面信息
        RestoreSichuanDingque(gameInfo); // 四川：重连/进局中时恢复各家定缺标记

        // 获取自己的手牌信息（从 PlayerInfo 中获取）
        PlayerInfo selfPlayerInfo = GetSelfPlayerInfo(gameInfo);
        int[] selfHandTilesArray = selfPlayerInfo.hand_tiles;

        // 初始化手牌区域
        GameCanvas.Instance.ChangeHandCards("InitHandCards",0,selfHandTilesArray,null);

        // 初始化他人手牌区域
        Game3DManager.Instance.Change3DTile("InitHandCards",0,0,null,false,null);

        // 初始化游戏开始UI（观战者不显示自动操作）
        if (IsRealtimeSpectator) {
            GameSceneUIManager.Instance.InitRealtimeSpectatorStart();
        } else {
            GameSceneUIManager.Instance.InitGameStart();
        }

        // InitGameStart内的HideGameRecord 会将鼠标输入置为 Idle，在此之后进入对局输入模式
        GameSceneMouseInputController.Instance.SetState(GameSceneMouseInputController.StateGame);

        // 根据对局信息生成他家已有的3D卡牌（弃牌、副露、花牌）
        GenerateOtherPlayers3DTiles(gameInfo);

        // 重连/初始化时：tag_list 中含 riichi/daburu_riichi 的玩家直接放置立直棒（无飞行动画）
        RestoreRiichiTenbous(gameInfo);

        // 重连时 server 仅向当前行动者补发 ask；其余玩家需从 game_info 恢复黄条
        if (gameInfo != null && indexToPosition.TryGetValue(gameInfo.current_player_index, out string currentPos)) {
            BoardCanvas.Instance.ShowCurrentPlayer(currentPos, remainTiles);
            CurrentPlayer = currentPos;
        }

        IsGameActive = true;
        IsSelfActionRequired = false;
        TipsContainer.Instance.ResetRyuukyokuTenpaiChoiceForRound();
        TipsContainer.Instance.HideRyuukyokuTenpaiChoice();
        if (IsRealtimeSpectator && tips && player_to_info.TryGetValue("self", out PlayerInfoClass selfInfo)) {
            TipsBlock.Instance.ShowTipsBlock(selfHandTiles, selfInfo.combination_tiles ?? new List<string>());
        }
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
    private void InitializeSetInfo(GameInfo gameInfo, bool isNewMatch){
        // 清空操作列表
        allowActionList = new List<string>();
        selfForcedCutTiles.Clear();
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
        player_to_info["self"].combination_masks = new List<int[]>();
        player_to_info["left"].combination_masks = new List<int[]>();
        player_to_info["top"].combination_masks = new List<int[]>();
        player_to_info["right"].combination_masks = new List<int[]>();

        // 如果gameinfo.user_id等于自己的user_id，则设置自身索引为gameinfo.player_index
        if (IsRealtimeSpectator) {
            if (gameInfo.view_player_index.HasValue) {
                selfIndex = gameInfo.view_player_index.Value;
            } else if (RealtimeSpectatorHostUserId > 0) {
                foreach (var player in gameInfo.players_info) {
                    if (player.user_id == RealtimeSpectatorHostUserId) {
                        selfIndex = player.player_index;
                        break;
                    }
                }
            } else {
                foreach (var player in gameInfo.players_info) {
                    if (player.hand_tiles != null && player.hand_tiles.Length > 0) {
                        selfIndex = player.player_index;
                        break;
                    }
                }
            }
        } else {
            foreach (var player in gameInfo.players_info) {
                if (player.user_id == UserDataManager.Instance.UserId) {
                    selfIndex = player.player_index;
                    break;
                }
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
        maxRound = gameInfo.max_round;

        // 获取自己的手牌信息（从 PlayerInfo 中获取）
        PlayerInfo selfPlayerInfo = GetSelfPlayerInfo(gameInfo);
        if (selfPlayerInfo != null && selfPlayerInfo.hand_tiles != null){
            selfHandTiles = selfPlayerInfo.hand_tiles.ToList();
        } else {
            selfHandTiles = new List<int>();
        }
        player_to_info["self"].hand_tiles_count = selfHandTiles.Count;

        tips = gameInfo.tips; // 存储是否提示
        showMoqieHint = gameInfo.show_moqie_hint; // 手摸切灰显
        isOpenCuoHe = gameInfo.open_cuohe; // 存储是否开启错和
        isSetRandomSeed = gameInfo.isPlayerSetRandomSeed; // 存储是否设置随机种子

        // 立直麻将字段同步：服务端未下发时使用默认值
        honba = gameInfo.honba ?? 0;
        riichiSticks = gameInfo.riichi_sticks ?? 0;
        doraIndicators = gameInfo.dora_indicators != null ? new List<int>(gameInfo.dora_indicators) : new List<int>();
        kanDoraIndicators = gameInfo.kan_dora_indicators != null ? new List<int>(gameInfo.kan_dora_indicators) : new List<int>();
        hepaiWay = gameInfo.hepai_way ?? "multi_ron";
        redDora = gameInfo.red_dora ?? false;
        dealerIndex = gameInfo.dealer_index ?? 0;
        changshaBaseScoreNoDealer = gameInfo.base_score_no_dealer ?? false;
        changshaSmallHuScore = Mathf.Max(gameInfo.small_hu_score ?? 2, 1);
        changshaBigHuScore = Mathf.Max(gameInfo.big_hu_score ?? 8, 1);
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
                player_to_info["self"].combination_masks = CopyCombinationMasks(player.combination_mask);
                player_to_info["self"].huapai_list = player.huapai_list.ToList(); // 存储花牌列表
                player_to_info["self"].title_used = player.title_used; // 存储使用的称号ID
                player_to_info["self"].profile_used = player.profile_used; // 存储使用的头像ID
                player_to_info["self"].character_used = player.character_used; // 存储使用的角色ID
                player_to_info["self"].voice_used = player.voice_used; // 存储使用的音色ID
                player_to_info["self"].score_history = player.score_history.ToList(); // 存储分数历史变化列表
                player_to_info["self"].round_number_history = player.round_number_history != null ? player.round_number_history.ToList() : new List<int>(); // 存储每手对应局数
                ScoreHistorySettlementHelper.AlignRoundNumberHistory(player_to_info["self"].score_history, player_to_info["self"].round_number_history);
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
                player_to_info["right"].combination_masks = CopyCombinationMasks(player.combination_mask);
                player_to_info["right"].huapai_list = player.huapai_list.ToList(); // 存储花牌列表
                player_to_info["right"].hand_tiles_count = player.hand_tiles_count; // 存储手牌数量
                player_to_info["right"].title_used = player.title_used; // 存储使用的称号ID
                player_to_info["right"].profile_used = player.profile_used; // 存储使用的头像ID
                player_to_info["right"].character_used = player.character_used; // 存储使用的角色ID
                player_to_info["right"].voice_used = player.voice_used; // 存储使用的音色ID
                player_to_info["right"].score_history = player.score_history.ToList(); // 存储分数历史变化列表
                player_to_info["right"].round_number_history = player.round_number_history != null ? player.round_number_history.ToList() : new List<int>(); // 存储每手对应局数
                ScoreHistorySettlementHelper.AlignRoundNumberHistory(player_to_info["right"].score_history, player_to_info["right"].round_number_history);
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
                player_to_info["top"].combination_masks = CopyCombinationMasks(player.combination_mask);
                player_to_info["top"].huapai_list = player.huapai_list.ToList(); // 存储花牌列表
                player_to_info["top"].hand_tiles_count = player.hand_tiles_count; // 存储手牌数量
                player_to_info["top"].title_used = player.title_used; // 存储使用的称号ID
                player_to_info["top"].profile_used = player.profile_used; // 存储使用的头像ID
                player_to_info["top"].character_used = player.character_used; // 存储使用的角色ID
                player_to_info["top"].voice_used = player.voice_used; // 存储使用的音色ID
                player_to_info["top"].score_history = player.score_history.ToList(); // 存储分数历史变化列表
                player_to_info["top"].round_number_history = player.round_number_history != null ? player.round_number_history.ToList() : new List<int>(); // 存储每手对应局数
                ScoreHistorySettlementHelper.AlignRoundNumberHistory(player_to_info["top"].score_history, player_to_info["top"].round_number_history);
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
                player_to_info["left"].combination_masks = CopyCombinationMasks(player.combination_mask);
                player_to_info["left"].huapai_list = player.huapai_list.ToList(); // 存储花牌列表
                player_to_info["left"].hand_tiles_count = player.hand_tiles_count; // 存储手牌数量
                player_to_info["left"].title_used = player.title_used; // 存储使用的称号ID
                player_to_info["left"].profile_used = player.profile_used; // 存储使用的头像ID
                player_to_info["left"].character_used = player.character_used; // 存储使用的角色ID
                player_to_info["left"].voice_used = player.voice_used; // 存储使用的音色ID
                player_to_info["left"].score_history = player.score_history.ToList(); // 存储分数历史变化列表
                player_to_info["left"].round_number_history = player.round_number_history != null ? player.round_number_history.ToList() : new List<int>(); // 存储每手对应局数
                ScoreHistorySettlementHelper.AlignRoundNumberHistory(player_to_info["left"].score_history, player_to_info["left"].round_number_history);
                player_to_info["left"].original_player_index = player.original_player_index; // 存储原始玩家索引
                player_to_info["left"].tag_list = player.tag_list; // 存储标签列表
            }
        }

        // 重连兜底：同一对局重连时，若本地快照行数与服务端恢复的 score_history 不一致，清空以免错位。
        // 正常下一局 game_start 时两者同步增长，不会进入此分支，主番快照得以保留。
        if (!isNewMatch) {
            int scoreRows = 0;
            foreach (var info in player_to_info.Values) {
                if (info.score_history != null) scoreRows = Mathf.Max(scoreRows, info.score_history.Count);
            }
            if (roundSettlementHistory.Count != scoreRows) {
                ClearRoundSettlementHistory();
            }
        }
    }

    // 获取自己的 PlayerInfo
    private PlayerInfo GetSelfPlayerInfo(GameInfo gameInfo){
        if (gameInfo.players_info == null) return null;

        if (IsRealtimeSpectator) {
            foreach (var player in gameInfo.players_info) {
                if (player.player_index == selfIndex) {
                    return player;
                }
            }
            return null;
        }

        int selfUserId = UserDataManager.Instance.UserId;
        foreach (var player in gameInfo.players_info){
            if (player.user_id == selfUserId){
                return player;
            }
        }
        return null;
    }

    private static List<int[]> CopyCombinationMasks(int[][] source) {
        var list = new List<int[]>();
        if (source == null) return list;
        foreach (var mask in source) {
            list.Add(mask);
        }
        return list;
    }
}
