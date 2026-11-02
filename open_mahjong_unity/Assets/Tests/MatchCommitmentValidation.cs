#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>Run on an idle test client; exercises authoritative match responses and stale polls.</summary>
public static class MatchCommitmentValidation {
    public static string Run() {
        if (MatchNetworkManager.Instance == null || GameSessionGuard.HasExclusiveSession
            || !string.IsNullOrEmpty(PlayerSession.Current.GamestateId))
            throw new InvalidOperationException("Match validation requires an idle test client.");
        var manager = MatchNetworkManager.Instance;
        var checks = new List<string>();
        void Check(bool condition, string name) {
            if (!condition) throw new InvalidOperationException("Match commitment: " + name);
            checks.Add(name);
        }
        void Receive(string type, long revision, bool committed, string queue, string[] queues = null) {
            manager.HandleMatchMessage(new Response {
                type = type, success = true, match_revision = revision,
                my_queues = queues ?? Array.Empty<string>(), match_committed = committed,
                match_queue_type = queue
            });
        }
        try {
            foreach (string queue in new[] { "hongque_elo_banzhuang", "hongque_elo_quanzhuang", "beginner_dongfeng" }) {
                foreach (string source in new[] { "match/match_found", "match/queue_status" }) {
                    manager.ClearLocalMatchState();
                    Receive(source, 10, true, queue);
                    Check(manager.IsMatchCommitted, queue + " locks from " + source);
                    Receive("match/queue_status", 9, false, null);
                    Check(manager.IsMatchCommitted, "older release cannot undo commitment");
                    Receive("match/queue_status", 11, false, null);
                    Check(!manager.IsMatchCommitted && !MatchStateManager.Instance.IsMatchFound,
                        "newer release clears both network and countdown state");
                    Check(manager.CurrentQueues.Count == 0, "release clears visible committed queue");
                    Check(MatchFoundedPanel.Instance == null || !MatchFoundedPanel.Instance.gameObject.activeSelf,
                        "release hides entering-game overlay");
                    Receive("match/queue_status", 10, true, queue);
                    Check(!manager.IsMatchCommitted, "late commitment cannot lock released match again");
                    Receive("match/match_found", 12, true, queue);
                    Check(manager.IsMatchCommitted, "next match can commit");
                    Receive("match/failed", 13, false, null);
                    Check(!manager.IsMatchCommitted && !MatchStateManager.Instance.IsMatchFound,
                        "start failure clears match and countdown state");

                    manager.ClearLocalMatchState();
                    MatchStateManager.Instance.StartQueueing(queue);
                    Receive(source, 20, true, queue);
                    Receive("match/queue_status", 21, false, queue, new[] { queue });
                    Check(!manager.IsMatchCommitted && !MatchStateManager.Instance.IsMatchFound
                        && MatchStateManager.Instance.IsQueueing,
                        "released commitment can resume queueing after " + source);
                    Check(manager.CurrentQueues.Count == 1 && manager.CurrentQueues[0] == queue,
                        "resumed queue comes from the authoritative response");
                }
            }
            manager.ClearLocalMatchState();
            GameSession.Current.IsGameActive = true;
            Receive("match/queue_status", 20, true, "hongque_elo_quanzhuang");
            Check(!manager.IsMatchCommitted, "queue polls cannot take over an active game");
            GameSession.Current.IsGameActive = false;
            GameSession.Current.IsRealtimeSpectator = true;
            Receive("match/queue_status", 21, true, "hongque_elo_quanzhuang");
            Check(!manager.IsMatchCommitted, "queue polls cannot take over realtime spectating");
            return JsonConvert.SerializeObject(new { passed = checks.Count, checks });
        } finally {
            GameSession.Current.IsGameActive = false;
            GameSession.Current.IsRealtimeSpectator = false;
            manager.ClearLocalMatchState();
        }
    }
}
#endif
