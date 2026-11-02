#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;

public static class SettlementSyncValidation {
    public static string Run() {
        var checks = new List<string>();
        void Check(bool value, string label) {
            if (!value) throw new InvalidOperationException(label);
            checks.Add(label);
        }
        SettlementSync.Reset();
        double now = 100;
        SettlementSync.Now = () => now;
        SettlementSyncInfo Info(int sequence = 1, int revision = 2, string round = "g:1", double? remaining = 8000) => new SettlementSyncInfo {
            gamestate_id = "g", round_id = round, result_id = round + ":" + sequence,
            sequence = sequence, revision = revision, remaining_ms = remaining,
            player_to_ready = new Dictionary<int, bool> { [0] = true, [1] = false }
        };
        try {
            Check(SettlementSync.Accept(Info(), true, 98), "delivery queue time is deducted");
            Check(SettlementSync.RemainingSeconds == 6, "remaining time starts at server budget minus queue delay");
            Check(SettlementSync.Present(Info()), "first result is presented");
            Check(!SettlementSync.Present(Info()), "duplicate result does not replay scoring or presentation");
            now += 4;
            Check(SettlementSync.RemainingSeconds == 2, "countdown uses elapsed time, not frame count");
            Check(SettlementSync.Accept(Info(revision: 3, remaining: 7000)), "new ready revision accepted");
            Check(SettlementSync.RemainingSeconds == 2, "ready acknowledgement cannot extend deadline");
            Check(SettlementSync.Present(Info(revision: 3), true), "reconnect can restore the current result");
            now += 3;
            Check(SettlementSync.RemainingSeconds == 0, "expired countdown clamps to zero");
            Check(SettlementSync.Accept(Info(sequence: 2)), "next settlement step accepted");
            Check(!SettlementSync.Accept(Info(revision: 99)), "late previous step is rejected");
            Check(SettlementSync.Accept(Info(sequence: 2, revision: 0, round: "g:2", remaining: null), true), "next hand snapshot accepted");
            Check(!SettlementSync.Accept(Info(sequence: 2, revision: 99)), "late ready cannot cross hand boundary");
            Check(SettlementSync.RemainingSeconds == null, "next hand clears old countdown");
            var response = JsonConvert.DeserializeObject<Response>("{\"type\":\"gamestate/guobiao/game_start\",\"settlement_restore\":[{\"type\":\"gamestate/guobiao/show_result\",\"settlement_sync\":{\"restore\":true,\"player_to_ready\":{\"0\":true}}}]}");
            Check(response.settlement_restore[0].settlement_sync.player_to_ready[0], "wire restores typed seat acknowledgements");
            SettlementSync.Dispatch(response.settlement_restore[0], () => Check(SettlementSync.IsRestoring, "restoration context is active during dispatch"));
            Check(!SettlementSync.IsRestoring, "restoration context cannot leak to subsequent live result");
            IEnumerator Nested() {
                Check(SettlementSync.IsRestoring, "nested coroutine retains restore context");
                yield return null;
                Check(SettlementSync.IsRestoring, "nested continuation retains restore context");
            }
            IEnumerator Delayed() {
                yield return null;
                Check(SettlementSync.IsRestoring, "delayed result retains restore context");
                yield return Nested();
            }
            IEnumerator delayed = null;
            SettlementSync.Dispatch(response.settlement_restore[0], () => delayed = SettlementSync.PreserveContext(Delayed()));
            delayed.MoveNext();
            Check(!SettlementSync.IsRestoring, "waiting coroutine does not affect live packets");
            delayed.MoveNext();
            var nested = (IEnumerator)delayed.Current;
            nested.MoveNext(); nested.MoveNext(); delayed.MoveNext();
            Check(!SettlementSync.IsRestoring, "completed nested coroutine restores dispatch context");
            return JsonConvert.SerializeObject(new { passed = checks.Count, checks });
        } finally { SettlementSync.Reset(); }
    }
}
#endif
