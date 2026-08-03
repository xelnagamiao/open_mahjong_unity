using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 将虹雀的权威内存快照适配到现有麻将桌。桌面、2D 手牌、3D 手牌/牌河/副露
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
        if (newState == null || newState.players == null || newState.players.Length != 4) return;
        bool newMatch = gamestateId != newGamestateId;
        HongqueStateInfo previousState = state;
        if (newMatch) {
            lastProcessedEventId = 0;
            selfHasUnmergedDraw = false;
        }
        gamestateId = newGamestateId;
        state = newState;

        HongqueEventInfo openingDraw = FindOpeningDrawEvent(state);
        GameInfo tableState = BuildGameInfo(openingDraw);
        if (!tableInitialized || newMatch || displayedRound != state.round) {
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
        } else if (TryApplyEventBatch(state)) {
            // Normal draw/discard/meld changes already went through the same
            // incremental animation path used by the regular mahjong games.
        } else {
            // Reconnects from an old server and variable-length Hongque kong
            // upgrades use one generic snapshot recovery.  Ordinary play never
            // reaches this path, so draw/discard/call animations stay intact.
            NormalGameStateManager.Instance.RefreshTableSnapshot(tableState);
            ShowSnapshotFallbackActions(state.events);
            selfHasUnmergedDraw = false;
            MarkEventsProcessed(state);
        }
        ShowRoundResult(previousState, state);
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

        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        gsm.allowActionList = actions;
        if (actions.Count > 0) {
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

    private void MarkEventsProcessed(HongqueStateInfo snapshot) {
        if (snapshot?.events == null || snapshot.events.Length == 0) return;
        lastProcessedEventId = Mathf.Max(lastProcessedEventId, snapshot.events.Max(item => item?.id ?? 0));
    }

    private bool TryApplyEventBatch(HongqueStateInfo snapshot) {
        if (snapshot?.events == null) return false; // old server: retain snapshot compatibility
        HongqueEventInfo[] pending = snapshot.events
            .Where(item => item != null && item.id > lastProcessedEventId)
            .OrderBy(item => item.id)
            .ToArray();
        if (pending.Length == 0) return true;
        if (pending.Any(item => !CanApplyIncrementally(item))) return false;

        foreach (HongqueEventInfo actionEvent in pending) ApplyIncrementalEvent(actionEvent);
        lastProcessedEventId = pending[pending.Length - 1].id;
        return true;
    }

    private static bool CanApplyIncrementally(HongqueEventInfo actionEvent) {
        switch (actionEvent.type) {
            case "draw":
            case "supplement":
            case "discard":
            case "sequence":
            case "triplet":
            case "rainbow":
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
        if (actionEvent.type == "draw" || actionEvent.type == "supplement") {
            int tileId = string.IsNullOrEmpty(actionEvent.tile) ? 0 : HongqueTileVisual.FromCode(actionEvent.tile);
            if (actionEvent.player == state.you && selfHasUnmergedDraw) {
                // 补牌可以在同一回合连续发生。先把上一张摸牌收进主列，随后
                // deal_tile 才能把新牌放到唯一的最右摸牌位；两步均走原手牌队列。
                GameCanvas.Instance.ChangeHandCards("ReSetHandCards", 0, null, null);
            }
            gsm.DoAction(
                new[] { "deal_tile" }, actionEvent.player, null, null, null, null,
                tileId, null, null, null, null, isSilent: false);
            if (actionEvent.player == state.you) selfHasUnmergedDraw = true;
            return;
        }
        if (actionEvent.type == "discard") {
            int tileId = HongqueTileVisual.FromCode(actionEvent.tile);
            gsm.DoAction(
                new[] { "cut" }, actionEvent.player, tileId, null, null, actionEvent.cut_class,
                null, null, null, null, null, isSilent: false);
            if (actionEvent.player == state.you) selfHasUnmergedDraw = false;
            return;
        }

        if (actionEvent.type == "self_draw" || actionEvent.type == "ron" || actionEvent.type == "draw_game") {
            return;
        }

        int claimedTile = HongqueTileVisual.FromCode(actionEvent.tile);
        int[] meldMask = BuildMeldMask(actionEvent);
        string baseKind = string.IsNullOrEmpty(actionEvent.base_kind) ? actionEvent.type : actionEvent.base_kind;
        bool rainbow = actionEvent.type == "rainbow";
        string tableAction = baseKind == "triplet" ? "peng" : "chi_left";
        int[] meldTiles = ConvertTiles(actionEvent.tiles);
        string prefix = baseKind == "triplet" ? "k" : "s";
        string target = prefix + (meldTiles.Length > 0 ? meldTiles[0].ToString() : claimedTile.ToString());
        gsm.DoAction(
            new[] { tableAction }, actionEvent.player, claimedTile, null, null, false,
            null, null, null, meldMask, target,
            isSilent: rainbow, cut_from_player: actionEvent.from_player);
        if (actionEvent.player == state.you) selfHasUnmergedDraw = false;
        if (rainbow && gsm.indexToPosition.TryGetValue(actionEvent.player, out string position)) {
            GameCanvas.Instance.ShowActionDisplay(position, "hongque_rainbow", "hongque");
        }
    }

    private void ShowSnapshotFallbackActions(HongqueEventInfo[] actionEvents) {
        if (actionEvents == null) return;
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        foreach (HongqueEventInfo actionEvent in actionEvents) {
            if (actionEvent == null || actionEvent.type != "kong") continue;
            if (gsm.indexToPosition.TryGetValue(actionEvent.player, out string position)) {
                SoundManager.Instance.PlayActionSound(position, "gang");
                GameCanvas.Instance.ShowActionDisplay(position, "gang", "hongque");
            }
        }
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
        if (EndResultPanel.Instance != null) EndResultPanel.Instance.ClearEndResultPanel();
    }

    private void ShowRoundResult(HongqueStateInfo previous, HongqueStateInfo current) {
        if (current?.round_result == null || current.phase != "round_end") return;
        if (previous != null && previous.phase == "round_end" && previous.action_tick == current.action_tick) return;
        int[] winners = current.round_result.winner_indices ?? Array.Empty<int>();
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        foreach (int winner in winners) {
            if (!gsm.indexToPosition.TryGetValue(winner, out string position)) continue;
            string action = current.round_result.reason == "self_draw" ? "hu_self" : "hu";
            GameCanvas.Instance.ShowActionDisplay(position, action, "hongque");
            // 与普通麻将结算路径一致：和牌字样与对应角色语音同时播放。
            SoundManager.Instance.PlayActionSound(position, action);
        }
        if (winners.Length == 0) {
            GameSceneUIManager.Instance.ShowEndLiuju("流局");
            return;
        }
        if (current.round_result.winners == null || current.round_result.winners.Length == 0) return;

        HongqueWinnerResultInfo result = current.round_result.winners
            .FirstOrDefault(item => item != null && item.player == current.you)
            ?? current.round_result.winners.FirstOrDefault(item => item != null);
        if (result == null || result.player < 0 || result.player >= current.players.Length) return;

        Dictionary<int, int> scores = current.players.ToDictionary(player => player.index, player => player.score);
        PlayerInfo winnerInfo = BuildPlayerInfo(current.players[result.player]);
        string[] fanTokens = (result.fans ?? Array.Empty<HongqueFanInfo>())
            .Select(fan => $"{fan.name}|{fan.total}")
            .ToArray();
        string huClass = current.round_result.reason == "self_draw" ? "hu_self" : "hu";
        GameSceneUIManager.Instance.ShowEndResult(
            result.player,
            scores,
            result.points,
            fanTokens,
            huClass,
            ConvertTiles(result.hand),
            Array.Empty<int>(),
            winnerInfo.combination_mask ?? Array.Empty<int[]>(),
            result.@base);
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
        int roomId = state.room_id;
        if (roomId <= 0) int.TryParse(UserDataManager.Instance.RoomId, out roomId);
        PlayerInfo self = players.First(player => player.player_index == state.you);
        return new GameInfo {
            room_id = roomId,
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

/// <summary>
/// 虹雀适配器仅在变长杠牌或旧服务端重连时使用的快照恢复接口。
/// 放在适配器文件中，避免向公共麻将状态机文件加入虹雀分支。
/// </summary>
public partial class NormalGameStateManager {
    public void RefreshTableSnapshot(GameInfo gameInfo) {
        Game3DManager.Instance.Clear3DTile();
        InitializeSetInfo(gameInfo, false);
        GameCanvas.Instance.InitializeUIInfo(gameInfo, indexToPosition);
        BoardCanvas.Instance.InitializeBoardInfo(gameInfo, indexToPosition);

        PlayerInfo selfPlayerInfo = GetSelfPlayerInfo(gameInfo);
        int[] hand = selfPlayerInfo?.hand_tiles ?? Array.Empty<int>();
        GameCanvas.Instance.ChangeHandCards("InitHandCards", 0, hand, null);
        Game3DManager.Instance.Change3DTile("InitHandCards", 0, 0, null, false, null);
        GenerateOtherPlayers3DTiles(gameInfo);

        if (indexToPosition.TryGetValue(gameInfo.current_player_index, out string currentPos)) {
            BoardCanvas.Instance.ShowCurrentPlayer(currentPos, remainTiles);
            CurrentPlayer = currentPos;
        }
        IsGameActive = true;
        IsSelfActionRequired = false;
    }
}
