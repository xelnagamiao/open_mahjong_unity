using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 将虹雀权威事件流适配到现有麻将桌。开局/重连恢复桌面，实时消息只更新动作。
/// 全部继续走原有 GameCanvas、Game3DManager 和 NormalGameStateManager。
/// </summary>
public sealed class HongqueTableAdapter : MonoBehaviour {
    private const string ClaimActionPrefix = "hongque_claim:";
    private const string KongActionPrefix = "hongque_kong:";

    public static HongqueTableAdapter Instance { get; private set; }
    public static bool IsActive => Instance != null && Instance.state != null;
    public bool IsRoundEnd => state != null && state.phase == "round_end";

    private string gamestateId;
    private HongqueStateInfo state;
    private bool tableInitialized;
    private int displayedRound = -1;
    private int lastProcessedEventId;
    private bool selfHasUnmergedDraw;
    private bool gameEndShown;
    private string lastActionUiKey;
    private string lastTipsUiKey;
    private int lastDiscardTileId;

    public static HongqueTableAdapter EnsureInstance() {
        if (Instance != null) return Instance;
        GameObject root = new GameObject("HongqueTableAdapter");
        DontDestroyOnLoad(root);
        return root.AddComponent<HongqueTableAdapter>();
    }

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void ApplyState(string newGamestateId, HongqueStateInfo newState) {
        if (newState == null) return;
        bool fullSync = newState.sync_mode == "round_start" || newState.sync_mode == "reconnect";
        if (fullSync) {
            if (newState.players == null || newState.players.Length != 4) {
                Debug.LogError($"虹雀 {newState.sync_mode} 缺少完整四家桌面数据");
                return;
            }
        } else {
            if (newState.sync_mode != "events") {
                Debug.LogError($"虹雀协议类型无效: {newState.sync_mode ?? "<null>"}");
                return;
            }
            if (!tableInitialized || state == null || gamestateId != newGamestateId) {
                Debug.LogError("虹雀在开局/重连完成前收到增量消息，已丢弃");
                return;
            }
            // Incremental packets intentionally omit all table snapshots.
            // Retain immutable/full-sync data locally for the next UI update.
            newState.players = state.players;
            newState.hand = state.hand;
            newState.room_id = state.room_id;
            newState.max_round = state.max_round;
            newState.round_time = state.round_time;
            newState.step_time = state.step_time;
        }
        bool newMatch = gamestateId != newGamestateId;
        HongqueStateInfo previousState = state;
        if (newMatch) {
            lastProcessedEventId = 0;
            selfHasUnmergedDraw = false;
            gameEndShown = false;
            lastActionUiKey = null;
            lastTipsUiKey = null;
            lastDiscardTileId = 0;
            // 将 126 张牌面的同步磁盘加载集中到开局，避免首次见到某张牌时卡住出牌/摸牌帧。
            HongqueTileVisual.PreloadAllTextures();
            MahjongObjectPool.Instance?.PrewarmHongquePool();
        }
        gamestateId = newGamestateId;
        state = newState;

        bool reconnectRestore = state.sync_mode == "reconnect";
        bool roundStart = state.sync_mode == "round_start";
        HongqueEventInfo openingDraw = reconnectRestore ? null : FindOpeningDrawEvent(state);
        if (!tableInitialized || newMatch || reconnectRestore || roundStart) {
            GameInfo tableState = BuildGameInfo(openingDraw);
            NormalGameStateManager.Instance.InitializeGame(true, state.message, tableState);
            tableInitialized = true;
            displayedRound = state.round;
            selfHasUnmergedDraw = false;
            if (openingDraw != null) {
                ApplyIncrementalEvent(openingDraw);
            }
            MarkEventsProcessed(state);
        } else if (state.phase == "round_end" || state.phase == "game_end") {
            // The hand/table mutations have already arrived as incremental
            // events.  Settlement is an overlay and must not rebuild the table.
            MarkEventsProcessed(state);
        } else {
            // Live messages are event-only.  A missing/unknown event is a
            // protocol error, never permission to reconstruct the table.
            ApplyEventBatch(state);
        }
        ShowRoundResult(previousState, state);
        if (state.phase == "round_end" || state.phase == "game_end") {
            // Settlement owns the UI until the shared ready phase completes.
            // Do not let the generic no-action branch manufacture a new timer.
            NormalGameStateManager.Instance.SwitchCurrentPlayer("None", "ClearAction", 0);
            if (state.phase == "game_end") {
                ShowGameEnd(state);
            } else {
                ApplyRuleTips();
            }
            return;
        }
        ApplyAvailableActions();
        ApplyRuleTips();
    }

    public void SendDiscard(int tileId) {
        if (!IsActive) return;
        Send("discard", HongqueTileVisual.ToCode(tileId));
        NormalGameStateManager.Instance.allowActionList.Clear();
        GameCanvas.Instance.ClearActionButton();
    }

    public bool TryChooseAction(string encodedAction) {
        if (!IsActive || string.IsNullOrEmpty(encodedAction) || !encodedAction.StartsWith("hongque_")) {
            return false;
        }
        if (encodedAction == "hongque_win") Send("win");
        else if (encodedAction == "hongque_supplement") Send("supplement");
        else if (encodedAction == "hongque_pass") Send("pass");
        else if (encodedAction.StartsWith(ClaimActionPrefix)) {
            Send("claim", null, encodedAction.Substring(ClaimActionPrefix.Length));
        }
        else if (encodedAction.StartsWith(KongActionPrefix)) {
            Send("kong", null, encodedAction.Substring(KongActionPrefix.Length));
        }
        else return false;

        NormalGameStateManager.Instance.allowActionList.Clear();
        GameCanvas.Instance.ClearActionButton();
        GameSceneMouseInputController.Instance.SetActionInputPhase(
            GameSceneMouseInputController.InputPhaseNone);
        return true;
    }

    public static string GetActionLabel(string encodedAction) {
        if (encodedAction == "hongque_group:sequence") return "吃";
        if (encodedAction == "hongque_group:triplet") return "碰";
        if (encodedAction == "hongque_group:rainbow") return "虹";
        if (encodedAction == "hongque_group:kong") return "杠";
        if (encodedAction == "hongque_win") return "和";
        if (encodedAction == "hongque_supplement") return "补牌";
        if (encodedAction == "hongque_pass") return "取消";
        if (!IsActive) return "操作";
        string candidateId = encodedAction.Contains(":") ? encodedAction.Substring(encodedAction.IndexOf(':') + 1) : null;
        HongqueCandidateInfo candidate = Instance.state.candidates?.FirstOrDefault(item => item.id == candidateId);
        if (encodedAction.StartsWith(KongActionPrefix)) return "杠";
        string label;
        switch (candidate?.kind) {
            case "win": label = "和"; break;
            case "sequence": label = "吃"; break;
            case "triplet": label = "碰"; break;
            case "rainbow": label = "虹"; break;
            default: label = "亮牌"; break;
        }
        return label;
    }

    public HongqueCandidateInfo[] GetCandidates(string kind) {
        return state?.candidates?
            .Where(item => item != null && item.kind == kind && !string.IsNullOrEmpty(item.id))
            .ToArray() ?? Array.Empty<HongqueCandidateInfo>();
    }

    public string EncodeCandidateAction(HongqueCandidateInfo candidate) {
        if (candidate == null) return null;
        if (candidate.kind == "kong") return KongActionPrefix + candidate.id;
        return ClaimActionPrefix + candidate.id;
    }

    private void Send(string action, string tile = null, string candidateId = null) {
        GameStateNetworkManager.Instance.SendHongqueAction(
            gamestateId, state.action_tick, action, tile, candidateId);
    }

    private void ApplyAvailableActions() {
        if (state.phase == "round_end" || state.phase == "game_end") {
            NormalGameStateManager.Instance.SwitchCurrentPlayer("None", "ClearAction", 0);
            lastActionUiKey = null;
            return;
        }
        List<string> actions = new List<string>();
        string[] legalActions = state.legal_actions ?? Array.Empty<string>();
        if (legalActions.Contains("discard")) actions.Add("cut");
        if (legalActions.Contains("win")) actions.Add("hongque_win");
        if (legalActions.Contains("supplement")) actions.Add("hongque_supplement");
        if (state.candidates != null) {
            if (legalActions.Contains("kong") && state.candidates.Any(item => item?.kind == "kong")) {
                actions.Add("hongque_group:kong");
            }
            if (legalActions.Contains("claim")) {
                HongqueCandidateInfo ron = state.candidates.FirstOrDefault(item => item?.kind == "win");
                if (ron != null) actions.Add("hongque_claim:" + ron.id);
                foreach (string kind in new[] { "rainbow", "triplet", "sequence" }) {
                    if (state.candidates.Any(item => item?.kind == kind)) actions.Add("hongque_group:" + kind);
                }
            }
        }
        if (legalActions.Contains("pass")) actions.Add("hongque_pass");

        string actionUiKey = string.Join("|", new[] {
            gamestateId ?? string.Empty,
            state.round.ToString(),
            state.action_tick.ToString(),
            state.phase ?? string.Empty,
            state.current_player.ToString(),
            string.Join(",", actions),
            string.Join(",", (state.candidates ?? Array.Empty<HongqueCandidateInfo>())
                .Where(item => item != null)
                .Select(item => $"{item.id}:{item.kind}"))
        });
        if (actionUiKey == lastActionUiKey) return;
        lastActionUiKey = actionUiKey;

        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        gsm.allowActionList = actions;
        if (actions.Count > 0) {
            // 虹雀询问用最近一次 discard 事件的牌 id 定位可操作牌，避免场上同点数歧义。
            gsm.currentAskCutTileId = lastDiscardTileId;
            string switchType = state.phase == "claim" ? "askMingPaiAction" : "askHandAction";
            gsm.SwitchCurrentPlayer(
                "self", switchType, state.remaining_time, state.you,
                false, null, state.step_remaining);
        } else if (state.phase == "claim") {
            // This viewer has already answered the claim window.  Clear local
            // input without manufacturing a fresh hand-action timer.
            gsm.SwitchCurrentPlayer("self", "ClearAction", 0);
        } else {
            gsm.AskHandAction(0, state.current_player, state.wall_count, Array.Empty<string>());
        }
    }

    private void ApplyRuleTips() {
        if (TipsBlock.Instance == null || TipsContainer.Instance == null) return;
        string tipsUiKey = string.Join("|", new[] {
            gamestateId ?? string.Empty,
            state.round.ToString(),
            state.action_tick.ToString(),
            state.phase ?? string.Empty,
            state.tips.ToString(),
            state.win_hint?.tile ?? string.Empty,
            string.Join(",", (state.waiting_hints ?? Array.Empty<HongqueScoreHintInfo>())
                .Where(item => item != null)
                .Select(item => item.tile ?? string.Empty))
        });
        if (tipsUiKey == lastTipsUiKey) return;
        lastTipsUiKey = tipsUiKey;
        if (!state.tips || state.phase == "round_end" || state.phase == "game_end") {
            TipsBlock.Instance.HideTipsBlock();
            TipsContainer.Instance.HideTips();
            return;
        }
        HongqueScoreHintInfo[] waits = state.waiting_hints ?? Array.Empty<HongqueScoreHintInfo>();
        if (state.win_hint != null || waits.Length > 0) {
            TipsBlock.Instance.ShowHongqueTips(waits, state.win_hint);
            return;
        }
        TipsBlock.Instance.HideTipsBlock();
        TipsContainer.Instance.HideTips();
    }

    private void MarkEventsProcessed(HongqueStateInfo update) {
        if (update?.events == null || update.events.Length == 0) return;
        lastProcessedEventId = Mathf.Max(lastProcessedEventId, update.events.Max(item => item?.id ?? 0));
    }

    private void ApplyEventBatch(HongqueStateInfo update) {
        if (update?.events == null) return;
        HongqueEventInfo[] pending = update.events
            .Where(item => item != null && item.id > lastProcessedEventId)
            .OrderBy(item => item.id)
            .ToArray();
        if (pending.Length == 0) return;
        foreach (HongqueEventInfo actionEvent in pending) {
            if (!CanApplyIncrementally(actionEvent)) {
                Debug.LogError($"虹雀实时事件未实现，已丢弃事件: {actionEvent.type} (id={actionEvent.id})");
                continue;
            }
            ApplyIncrementalEvent(actionEvent);
        }
        lastProcessedEventId = pending[pending.Length - 1].id;
    }

    private static bool CanApplyIncrementally(HongqueEventInfo actionEvent) {
        switch (actionEvent.type) {
            case "draw":
            case "supplement":
            case "discard":
            case "claim_apply":
            case "sequence":
            case "triplet":
            case "rainbow":
            case "kong":
            case "presence":
            case "self_draw":
            case "ron":
            case "draw_game":
                return true;
            default:
                return false;
        }
    }

    private void ApplyIncrementalEvent(HongqueEventInfo actionEvent) {
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        if (actionEvent.type == "presence") {
            HongquePlayerInfo player = state.players.FirstOrDefault(item => item.index == actionEvent.player);
            if (player != null) player.online = actionEvent.online;
            if (gsm.indexToPosition.TryGetValue(actionEvent.player, out string playerPosition)
                    && gsm.player_to_info.TryGetValue(playerPosition, out PlayerInfoClass playerInfo)) {
                playerInfo.tag_list = actionEvent.online ? Array.Empty<string>() : new[] { "offline" };
                GameCanvas.Instance.UpdatePlayerTagList(new Dictionary<int, string[]> {
                    { actionEvent.player, playerInfo.tag_list }
                });
            }
            return;
        }
        if (actionEvent.type == "draw" || actionEvent.type == "supplement") {
            bool isSupplement = actionEvent.type == "supplement";
            int tileId = string.IsNullOrEmpty(actionEvent.tile) ? 0 : HongqueTileVisual.FromCode(actionEvent.tile);
            if (actionEvent.player == state.you && selfHasUnmergedDraw) {
                // 补牌可以在同一回合连续发生。先把上一张摸牌收进主列，随后
                // deal_tile 才能把新牌放到唯一的最右摸牌位；两步均走原手牌队列。
                GameCanvas.Instance.ChangeHandCards("ReSetHandCards", 0, null, null);
            }
            if (isSupplement && gsm.indexToPosition.TryGetValue(actionEvent.player, out string supplementPosition)) {
                // 虹雀补牌不移出一张花牌，因此不能伪造 buhua 状态动作；只复用标准补花的
                // 字样、角色语音和物理音，再用标准补花后摸牌动作加入新张。
                GameCanvas.Instance.ShowActionDisplay(supplementPosition, "hongque_supplement", "hongque");
                SoundManager.Instance.PlayActionSound(supplementPosition, "buhua");
                SoundManager.Instance.PlayPhysicsSound("buhua");
            }
            gsm.DoAction(
                new[] { isSupplement ? "deal_buhua_tile" : "deal_tile" },
                actionEvent.player, null, null, null, null,
                tileId, null, null, null, null, isSilent: isSupplement);
            if (actionEvent.player == state.you) selfHasUnmergedDraw = true;
            return;
        }
        if (actionEvent.type == "discard") {
            int tileId = HongqueTileVisual.FromCode(actionEvent.tile);
            lastDiscardTileId = tileId;
            gsm.DoAction(
                new[] { "cut" }, actionEvent.player, tileId, null, null, actionEvent.cut_class,
                null, null, null, null, null, isSilent: false);
            if (actionEvent.player == state.you) selfHasUnmergedDraw = false;
            return;
        }

        if (actionEvent.type == "self_draw" || actionEvent.type == "ron" || actionEvent.type == "draw_game") {
            return;
        }

        if (actionEvent.type == "kong") {
            ApplyKongEvent(actionEvent);
            return;
        }

        if (actionEvent.type == "claim_apply") {
            ApplyClaimApplication(actionEvent);
            return;
        }

        int claimedTile = HongqueTileVisual.FromCode(actionEvent.tile);
        int[] meldMask = BuildMeldMask(actionEvent);
        string baseKind = string.IsNullOrEmpty(actionEvent.base_kind) ? actionEvent.type : actionEvent.base_kind;
        bool rainbow = actionEvent.type == "rainbow";
        bool claimApplied = actionEvent.silent;
        string tableAction = baseKind == "triplet" ? "peng" : "chi_left";
        int[] meldTiles = ConvertTiles(actionEvent.tiles);
        string prefix = baseKind == "triplet" ? "k" : "s";
        string target = prefix + (meldTiles.Length > 0 ? meldTiles[0].ToString() : claimedTile.ToString());
        gsm.DoAction(
            new[] { tableAction }, actionEvent.player, claimedTile, null, null, false,
            null, null, null, meldMask, target,
            isSilent: rainbow || claimApplied, cut_from_player: actionEvent.from_player);
        if (actionEvent.player == state.you) selfHasUnmergedDraw = false;
        if (rainbow && !claimApplied
                && gsm.indexToPosition.TryGetValue(actionEvent.player, out string position)) {
            GameCanvas.Instance.ShowActionDisplay(position, "hongque_rainbow", "hongque");
        }
    }

    /// <summary>
    /// 战术鸣牌申请帧：只播放发声/动画，不改动牌面。
    /// 申请已亮相后，对应执行帧会带 silent 标记，本家按钮在提交后由服务端状态清空，
    /// 其它更高优先级玩家仍保留亮牌按钮，可在剩余询问窗口内抢断。
    /// </summary>
    private void ApplyClaimApplication(HongqueEventInfo actionEvent) {
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        if (gsm == null || !gsm.indexToPosition.TryGetValue(actionEvent.player, out string position)) {
            return;
        }
        string kind = string.IsNullOrEmpty(actionEvent.kind) ? actionEvent.base_kind : actionEvent.kind;
        if (kind == "rainbow") {
            GameCanvas.Instance.ShowActionDisplay(position, "hongque_rainbow", "hongque");
            return;
        }
        string action = kind == "win" ? "hu" : (kind == "triplet" ? "peng" : "chi_left");
        SoundManager.Instance.PlayActionSound(position, action);
        SoundManager.Instance.PlayPhysicsSound(action);
        GameCanvas.Instance.ShowActionDisplay(position, action, "hongque");
    }

    private void ApplyKongEvent(HongqueEventInfo actionEvent) {
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        if (!gsm.indexToPosition.TryGetValue(actionEvent.player, out string position)
                || !gsm.player_to_info.TryGetValue(position, out PlayerInfoClass playerInfo)) return;
        string[] addedCodes = actionEvent.hand_tiles ?? Array.Empty<string>();
        if (addedCodes.Length == 0) return;

        if (actionEvent.meld_index < 0 || actionEvent.meld_index >= playerInfo.combination_tiles.Count) {
            Debug.LogError($"虹雀杠事件的副露索引无效: {actionEvent.meld_index}");
            return;
        }

        // 组装增长后的完整副露掩码：flag 3=本次加入，1=原副露的认走张，0=其余旧张。
        List<string> remainingAdded = addedCodes.ToList();
        bool claimedMarked = false;
        List<int> mask = new List<int>();
        foreach (string code in actionEvent.tiles ?? Array.Empty<string>()) {
            int addedIndex = remainingAdded.IndexOf(code);
            int flag;
            if (addedIndex >= 0) {
                flag = 3;
                remainingAdded.RemoveAt(addedIndex);
            } else if (!claimedMarked && code == actionEvent.claimed_tile) {
                flag = 1;
                claimedMarked = true;
            } else {
                flag = 0;
            }
            mask.Add(flag);
            mask.Add(HongqueTileVisual.FromCode(code));
        }

        // 更新副露数据：编码串 + 完整掩码（支持 3→4→5→6 连续补顺/补杠）。
        List<int> meldTiles = new List<int>();
        for (int i = 1; i < mask.Count; i += 2) {
            if (mask[i] != 0) meldTiles.Add(mask[i]);
        }
        string oldCode = playerInfo.combination_tiles[actionEvent.meld_index];
        HongqueMeldShape shape = HongqueScoring.ClassifyMeld(meldTiles);
        string prefix = shape == null
            ? (oldCode.Length > 0 ? oldCode[0].ToString() : "s")
            : (shape.Kind == "triplet" ? "k" : shape.Kind == "rainbow" ? "h" : "s");
        int firstTile = meldTiles.Count > 0
            ? meldTiles[0]
            : HongqueTileVisual.FromCode(actionEvent.claimed_tile);
        playerInfo.combination_tiles[actionEvent.meld_index] = prefix + firstTile;
        playerInfo.combination_masks[actionEvent.meld_index] = mask.ToArray();

        // 移出本次加入手牌的手牌：self 同步 2D/3D 手牌，其它家只减计数。
        for (int i = 0; i < addedCodes.Length; i++) {
            int tileId = HongqueTileVisual.FromCode(addedCodes[i]);
            if (position == "self") {
                gsm.selfHandTiles.Remove(tileId);
                GameCanvas.Instance.ChangeHandCards("RemoveJiagangCard", tileId, null, null);
            } else {
                playerInfo.hand_tiles_count = Mathf.Max(0, playerInfo.hand_tiles_count - 1);
            }
        }

        // 声音/动作字样 + 按权威数据重建该家全部副露。
        SoundManager.Instance.PlayActionSound(position, "gang");
        GameCanvas.Instance.ShowActionDisplay(position, "gang", "hongque");
        Game3DManager.Instance.RebuildPlayerMelds(position);
        selfHasUnmergedDraw = false;
    }

    private static int[] BuildMeldMask(HongqueEventInfo actionEvent) {
        List<int> mask = new List<int>();
        bool claimedMarked = false;
        foreach (string code in actionEvent.tiles ?? Array.Empty<string>()) {
            int id = HongqueTileVisual.FromCode(code);
            bool claimed = !claimedMarked && code == actionEvent.tile;
            mask.Add(claimed ? 1 : 0);
            mask.Add(id);
            claimedMarked |= claimed;
        }
        return mask.ToArray();
    }

    public void ConfirmRoundResult() {
        if (!IsRoundEnd) return;
        Send("ready");
    }

    private void ShowRoundResult(HongqueStateInfo previous, HongqueStateInfo current) {
        if (current?.round_result == null || current.phase != "round_end") return;
        if (previous != null && previous.phase == "round_end" && previous.action_tick == current.action_tick) return;
        int[] winners = current.round_result.winner_indices ?? Array.Empty<int>();
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        if (current.round_result.scores == null || current.round_result.scores.Count != 4) {
            Debug.LogError("虹雀结算消息缺少四家最终分数");
            return;
        }
        Dictionary<int, int> scores = new Dictionary<int, int>(current.round_result.scores);
        gsm.SwitchCurrentPlayer("None", "ClearAction", 0);
        TipsBlock.Instance?.HideTipsBlock();
        TipsContainer.Instance?.HideTips();
        foreach (KeyValuePair<int, string> seat in gsm.indexToPosition) {
            if (scores.TryGetValue(seat.Key, out int score) && gsm.player_to_info.TryGetValue(seat.Value, out PlayerInfoClass info)) {
                info.score = score;
            }
        }
        BoardCanvas.Instance?.UpdatePlayerScores(scores, gsm.indexToPosition);
        if (winners.Length == 0) {
            RoundEndPresentation.Instance.PresentLiuju("流局");
            return;
        }
        if (current.round_result.winners == null || current.round_result.winners.Length == 0) return;

        HongqueWinnerResultInfo result = current.round_result.winners
            .FirstOrDefault(item => item != null && item.player == current.you)
            ?? current.round_result.winners.FirstOrDefault(item => item != null);
        if (result == null || result.player < 0 || result.player >= 4) return;

        // 番种从大到小；可复计番（如清顺/清刻按组计）参照国标展示：按次数展开成多行。
        string[] fanTokens = (result.fans ?? Array.Empty<HongqueFanInfo>())
            .OrderByDescending(fan => fan.value)
            .SelectMany(fan => Enumerable.Repeat(
                $"{fan.name}|{fan.value}",
                Math.Max(1, fan.count)))
            .ToArray();
        string huClass = current.round_result.reason == "self_draw" ? "hu_self" : "hu";
        // 与其他麻将规则共用完整结算链：清操作与提示 -> 和牌字样/音效 -> 3D 倒牌 -> 结算面板。
        // room_rule=hongque 会使用普通荣和的原地倒牌，不会进入国标“从河里拿回和牌张”的分支。
        RoundEndPresentation.Instance.PresentHuResultSequence(
            result.player, scores, result.points, fanTokens, huClass,
            ConvertTiles(result.hand), Array.Empty<int>(),
            BuildResultMeldMasks(result.melds),
            result.@base, null, null, current.round_result.score_changes,
            isSilent: current.round_result.silent);
    }

    /// <summary>
    /// 终局：服务端在最后一局 ready 结束后广播 game_end。
    /// 关闭上一局的结算面板，用最终分数展示排位并允许返回大厅。
    /// </summary>
    private void ShowGameEnd(HongqueStateInfo current) {
        if (gameEndShown) return;
        gameEndShown = true;
        HongqueRoundResultInfo result = current.round_result;
        if (result?.scores == null || result.scores.Count != 4) {
            Debug.LogError("虹雀终局消息缺少四家最终分数");
            return;
        }
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        gsm.SwitchCurrentPlayer("None", "ClearAction", 0);
        TipsBlock.Instance?.HideTipsBlock();
        TipsContainer.Instance?.HideTips();

        Dictionary<int, int> scores = new Dictionary<int, int>(result.scores);
        foreach (KeyValuePair<int, string> seat in gsm.indexToPosition) {
            if (scores.TryGetValue(seat.Key, out int score)
                    && gsm.player_to_info.TryGetValue(seat.Value, out PlayerInfoClass info)) {
                info.score = score;
            }
        }
        BoardCanvas.Instance?.UpdatePlayerScores(scores, gsm.indexToPosition);

        // 关闭上一局的“和牌/流局”结算面板，展示最终排位与分数。
        EndResultPanel.Instance?.ClearEndResultPanel();
        EndGamePanel.Instance?.ClearEndGamePanel();

        List<HongquePlayerInfo> ordered = (current.players ?? Array.Empty<HongquePlayerInfo>())
            .Where(player => player != null)
            .OrderByDescending(player => scores.TryGetValue(player.index, out int s) ? s : player.score)
            .ToList();
        Dictionary<string, Dictionary<string, object>> finalData =
            new Dictionary<string, Dictionary<string, object>>();
        for (int i = 0; i < ordered.Count; i++) {
            HongquePlayerInfo player = ordered[i];
            int score = scores.TryGetValue(player.index, out int s) ? s : player.score;
            finalData[player.index.ToString()] = new Dictionary<string, object> {
                ["username"] = player.username,
                ["user_id"] = player.user_id,
                ["score"] = score,
                ["rank"] = i + 1,
                ["pt"] = 0f,
                ["original_player_index"] = player.index,
            };
        }
        EndGamePanel.Instance?.ShowGameEndPanel("", "", "", finalData);
    }

    private static int[][] BuildResultMeldMasks(HongqueMeldInfo[] melds) {
        if (melds == null) return Array.Empty<int[]>();
        List<int[]> result = new List<int[]>();
        foreach (HongqueMeldInfo meld in melds) {
            List<int> mask = new List<int>();
            bool claimedMarked = false;
            foreach (string code in meld.tiles ?? Array.Empty<string>()) {
                bool claimed = !claimedMarked && code == meld.claimed_tile;
                mask.Add(claimed ? 1 : 0);
                mask.Add(HongqueTileVisual.FromCode(code));
                claimedMarked |= claimed;
            }
            result.Add(mask.ToArray());
        }
        return result.ToArray();
    }

    private static HongqueEventInfo FindOpeningDrawEvent(HongqueStateInfo snapshot) {
        if (snapshot?.events == null || snapshot.phase != "turn" || snapshot.players == null) return null;
        if (snapshot.players.Any(player =>
                (player.discards != null && player.discards.Length > 0)
                || (player.melds != null && player.melds.Length > 0)
                || player.supplements > 0)) {
            return null;
        }
        return snapshot.events
            .Where(item => item != null && item.type == "draw" && item.player == snapshot.dealer)
            .OrderByDescending(item => item.id)
            .FirstOrDefault();
    }

    private GameInfo BuildGameInfo(HongqueEventInfo excludedOpeningDraw = null) {
        PlayerInfo[] players = new PlayerInfo[state.players.Length];
        for (int i = 0; i < state.players.Length; i++) {
            players[i] = BuildPlayerInfo(state.players[i], excludedOpeningDraw);
        }
        PlayerInfo self = players.First(player => player.player_index == state.you);
        return new GameInfo {
            room_id = state.room_id,
            gamestate_id = gamestateId,
            tips = state.tips,
            current_player_index = state.current_player,
            action_tick = state.action_tick,
            max_round = state.max_round,
            tile_count = state.wall_count,
            current_round = state.round,
            step_time = state.step_time,
            round_time = state.round_time,
            room_type = "custom",
            room_rule = "hongque",
            sub_rule = "hongque/v1.6",
            dealer_index = state.dealer,
            players_info = players,
            player_entry_order = players.Select(player => player.user_id).ToArray(),
            self_hand_tiles = self.hand_tiles ?? Array.Empty<int>(),
            dora_indicators = Array.Empty<int>(),
            kan_dora_indicators = Array.Empty<int>(),
            detailed_config = new Dictionary<string, object>(),
        };
    }

    private PlayerInfo BuildPlayerInfo(HongquePlayerInfo source, HongqueEventInfo excludedOpeningDraw = null) {
        List<string> combinations = new List<string>();
        List<int[]> masks = new List<int[]>();
        if (source.melds != null) {
            foreach (HongqueMeldInfo meld in source.melds) {
                int[] tileIds = ConvertTiles(meld.tiles);
                string prefix = meld.kind == "triplet" ? "k" : meld.kind == "rainbow" ? "h" : "s";
                combinations.Add(prefix + (tileIds.Length > 0 ? tileIds[0].ToString() : "0"));
                List<int> mask = new List<int>();
                bool claimedMarked = false;
                foreach (int id in tileIds) {
                    bool isClaimed = !claimedMarked && HongqueTileVisual.ToCode(id) == meld.claimed_tile;
                    mask.Add(isClaimed ? 1 : 0);
                    mask.Add(id);
                    claimedMarked |= isClaimed;
                }
                masks.Add(mask.ToArray());
            }
        }
        List<int> hand = (source.index == state.you ? ConvertTiles(state.hand) : ConvertTiles(source.hand)).ToList();
        bool excludeDraw = excludedOpeningDraw != null && excludedOpeningDraw.player == source.index;
        if (excludeDraw && source.index == state.you && !string.IsNullOrEmpty(excludedOpeningDraw.tile)) {
            hand.Remove(HongqueTileVisual.FromCode(excludedOpeningDraw.tile));
        }
        int handCount = source.index == state.you ? hand.Count : Mathf.Max(0, source.hand_count - (excludeDraw ? 1 : 0));
        return new PlayerInfo {
            username = source.username,
            user_id = source.user_id,
            hand_tiles_count = handCount,
            hand_tiles = hand.ToArray(),
            discard_tiles = ConvertTiles(source.discards),
            discard_origin_tiles = ConvertTiles(source.discards),
            combination_tiles = combinations.ToArray(),
            combination_mask = masks.ToArray(),
            remaining_time = state.remaining_time,
            player_index = source.index,
            original_player_index = source.index,
            score = source.score,
            title_used = source.title_used > 0 ? source.title_used : 1,
            profile_used = source.profile_used > 0 ? source.profile_used : 1,
            character_used = source.character_used > 0 ? source.character_used : 1,
            voice_used = source.voice_used > 0 ? source.voice_used : 1,
            huapai_list = Array.Empty<int>(),
            score_history = Array.Empty<string>(),
            round_number_history = Array.Empty<int>(),
            tag_list = Array.Empty<string>(),
            initial_hu_types = Array.Empty<string>(),
            discard_riichi_flags = new bool[source.discards?.Length ?? 0],
        };
    }

    private static int[] ConvertTiles(string[] codes) {
        if (codes == null) return Array.Empty<int>();
        return codes.Select(HongqueTileVisual.FromCode).Where(id => id != 0).ToArray();
    }
}
