#if UNITY_EDITOR
using System;

/// <summary>广东收件时钟的明确时间向量；不创建房间、不修改场景、不启动游戏。</summary>
public static class GuangdongTimingValidation {
    public static object RunAll() {
        int checks = 0;
        Action<int, int, int, int, int> vector = (bank, step, elapsed, expectedBank, expectedStep) => {
            var clock = new GuangdongAskClock();
            clock.EnterRound("gd-test", 1);
            clock.Project(1, bank, step, 100, 100 + elapsed, out int remaining, out int stepLeft);
            if (remaining != expectedBank || stepLeft != expectedStep)
                throw new Exception($"GD clock {bank}+{step} after {elapsed}: {remaining}+{stepLeft}");
            checks += 2;
        };
        vector(20, 5, 0, 20, 5);
        vector(20, 5, 3, 20, 2);
        vector(20, 5, 8, 17, 0);
        vector(0, 8, 5, 0, 3);
        vector(40, 10, 12, 38, 0);
        vector(20, 0, 7, 13, 0);
        vector(0, 0, 0, 0, 0);
        vector(20, 5, 40, 0, 0);
        var duplicate = new GuangdongAskClock();
        duplicate.EnterRound("gd-test", 1);
        duplicate.Project(7, 20, 5, 100, 103, out int bankLeft, out int graceLeft);
        duplicate.Project(7, 20, 5, 108, 108, out bankLeft, out graceLeft);
        if (bankLeft != 17 || graceLeft != 0) throw new Exception("Duplicate prompt extended GD window");
        checks += 2;
        duplicate.EnterRound("gd-test", 1); // A game_start for reconnect keeps the same round/window.
        duplicate.Project(7, 20, 5, 109, 109, out bankLeft, out graceLeft);
        if (bankLeft != 16 || graceLeft != 0) throw new Exception("Same-round restore reset GD clock");
        checks += 2;
        duplicate.Project(8, 17, 5, 110, 110, out bankLeft, out graceLeft);
        if (bankLeft != 17 || graceLeft != 5) throw new Exception("New cut window missed its step");
        checks += 2;
        duplicate.EnterRound("gd-test", 2);
        duplicate.Project(8, 20, 5, 200, 200, out bankLeft, out graceLeft);
        if (bankLeft != 20 || graceLeft != 5) throw new Exception("Next hand missed its bank reset");
        checks += 2;
        duplicate.Reset();
        duplicate.EnterRound("new-game", 1);
        duplicate.Project(8, 40, 10, 300, 300, out bankLeft, out graceLeft);
        if (bankLeft != 40 || graceLeft != 10) throw new Exception("New session retained old deadline");
        checks += 2;
        var packets = new GuangdongAskClock();
        packets.EnterRound("gd-packets", 1);
        var hand = Newtonsoft.Json.JsonConvert.DeserializeObject<Response>(
            "{\"type\":\"gamestate/guangdong/broadcast_hand_action\",\"received_monotonic\":123," +
            "\"ask_hand_action_info\":{\"remaining_time\":20,\"step_remaining\":0,\"action_tick\":1,\"action_list\":[\"cut\"]}}");
        if (hand.received_monotonic.HasValue) throw new Exception("Packet forged receipt clock");
        checks++;
        hand.received_monotonic = 100;
        packets.ApplyHand(hand, 8, 102);
        if (hand.ask_hand_action_info.remaining_time != 18 || hand.ask_hand_action_info.step_remaining != 0)
            throw new Exception("Hand packet reapplied full room step");
        checks += 2;
        var claim = Newtonsoft.Json.JsonConvert.DeserializeObject<Response>(
            "{\"type\":\"gamestate/guangdong/ask_other_action\",\"ask_other_action_info\":" +
            "{\"remaining_time\":0,\"step_remaining\":8,\"action_tick\":2,\"action_list\":[\"pass\"]}}");
        claim.received_monotonic = 200;
        packets.ApplyClaim(claim, 5, 205);
        if (claim.ask_other_action_info.remaining_time != 0 || claim.ask_other_action_info.step_remaining != 3)
            throw new Exception("Claim queue delay not deducted from step");
        checks += 2;
        var legacy = Newtonsoft.Json.JsonConvert.DeserializeObject<Response>(
            "{\"ask_hand_action_info\":{\"remaining_time\":20,\"action_tick\":3,\"action_list\":[\"cut\"]}}");
        legacy.received_monotonic = 300;
        packets.ApplyHand(legacy, 8, 303);
        if (legacy.ask_hand_action_info.remaining_time != 20 || legacy.ask_hand_action_info.step_remaining != 5)
            throw new Exception("Legacy packet ignored configured room step");
        checks += 2;
        packets.EnterRound("gd-view", 1, 1);
        packets.Project(9, 5, 5, 400, 405, out bankLeft, out graceLeft);
        packets.EnterRound("gd-view", 1, 2);
        packets.Project(9, 20, 5, 406, 406, out bankLeft, out graceLeft);
        if (bankLeft != 20 || graceLeft != 5) throw new Exception("Spectator inherited another seat's deadline");
        checks += 2;
        duplicate.Close();
        if (!duplicate.IsClosed(8) || !duplicate.IsClosed(7) || duplicate.IsClosed(9))
            throw new Exception("Closed/stale prompt can reopen GD input");
        checks += 3;
        duplicate.EnterRound("new-game", 2);
        if (duplicate.IsClosed(8)) throw new Exception("Next hand retained closed prompt");
        checks++;
        var fractional = new GuangdongAskClock();
        fractional.Project(1, 20, 5, 100, 100.25, out bankLeft, out graceLeft);
        fractional.Remaining(100.25, out double exactBank, out double exactStep);
        if (Math.Abs(exactBank - 20) > 1e-8 || Math.Abs(exactStep - 4.75) > 1e-8)
            throw new Exception("Queue fraction restored a full displayed step");
        checks += 2;
        fractional.Remaining(105.25, out exactBank, out exactStep);
        if (Math.Abs(exactBank - 19.75) > 1e-8 || exactStep != 0)
            throw new Exception("Queue fraction was not billed to displayed bank");
        checks += 2;
        var exactPacket = Newtonsoft.Json.JsonConvert.DeserializeObject<Response>(
            "{\"ask_hand_action_info\":{\"remaining_time\":19,\"step_remaining\":5," +
            "\"remaining_time_ms\":18200,\"step_remaining_ms\":5000,\"action_tick\":4,\"action_list\":[\"cut\"]}}");
        exactPacket.received_monotonic = 400;
        packets.ApplyHand(exactPacket, 5, 401.25);
        packets.Remaining(401.25, out exactBank, out exactStep);
        if (Math.Abs(exactBank - 18.2) > 1e-8 || Math.Abs(exactStep - 3.75) > 1e-8)
            throw new Exception("New hand rounded away its exact server bank");
        checks += 2;
        var exactClaim = Newtonsoft.Json.JsonConvert.DeserializeObject<Response>(
            "{\"ask_other_action_info\":{\"remaining_time\":16,\"step_remaining\":0," +
            "\"remaining_time_ms\":15950,\"step_remaining_ms\":0,\"action_tick\":5,\"action_list\":[\"peng\",\"pass\"]}}");
        exactClaim.received_monotonic = 500;
        packets.ApplyClaim(exactClaim, 5, 500.25);
        packets.Remaining(500.25, out exactBank, out exactStep);
        if (Math.Abs(exactBank - 15.7) > 1e-8 || exactStep != 0)
            throw new Exception("Claim reconnect restored a rounded second or room step");
        checks += 2;
        return new { passed = true, checks };
    }
}
#endif
