#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

public static class MatchRecordCodecValidation {
    public static string Run(string fixtures) {
        int checks = 0;
        foreach (var item in MatchRecordCodec.Parse("{\"cases\":" + fixtures + "}")["cases"]) {
            var value = item["value"];
            if (MatchRecordCodec.Digest(value) != (string)item["sha256"])
                throw new Exception("Canonical vector " + checks + " differs: " + MatchRecordCodec.Canonical(value));
            var response = ChangshaExchangeSixResponse.Decode("{\"type\":\"data/get_record_by_id\",\"record_detail\":{\"record\":" + MatchRecordCodec.Canonical(value) + "}}");
            if (MatchRecordCodec.Digest(JToken.FromObject(response.record_detail.record)) != (string)item["sha256"])
                throw new Exception("Network record changes canonical vector " + checks);
            checks++;
        }
        VerifyCompactStorage();
        return checks + " cross-language integrity checks and compact storage checks passed";
    }

    static void VerifyCompactStorage() {
        var source = JObject.Parse(@"{'game_title':{'rule':'guobiao'},'game_round':{
            'round_index_1':{'action_ticks':[['d',11],['c',11],['d',12],['c',12],['end']],
                             'action_times_ms':[0,1700000000123,-1,true]},
            'round_index_2':{'action_ticks':[['ask_other',42],['end']]}}}");
        var expanded = DeviceRecordMigration.ConvertSingle("CompactTest", source, new JArray());
        var compact = (JObject)expanded.DeepClone();
        compact["storage_codec"] = "om.single_round_meta.v1";
        var metadata = (JObject)compact["tables"]["main"]["round_meta"];
        ((JObject)metadata["round_index_1"]).Remove("action_meta");
        ((JObject)metadata["round_index_1"]).Remove("legacy_node_map");
        ((JObject)metadata["round_index_2"]).Remove("action_meta");
        var original = (JObject)compact.DeepClone();
        var session = MatchRecordSession.FromRecord(compact);
        var actions = session.Table("main")["round_meta"]["round_index_1"]["action_meta"];
        if ((long)actions[0]["occurred_at_ms"] != 0 || (long)actions[1]["occurred_at_ms"] != 1700000000123 ||
            Enumerable.Range(2, 3).Any(i => actions[i]["occurred_at_ms"].Type != JTokenType.Null) ||
            !JToken.DeepEquals(expanded["tables"]["main"], session.Table("main")) ||
            session.Manifest["storage_codec"] != null || !JToken.DeepEquals(original, compact))
            throw new Exception("Compact replay expansion changed a timestamp, explicit cursor map or input");
        var aliases = new JObject();
        DeviceRecordMigration.AddAliases(aliases, new RecordDetail { game_id = "CompactTest", record = compact });
        if (!JToken.DeepEquals(aliases["CompactTest"]["node_map"]["round_index_2"], new JArray(0, 0, 1)) ||
            !JToken.DeepEquals(aliases["CompactTest"]["node_map"]["round_index_1"], new JArray(0, 1, 2, 3, 4, 5)))
            throw new Exception("Compact replay aliases changed a historical bookmark");

        const string round = "round_index_1";
        var explicitActions = new JArray((JArray)expanded["tables"]["main"]["round_meta"][round]["action_meta"]);
        explicitActions[0]["occurred_at_ms"] = 299;
        metadata[round]["action_meta"] = explicitActions;
        if (!JToken.DeepEquals(MatchRecordSession.FromRecord(compact).Table("main")["round_meta"][round]["action_meta"], explicitActions))
            throw new Exception("Compact replay overwrote explicit action metadata");
        foreach (var extension in new JToken[] { "vendor.future.v3", new JObject { ["vendor"] = "v3" }, JValue.CreateNull() }) {
            var full = (JObject)expanded.DeepClone();
            full["storage_codec"] = extension;
            var legacySession = MatchRecordSession.FromRecord(full);
            if (!ReferenceEquals(MatchRecordCodec.ExpandStorage(full), full) ||
                !JToken.DeepEquals(legacySession.Manifest["storage_codec"], extension) ||
                !JToken.DeepEquals(legacySession.Table("main"), full["tables"]["main"]))
                throw new Exception("Full V2 archive lost an unrecognized extension");
        }
        foreach (string invalid in new[] { "paired", "unknown", "unmarked" }) {
            var damaged = (JObject)original.DeepClone();
            if (invalid == "paired") damaged["match_format"] = "paired_tables_2v2";
            if (invalid == "unknown") damaged["storage_codec"] = "om.unsupported";
            if (invalid == "unmarked") damaged.Remove("storage_codec");
            try { MatchRecordSession.FromRecord(damaged); }
            catch (FormatException) { continue; }
            throw new Exception("Invalid compact replay accepted: " + invalid);
        }
    }

    static DeviceRecordMigration.Plan PrepareBothWays(Dictionary<string,string> source) {
        var expected = DeviceRecordMigration.Prepare(source);
        var directory = source.Keys.ToDictionary(key => key, key => (string)null);
        var staged = new Dictionary<string,string>();
        var plan = new DeviceRecordMigration.Plan();
        var steps = DeviceRecordMigration.PrepareSteps(directory, plan, true);
        while (steps.MoveNext()) {
            if (steps.Current is string key) directory[key] = source[key];
            if (steps.Current is KeyValuePair<string,string> output) staged.Add(output.Key, output.Value);
        }
        foreach (var pair in plan.Writes) staged.Add(pair.Key, pair.Value);
        if (staged.Count != expected.Writes.Count || expected.Writes.Any(pair =>
                !staged.TryGetValue(pair.Key, out var value) || value != pair.Value) ||
            !expected.Deletes.SequenceEqual(plan.Deletes))
            throw new Exception("Streaming changed the migration output or deletion order");
        if (directory.Any(pair => !pair.Key.StartsWith("__", StringComparison.Ordinal) && pair.Value != null))
            throw new Exception("A migrated source body is still retained");
        return expected;
    }

    public static string VerifyDeviceMigration() {
        var raw = new JObject {
            ["game_title"] = new JObject { ["rule"] = "guobiao" },
            ["game_round"] = new JObject { ["round_index_1"] = new JObject {
                ["action_ticks"] = new JArray(new JArray("c", 11), new JArray("ask_other", 42), new JArray("d", 12)),
                ["action_times_ms"] = new JArray(100, 110)
            }}
        };
        var envelope = new JObject { ["game_id"] = "Original", ["record"] = raw, ["players"] = new JArray() };
        var input = new Dictionary<string,string> {
            ["Original"] = envelope.ToString(),
            [DeviceRecordMigration.Index] = new JArray(new JObject { ["game_id"] = "Original", ["note"] = "备注", ["is_favorite"] = true }).ToString()
        };
        var plan = PrepareBothWays(input);
        var saved = MatchRecordCodec.Parse(plan.Writes["Original"]);
        var alias = MatchRecordCodec.Parse(plan.Writes[DeviceRecordMigration.Aliases])["Original"];
        var mixed = new Dictionary<string, string>(plan.Writes);
        var legacy = (JObject)envelope.DeepClone();
        legacy["game_id"] = "Legacy";
        mixed["Legacy"] = legacy.ToString();
        var mixedIndex = JArray.Parse(mixed[DeviceRecordMigration.Index]);
        mixedIndex.Add(new JObject { ["game_id"] = "Legacy" });
        mixed[DeviceRecordMigration.Index] = mixedIndex.ToString();
        var repaired = PrepareBothWays(mixed);
        if (!MatchRecordCodec.IsUnified((JObject)MatchRecordCodec.Parse(repaired.Writes["Legacy"])["record"]) ||
            !JToken.DeepEquals(alias, MatchRecordCodec.Parse(repaired.Writes[DeviceRecordMigration.Aliases])["Original"]) ||
            PrepareBothWays(repaired.Writes).Writes.Count != 0)
            throw new Exception("Mixed local library did not migrate once and preserve its old locator");
        var table = saved["record"]["tables"]["main"];
        var row = JArray.Parse(plan.Writes[DeviceRecordMigration.Index]).Single();
        if (!MatchRecordCodec.IsUnified((JObject)saved["record"]) || saved["match_record"] != null ||
            !alias["node_map"]["round_index_1"].Values<int>().SequenceEqual(new[] {0, 1, 1, 2}) ||
            (int)table["transport_actions"]["round_index_1"][0]["time_ms"] != 110 ||
            (int)table["source_round_fields"]["round_index_1"]["time_count"] != 2 ||
            (string)row["note"] != "备注" || !(bool)row["is_favorite"])
            throw new Exception("Device migration changed an original prompt, timestamp, cursor or annotation");
        foreach (var d in new[]{envelope, saved}) {
            var imported = SharedRecordLink.ReadLocalConverted(d.ToString());
            if (!MatchRecordCodec.IsUnified(imported.record) || imported.cloud_saved || imported.game_id != SharedRecordLink.LocalConvertedGameId)
                throw new Exception("External replay did not use the standard local reader");
        }
        var match = (JObject)saved["record"];
        string matchId = (string)match["match_id"];
        var locator = (JObject)alias.DeepClone();
        locator["match_id"] = matchId;
        locator["source_sha256"] = table["source_sha256"].DeepClone();
        var catalog = new JObject {
            ["aliases"] = new JObject { ["Original"] = locator },
            ["matches"] = new JObject { [matchId] = "old.match.json" },
            ["archive_hashes"] = new JObject { [matchId] = MatchRecordCodec.Digest(match) }
        };
        var catalogOnly = new Dictionary<string, string> {
            [DeviceRecordMigration.Catalog] = catalog.ToString(), ["__match_v2/old.match.json"] = match.ToString(),
            [DeviceRecordMigration.Index] = input[DeviceRecordMigration.Index]
        };
        var recovered = PrepareBothWays(catalogOnly);
        if (!recovered.Writes.ContainsKey("Original") || JArray.Parse(recovered.Writes[DeviceRecordMigration.Index]).Count != 1)
            throw new Exception("Lost source could not be restored from its verified catalog");
        catalogOnly[DeviceRecordMigration.Index] = "[]";
        recovered = PrepareBothWays(catalogOnly);
        if (!recovered.Writes.ContainsKey("Original") || JArray.Parse(recovered.Writes[DeviceRecordMigration.Index]).Count != 0)
            throw new Exception("An evicted replay should still open by its old ID without returning to the recent list");
        input["__match_v2/unpublished.match.json"] = "{}";
        try { PrepareBothWays(input); }
        catch (InvalidDataException) { return "Device source, cursor, annotations, both import formats and unpublished-record protection passed"; }
        throw new Exception("Unpublished source would have been discarded");
    }
}
#endif
