#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>Exercise real inbound routes on a dedicated, initialized test client.</summary>
public static class SessionMessageValidation {
    public static string RunCapturedGameEnds(string capturedJson) {
        var session = GameSession.Current;
        if (!session.IsGameActive || string.IsNullOrEmpty(session.GamestateId))
            throw new InvalidOperationException("Session validation requires an active test table.");
        var checks = new List<string>();
        string id = session.GamestateId;
        var state = RuleRegistry.ActiveGameState;
        int handCount = GameCanvas.Instance.HandCardsContainer.childCount;
        foreach (JObject capture in JArray.Parse(capturedJson)) {
            var response = capture["packet"].ToObject<Response>();
            Check(!string.IsNullOrEmpty(response.gamestate_id) && response.gamestate_id != id,
                capture["case"] + " real broadcast carries its original game id", checks);
            GameStateNetworkManager.Instance.HandleGameStateMessage(response);
            Check(session.IsGameActive && session.GamestateId == id
                && ReferenceEquals(RuleRegistry.ActiveGameState, state)
                && GameCanvas.Instance.HandCardsContainer.childCount == handCount,
                capture["case"] + " real stale end preserves the current table", checks);
        }
        foreach (string type in new[] { "switch_seat", "refresh_player_tag_list" }) {
            // No body is necessary: a foreign-session envelope must be rejected
            // before any rule-independent table mutation or payload dereference.
            GameStateNetworkManager.Instance.HandleGameStateMessage(new Response {
                type = type, success = true, gamestate_id = id + "-previous"
            });
            Check(session.GamestateId == id && ReferenceEquals(RuleRegistry.ActiveGameState, state)
                && GameCanvas.Instance.HandCardsContainer.childCount == handCount,
                type + " from an old table cannot mutate the current one", checks);
        }
        foreach (string suffix in new[] { "game_start", "reconnect" }) {
            GameStateNetworkManager.Instance.HandleGameStateMessage(new Response {
                type = "gamestate/guobiao/" + suffix, success = true,
                gamestate_id = id + "-previous"
            });
            Check(session.GamestateId == id && ReferenceEquals(RuleRegistry.ActiveGameState, state)
                && GameCanvas.Instance.HandCardsContainer.childCount == handCount,
                suffix + " cannot replace an already-bound table", checks);
        }
        return JsonConvert.SerializeObject(new { passed = checks.Count, checks });
    }

    public static string RunForActiveTable(GameEndInfo endInfo) {
        var session = GameSession.Current;
        if (!session.IsGameActive || string.IsNullOrEmpty(session.GamestateId))
            throw new InvalidOperationException("Session validation requires an active test table.");
        var checks = new List<string>();
        string id = session.GamestateId;
        var state = RuleRegistry.ActiveGameState;
        int handCount = GameCanvas.Instance.HandCardsContainer.childCount;
        foreach (string rule in new[] { "hongque", "guobiao", "riichi", "qingque" }) {
            GameStateNetworkManager.Instance.HandleGameStateMessage(new Response {
                type = "gamestate/" + rule + "/game_end", success = true,
                gamestate_id = id + "-previous", game_end_info = endInfo
            });
            Check(session.IsGameActive && session.GamestateId == id
                && ReferenceEquals(RuleRegistry.ActiveGameState, state),
                rule + " stale end preserves current game and rule instance", checks);
            Check(GameCanvas.Instance.HandCardsContainer.childCount == handCount,
                rule + " stale end preserves hand objects", checks);
        }
        GameStateNetworkManager.Instance.HandleGameStateMessage(new Response {
            type = "gamestate/closed", success = false, gamestate_id = id + "-previous"
        });
        Check(session.IsGameActive && GameHost.Current.CurrentWindow == "game",
            "stale abnormal close preserves new table", checks);
        return JsonConvert.SerializeObject(new { passed = checks.Count, checks });
    }

    // Also run before the first snapshot and after game_end has cleared the
    // player's active membership, while the spectator session is still open.
    public static string RunForRealtimeSession() {
        var session = GameSession.Current;
        if (!session.IsRealtimeSpectator)
            throw new InvalidOperationException("Session validation requires realtime spectating.");
        var checks = new List<string>();
        string membership = PlayerSession.Current.GamestateId;
        string id = session.GamestateId;
        int handCount = GameCanvas.Instance.HandCardsContainer.childCount;
        foreach (string type in new[] { "friend/realtime_ended", "friend/realtime_kicked" }) {
            foreach (string previous in new[] { "previous-spectator-game", null }) {
                FriendNetworkManager.Instance.HandleFriendMessage(new Response {
                    type = type, success = true, realtime_gamestate_id = previous
                });
                Check(session.IsRealtimeSpectator && session.GamestateId == id
                    && PlayerSession.Current.GamestateId == membership
                    && GameCanvas.Instance.HandCardsContainer.childCount == handCount,
                    type + " cannot close another session: " + (previous ?? "missing id"), checks);
            }
        }
        foreach (string suffix in new[] { "game_start", "reconnect" }) {
            GameStateNetworkManager.Instance.HandleGameStateMessage(new Response {
                type = "gamestate/guobiao/" + suffix, success = true,
                gamestate_id = "previous-spectator-game"
            });
            Check(session.IsRealtimeSpectator && session.GamestateId == id
                && PlayerSession.Current.GamestateId == membership
                && GameCanvas.Instance.HandCardsContainer.childCount == handCount,
                suffix + " respects pending, live and completed spectator ownership", checks);
        }
        return JsonConvert.SerializeObject(new { passed = checks.Count, checks });
    }

    private static void Check(bool condition, string name, List<string> checks) {
        if (!condition) throw new InvalidOperationException("Session routing: " + name);
        checks.Add(name);
    }
}
#endif
