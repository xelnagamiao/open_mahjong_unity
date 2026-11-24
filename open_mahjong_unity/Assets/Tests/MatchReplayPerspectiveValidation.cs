#if UNITY_EDITOR || MATCH_RECORD_HEADLESS
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

public static class MatchReplayPerspectiveValidation {
    public static string Run(string fixtureJson, string realJson) {
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
        var fixture = (JObject)JObject.Parse(fixtureJson)["archive"];
        var session = MatchRecordSession.FromRecord(fixture);
        var source = session.Table("open")["round_meta"]["round_index_1"];
        var actions = (JArray)source["action_meta"];
        var origin = (JObject)actions[1]["wall_ref"].DeepClone();
        actions.Clear();
        var ticks = (JArray)session.Table("open")["payload"]["game_round"]["round_index_1"]["action_ticks"];
        ticks.Clear();
        for (int seat = 0; seat < 4; seat++) {
            var wall = (JObject)origin.DeepClone(); wall["offset"] = seat + 4;
            wall["source_original_seat"] = (seat + 2) % 4;
            actions.Add(new JObject { ["actor_original_seat"] = seat, ["wall_ref"] = wall, ["wall_ref_basis"] = "recorded" });
            ticks.Add(new JArray("d", 11));
        }
        for (int seat = 0; seat < 4; seat++) {
            Check((int)session.AnchorAt(1, 4, "after_draw", seat)["offset"] == seat + 4, "each selected player owns a separate latest draw " + seat);
            Check((int)session.AnchorAt(1, 4, "before_draw", seat)["source_original_seat"] == (seat + 2) % 4, "borrowed origin uses actor, not wall owner " + seat);
        }
        Check(session.AnchorAt(1, 0, "after_draw", 2) == null, "nondealer never borrows initial dealer draw");
        Check(session.AnchorAt(1, 0, "before_draw", 2) == null, "no scanning future for selected player");
        Check((int)session.AnchorAt(1, 2, "before_draw", 2)["offset"] == 6, "immediate selected draw uses before prefix");
        var common = (JObject)actions[2]["wall_ref"]; common["zone"] = "common_wall"; common["source_original_seat"] = null;
        Check((string)session.AnchorAt(1, 4, "after_draw", 2)["zone"] == "common_wall", "common wall retains actor identity");
        var targetMeta = session.Table("closed")["round_meta"]["round_index_1"];
        targetMeta["action_meta"] = actions.DeepClone();
        targetMeta["action_meta"][2]["actor_original_seat"] = 0;
        session.Table("closed")["payload"]["game_round"]["round_index_1"]["action_ticks"] = ticks.DeepClone();
        session.Remember(1, 1, 3);
        session.Switch("closed", 1, 1, false, "after_draw", 3);
        session.Remember(1, 2, 1);
        session.Switch("open", 1, 2, false, "after_draw", 1);
        var original = session.Position("open");
        Check(original.Node == 1 && original.ViewSeat == 3, "independent position includes perspective");
        session.ChangeMode(MatchRecordSession.PlaybackMode.Compare, 1, 1, 3);
        var located = session.Switch("closed", 1, 4, true, "after_draw", 2);
        Check(located.Exact && located.Node == 3 && located.ActorOriginalSeat == 0 && located.ViewSeat == 2, "target actual actor does not change perspective");
        var restored = session.ChangeMode(MatchRecordSession.PlaybackMode.History, 1, 3, 2);
        Check(restored.Node == 2 && restored.ViewSeat == 1, "leaving comparison restores independent target cursor");
        var returned = session.Switch("open", 1, 2, false, "after_draw", 1);
        Check(returned.Node == 1 && returned.ViewSeat == 3, "comparison did not overwrite source cursor");
        actions[2]["wall_ref"] = null; actions[2]["wall_ref_basis"] = "unavailable";
        Check(session.AnchorAt(1, 4, "after_draw", 2) == null, "missing latest draw origin cannot fall back");
        Check(session.AnchorAt(1, 2, "before_draw", 2) == null, "missing immediate draw origin cannot fall back");

        var json = JObject.Parse(realJson);
        var real = json["record"] as JObject ?? json;
        var replay = MatchRecordSession.FromRecord(real);
        Check(MatchReplayProjection.SyncMode(replay) == "同步", "sync flag read from real archive");
        foreach (string table in replay.TableIds) {
            replay.Table(table)["payload"]["game_title"]["duplicate_sync_mode"] = false;
            replay.Table(table)["payload"]["duplicate_match"]["sync_mode"] = false;
        }
        Check(MatchReplayProjection.SyncMode(replay) == "记录冲突", "metadata disagreement is explicit");
        replay.Manifest["sync_mode"] = false;
        Check(MatchReplayProjection.SyncMode(replay) == "非同步", "async metadata supported");
        long blue = 0, red = 0;
        int round = 0;
        foreach (var pair in ((JObject)replay.Table("open")["payload"]["game_round"]).Properties().OrderBy(p => (int)p.Value["round_index"])) {
            round = (int)pair.Value["round_index"];
            var steps = (JArray)pair.Value["action_ticks"];
            int summary = steps.Select((t, i) => new { t, i }).Single(x => (string)x.t[0] == "liuju" && (string)x.t[1] == "board_summary").i;
            var start = MatchReplayProjection.ScoreAt(replay, round, 0);
            var before = MatchReplayProjection.ScoreAt(replay, round, summary);
            Check(start.Blue == blue && start.Red == red && before.Blue == blue && before.Red == red, "no future IMP before formal settlement " + round);
            var settlement = replay.Manifest["settlements"].Single(r => (int)r["number"] == round);
            blue += (long)settlement["imp_scores"][0]; red += (long)settlement["imp_scores"][1];
            var after = MatchReplayProjection.ScoreAt(replay, round, summary + 1);
            Check(after.Blue == blue && after.Red == red && after.SettledBoards == round, "cumulative archived IMP at summary " + round);
        }
        var finalImp = real["result"]["imp_scores"];
        Check(blue == (long)finalImp["0"] && red == (long)finalImp["1"], "cumulative board IMP matches the recorded final team result");
        Check(MatchReplayProjection.ScoreAt(replay, 1, 0).Blue == 0, "seeking backward recomputes totals");
        replay.Manifest["rule_profile"]["scoring"] = "raw_difference";
        Check(MatchReplayProjection.ScoreAt(replay, round, 9999).Blue == (long)finalImp["0"], "raw-difference official rule still displays replay IMP");
        replay.Manifest["settlements"][0]["imp_scores"] = null;
        Check(!MatchReplayProjection.ScoreAt(replay, 2, 0).Available, "missing IMP never becomes zero or raw points");
        var unknown = MatchRecordSession.FromRecord(fixture);
        unknown.Manifest.Remove("sync_mode");
        foreach (string table in unknown.TableIds) {
            ((JObject)unknown.Table(table)["payload"]["game_title"]).Remove("duplicate_sync_mode");
            ((JObject)unknown.Table(table)["payload"]).Remove("duplicate_match");
        }
        Check(MatchReplayProjection.SyncMode(unknown) == "未记录", "missing flags remain unknown");
        var conflict = MatchRecordSession.FromRecord(real);
        ((JArray)conflict.Manifest["board_catalog"]).Add(conflict.Manifest["board_catalog"][0].DeepClone());
        Check(!MatchReplayProjection.ScoreAt(conflict, 2, 0).Available, "ambiguous board number does not reuse a settlement");
        Check(MatchReplayProjection.WallSource(new JObject { ["wall_source"] = "training" }) == "训练牌山", "training source has explicit label");
        return checks + " perspective/projection checks passed";
    }
}
#endif
