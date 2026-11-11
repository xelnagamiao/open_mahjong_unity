#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>真实时间计时与保留操作合同；无需开局，不修改场景或运行中的询问。</summary>
public static class SichuanClaimWaitValidation {
    public static object RunAll() {
        var checks = new List<string>();
        void Check(bool value, string message) {
            if (!value) throw new Exception("Sichuan claim wait: " + message);
            checks.Add(message);
        }

        var clock = new SichuanClaimWaitClock();
        clock.Close(50);
        Check(!clock.IsStaleAsk(1, 50), "ordinary-only lifecycle does not enable the new stale-ask guard");
        Check(clock.TryProject(10, "real_time", 5, 100, 101.25, out double remaining)
            && Math.Abs(remaining - 3.75) < 1e-9, "queue delay deducted from independent five seconds");
        Check(clock.IsStaleAsk(9, 10) && !clock.IsStaleAsk(10, 10) && !clock.IsStaleAsk(11, 10),
            "after retained ask, older hand or claim packets are rejected while same tick remains compatible");
        Check(clock.TryProject(10, "real_time", 5, 103, 103, out remaining)
            && Math.Abs(remaining - 2) < 1e-9, "repeated packet cannot restore full second wait");
        Check(clock.TryProject(10, "real_time", 5, 105.1, 105.1, out remaining)
            && remaining == 0 && clock.IsOpen, "soft expiry retains claim eligibility");
        Check(clock.TryProject(10, "waiting_draw", 0, 105.2, 105.2, out remaining)
            && remaining == 0 && clock.IsOpen, "server waiting state retains same action window");
        Check(clock.TryProject(10, "real_time", 5, 106, 106, out remaining)
            && remaining == 0, "late countdown packet cannot leave passive waiting");
        clock.Close();
        Check(!clock.IsOpen && !clock.TryProject(10, "waiting_draw", 0, 107, 107, out remaining),
            "submitted or server-closed window cannot reopen from queued waiting update");
        Check(!clock.TryProject(9, "real_time", 20, 108, 108, out remaining), "older tick rejected");
        Check(clock.TryProject(11, "real_time", 8, 110, 111, out remaining) && remaining == 7,
            "new discard receives new configured duration");
        Check(clock.TryProject(11, "waiting_draw", 0, 112, 112, out remaining) && remaining == 0,
            "authoritative early transition immediately removes countdown");
        Check(clock.TryProject(12, "real_time", 15, 120, 121, out remaining) && remaining == 14,
            "later ask leaves passive state");
        clock.Close(14);
        Check(!clock.TryProject(13, "real_time", 5, 122, 122, out remaining)
            && !clock.TryProject(14, "waiting_draw", 0, 122, 122, out remaining),
            "newer authoritative close rejects intermediate and same-tick stale asks");
        clock.Reset();
        Check(!clock.IsStaleAsk(1, 50), "round or reconnect reset restores ordinary packet compatibility");
        Check(!clock.IsOpen && clock.TryProject(1, "real_time", 1.75, 200, 200.5, out remaining)
            && Math.Abs(remaining - 1.25) < 1e-9, "reconnect restores server remaining duration without full reset");
        clock.Reset();
        Check(clock.TryProject(1, "waiting_draw", 0, 210, 210, out remaining)
            && remaining == 0 && clock.IsOpen, "reconnect can restore passive claim window directly");
        Check(!SichuanClaimWaitClock.IsRetainedMode(null) && !SichuanClaimWaitClock.IsRetainedMode("tactical"),
            "legacy and tactical asks keep ordinary hard timeout");

        foreach (int seconds in new[] { 3, 5, 8, 10, 15, 20 }) {
            clock.Reset();
            Check(clock.TryProject(1, "real_time", seconds, 300, 300, out remaining) && remaining == seconds,
                "configured duration " + seconds + " has no room step added");
        }
        var response = JsonConvert.DeserializeObject<Response>(
            "{\"ask_other_action_info\":{\"action_tick\":42,\"action_list\":[\"peng\",\"gang\",\"hu\",\"pass\"]," +
            "\"claim_wait_mode\":\"real_time\",\"claim_seconds_remaining\":4.625,\"remaining_time\":20}}");
        Check(response.ask_other_action_info.claim_wait_mode == "real_time"
            && response.ask_other_action_info.claim_seconds_remaining == 4.625
            && response.ask_other_action_info.remaining_time == 20,
            "wire duration remains independent of ordinary round bank");
        var legacy = JsonConvert.DeserializeObject<Response>("{\"ask_other_action_info\":{\"remaining_time\":20}}");
        Check(legacy.ask_other_action_info.claim_wait_mode == null
            && !legacy.ask_other_action_info.claim_seconds_remaining.HasValue, "old packet keeps nullable defaults");

        Check(GameCanvas.ClaimWaitDotCount(0) == 1 && GameCanvas.ClaimWaitDotCount(0.49) == 1
            && GameCanvas.ClaimWaitDotCount(0.5) == 2 && GameCanvas.ClaimWaitDotCount(1) == 3
            && GameCanvas.ClaimWaitDotCount(1.5) == 1, "ellipsis cycles one-two-three every half second");

        // 独立实例验证实际超时入口，避免触碰主场景的 TurnClock.Current。
        var retained = new TurnClock { AllowActionList = new List<string> { "peng", "gang", "hu", "pass" },
            LastAskActionTick = 42, CurrentAskCutTileId = 18, IsSelfActionRequired = true };
        typeof(TurnClock).GetProperty(nameof(TurnClock.IsRetainedClaimAsk)).GetSetMethod(true)
            .Invoke(retained, new object[] { true });
        retained.Timeout();
        Check(retained.AllowActionList.Count == 4 && retained.CurrentAskCutTileId == 18
            && retained.LastAskActionTick == 42 && retained.IsSelfActionRequired,
            "actual Timeout preserves soft-window buttons, target, tick and input eligibility");
        return new { passed = true, count = checks.Count, checks };
    }
}
#endif
