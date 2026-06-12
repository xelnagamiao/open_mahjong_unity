using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class NormalGameStateManager {
    // 询问手牌操作 手牌操作包括 切牌 补花 胡 暗杠 加杠
    public void AskHandAction(int remaining_time, int playerIndex, int remain_tiles, string[] action_list,
                              Dictionary<int, int[]> riichi_candidate_cuts = null, int[] forbidden_cut_tiles = null) {
        TryResumeAfterCuoheContinue();
        string GetCardPlayer = indexToPosition[playerIndex];
        remainTiles = remain_tiles;
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
        TryResumeAfterCuoheContinue();
        chiCandidates = chi_candidates ?? new Dictionary<string, int[][]>();
        selfRiichiCandidateCuts.Clear();
        selfForbiddenCutTiles.Clear();
        IsQiangGangAsk = pendingAskFromJiagang;
        if (action_list.Length > 0){
            allowActionList = BuildMingPaiAllowActionList(action_list);
            SwitchCurrentPlayer("self","askMingPaiAction",remaining_time);
        }
    }

    private void ClearQiangGangAskState() {
        IsQiangGangAsk = false;
        pendingAskFromJiagang = false;
    }

    // 执行行动
    public void DoAction(string[] action_list, int action_player, int? cut_tile, int? cut_tile_index, bool? cut_class, int? deal_tile, int? buhua_tile, int[] combination_mask,string combination_target, bool? is_riichi_horizontal = null, bool isClaim = false, bool isSilent = false, bool? is_mo_gang = null) {
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
                            GameCanvas.Instance.ChangeHandCards("RemoveHandCard",cut_tile.Value,null,cut_tile_index.Value); // 2D手切行为
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
                    player_to_info[GetCardPlayer].huapai_list.Add(buhua_tile_id); // 存储花牌
                    if (GetCardPlayer == "self"){
                        selfHandTiles.Remove(buhua_tile_id); // 删除手牌
                        GameCanvas.Instance.ChangeHandCards("RemoveBuhuaCard",buhua_tile_id,null,null); // 2D补花行为
                    }
                    else{
                        player_to_info[GetCardPlayer].hand_tiles_count--; // 减少手牌
                    }
                    Game3DManager.Instance.Change3DTile("Buhua",buhua_tile_id,0,GetCardPlayer,false,null); // 3D补花行为
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
                        string combination_target_str = combination_target.Substring(1);
                        ReplaceCombinationMask(player_to_info[GetCardPlayer], combination_target, combination_mask);
                        player_to_info[GetCardPlayer].combination_tiles.Add($"g{combination_target_str}");
                        player_to_info[GetCardPlayer].combination_tiles.Remove(combination_target);
                        int? jiagangTileFromMask = GameRecordMeldCodec.ExtractTileByFlag(combination_mask, 3);
                        int tile_id = jiagangTileFromMask ?? int.Parse(combination_target_str);
                        bool isMoGang = is_mo_gang == true;
                        if (GetCardPlayer == "self"){
                            selfHandTiles.Remove(tile_id);
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
    }

    private static void AppendCombinationMask(PlayerInfoClass player, int[] mask) {
        if (player.combination_masks == null) player.combination_masks = new List<int[]>();
        player.combination_masks.Add(mask);
    }

    private static void ReplaceCombinationMask(PlayerInfoClass player, string oldCombo, int[] mask) {
        if (player.combination_masks == null) player.combination_masks = new List<int[]>();
        int idx = player.combination_tiles.IndexOf(oldCombo);
        if (idx >= 0 && idx < player.combination_masks.Count) {
            player.combination_masks[idx] = mask;
        } else {
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
            "hu_first", "hu_second", "hu_third", "pass"
        };
        List<string> result = new List<string>();
        foreach (string action in action_list) {
            if (allowOtherActionCheck.Contains(action)) {
                result.Add(action);
            }
        }
        return result;
    }

    // 战术鸣牌：处理 is_claim 申请广播
    // 仅播放发声、字体动画并启动战术鸣牌面板倒计时，不修改任何游戏状态
    private void HandleTacticalClaim(string actor, string[] action_list) {
        TacticalCallPanel.Instance.ShowClaim();
        foreach (string action in action_list) {
            SoundManager.Instance.PlayActionSound(actor, action);
            SoundManager.Instance.PlayPhysicsSound(action);
            // ShowActionDisplay 根据规则处理 hu_first/hu_second/hu_third/hu_self 的文案
            GameCanvas.Instance.ShowActionDisplay(actor, action, roomRule);
        }
    }
}
