#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

public static class BackgroundResyncValidation {
    public static string Run() {
        var checks = new List<string>();
        void Check(bool value, string label) {
            if (!value) throw new InvalidOperationException(label);
            checks.Add(label);
        }

        // 1. Wire deserialization of snapshot timeout_auto_moqie and transition packet.
        var snap = JsonConvert.DeserializeObject<Response>(
            "{\"type\":\"gamestate/guobiao/game_start\",\"gamestate_id\":\"g1\",\"timeout_auto_moqie\":true}");
        Check(snap != null && snap.timeout_auto_moqie == true, "snapshot deserializes timeout_auto_moqie=true");

        var toggle = JsonConvert.DeserializeObject<Response>(
            "{\"type\":\"gamestate/timeout_auto_moqie\",\"gamestate_id\":\"g1\",\"active\":false,\"timeout_auto_moqie\":false}");
        Check(toggle != null && toggle.active == false && toggle.timeout_auto_moqie == false,
            "transition packet deserializes timeout_auto_moqie=false");

        // 2. Server-authoritative timeout hosting suppresses client recount.
        var policy = new AutoActionPolicy();
        var serverAuthField = typeof(AutoActionPolicy).GetField("serverAuthoritativeTimeoutHosting", BindingFlags.Instance | BindingFlags.NonPublic);
        var activeField = typeof(AutoActionPolicy).GetField("timeoutAutoMoqieActive", BindingFlags.Instance | BindingFlags.NonPublic);
        var countField = typeof(AutoActionPolicy).GetField("consecutiveSelfTimeoutCuts", BindingFlags.Instance | BindingFlags.NonPublic);
        policy.ApplyServerTimeoutAutoMoqie(true);
        Check((bool)serverAuthField.GetValue(policy), "ApplyServerTimeoutAutoMoqie switches policy to server-authoritative");
        Check((bool)activeField.GetValue(policy), "ApplyServerTimeoutAutoMoqie(true) sets active");
        policy.ApplyServerTimeoutAutoMoqie(false);
        Check(!(bool)activeField.GetValue(policy), "ApplyServerTimeoutAutoMoqie(false) clears active");
        for (int i = 0; i < 6; i++) {
            policy.RegisterSelfTimeoutCut(new[] { "cut" }, GameSession.Current.SelfIndex, true);
        }
        Check(!(bool)activeField.GetValue(policy) && (int)countField.GetValue(policy) == 0,
            "replayed historical timeout cuts do not re-trigger client popup once server-authoritative");

        // 3. RoundEndPresentation epoch advances on hard stop and gameplay control restore.
        var presGo = new GameObject("BackgroundResyncValidation_Presentation");
        try {
            var pres = presGo.AddComponent<RoundEndPresentation>();
            int e0 = pres.PresentationEpoch;
            pres.StopActiveSequence();
            int e1 = pres.PresentationEpoch;
            Check(e1 > e0, "StopActiveSequence advances presentationEpoch");
            pres.ShowSelfGameplayControlAndResyncHand3D();
            int e2 = pres.PresentationEpoch;
            Check(e2 > e1, "ShowSelfGameplayControlAndResyncHand3D advances presentationEpoch");
        } finally {
            UnityEngine.Object.DestroyImmediate(presGo);
        }

        // 4. NetworkManager game-state backlog drop preserves control messages.
        var isGameMethod = typeof(NetworkManager).GetMethod("IsGameStateQueueJson", BindingFlags.Static | BindingFlags.NonPublic);
        Check((bool)isGameMethod.Invoke(null, new object[] { "{\"type\":\"gamestate/guobiao/do_action\"}" }),
            "gamestate/* identified as droppable game message");
        Check((bool)isGameMethod.Invoke(null, new object[] { "{\"type\":\"refresh_player_tag_list\"}" }),
            "refresh_player_tag_list identified as droppable game message");
        Check(!(bool)isGameMethod.Invoke(null, new object[] { "{\"type\":\"message\",\"message\":\"login_kickout\"}" }),
            "login_kickout preserved across resync queue drop");
        Check(!(bool)isGameMethod.Invoke(null, new object[] { "{\"type\":\"pong\"}" }),
            "pong preserved across resync queue drop");

        // 5. ChatManager classifies silent polls as non-interactive (no timeout tip spam).
        var isInteractiveChat = typeof(ChatManager).GetMethod("IsInteractiveConversationRequest", BindingFlags.Static | BindingFlags.NonPublic);
        Check((bool)isInteractiveChat.Invoke(null, new object[] { "chat/send" }), "chat/send is interactive");
        Check((bool)isInteractiveChat.Invoke(null, new object[] { "chat/open_direct" }), "chat/open_direct is interactive");
        Check(!(bool)isInteractiveChat.Invoke(null, new object[] { "chat/list" }), "chat/list poll is silent");
        Check(!(bool)isInteractiveChat.Invoke(null, new object[] { "chat/venues" }), "chat/venues poll is silent");
        Check(!(bool)isInteractiveChat.Invoke(null, new object[] { "chat/read" }), "chat/read is silent");

        // 6. AutoReconnect.OnLoginResponse ignores late tips once login has already succeeded.
        var arType = typeof(AutoReconnect);
        var stateField = arType.GetField("_state", BindingFlags.Static | BindingFlags.NonPublic);
        var waitField = arType.GetField("_waitState", BindingFlags.Static | BindingFlags.NonPublic);
        var stateEnum = arType.GetNestedType("State", BindingFlags.NonPublic);
        var waitType = arType.GetNestedType("WaitState", BindingFlags.NonPublic);
        var prevState = stateField.GetValue(null);
        var prevWait = waitField.GetValue(null);
        try {
            var wait = Activator.CreateInstance(waitType);
            waitType.GetField("AwaitingLogin").SetValue(wait, true);
            stateField.SetValue(null, Enum.Parse(stateEnum, "Reconnecting"));
            waitField.SetValue(null, wait);
            AutoReconnect.OnLoginResponse(true);
            Check((bool)waitType.GetField("LoginDone").GetValue(wait) && (bool)waitType.GetField("LoginSuccess").GetValue(wait),
                "OnLoginResponse(true) succeeds during Phase 2");
            // Late business tip during Phase 3/4 must NOT flip LoginSuccess back to false.
            AutoReconnect.OnLoginResponse(false);
            Check((bool)waitType.GetField("LoginSuccess").GetValue(wait),
                "late tips during Phase 3/4 cannot overwrite LoginSuccess");
        } finally {
            stateField.SetValue(null, prevState);
            waitField.SetValue(null, prevWait);
        }

        // 7. TableCatchupMode fast-forward toggle & AutoReconnect preserved scoreboard history.
        TableCatchupMode.SetFastForwarding(true);
        Check(TableCatchupMode.IsFastForwarding, "TableCatchupMode enters fast-forward");
        TableCatchupMode.SetFastForwarding(false);
        Check(!TableCatchupMode.IsFastForwarding, "TableCatchupMode exits fast-forward");

        var idField = arType.GetField("_preservedHistoryGamestateId", BindingFlags.Static | BindingFlags.NonPublic);
        var histField = arType.GetField("_preservedSettlementHistory", BindingFlags.Static | BindingFlags.NonPublic);
        idField.SetValue(null, "match-keep-1");
        histField.SetValue(null, new List<RoundSettlementSnapshot> {
            new RoundSettlementSnapshot { huClass = "hu_self", huScore = 8 },
            new RoundSettlementSnapshot { huClass = "hu_second", huScore = 16 },
        });
        var restored = AutoReconnect.ConsumePreservedSettlementHistory("match-keep-1");
        Check(restored != null && restored.Count == 2 && restored[0].huScore == 8 && restored[1].huScore == 16,
            "same-match AutoReconnect restores preserved main-fan settlement history");
        Check(AutoReconnect.ConsumePreservedSettlementHistory("match-keep-1") == null,
            "preserved settlement history is consumed once");

        idField.SetValue(null, "match-keep-1");
        histField.SetValue(null, new List<RoundSettlementSnapshot> {
            new RoundSettlementSnapshot { huClass = "hu_self", huScore = 8 },
        });
        Check(AutoReconnect.ConsumePreservedSettlementHistory("match-other-2") == null,
            "different gamestate_id discards preserved history without leaking");

        return JsonConvert.SerializeObject(new { passed = checks.Count, checks });
    }
}
#endif
