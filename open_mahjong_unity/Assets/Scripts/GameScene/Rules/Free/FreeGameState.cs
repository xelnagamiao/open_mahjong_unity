using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

public enum FreeDiscardDest {
    River,
    Flower,
    Transfer,
}

/// <summary>
/// 自由模式族 GameState：无回合，常驻按钮，动作走 gamestate/free/*。
/// </summary>
public sealed class FreeGameState : GameStateBase {
    public const string RuleId = "free";

    public static FreeGameState Active => RuleRegistry.ActiveGameState as FreeGameState;

    public override string StateId => "free.sandbox";
    public override bool IsActive => tableReady;

    public FreeDiscardDest DiscardDest { get; set; } = FreeDiscardDest.River;
    public int ScoreRevision { get; private set; }
    public int? TransferTile { get; private set; }
    public int? LastRiverPlayer { get; private set; }
    public int? LastRiverTile { get; private set; }
    public readonly Dictionary<int, string> Votes = new Dictionary<int, string>();
    public readonly Dictionary<int, bool> Revealed = new Dictionary<int, bool>();

    private bool tableReady;
    private readonly Dictionary<int, List<int>> revealedHands = new Dictionary<int, List<int>>();
    private readonly Dictionary<int, int> scoreDraft = new Dictionary<int, int>();
    private bool hasScoreDraft;
    private FreeModeHud hud;

    private static TableMirror Mirror => TableMirror.Current;
    private static TurnClock Clock => TurnClock.Current;
    private static MonoBehaviour Host => GameSession.Current.Host;

    public override void OnGameStart(GameInfo gameInfo) {
        tableReady = true;
        CaptureRevealedHandsFromGameInfo(gameInfo);
        EnsureHud();
        RestorePersistentAsk();
        hud?.Refresh();
    }

    public override void OnSessionReset() {
        tableReady = false;
        Votes.Clear();
        Revealed.Clear();
        revealedHands.Clear();
        scoreDraft.Clear();
        hasScoreDraft = false;
        TransferTile = null;
        if (hud != null) {
            UnityEngine.Object.Destroy(hud.gameObject);
            hud = null;
        }
    }

    public override bool HandleMessage(string suffix, Response response) {
        switch (suffix) {
            case "game_start":
                AutoReconnect.OnGameRestored();
                NormalGameStateManager.Instance.InitializeGame(response.success, response.message, response.game_info);
                ApplyTable(response.free_table_info);
                RestorePersistentAsk();
                if (HasAnyRevealed()) RelayoutTable();
                hud?.Refresh();
                return true;
            case "do_action":
                PlayDoAction(response);
                return true;
            case "reveal":
            case "stand":
            case "scores":
            case "votes":
            case "transfer":
                ApplyTable(response.free_table_info);
                if (suffix == "reveal" && Host != null) Host.StartCoroutine(CoReveal(response.free_table_info));
                if (suffix == "stand") RestoreStand(response.free_table_info);
                if (suffix == "scores") ApplyScores(response.free_table_info);
                RestorePersistentAsk();
                hud?.Refresh();
                return true;
            case "game_end":
                ExitToRoom();
                return true;
            default:
                return false;
        }
    }

    public override bool TryChooseAction(string actionType) {
        if (!IsActive) return false;
        if (actionType == FreeActionWords.Draw) {
            SendAction("draw");
            return true;
        }
        if (actionType == FreeActionWords.Push) {
            SendAction("reveal");
            return true;
        }
        if (actionType == FreeActionWords.Stand) {
            SendAction("stand");
            return true;
        }
        if (actionType == "hu" || actionType == "hu_self" || actionType == "chi_left"
            || actionType == "peng" || actionType == "gang" || actionType == "buhua") {
            SendAction(actionType);
            return true;
        }
        return false;
    }

    public override bool TryCutTile(int tileId) {
        if (!IsActive) return false;
        if (DiscardDest == FreeDiscardDest.Flower) {
            SendAction("to_flower", tileId);
            return true;
        }
        if (DiscardDest == FreeDiscardDest.Transfer) {
            if (TransferTile.HasValue) {
                GameCanvas.Instance.ClearPendingLocalCuts();
                return true;
            }
            SendTransferPut(tileId);
            return true;
        }
        return false;
    }

    public void SendCreateMeld(int[] mask, bool includeRiver) {
        SendRaw(new {
            type = "gamestate/free/send_action",
            gamestate_id = PlayerSession.Current.GamestateId,
            action = "create_meld",
            combination_mask = mask,
            include_river = includeRiver,
        });
    }

    public void SendRecallRiver(int index, int tileId) {
        SendRaw(new {
            type = "gamestate/free/send_action",
            gamestate_id = PlayerSession.Current.GamestateId,
            action = "recall_river",
            index,
            tile = tileId,
        });
    }

    public void SendRecallFlower(int index, int tileId) {
        SendRaw(new {
            type = "gamestate/free/send_action",
            gamestate_id = PlayerSession.Current.GamestateId,
            action = "recall_flower",
            index,
            tile = tileId,
        });
    }

    public void SendRecallMeld(int meldIndex) {
        SendRaw(new {
            type = "gamestate/free/send_action",
            gamestate_id = PlayerSession.Current.GamestateId,
            action = "recall_meld",
            meld_index = meldIndex,
        });
    }

    public void SendTransferPut(int tileId) {
        SendRaw(new {
            type = "gamestate/free/send_action",
            gamestate_id = PlayerSession.Current.GamestateId,
            action = "transfer_put",
            tile = tileId,
        });
    }

    public void SendTransferTake() {
        SendAction("transfer_take");
    }

    public void SendScores(Dictionary<int, int> scores) {
        var payload = new Dictionary<string, int>();
        foreach (KeyValuePair<int, int> pair in scores) payload[pair.Key.ToString()] = pair.Value;
        SendRaw(new {
            type = "gamestate/free/set_scores",
            gamestate_id = PlayerSession.Current.GamestateId,
            score_revision = ScoreRevision,
            scores = payload,
        });
    }

    public void SendVote(string vote) {
        SendRaw(new {
            type = "gamestate/free/set_vote",
            gamestate_id = PlayerSession.Current.GamestateId,
            vote,
        });
    }

    public IReadOnlyList<int> SelfHand() => Mirror.SelfHandTiles;
    public PlayerInfoClass SelfInfo() => Mirror.Self;

    public bool TryGetScoreDraft(int playerIndex, out int score) {
        return scoreDraft.TryGetValue(playerIndex, out score);
    }

    public void SetScoreDraft(int playerIndex, int score) {
        scoreDraft[playerIndex] = score;
        hasScoreDraft = true;
    }

    public bool HasScoreDraft => hasScoreDraft;

    public void CommitScoreDraft() {
        var current = new Dictionary<int, int>();
        foreach (KeyValuePair<int, string> seat in Mirror.IndexToPosition) {
            PlayerInfoClass info = Mirror.Info(seat.Value);
            current[seat.Key] = info != null ? info.score : 0;
        }
        if (hasScoreDraft) {
            foreach (KeyValuePair<int, int> pair in scoreDraft) current[pair.Key] = pair.Value;
        }
        SendScores(current);
    }

    private void PlayDoAction(Response response) {
        DoActionInfo info = response.do_action_info;
        if (info == null) return;
        TableAction action = TableAction.From(info, Mirror);
        if (action.IsClaim) {
            ActionPlayback.Current.AnnounceClaim(action);
        } else {
            ActionPlayback.Current.Play(action);
            if (action.Words != null) {
                foreach (string word in action.Words) {
                    if (FreeActionWords.NeedsTableRelayout(word)) {
                        RelayoutTable();
                        break;
                    }
                }
                if (action.HasWord("deal_tile") && IsRevealed(action.PlayerIndex) && Host != null) {
                    AppendRevealedTile(action.PlayerIndex, action.DealTile ?? 0);
                    Host.StartCoroutine(CoRevealPlayer(action.PlayerIndex));
                }
            }
        }
        ApplyTable(response.free_table_info);
        RestorePersistentAsk();
        hud?.Refresh();
    }

    private void ApplyTable(FreeTableInfo table) {
        if (table == null) return;
        ScoreRevision = table.score_revision;
        TransferTile = table.transfer_tile;
        LastRiverPlayer = table.last_river_player;
        LastRiverTile = table.last_river_tile;
        CopyIntMap(table.votes, Votes, "blank");
        CopyBoolMap(table.revealed, Revealed);
        if (table.revealed_player_index.HasValue && table.revealed_hand != null) {
            revealedHands[table.revealed_player_index.Value] = new List<int>(table.revealed_hand);
        }
        if (table.scores != null) {
            bool remoteChanged = false;
            foreach (KeyValuePair<string, int> pair in table.scores) {
                if (!int.TryParse(pair.Key, out int index)) continue;
                string seat = Mirror.SeatOf(index);
                if (seat == null) continue;
                PlayerInfoClass info = Mirror.Info(seat);
                if (info != null && info.score != pair.Value) remoteChanged = true;
                if (info != null) info.score = pair.Value;
            }
            if (remoteChanged) {
                hasScoreDraft = false;
                scoreDraft.Clear();
            }
            ApplyScores(table);
        }
    }

    private void ApplyScores(FreeTableInfo table) {
        if (table?.scores == null) return;
        var scores = new Dictionary<int, int>();
        foreach (KeyValuePair<string, int> pair in table.scores) {
            if (int.TryParse(pair.Key, out int index)) scores[index] = pair.Value;
        }
        BoardCanvas.Instance?.UpdatePlayerScores(scores, Mirror.IndexToPosition);
    }

    private IEnumerator CoReveal(FreeTableInfo table) {
        if (table?.revealed_player_index == null || table.revealed_hand == null || table.revealed_hand.Length == 0) {
            yield break;
        }
        yield return CoRevealPlayer(table.revealed_player_index.Value);
    }

    private IEnumerator CoRevealPlayer(int playerIndex) {
        if (!revealedHands.TryGetValue(playerIndex, out List<int> hand) || hand == null || hand.Count == 0) {
            yield break;
        }
        string seat = Mirror.SeatOf(playerIndex);
        var request = new HepaiPresentationRequest {
            HepaiPlayerIndex = playerIndex,
            WinnerPosition = seat,
            HepaiPlayerHand = hand.ToArray(),
            WinTileMode = HepaiWinTilePresentMode.RonInstantThenPause,
        };
        yield return Game3DManager.Instance.PlayHepaiHandReveal(request);
    }

    private void RestoreStand(FreeTableInfo table) {
        if (table?.revealed_player_index == null) return;
        int playerIndex = table.revealed_player_index.Value;
        Revealed[playerIndex] = false;
        revealedHands.Remove(playerIndex);
        string seat = Mirror.SeatOf(playerIndex);
        Game3DManager.Instance.RestoreMidGameHandAfterCuoheRonReveal(seat);
    }

    private void RelayoutTable() {
        Game3DManager.Instance.StopAllRunningAnimations();
        Game3DManager.Instance.Clear3DTile();
        Transform handContainer = GameCanvas.Instance.HandCardsContainer;
        if (handContainer != null) {
            for (int i = handContainer.childCount - 1; i >= 0; i--) {
                Destroyer.Instance.AddToDestroyer(handContainer.GetChild(i));
            }
        }
        GameCanvas.Instance.ChangeHandCards("InitHandCards", 0, Mirror.SelfHandTiles.ToArray(), null);
        Game3DManager.Instance.Change3DTile("InitHandCards", 0, 0, null, false, null);
        foreach (string seat in TableMirror.Seats) {
            PlayerInfoClass info = Mirror.Info(seat);
            if (info == null) continue;
            if (info.discard_tiles != null) {
                for (int i = 0; i < info.discard_tiles.Count; i++) {
                    bool horizontal = info.discard_riichi_flags != null && i < info.discard_riichi_flags.Count && info.discard_riichi_flags[i];
                    Game3DManager.Instance.Change3DTile("SetDiscardWithoutAnimation", info.discard_tiles[i], 0, seat, false, null, horizontal);
                }
            }
            if (info.huapai_list != null) {
                foreach (int tileId in info.huapai_list) {
                    Game3DManager.Instance.Change3DTile("SetBuhuacardWithoutAnimation", tileId, 0, seat, false, null);
                }
            }
            Game3DManager.Instance.RebuildPlayerMelds(seat);
        }
        foreach (KeyValuePair<int, bool> pair in Revealed) {
            if (pair.Value && Host != null) Host.StartCoroutine(CoRevealPlayer(pair.Key));
        }
        BoardCanvas.Instance?.ShowCurrentPlayer(Clock.CurrentPlayer ?? "self", Mirror.RemainTiles);
    }

    private void RestorePersistentAsk() {
        Clock.AllowActionList = new List<string>(FreeActionWords.PersistentButtons);
        GameCanvas.Instance.StopTimeRunning();
        GameCanvas.Instance.SetActionButton(Clock.AllowActionList);
        GameSceneMouseInputController.Instance.SetActionInputPhase(GameSceneMouseInputController.InputPhaseAskHand);
        Clock.IsSelfActionRequired = true;
        GameCanvas.Instance.RefreshHandTileSelectability();
        BoardCanvas.Instance?.ShowCurrentPlayer("self", Mirror.RemainTiles);
    }

    private void EnsureHud() {
        if (hud != null || GameCanvas.Instance == null) return;
        var go = new GameObject("FreeModeHud", typeof(RectTransform));
        go.transform.SetParent(GameCanvas.Instance.transform, false);
        go.transform.SetAsLastSibling();
        hud = go.AddComponent<FreeModeHud>();
        hud.Bind(this);
    }

    private void CaptureRevealedHandsFromGameInfo(GameInfo gameInfo) {
        revealedHands.Clear();
        if (gameInfo?.players_info == null) return;
        foreach (PlayerInfo player in gameInfo.players_info) {
            if (player.hand_tiles == null || player.hand_tiles.Length == 0) continue;
            revealedHands[player.player_index] = new List<int>(player.hand_tiles);
        }
    }

    private bool IsRevealed(int playerIndex) {
        return Revealed.TryGetValue(playerIndex, out bool value) && value;
    }

    private bool HasAnyRevealed() {
        foreach (KeyValuePair<int, bool> pair in Revealed) {
            if (pair.Value) return true;
        }
        return false;
    }

    private void AppendRevealedTile(int playerIndex, int tileId) {
        if (tileId <= 0) return;
        if (!revealedHands.TryGetValue(playerIndex, out List<int> hand)) {
            hand = new List<int>();
            revealedHands[playerIndex] = hand;
        }
        hand.Add(tileId);
    }

    private void SendAction(string action, int tileId = 0) {
        object request = tileId > 0
            ? (object)new {
                type = "gamestate/free/send_action",
                gamestate_id = PlayerSession.Current.GamestateId,
                action,
                tile = tileId,
            }
            : new {
                type = "gamestate/free/send_action",
                gamestate_id = PlayerSession.Current.GamestateId,
                action,
            };
        SendRaw(request);
    }

    private async void SendRaw(object request) {
        try {
            await NetworkManager.Instance.GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"发送自由模式消息失败: {e.Message}");
        }
    }

    private static void ExitToRoom() {
        PlayerSession.Current.SetGamestateId("");
        GameSceneTeardown.ResetToIdle();
        GameHost.Current.SwitchWindow("room");
        RoomNetworkManager.Instance?.SyncMyRoom();
    }

    private static void CopyIntMap(Dictionary<string, string> src, Dictionary<int, string> dest, string fallback) {
        dest.Clear();
        if (src == null) return;
        foreach (KeyValuePair<string, string> pair in src) {
            if (int.TryParse(pair.Key, out int index)) dest[index] = pair.Value ?? fallback;
        }
    }

    private static void CopyBoolMap(Dictionary<string, bool> src, Dictionary<int, bool> dest) {
        dest.Clear();
        if (src == null) return;
        foreach (KeyValuePair<string, bool> pair in src) {
            if (int.TryParse(pair.Key, out int index)) dest[index] = pair.Value;
        }
    }
}
