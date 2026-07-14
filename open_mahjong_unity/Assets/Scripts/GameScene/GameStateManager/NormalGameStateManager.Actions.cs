using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class NormalGameStateManager {
    // 询问手牌操作 手牌操作包括 切牌 补花 胡 暗杠 加杠
    public void AskHandAction(int remaining_time, int playerIndex, int remain_tiles, string[] action_list,
                              Dictionary<int, int[]> riichi_candidate_cuts = null, int[] forbidden_cut_tiles = null,
                              int[] forced_cut_tiles = null) {
        TryResumeAfterCuoheContinue();
        TryResumeAfterSichuanContinue();
        string GetCardPlayer = indexToPosition[playerIndex];
        remainTiles = remain_tiles;
        // 立直麻将自家手牌可点状态依据：每次询问刷新
        selfRiichiCandidateCuts = riichi_candidate_cuts ?? new Dictionary<int, int[]>();
        selfForbiddenCutTiles = forbidden_cut_tiles != null
            ? new HashSet<int>(forbidden_cut_tiles)
            : new HashSet<int>();
        selfForcedCutTiles = forced_cut_tiles != null
            ? new HashSet<int>(forced_cut_tiles)
            : new HashSet<int>();
        RefreshChangshaSeaBottomConcealedTile(remain_tiles);
        // 如果行动者是自己
        if (playerIndex == selfIndex){
            allowActionList.Clear();
            // 存储全部可用行动；riichi_cut 在 UI 上以「立直」按钮展示
            string[] AllowHandActionCheck = new string[] {"cut", "buhua", "hu_self", "initial_hu", "sea_bottom", "buzhang", "angang", "jiagang", "jiuzhongjiupai", "riichi_cut", "pass"};
            foreach (string action in action_list){
                if (AllowHandActionCheck.Contains(action)){
                    allowActionList.Add(action);
                }
            }
        }
        // 切换行动者
        SwitchCurrentPlayer(GetCardPlayer, "askHandAction", remaining_time, playerIndex);
    }

    // 询问鸣牌操作 鸣牌操作包括 吃 碰 杠 胡 跳过
    public void AskMingPaiAction(int remaining_time,string[] action_list,int cut_tile, Dictionary<string, int[][]> chi_candidates = null, bool isTacticalRecheck = false){
        TryResumeAfterCuoheContinue();
        TryResumeAfterSichuanContinue();
        chiCandidates = chi_candidates ?? new Dictionary<string, int[][]>();
        selfRiichiCandidateCuts.Clear();
        selfForbiddenCutTiles.Clear();
        IsQiangGangAsk = pendingAskFromJiagang;
        currentAskCutTileId = cut_tile;
        if (action_list.Length > 0){
            allowActionList = BuildMingPaiAllowActionList(action_list);
            SwitchCurrentPlayer("self","askMingPaiAction",remaining_time, isTacticalRecheck: isTacticalRecheck);
        }
    }

    private void ClearQiangGangAskState() {
        IsQiangGangAsk = false;
        pendingAskFromJiagang = false;
        currentAskCutTileId = 0;
    }

    // 执行行动
    public void DoAction(string[] action_list, int action_player, int? cut_tile, int[] cut_tiles, int? cut_tile_index, bool? cut_class, int? deal_tile, int[] deal_tiles, int? buhua_tile, int[] combination_mask,string combination_target, bool? is_riichi_horizontal = null, bool isClaim = false, bool isSilent = false, bool? is_mo_gang = null, Dictionary<int, int> gangScoreChanges = null, bool? is_mo_buhua = null, int action_tick = 0, int? cut_from_player = null, float? meld_reveal_delay = null, bool? sea_bottom_discard = null) {
        string GetCardPlayer = indexToPosition[action_player]; // 获取执行操作的玩家位置
        bool isRiichiHorizontalCut = is_riichi_horizontal == true;
        if (isClaim) {
            // 战术鸣牌申请：仅吃牌固定播放；碰/和/杠仅在有更高优先级竞争者时由服务端下发
            HandleTacticalClaim(GetCardPlayer, action_list);
            return;
        }
        // isSilent：战术鸣牌申请阶段已发声/动画，实际行为仅同步状态
        float meldRevealDelaySec = meld_reveal_delay ?? 0f;
        foreach (string action in action_list) {

            Debug.Log($"执行DoAction操作: {action} (silent={isSilent})");
            bool deferMeldFeedback = !isSilent && meldRevealDelaySec > 0f && IsChiPengMingGangAction(action);
            string soundAction = action == "buzhang"
                ? (!string.IsNullOrEmpty(combination_target) && combination_target.StartsWith("G") ? "angang" : "jiagang")
                : action;
            if (!isSilent && !deferMeldFeedback) {
                SoundManager.Instance.PlayActionSound(GetCardPlayer, soundAction);
                // 切牌物理音改在 3D 出牌手牌队列中播放，避免吃牌后立刻收到 cut 消息时声音早于出牌动画
                if (action != "cut") {
                    SoundManager.Instance.PlayPhysicsSound(soundAction);
                }
                GameCanvas.Instance.ShowActionDisplay(GetCardPlayer, action, roomRule);
            }
            switch (action) { // action_list 实际上只会包含一个操作

                // 摸牌（普通摸牌 / 杠后摸牌 / 补花后摸牌）
                case "deal_tile":
                case "deal_gang_tile":
                case "deal_buhua_tile":
                    lastDealTileType = action;
                    int[] resolvedDealTiles = deal_tiles != null && deal_tiles.Length > 0
                        ? deal_tiles
                        : (deal_tile.HasValue ? new int[] { deal_tile.Value } : new int[0]);
                    remainTiles -= resolvedDealTiles.Length;
                    if (GetCardPlayer == "self"){
                        if (resolvedDealTiles.Length == 0) {
                            Debug.LogError($"Missing deal_tile: action={action}, player={action_player}");
                            break;
                        }
                        lastDealTileId = resolvedDealTiles[resolvedDealTiles.Length - 1];
                        for (int i = 0; i < resolvedDealTiles.Length; i++) {
                            int dealtTile = resolvedDealTiles[i];
                            selfHandTiles.Add(dealtTile);
                            string handChangeType = i == 0
                                ? "GetCard"
                                : (action == "deal_gang_tile" ? "GetGangReplacementCardNoLayout" : "GetCardNoAnimation");
                            GameCanvas.Instance.ChangeHandCards(handChangeType, dealtTile, null, null);
                            Game3DManager.Instance.Change3DTile("GetCard", dealtTile, 0, GetCardPlayer, false, null);
                        }
                    }
                    else{
                        int dealCount = resolvedDealTiles.Length > 0 ? resolvedDealTiles.Length : 1;
                        player_to_info[GetCardPlayer].hand_tiles_count += dealCount;
                        for (int i = 0; i < dealCount; i++) {
                            int hiddenTile = resolvedDealTiles.Length > i ? resolvedDealTiles[i] : 0;
                            Game3DManager.Instance.Change3DTile("GetCard", hiddenTile, 0, GetCardPlayer, false, null);
                        }
                    }
                    break;

                // 切牌
                case "cut":
                    if (sea_bottom_discard == true && roomRule == "changsha") {
                        HandleChangshaSeaBottomDiscard(GetCardPlayer, cut_tile, cut_class == true, isRiichiHorizontalCut);
                        break;
                    }
                    pendingAskFromJiagang = false;
                    int[] resolvedCutTiles = cut_tiles != null && cut_tiles.Length > 0
                        ? cut_tiles
                        : (cut_tile.HasValue ? new int[] { cut_tile.Value } : new int[0]);
                    if (resolvedCutTiles.Length == 0) {
                        Debug.LogError($"Missing cut_tile: action={action}, player={action_player}");
                        break;
                    }
                    int claimedOrLastCutTile = cut_tile ?? resolvedCutTiles[resolvedCutTiles.Length - 1];
                    lastCutCardID = claimedOrLastCutTile;
                    lastDiscardPlayerPosition = GetCardPlayer;
                    foreach (int discardedTile in resolvedCutTiles) {
                        player_to_info[GetCardPlayer].discard_tiles.Add(discardedTile);
                        player_to_info[GetCardPlayer].discard_riichi_flags.Add(isRiichiHorizontalCut);
                    }
                    if (GetCardPlayer == "self"){
                        lastDealTileId = 0;
                        if (cut_class.Value && resolvedCutTiles.Length > 1) {
                            for (int i = 0; i < resolvedCutTiles.Length; i++) {
                                int discardedTile = resolvedCutTiles[i];
                                selfHandTiles.Remove(discardedTile);
                            }
                            Game3DManager.Instance.Change3DDiscardTiles(resolvedCutTiles, GetCardPlayer, cut_class.Value, isRiichiHorizontalCut, playCutPhysicsSound: !isSilent);
                            GameCanvas.Instance.ChangeHandCards("RemoveGetCards", 0, resolvedCutTiles, null);
                        } else {
                            int tileToCut = cut_tile ?? resolvedCutTiles[resolvedCutTiles.Length - 1];
                            selfHandTiles.Remove(tileToCut);
                            Game3DManager.Instance.Change3DTile("Discard", tileToCut, 0, GetCardPlayer, cut_class.Value, null, isRiichiHorizontalCut, playCutPhysicsSound: !isSilent);
                            if (cut_class.Value) {
                                GameCanvas.Instance.ChangeHandCards("RemoveGetCard", tileToCut, null, null);
                            } else {
                                GameCanvas.Instance.ChangeHandCards("RemoveHandCard", tileToCut, null, cut_tile_index);
                            }
                        }
                    }
                    else{
                        player_to_info[GetCardPlayer].hand_tiles_count -= resolvedCutTiles.Length;
                        Game3DManager.Instance.Change3DDiscardTiles(resolvedCutTiles, GetCardPlayer, cut_class.Value, isRiichiHorizontalCut, playCutPhysicsSound: !isSilent);
                    }
                    break;

                // 补花
                case "buhua":
                    int buhua_tile_id = buhua_tile.Value;
                    bool isMoBuhua = is_mo_buhua == true;
                    player_to_info[GetCardPlayer].huapai_list.Add(buhua_tile_id);
                    if (GetCardPlayer == "self"){
                        selfHandTiles.Remove(buhua_tile_id);
                        // 摸补/手补统一走 RemoveBuhuaCard：摸牌区花牌会转为 RemoveBuhuaGetCard，不触发全手收拢
                        GameCanvas.Instance.ChangeHandCards("RemoveBuhuaCard", buhua_tile_id, null, null);
                    }
                    else{
                        player_to_info[GetCardPlayer].hand_tiles_count--;
                    }
                    Game3DManager.Instance.Change3DTile("Buhua", buhua_tile_id, 0, GetCardPlayer, isMoBuhua, null);
                    break;

                // 和牌：语音与动作文字已在 do_action 阶段播放
                case "hu_self":
                case "hu_first":
                case "hu_second":
                case "hu_third":
                case "hu":
                    break;

                // 吃碰杠
                case "chi_left": case"chi_mid": case"chi_right": case "buzhang": case "angang": case "jiagang": case "peng": case "gang":
                    bool isBuzhangJiagang = action == "buzhang"
                        && !string.IsNullOrEmpty(combination_target)
                        && combination_target.StartsWith("k");
                    bool isBuzhangAngang = action == "buzhang"
                        && !string.IsNullOrEmpty(combination_target)
                        && combination_target.StartsWith("G");
                    if (action == "jiagang" || isBuzhangJiagang){
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
                    else if (action == "angang" || isBuzhangAngang){
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
                        // 吃 / 碰 / 明杠：河牌被取走；打牌者与牌张由服务端 cut_from_player / cut_tile 必填下发。
                        currentMeldDiscarderPos = RequireMeldDiscarder(cut_from_player, GetCardPlayer);
                        if (!cut_tile.HasValue || cut_tile.Value <= 0) {
                            throw new System.Exception(
                                $"鸣牌缺少 cut_tile: action={action}, player={GetCardPlayer}, cut_from_player={cut_from_player}");
                        }
                        currentMeldClaimedTileId = cut_tile.Value;
                        RemoveClaimedDiscardFromRiver(currentMeldDiscarderPos, currentMeldClaimedTileId);

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
                        Game3DManager.Instance.Change3DTile(action, 0, need_remove_list.Count, GetCardPlayer, false, combination_mask,
                            meldRevealDelay: meldRevealDelaySec,
                            meldDiscarderPos: currentMeldDiscarderPos,
                            meldClaimedTile: currentMeldClaimedTileId,
                            meldFeedbackAction: deferMeldFeedback ? action : null,
                            meldFeedbackRoomRule: deferMeldFeedback ? roomRule : null);
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
        if (!tips) return;
        foreach (string action in action_list) {
            if (!ActionAffectsVisibleTiles(action)) continue;
            // 自家鸣牌后 ShowTipsBlock 会全量重算；刷新前同步手牌避免缓存与 GameState 不一致
            TipsContainer.Instance.RefreshTenpaiTipsIfCached(syncHandFromLiveState: true);
            return;
        }
    }

    /// <summary>
    /// 吃/碰/明杠：从出牌者河牌移除被鸣走的一张（与服务端 discard_tiles.pop(-1) 及牌谱回放一致）。
    /// discarderPos / claimedTile 由服务端 cut_from_player / cut_tile 必填提供。
    /// </summary>
    private void RemoveClaimedDiscardFromRiver(string discarderPos, int claimedTile) {
        if (string.IsNullOrEmpty(discarderPos)
            || claimedTile <= 0
            || !player_to_info.TryGetValue(discarderPos, out PlayerInfoClass discarder)
            || discarder.discard_tiles == null
            || discarder.discard_tiles.Count == 0) {
            Debug.LogWarning(
                $"RemoveClaimedDiscardFromRiver: 无法移除河牌 discarder={discarderPos}, claimed={claimedTile}");
            return;
        }

        int lastIdx = discarder.discard_tiles.Count - 1;
        int removedTile;
        if (discarder.discard_tiles[lastIdx] == claimedTile) {
            removedTile = claimedTile;
            discarder.discard_tiles.RemoveAt(lastIdx);
        } else {
            int idx = discarder.discard_tiles.LastIndexOf(claimedTile);
            if (idx >= 0) {
                removedTile = claimedTile;
                discarder.discard_tiles.RemoveAt(idx);
            } else {
                removedTile = discarder.discard_tiles[lastIdx];
                discarder.discard_tiles.RemoveAt(lastIdx);
                Debug.LogWarning(
                    $"RemoveClaimedDiscardFromRiver: 河末张 {removedTile} 与鸣牌张 {claimedTile} 不一致，已移除末张");
            }
        }

        if (discarder.discard_riichi_flags != null && discarder.discard_riichi_flags.Count > 0) {
            discarder.discard_riichi_flags.RemoveAt(discarder.discard_riichi_flags.Count - 1);
        }
        discarder.discard_origin_tiles.Add(removedTile);
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

    private static bool IsChiPengMingGangAction(string action) {
        switch (action) {
            case "chi_left":
            case "chi_mid":
            case "chi_right":
            case "peng":
            case "gang":
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// 鸣牌必填 cut_from_player：映射为打牌者座位；缺失或与鸣牌者相同则抛错。
    /// </summary>
    private string RequireMeldDiscarder(int? cutFromPlayer, string meldPlayer) {
        if (!cutFromPlayer.HasValue) {
            throw new System.Exception($"鸣牌缺少 cut_from_player: meldPlayer={meldPlayer}");
        }
        if (!indexToPosition.TryGetValue(cutFromPlayer.Value, out string pos) || string.IsNullOrEmpty(pos)) {
            throw new System.Exception(
                $"鸣牌 cut_from_player={cutFromPlayer.Value} 无法映射座位: meldPlayer={meldPlayer}");
        }
        if (pos == meldPlayer) {
            throw new System.Exception(
                $"鸣牌 cut_from_player 与鸣牌者相同: seat={pos}, index={cutFromPlayer.Value}");
        }
        return pos;
    }
}
