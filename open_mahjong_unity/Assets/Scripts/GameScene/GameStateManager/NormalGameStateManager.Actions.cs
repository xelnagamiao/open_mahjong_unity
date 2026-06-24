using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class NormalGameStateManager {
    // 询问手牌操作 手牌操作包括 切牌 补花 胡 暗杠 加杠
    public void AskHandAction(int remaining_time, int playerIndex, int remain_tiles, string[] action_list,
                              Dictionary<int, int[]> riichi_candidate_cuts = null, int[] forbidden_cut_tiles = null) {
        TryResumeAfterCuoheContinue();
        TryResumeAfterSichuanContinue();
        string GetCardPlayer = indexToPosition[playerIndex];
        remainTiles = remain_tiles;
        // 立直麻将自家手牌可点状态依据：每次询问刷新
        selfRiichiCandidateCuts = riichi_candidate_cuts ?? new Dictionary<int, int[]>();
        selfForbiddenCutTiles = forbidden_cut_tiles != null
            ? new HashSet<int>(forbidden_cut_tiles)
            : new HashSet<int>();
        // 如果行动者是自己
        if (playerIndex == selfIndex){
            allowActionList.Clear();
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
        TryResumeAfterCuoheContinue();
        TryResumeAfterSichuanContinue();
        chiCandidates = chi_candidates ?? new Dictionary<string, int[][]>();
        selfRiichiCandidateCuts.Clear();
        selfForbiddenCutTiles.Clear();
        IsQiangGangAsk = pendingAskFromJiagang;
        currentAskCutTileId = cut_tile;
        if (action_list.Length > 0){
            allowActionList = BuildMingPaiAllowActionList(action_list);
            SwitchCurrentPlayer("self","askMingPaiAction",remaining_time);
        }
    }

    private void ClearQiangGangAskState() {
        IsQiangGangAsk = false;
        pendingAskFromJiagang = false;
        currentAskCutTileId = 0;
    }

    // 执行行动
    public void DoAction(string[] action_list, int action_player, int? cut_tile, int? cut_tile_index, bool? cut_class, int? deal_tile, int? buhua_tile, int[] combination_mask,string combination_target, bool? is_riichi_horizontal = null, bool isClaim = false, bool isSilent = false, bool? is_mo_gang = null, Dictionary<int, int> gangScoreChanges = null, bool? is_mo_buhua = null) {
        string GetCardPlayer = indexToPosition[action_player]; // 获取执行操作的玩家位置
        bool isRiichiHorizontalCut = is_riichi_horizontal == true;
        if (isClaim) {
            // 战术鸣牌申请：仅吃牌固定播放；碰/和/杠仅在有更高优先级竞争者时由服务端下发
            HandleTacticalClaim(GetCardPlayer, action_list);
            return;
        }
        // isSilent：战术鸣牌申请阶段已发声/动画，实际行为仅同步状态
        foreach (string action in action_list) {

            Debug.Log($"执行DoAction操作: {action} (silent={isSilent})");
            if (!isSilent) {
                SoundManager.Instance.PlayActionSound(GetCardPlayer, action);
                // 切牌物理音改在 3D 出牌手牌队列中播放，避免吃牌后立刻收到 cut 消息时声音早于出牌动画
                if (action != "cut") {
                    SoundManager.Instance.PlayPhysicsSound(action);
                }
                GameCanvas.Instance.ShowActionDisplay(GetCardPlayer, action, roomRule);
            }
            switch (action) { // action_list 实际上只会包含一个操作

                // 摸牌（普通摸牌 / 杠后摸牌 / 补花后摸牌）
                case "deal_tile":
                case "deal_gang_tile":
                case "deal_buhua_tile":
                    lastDealTileType = action;
                    remainTiles--; // 剩余牌数减少
                    if (GetCardPlayer == "self"){
                        if (!deal_tile.HasValue) {
                            Debug.LogError($"摸牌操作缺少 deal_tile: action={action}, player={action_player}");
                            break;
                        }
                        selfHandTiles.Add(deal_tile.Value);
                        GameCanvas.Instance.ChangeHandCards("GetCard", deal_tile.Value, null, null);
                        Game3DManager.Instance.Change3DTile("GetCard", deal_tile.Value, 0, GetCardPlayer, false, null);
                    }
                    else{
                        player_to_info[GetCardPlayer].hand_tiles_count++;
                        // 服务端对他人不下发 deal_tile；3D 仅增加背面牌，tileId 传 0
                        Game3DManager.Instance.Change3DTile("GetCard", deal_tile ?? 0, 0, GetCardPlayer, false, null);
                    }
                    break;
                
                // 切牌
                case "cut": 
                    pendingAskFromJiagang = false;
                    lastCutCardID = cut_tile.Value; // 存储上次切牌的ID
                    lastDiscardPlayerPosition = GetCardPlayer;
                    player_to_info[GetCardPlayer].discard_tiles.Add(cut_tile.Value); // 存储弃牌
                    // 同步保存横置标记，用于他家鸣牌后下一张续横、重连/牌谱重建时还原立直横置弃牌
                    player_to_info[GetCardPlayer].discard_riichi_flags.Add(isRiichiHorizontalCut);
                    if (GetCardPlayer == "self"){
                        selfHandTiles.Remove(cut_tile.Value); // 删除手牌
                        Game3DManager.Instance.Change3DTile("Discard",cut_tile.Value,0,GetCardPlayer,cut_class.Value,null,isRiichiHorizontalCut, playCutPhysicsSound: !isSilent); // 3D切牌行为
                        if (cut_class.Value){
                            GameCanvas.Instance.ChangeHandCards("RemoveGetCard",cut_tile.Value,null,null); // 2D摸切行为
                        }
                        else{
                            GameCanvas.Instance.ChangeHandCards("RemoveHandCard", cut_tile.Value, null, cut_tile_index); // 2D手切；定缺纠正时 cut_tile_index 可为 null，按 tileId 删牌
                        }
                    }
                    else{
                        player_to_info[GetCardPlayer].hand_tiles_count--; // 减少手牌
                        Game3DManager.Instance.Change3DTile("Discard",cut_tile.Value,0,GetCardPlayer,cut_class.Value,null,isRiichiHorizontalCut, playCutPhysicsSound: !isSilent); // 3D切牌行为
                    } 
                    break;

                // 补花
                case "buhua":
                    int buhua_tile_id = buhua_tile.Value;
                    bool isMoBuhua = is_mo_buhua == true;
                    player_to_info[GetCardPlayer].huapai_list.Add(buhua_tile_id);
                    if (GetCardPlayer == "self"){
                        selfHandTiles.Remove(buhua_tile_id);
                        if (isMoBuhua) {
                            GameCanvas.Instance.ChangeHandCards("RemoveGetCard", buhua_tile_id, null, null);
                        } else {
                            GameCanvas.Instance.ChangeHandCards("RemoveBuhuaCard", buhua_tile_id, null, null);
                        }
                    }
                    else{
                        player_to_info[GetCardPlayer].hand_tiles_count--;
                    }
                    Game3DManager.Instance.Change3DTile("Buhua", buhua_tile_id, 0, GetCardPlayer, false, null);
                    break;

                // 和牌：语音与动作文字已在 do_action 阶段播放
                case "hu_self":
                case "hu_first":
                case "hu_second":
                case "hu_third":
                case "hu":
                    break;

                // 吃碰杠
                case "chi_left": case"chi_mid": case"chi_right": case "angang": case "jiagang": case "peng": case "gang":
                    if (action == "jiagang"){
                        pendingAskFromJiagang = true;
                        var meldPlayer = player_to_info[GetCardPlayer];
                        int meldIdx = GameRecordMeldCodec.FindCombinationIndex(meldPlayer.combination_tiles, combination_target);
                        string gCombo = GameRecordMeldCodec.BuildCombinationKey('g',
                            GameRecordMeldCodec.NormalizeCombinationTileId(combination_target));
                        if (meldIdx >= 0) {
                            ReplaceCombinationMaskAtIndex(meldPlayer, meldIdx, combination_mask);
                            meldPlayer.combination_tiles[meldIdx] = gCombo;
                        } else {
                            ReplaceCombinationMask(meldPlayer, combination_target, combination_mask);
                            meldPlayer.combination_tiles.Add(gCombo);
                            meldPlayer.combination_tiles.Remove(combination_target);
                        }
                        int normJia = GameRecordMeldCodec.NormalizeCombinationTileId(combination_target);
                        int? actualJia = GameRecordMeldCodec.ExtractTileByFlag(combination_mask, 3);
                        bool isMoGang = is_mo_gang == true;
                        int tile_id = actualJia ?? normJia;
                        if (GetCardPlayer == "self"){
                            var removed = GameRecordMeldCodec.RemoveOneJiagangTile(
                                selfHandTiles, actualJia, normJia, isMoGang);
                            if (removed.Count > 0) tile_id = removed[0];
                            if (isMoGang) {
                                GameCanvas.Instance.ChangeHandCards("RemoveGetCard", tile_id, null, null);
                            } else {
                                GameCanvas.Instance.ChangeHandCards("RemoveJiagangCard", tile_id, null, null);
                            }
                        }
                        else{
                            player_to_info[GetCardPlayer].hand_tiles_count -= 1;
                        }
                        Game3DManager.Instance.Change3DTile("jiagang", tile_id, 1, GetCardPlayer, isMoGang, combination_mask);
                    }
                    else if (action == "angang"){
                        bool isMoGang = is_mo_gang == true;
                        player_to_info[GetCardPlayer].combination_tiles.Add(combination_target);
                        AppendCombinationMask(player_to_info[GetCardPlayer], combination_mask);
                        List<int> angangRemoveList = GameRecordMeldCodec.ExtractHandTilesFromMask(combination_mask);
                        if (GetCardPlayer == "self"){
                            foreach (int tile_id in angangRemoveList){
                                selfHandTiles.Remove(tile_id);
                            }
                            ApplyAngangHandCardRemoval(angangRemoveList, isMoGang);
                        } else {
                            player_to_info[GetCardPlayer].hand_tiles_count -= angangRemoveList.Count;
                        }
                        Game3DManager.Instance.Change3DTile(action, 0, angangRemoveList.Count, GetCardPlayer, false, combination_mask, isMoGang: isMoGang);
                    }
                    else{
                        // 吃 / 碰 / 明杠：河牌被取走，按掩码 flag!=1 删手牌侧真实 ID
                        player_to_info[CurrentPlayer].discard_tiles.Remove(lastCutCardID);
                        if (player_to_info[CurrentPlayer].discard_riichi_flags.Count > 0){
                            player_to_info[CurrentPlayer].discard_riichi_flags.RemoveAt(player_to_info[CurrentPlayer].discard_riichi_flags.Count - 1);
                        }
                        player_to_info[CurrentPlayer].discard_origin_tiles.Add(lastCutCardID);

                        player_to_info[GetCardPlayer].combination_tiles.Add(combination_target);
                        AppendCombinationMask(player_to_info[GetCardPlayer], combination_mask);
                        List<int> need_remove_list = GameRecordMeldCodec.ExtractHandTilesFromMask(combination_mask);
                        if (GetCardPlayer == "self"){
                            foreach (int tile_id in need_remove_list){
                                selfHandTiles.Remove(tile_id);
                            }
                            GameCanvas.Instance.ChangeHandCards("RemoveCombinationCard", 0, need_remove_list.ToArray(), null);
                        }
                        else{
                            player_to_info[GetCardPlayer].hand_tiles_count -= need_remove_list.Count;
                        }
                        Game3DManager.Instance.Change3DTile(action, 0, need_remove_list.Count, GetCardPlayer, false, combination_mask);
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
        RefreshTableTipsAfterAction(action_list, GetCardPlayer);
        if (!isClaim && !isSilent && IsSichuanRule() && ContainsGangAction(action_list)
            && GameCanvas.HasNonZeroGangScoreChanges(gangScoreChanges)) {
            ApplyGangScoreDeltas(gangScoreChanges);
            GameCanvas.Instance.ShowGangScoreFloats(gangScoreChanges);
        }
    }

    public void ApplyGangScoreDeltas(Dictionary<int, int> changes) {
        if (changes == null || changes.Count == 0) return;
        var scoreBySeat = new Dictionary<int, int>();
        foreach (var kvp in indexToPosition) {
            int seatIdx = kvp.Key;
            string pos = kvp.Value;
            if (!player_to_info.TryGetValue(pos, out PlayerInfoClass info)) continue;
            int delta = changes.ContainsKey(seatIdx) ? changes[seatIdx] : 0;
            if (delta == 0) {
                scoreBySeat[seatIdx] = info.score;
                continue;
            }
            info.score += delta;
            scoreBySeat[seatIdx] = info.score;
        }
        if (scoreBySeat.Count > 0) {
            BoardCanvas.Instance.UpdatePlayerScores(scoreBySeat, indexToPosition);
        }
    }

    private static bool ContainsGangAction(string[] actionList) {
        if (actionList == null) return false;
        foreach (string action in actionList) {
            if (action == "angang" || action == "jiagang" || action == "gang") return true;
        }
        return false;
    }

    private static bool ActionAffectsVisibleTiles(string action) {
        switch (action) {
            case "cut":
            case "chi_left":
            case "chi_mid":
            case "chi_right":
            case "peng":
            case "gang":
            case "jiagang":
            case "angang":
                return true;
            default:
                return false;
        }
    }

    /// <summary>他家操作改变牌桌可见牌时，刷新已缓存的听牌提示（绝张/余张/番数）。</summary>
    private void RefreshTableTipsAfterAction(string[] action_list, string actionPlayer) {
        if (!tips || actionPlayer == "self") return;
        if (TipsBlock.Instance == null || !TipsBlock.Instance.IsBlockActive) return;
        foreach (string action in action_list) {
            if (ActionAffectsVisibleTiles(action)) {
                TipsContainer.Instance?.RefreshTenpaiTipsIfCached();
                return;
            }
        }
    }

    private static void AppendCombinationMask(PlayerInfoClass player, int[] mask) {
        if (player.combination_masks == null) player.combination_masks = new List<int[]>();
        player.combination_masks.Add(mask);
    }

    private static void ReplaceCombinationMask(PlayerInfoClass player, string oldCombo, int[] mask) {
        if (player.combination_masks == null) player.combination_masks = new List<int[]>();
        int idx = GameRecordMeldCodec.FindCombinationIndex(player.combination_tiles, oldCombo);
        if (idx < 0) idx = player.combination_tiles.IndexOf(oldCombo);
        ReplaceCombinationMaskAtIndex(player, idx, mask);
    }

    private static void ReplaceCombinationMaskAtIndex(PlayerInfoClass player, int idx, int[] mask) {
        if (player.combination_masks == null) player.combination_masks = new List<int[]>();
        if (idx >= 0 && idx < player.combination_masks.Count) {
            player.combination_masks[idx] = mask;
        } else {
            Debug.LogWarning($"ReplaceCombinationMask: 副露索引无效 idx={idx}, masks={player.combination_masks?.Count ?? 0}");
            player.combination_masks.Add(mask);
        }
    }

    private static void ApplyAngangHandCardRemoval(List<int> handTiles, bool isMoGang) {
        if (handTiles == null || handTiles.Count == 0) return;
        if (isMoGang) {
            GameCanvas.Instance.ChangeHandCards("RemoveGetCard", handTiles[0], null, null);
            if (handTiles.Count > 1) {
                GameCanvas.Instance.ChangeHandCards(
                    "RemoveCombinationCard", 0, handTiles.Skip(1).ToArray(), null);
            }
        } else {
            GameCanvas.Instance.ChangeHandCards("RemoveCombinationCard", 0, handTiles.ToArray(), null);
        }
    }

    private static List<string> BuildMingPaiAllowActionList(string[] action_list) {
        string[] allowOtherActionCheck = new string[] {
            "chi_left", "chi_mid", "chi_right", "peng", "gang",
            "hu", "hu_first", "hu_second", "hu_third", "pass"
        };
        List<string> result = new List<string>();
        foreach (string action in action_list) {
            if (allowOtherActionCheck.Contains(action)) {
                result.Add(action);
            }
        }
        return result;
    }

    // 战术鸣牌：处理 is_claim 申请广播（仅发声/字体动画，等待窗口由服务端 ask_other 驱动）
    private void HandleTacticalClaim(string actor, string[] action_list) {
        foreach (string action in action_list) {
            SoundManager.Instance.PlayActionSound(actor, action);
            SoundManager.Instance.PlayPhysicsSound(action);
            // ShowActionDisplay 根据规则处理 hu_first/hu_second/hu_third/hu_self 的文案
            GameCanvas.Instance.ShowActionDisplay(actor, action, roomRule);
        }
        // 申请-停顿期间隐藏本家操作按钮；可抢断玩家会在 ask_other 再次询问时重新弹出按钮。
        GameCanvas.Instance.ClearActionButton();
    }
}
