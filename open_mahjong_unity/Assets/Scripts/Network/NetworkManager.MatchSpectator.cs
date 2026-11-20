using System;
using System.Collections;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public partial class NetworkManager {
    private MatchRecordSession matchSpectatorSession;
    private string matchSpectatorRequest;
    private string matchSpectatorSubscription;
    private bool enteringMatchSpectator;
    private bool restoringMatchSpectator;
    public static bool HasMatchSpectatorSession => Instance != null && Instance.matchSpectatorSession != null;

    public async void BeginMatchSpectating(string matchId, string selectedTable = "open") {
        if (LobbyStateGuard.BlockIfInMatchQueueForSpectator() || GameSessionGuard.BlockIfExclusiveSession("进入比赛观战")) return;
        matchSpectatorSession = new MatchRecordSession(matchId, true, selectedTable);
        try { await SubscribeMatchSpectator(); }
        catch (Exception e) {
            ClearMatchSpectatorSession();
            NotificationManager.Instance?.ShowTip("观战", false, e.Message);
        }
    }

    private async Task SubscribeMatchSpectator() {
        if (matchSpectatorSession == null) return;
        matchSpectatorRequest = Guid.NewGuid().ToString();
        matchSpectatorSubscription = null;
        await GetWebSocket().SendText(JsonConvert.SerializeObject(new {
            type = "spectator/match_subscribe", protocol_version = 2,
            match_id = matchSpectatorSession.MatchId, request_id = matchSpectatorRequest,
            requested_tables = new[] { "open", "closed" }, resume = matchSpectatorSession.ResumeCursors()
        }));
    }

    public async void ResumeMatchSpectator() {
        if (matchSpectatorSession == null) return;
        var manager = GameRecordManager.Instance;
        if (manager?.MatchSession == matchSpectatorSession) {
            matchSpectatorSession.Remember(manager.currentRoundIndex, manager.currentNode);
            restoringMatchSpectator = true;
        }
        try { await SubscribeMatchSpectator(); }
        catch (Exception e) { NotificationManager.Instance?.ShowTip("观战", false, "恢复观战失败：" + e.Message); }
    }

    public void SuspendMatchSpectator() {
        if (matchSpectatorSession == null) return;
        var manager = GameRecordManager.Instance;
        if (manager?.MatchSession == matchSpectatorSession) {
            matchSpectatorSession.Remember(manager.currentRoundIndex, manager.currentNode);
            manager.SuspendMatchRecordView();
        }
        restoringMatchSpectator = true;
    }

    public void ClearMatchSpectatorSession() {
        matchSpectatorSession = null; matchSpectatorRequest = null; matchSpectatorSubscription = null;
        enteringMatchSpectator = false;
        restoringMatchSpectator = false;
    }

    public async void LeaveMatchSpectator() {
        string subscription = matchSpectatorSubscription;
        ClearMatchSpectatorSession();
        if (string.IsNullOrEmpty(subscription)) return;
        try { await GetWebSocket().SendText(JsonConvert.SerializeObject(new {
            type = "spectator/match_unsubscribe", protocol_version = 2, subscription_id = subscription
        })); } catch (Exception) { /* Disconnect cleanup also removes the subscription. */ }
    }

    private bool HandleMatchSpectatorMessage(string json, string type) {
        if (type != "spectator/match_accepted" && type != "spectator/stream_snapshot" && type != "spectator/stream_delta"
            && type != "spectator/error" && type != "spectator/match_unsubscribed") return false;
        var packet = MatchRecordCodec.Parse(json);
        if (type == "spectator/match_unsubscribed" || matchSpectatorSession == null) return true;
        if (type == "spectator/error") {
            if ((string)packet["request_id"] != matchSpectatorRequest && (string)packet["subscription_id"] != matchSpectatorSubscription) return true;
            string code = (string)packet["code"];
            NotificationManager.Instance?.ShowTip("观战", false, "比赛观战已中断：" + code);
            if ((bool?)packet["retryable"] == true) ResumeMatchSpectator();
            else {
                GameRecordManager.Instance?.ResetForSessionEnd(); ClearMatchSpectatorSession();
                GameSceneTeardown.ResetToIdle();
                WindowsManager.Instance?.ExitGameToReturnWindow();
            }
            return true;
        }
        if (type == "spectator/match_accepted") {
            if ((string)packet["request_id"] != matchSpectatorRequest || (string)packet["match_id"] != matchSpectatorSession.MatchId) return true;
            matchSpectatorSubscription = (string)packet["subscription_id"];
            var allowed = (JArray)packet["allowed_tables"];
            if (allowed == null || allowed.Count == 0) { LeaveMatchSpectator(); return true; }
            if (matchSpectatorSession.RestrictTables(System.Linq.Enumerable.Select(allowed, t => (string)t))) {
                restoringMatchSpectator = true;
                RequestMatchStreamResync("match");
            }
            TryEnterMatchSpectator();
            return true;
        }
        if (string.IsNullOrEmpty(matchSpectatorSubscription) || (string)packet["subscription_id"] != matchSpectatorSubscription) return true;
        string applied = matchSpectatorSession.ApplyPacket(packet);
        if (applied == "resync") { RequestMatchStreamResync((string)packet["stream_id"]); return true; }
        if (applied == "applied") {
            if (GameRecordManager.Instance?.MatchSession == matchSpectatorSession)
                GameRecordManager.Instance.RefreshMatchStream((string)packet["stream_id"]);
            else TryEnterMatchSpectator();
        }
        AcknowledgeMatchStreams();
        return true;
    }

    private async void RequestMatchStreamResync(string stream) {
        try { await GetWebSocket().SendText(JsonConvert.SerializeObject(new {
            type = "spectator/stream_resync", protocol_version = 2, subscription_id = matchSpectatorSubscription, stream_id = stream
        })); } catch (Exception e) { Debug.LogWarning("比赛观战补发请求失败：" + e.Message); }
    }

    private async void AcknowledgeMatchStreams() {
        if (matchSpectatorSession == null) return;
        try { await GetWebSocket().SendText(JsonConvert.SerializeObject(new {
            type = "spectator/stream_ack", protocol_version = 2, subscription_id = matchSpectatorSubscription,
            cursors = matchSpectatorSession.ResumeCursors()
        })); } catch (Exception e) { Debug.LogWarning("比赛观战确认失败：" + e.Message); }
    }

    private void TryEnterMatchSpectator() {
        if (enteringMatchSpectator || matchSpectatorSession == null || !matchSpectatorSession.Ready(matchSpectatorSession.ActiveTable)) return;
        if (GameRecordManager.Instance?.MatchSession == matchSpectatorSession && !restoringMatchSpectator) return;
        enteringMatchSpectator = true;
        StartCoroutine(EnterMatchSpectator(matchSpectatorSession));
    }

    private IEnumerator EnterMatchSpectator(MatchRecordSession session) {
        // Matchmaking/normal table entry may have overtaken an in-flight subscribe.
        if (BlocksMatchSpectatorEntry(session)) { LeaveMatchSpectator(); yield break; }
        WindowsManager.Instance.SwitchWindow("game");
        yield return null;
        enteringMatchSpectator = false;
        if (matchSpectatorSession != session) yield break;
        if (BlocksMatchSpectatorEntry(session)) { LeaveMatchSpectator(); yield break; }
        try {
            if (GameRecordManager.Instance == null) throw new InvalidOperationException("对局 UI 尚未就绪");
            GameRecordManager.Instance.LoadMatchSession(session);
            restoringMatchSpectator = false;
            if (session.FinalStreamsApplied) GameRecordManager.Instance.RefreshMatchStream("match");
        } catch (Exception e) {
            LeaveMatchSpectator();
            GameRecordManager.Instance?.ResetForSessionEnd();
            NotificationManager.Instance?.ShowTip("观战", false, "无法打开比赛观战：" + e.Message);
        }
    }

    private static bool BlocksMatchSpectatorEntry(MatchRecordSession session) {
        var state = NormalGameStateManager.Instance;
        var record = GameRecordManager.Instance;
        return LobbyStateGuard.IsInMatchQueue || (state != null && (state.IsGameActive || state.IsRealtimeSpectator))
            || (record != null && record.IsSpectating && record.MatchSession != session);
    }
}
