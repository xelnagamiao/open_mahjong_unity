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
        return checks + " cross-language integrity checks passed";
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
        var plan = DeviceRecordMigration.Prepare(input);
        var saved = MatchRecordCodec.Parse(plan.Writes["Original"]);
        var alias = MatchRecordCodec.Parse(plan.Writes[DeviceRecordMigration.Aliases])["Original"];
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
        var recovered = DeviceRecordMigration.Prepare(catalogOnly);
        if (!recovered.Writes.ContainsKey("Original") || JArray.Parse(recovered.Writes[DeviceRecordMigration.Index]).Count != 1)
            throw new Exception("Lost source could not be restored from its verified catalog");
        catalogOnly[DeviceRecordMigration.Index] = "[]";
        recovered = DeviceRecordMigration.Prepare(catalogOnly);
        if (!recovered.Writes.ContainsKey("Original") || JArray.Parse(recovered.Writes[DeviceRecordMigration.Index]).Count != 0)
            throw new Exception("An evicted replay should still open by its old ID without returning to the recent list");
        input["__match_v2/unpublished.match.json"] = "{}";
        try { DeviceRecordMigration.Prepare(input); }
        catch (InvalidDataException) { return "Device source, cursor, annotations, both import formats and unpublished-record protection passed"; }
        throw new Exception("Unpublished source would have been discarded");
    }
}
#endif
