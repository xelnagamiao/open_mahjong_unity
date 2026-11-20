#if UNITY_EDITOR || MATCH_RECORD_HEADLESS
using System;
using Newtonsoft.Json.Linq;

public static class MatchRecordValidation {
    public static string Run(string fixtureJson) {
        int checks = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
        var fixture = JObject.Parse(fixtureJson);
        var record = (JObject)fixture["archive"];
        var session = MatchRecordSession.FromRecord(record);
        var origin = session.AnchorAt(1, 2, "after_draw", 1);
        Check(origin != null, "physical origin present");
        Check(session.Switch("closed", 1, 2, true, "after_draw", 1).Node == 3, "different node counts align to same original wall slot");
        Check(session.Mode == MatchRecordSession.PlaybackMode.Compare, "compare pauses playback");
        Check(session.Switch("open", 1, 3, true, "before_draw", 1).Node == 1, "before draw is the unconsumed prefix");
        var initial = session.AnchorAt(1, 0, "after_draw", 0);
        Check(MatchRecordSession.Locate(session.Table("closed"), initial).Node == 0, "initial dealer draw maps to node zero");
        Check(MatchRecordSession.Locate(session.Table("closed"), initial, "before_draw").Status == "PHASE_UNAVAILABLE", "initial before draw does not exist");
        var absent = (JObject)origin.DeepClone(); absent["offset"] = 13;
        Check(MatchRecordSession.Locate(session.Table("closed"), absent).Status == "NOT_CONSUMED", "completed round missing origin");
        absent["board_instance_id"] = "different-attempt";
        Check(MatchRecordSession.Locate(session.Table("closed"), absent).Status == "INSTANCE_UNVERIFIED", "equal walls cannot override board instance");
        session.Table("closed")["round_meta"]["round_index_1"]["alignment_status"] = "partial";
        absent["board_instance_id"] = origin["board_instance_id"].DeepClone();
        Check(MatchRecordSession.Locate(session.Table("closed"), absent).Status == "ORIGIN_UNAVAILABLE", "partial historical metadata never invents an origin");
        var before = session.ActiveTable;
        var failed = session.Switch("main", 1, 1, true, "after_draw", 1);
        Check(!failed.Exact && session.ActiveTable == before, "failed comparison keeps the current table");
        session.Mode = MatchRecordSession.PlaybackMode.History;
        session.Switch("closed", 1, 2, false, "after_draw", 1);
        session.Switch("open", 1, 1, false, "after_draw", 1);
        Check(session.Position("closed").Node == 1 && session.Position("open").Node == 2, "each table keeps its own cursor");
        Check((string)record["tables"]["closed"]["round_meta"]["round_index_1"]["alignment_status"] == "exact", "archive is immutable to cache callers");

        var live = new MatchRecordSession("test-match", true);
        var packets = (JArray)fixture["packets"];
        for (int i = 0; i < 3; i++) Check(live.ApplyPacket((JObject)packets[i]) == "applied", "initial snapshots " + i);
        Check(live.Position("open", true).Node == 1 && live.Position("closed", true).Node == 1, "snapshots contain only released actions");
        var delta = (JObject)packets[3];
        var gap = (JObject)delta.DeepClone(); gap["from_seq"] = 5; gap["to_seq"] = 5;
        Check(live.ApplyPacket(gap) == "resync" && live.Position("open", true).Node == 1, "sequence gap cannot mutate cache");
        var corrupt = (JObject)delta.DeepClone(); corrupt["events"][0]["start_node"] = 0;
        Check(live.ApplyPacket(corrupt) == "resync" && live.Position("open", true).Node == 1, "node gap cannot mutate cache");
        Check(live.ApplyPacket(delta) == "applied", "resume delta accepted");
        Check(live.Position("open", true).Node == 2 && live.Position("closed", true).Node == 1, "table streams progress independently");
        Check(live.ApplyPacket(delta) == "duplicate" && live.Position("open", true).Node == 2, "duplicate packet does not append twice");
        live.Mode = MatchRecordSession.PlaybackMode.Compare;
        Check(live.ApplyPacket((JObject)packets[4]) == "applied" && live.Mode == MatchRecordSession.PlaybackMode.Compare, "incoming tail cannot restore live while comparing");
        var wrongEpoch = (JObject)delta.DeepClone(); wrongEpoch["stream_epoch"] = "expired";
        Check(live.ApplyPacket(wrongEpoch) == "resync", "old epoch requires snapshot");
        var snapshot = (JObject)packets[1].DeepClone(); snapshot["stream_epoch"] = "new-attempt";
        Check(live.ApplyPacket(snapshot) == "applied" && live.Position("open", true).Node == 1, "snapshot replaces prior epoch");
        Check(live.ApplyPacket((JObject)packets[1]) == "duplicate", "late snapshot cannot resurrect a retired epoch");
        Check(!live.FinalStreamsApplied, "completion waits for final vector");
        var finish = (JObject)packets[0].DeepClone(); finish["at_seq"] = 1;
        finish["body"]["final_streams"] = new JObject {
            ["open"] = new JObject { ["stream_epoch"] = "new-attempt", ["applied_seq"] = 1 },
            ["closed"] = new JObject { ["stream_epoch"] = "epoch-closed", ["applied_seq"] = 1 }
        };
        Check(live.ApplyPacket(finish) == "applied" && !live.FinalStreamsApplied, "manifest can arrive before final table delta");
        var finalDelta = (JObject)delta.DeepClone(); finalDelta["stream_epoch"] = "new-attempt"; live.ApplyPacket(finalDelta);
        Check(live.FinalStreamsApplied, "all final sequence cursors form completion barrier");
        Check((int)live.ExportRecord()["schema_version"] == 2, "live stream exports a versioned archive");
        Check(live.RestrictTables(new[] { "closed" }), "revocation changes the visible cache");
        Check(live.Table("open") == null && live.Manifest == null && live.ActiveTable == "closed", "revoked table and metadata are removed before rejoin");
        Check(live.ResumeCursors()["open"] == null && live.ResumeCursors()["match"] == null, "revoked stream cannot resume from stale permissions");
        return checks + " match record checks passed";
    }
}
#endif
