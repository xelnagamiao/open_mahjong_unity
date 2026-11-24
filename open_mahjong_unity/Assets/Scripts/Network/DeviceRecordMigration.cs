using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>One lossless conversion shared by the native and WebGL local stores.</summary>
internal static class DeviceRecordMigration {
    internal const string Index = "__index__", Aliases = "__record_aliases__", Catalog = "__match_v2_catalog__";
    internal sealed class Plan {
        public readonly Dictionary<string, string> Writes = new Dictionary<string, string>();
        public readonly List<string> Deletes = new List<string>();
    }
    public static JObject ConvertSingle(string id, JObject raw, JArray players) {
        var detail = new JObject { ["game_id"] = id, ["record"] = raw, ["players"] = players };
        return DeviceRecordConversion.Build(MatchRecordCodec.Identity("single", id),
            new Dictionary<string, JObject> { { "main", detail } });
    }

    static void Require(bool ok, string why) {
        if (!ok)
            throw new InvalidDataException(why);
    }
    static JObject RestoreSource(JObject table) {
        var source = (JObject)table["payload"].DeepClone();
        foreach (string key in table["source_omitted_title_fields"] as JArray ?? new JArray())
            ((JObject)source["game_title"]).Remove(key);
        foreach (var round in ((JObject)source["game_round"]).Properties()) {
            var rd = (JObject)round.Value;
            var actions = rd["action_ticks"] as JArray;
            var times = rd["action_times_ms"] as JArray;
            foreach (var prompt in table["transport_actions"]?[round.Name] as JArray ?? new JArray()) {
                int index = (int)prompt["node"] - 1;
                Require(actions != null && index >= 0 && index <= actions.Count, "原始动作索引无效");
                actions.Insert(index, prompt["tick"].DeepClone());
                times?.Insert(index, prompt["time_ms"]?.DeepClone() ?? JValue.CreateNull());
            }
            var extra = table["source_round_fields"]?[round.Name];
            if ((int?)extra?["time_count"] is int timeCount)
                rd["action_times_ms"] = new JArray(times.Concat(extra["time_tail"] as JArray ?? new JArray()).Take(timeCount));
            if ((bool?)extra?["no_action_ticks"] == true)
                rd.Remove("action_ticks");
        }
        return source;
    }
    static string SourceTable(JObject entry, JObject published) {
        var title = (JObject)entry["record"]["game_title"];
        if (!SichuanLobby.IsDuplicate((string)title["sub_rule"]) || published == null)
            return DeviceRecordConversion.TableId(title);
        string sourceId = (string)entry["game_id"];
        var tables = ((JObject)published["tables"]).Properties()
            .Where(p => (string)p.Value["table_record_id"] == sourceId).ToList();
        if (tables.Count == 0)
            return DeviceRecordConversion.TableId(title);
        Require(tables.Count == 1 && (tables[0].Name == "open" || tables[0].Name == "closed")
            && ((string)title["duplicate_match_id"] == null || (string)title["duplicate_match_id"] == (string)published["match_id"])
            && ((string)title["duplicate_table"] == null || (string)title["duplicate_table"] == tables[0].Name),
            "复式牌谱身份与已保存数据冲突：" + sourceId);
        return tables[0].Name;
    }
    static void CompleteRoster(JObject table) {
        var roster = (JArray)table["players"];
        var title = (JObject)table["payload"]["game_title"];
        for (int seat = 0; seat < 4; seat++) {
            var uid = title["p" + seat + "_uid"];
            if (uid == null || uid.Type == JTokenType.Null ||
                roster.Any(p => (int?)p["original_player_index"] == seat))
                continue;
            var player = roster.OfType<JObject>().FirstOrDefault(p =>
                (int?)p["original_player_index"] == null && JToken.DeepEquals(p["user_id"], uid));
            if (player == null) {
                player = new JObject { ["user_id"] = uid.DeepClone(), ["username"] = title["p" + seat + "_name"]?.DeepClone() };
                roster.Add(player);
            }
            player["original_player_index"] = seat;
        }
    }
    public static JObject Alias(string id, string table, JObject data) {
        var rounds = new JObject();
        var nodes = new JObject();
        foreach (var rd in ((JObject)data["round_meta"]).Properties()) {
            rounds[rd.Name] = rd.Value["table_round_id"].DeepClone();
            nodes[rd.Name] = rd.Value["legacy_node_map"].DeepClone();
        }
        return new JObject { ["game_id"] = id, ["table_id"] = table, ["round_map"] = rounds, ["node_map"] = nodes };
    }
    public static string FirstTable(JObject record) {
        return new[] { "main", "open", "closed" }.First(k => record["tables"][k]?["payload"] is JObject);
    }
    internal static void AddAliases(JObject aliases, RecordDetail detail) {
        var record = MatchRecordCodec.ExpandStorage(detail.record);
        foreach (var t in ((JObject)record["tables"]).Properties()) {
            string id = (string)t.Value["table_record_id"];
            if (string.IsNullOrEmpty(id) || !(t.Value["payload"] is JObject))
                continue;
            var alias = Alias(detail.game_id, t.Name, (JObject)t.Value);
            Require(aliases[id] == null || JToken.DeepEquals(aliases[id], alias), "牌谱旧编号指向不同比赛");
            aliases[id] = alias;
        }
    }
    static int Weight(JObject body) => ((JObject)body["tables"]).Properties().Count(t => t.Value["payload"] is JObject);
    internal static JObject Merge(JObject a, JObject b) {
        a = MatchRecordCodec.ExpandStorage(a);
        b = MatchRecordCodec.ExpandStorage(b);
        if (a == null)
            return b;
        if (b == null)
            return a;
        Require((string)a["match_id"] == (string)b["match_id"] && (string)a["match_format"] == (string)b["match_format"], "本地牌谱编号冲突");
        if (Weight(a) > Weight(b))
            (a, b) = (b, a);
        var result = (JObject)b.DeepClone();
        var tables = (JObject)result["tables"];
        bool joined = false;
        foreach (var item in ((JObject)a["tables"]).Properties()) {
            if (!(item.Value["payload"] is JObject payload))
                continue;
            var table = tables[item.Name] as JObject;
            if (!(table?["payload"] is JObject existing)) { tables[item.Name] = item.Value.DeepClone(); joined = true; continue; }
            Require(JToken.DeepEquals(payload, existing) || MatchRecordCodec.Digest(payload) == MatchRecordCodec.Digest(existing),
                "同桌牌谱内容冲突：" + (string)item.Value["table_record_id"] + " / " + item.Name);
            foreach (var field in new[] { "transport_actions", "source_round_fields", "source_omitted_title_fields" })
                if (item.Value[field] != null) {
                    Require(table[field] == null || JToken.DeepEquals(table[field], item.Value[field]), "原始动作元数据冲突");
                    table[field] = item.Value[field].DeepClone();
                }
        }
        if (joined) {
            result["completeness"] = Weight(result) == 2 ? "complete" : "partial";
            // Build both rooms together so board identities, teams and derived metadata cover both.
            var raw = new Dictionary<string, JObject>();
            foreach (var p in tables.Properties().Where(t => t.Value["payload"] is JObject))
                raw[p.Name] = new JObject { ["game_id"] = p.Value["table_record_id"].DeepClone(), ["record"] = p.Value["payload"].DeepClone(), ["players"] = p.Value["players"].DeepClone() };
            var joinedBody = DeviceRecordConversion.Build((string)result["match_id"], raw);
            foreach (var p in ((JObject)joinedBody["tables"]).Properties()) {
                var original = tables[p.Name];
                if (!(original["payload"] is JObject))
                    continue;
                var rebuilt = (JObject)p.Value;
                foreach (var f in new[] { "transport_actions", "source_round_fields", "source_omitted_title_fields", "source_sha256" })
                    if (original[f] != null)
                        rebuilt[f] = original[f].DeepClone();
                foreach (var rd in ((JObject)original["round_meta"]).Properties()) {
                    var upgraded = (JObject)rebuilt["round_meta"][rd.Name];
                    if ((string)rd.Value["alignment_status"] == "exact")
                        rebuilt["round_meta"][rd.Name] = rd.Value.DeepClone();
                    else
                        upgraded["legacy_node_map"] = rd.Value["legacy_node_map"].DeepClone();
                }
            }
            foreach (var source in (JArray)joinedBody["provenance"]["sources"])
                source["sha256"] = joinedBody["tables"][(string)source["table_id"]]["source_sha256"].DeepClone();
            foreach (var field in new[] { "tables", "teams", "board_catalog", "provenance" })
                result[field] = joinedBody[field].DeepClone();
        }
        return result;
    }

    static JObject ReadOriginal(string id, string json) {
        var entry = MatchRecordCodec.Parse(json);
        Require(id == (string)entry["game_id"] && entry["record"] is JObject, "本地牌谱编号或正文无效：" + id);
        return entry;
    }

    static string MatchIdentity(string id, JObject body, JObject bundle) {
        if (bundle != null) {
            Require(MatchRecordCodec.IsUnified(bundle), "本地比赛格式错误");
            return (string)bundle["match_id"];
        }
        var title = body["game_title"] as JObject;
        Require(title != null && body["game_round"] is JObject, "本地牌谱缺少局数据");
        return DeviceRecordConversion.TableId(title) == "main"
            ? MatchRecordCodec.Identity("single", id) : (string)title["duplicate_match_id"];
    }

    static string LocateMatch(string id, IDictionary<string, string> store, JObject catalogAliases, out bool unified) {
        unified = false;
        if (catalogAliases[id] is JObject catalogEntry)
            return (string)catalogEntry["match_id"];
        Require(store.TryGetValue(id, out var original), "索引引用缺失的本地牌谱：" + id);
        var entry = ReadOriginal(id, original);
        var body = (JObject)entry["record"];
        unified = MatchRecordCodec.IsUnified(body);
        return MatchIdentity(id, body, unified ? body : entry["match_record"] as JObject);
    }

    static List<JObject> LoadMatch(string match, List<string> sourceIds, IDictionary<string, string> store,
            JObject catalog, JObject catalogAliases, Dictionary<string, JObject> indexRows, HashSet<string> verifiedArchives) {
        var entries = new List<JObject>();
        var published = new Dictionary<string, JObject>();
        foreach (var sourceId in sourceIds) {
            JObject archived = null;
            var alias = catalogAliases[sourceId];
            if (alias is JObject) {
                string mid = (string)alias["match_id"], file = (string)catalog["matches"]?[mid];
                Require(!string.IsNullOrEmpty(file) && Path.GetFileName(file) == file && file.EndsWith(".match.json", StringComparison.Ordinal), "迁移目录映射无效");
                if (!published.TryGetValue(mid, out archived)) {
                    Require(store.TryGetValue("__match_v2/" + file, out var json), "本地比赛归档缺失");
                    archived = MatchRecordCodec.Parse(json);
                    Require((string)archived["match_id"] == mid && MatchRecordCodec.Digest(archived) == (string)catalog["archive_hashes"]?[mid], "本地归档校验失败");
                    published[mid] = archived;
                    verifiedArchives.Add(mid);
                }
            }
            JObject entry;
            if (store.TryGetValue(sourceId, out string original)) {
                entry = ReadOriginal(sourceId, original);
            } else {
                Require(archived != null && alias is JObject, "索引引用缺失的本地牌谱：" + sourceId);
                string tableId = (string)alias["table_id"];
                var table = archived["tables"]?[tableId] as JObject;
                Require(table?["payload"] is JObject && (string)table["table_record_id"] == sourceId
                    && (string)table["source_sha256"] == (string)alias["source_sha256"], "旧牌谱来源无法核对：" + sourceId);
                entry = indexRows.TryGetValue(sourceId, out var row) ? (JObject)row.DeepClone() : new JObject();
                entry["game_id"] = sourceId;
                entry["selected_table_id"] = tableId;
                entry["record"] = archived;
                entry["players"] ??= table["players"]?.DeepClone();
            }
            var body = (JObject)entry["record"];
            JObject bundle = MatchRecordCodec.IsUnified(body) ? body : entry["match_record"] as JObject;
            if (archived != null) {
                if (!MatchRecordCodec.IsUnified(body))
                    Require(MatchRecordCodec.Digest(body) == (string)alias["source_sha256"], "迁移来源已改变：" + sourceId);
                bundle = Merge(bundle, archived);
            }
            Require(MatchIdentity(sourceId, body, bundle) == match, "原始牌谱与索引中的比赛身份冲突：" + sourceId);
            if (bundle != null)
                entry["match_record"] = bundle;
            entries.Add(entry);
        }
        return entries;
    }

    static void ConvertMatch(string match, List<JObject> entries, Dictionary<string, JObject> rows,
            Dictionary<string, string> oldIds, JObject aliases, Plan plan) {
        var raw = new Dictionary<string, JObject>();
        JObject unified = null;
        foreach (var entry in entries) {
            unified = Merge(unified, entry["match_record"] as JObject);
            var body = (JObject)entry["record"];
            if (MatchRecordCodec.IsUnified(body))
                continue;
            var table = SourceTable(entry, entry["match_record"] as JObject);
            if (raw.TryGetValue(table, out var existing))
                Require(JToken.DeepEquals(existing["record"], body), "重复桌号对应不同内容");
            else
                raw[table] = entry;
        }
        if (raw.Count > 0) {
            var corrected = new Dictionary<string, JObject>(raw);
            var omitted = new Dictionary<string, JArray>();
            foreach (var source in raw.Where(p => p.Key != "main")) {
                var copy = (JObject)source.Value.DeepClone();
                var title = (JObject)copy["record"]["game_title"];
                var added = new JArray();
                foreach (var property in new JObject { ["duplicate_match_id"] = match, ["duplicate_table"] = source.Key }.Properties()) {
                    if (title.Property(property.Name) != null)
                        continue;
                    title[property.Name] = property.Value.DeepClone();
                    added.Add(property.Name);
                }
                if (added.Count != 0) { corrected[source.Key] = copy; omitted[source.Key] = added; }
            }
            var rebuilt = DeviceRecordConversion.Build(match, corrected);
            foreach (var added in omitted) {
                var table = rebuilt["tables"][added.Key];
                table["source_omitted_title_fields"] = added.Value;
                table["source_sha256"] = MatchRecordCodec.Digest(raw[added.Key]["record"]);
                rebuilt["provenance"]["sources"].Single(s => (string)s["table_id"] == added.Key)["sha256"] = table["source_sha256"].DeepClone();
            }
            // Published data may contain verified physical tile identities absent from the old file.
            unified = Merge(rebuilt, unified);
            if ((string)unified["match_format"] == "paired_tables_2v2") {
                if (unified["sync_mode"]?.Type == JTokenType.Null)
                    unified["sync_mode"] = rebuilt["sync_mode"]?.DeepClone();
                foreach (var p in ((JObject)rebuilt["tables"]).Properties()) {
                    foreach (var round in ((JObject)p.Value["round_meta"]).Properties()) {
                        var existing = (JObject)unified["tables"][p.Name]["round_meta"][round.Name];
                        if ((string)existing["alignment_status"] == "exact" || (string)round.Value["alignment_status"] != "exact")
                            continue;
                        Require((string)existing["table_round_id"] == (string)round.Value["table_round_id"],
                            "重算后的局号与旧牌谱不一致");
                        var improved = (JObject)round.Value.DeepClone();
                        improved["legacy_node_map"] = existing["legacy_node_map"].DeepClone();
                        existing.Replace(improved);
                    }
                }
                var catalogBoards = (JArray)unified["board_catalog"];
                foreach (var board in (JArray)rebuilt["board_catalog"])
                    if (!catalogBoards.Any(s => (string)s["board_instance_id"] == (string)board["board_instance_id"]))
                        catalogBoards.Add(board.DeepClone());
            }
        }
        Require(unified != null, "没有可迁移的牌谱");
        foreach (var item in ((JObject)unified["tables"]).Properties()) {
            var table = (JObject)item.Value;
            if (!(table["payload"] is JObject))
                continue;
            var restored = RestoreSource(table);
            string checksum = (string)table["source_sha256"];
            bool verified = MatchRecordCodec.Digest(restored) == checksum;
            if (raw.TryGetValue(item.Name, out var input)) {
                Require((string)table["table_record_id"] == (string)input["game_id"] && verified
                    && MatchRecordCodec.Digest(input["record"]) == checksum, "原始牌谱校验失败：" + (string)input["game_id"]);
            } else if (!verified)
                table["original_source_unavailable"] = true;
            if (item.Name != "main")
                CompleteRoster(table);
        }
        string id = MatchRecordCodec.GameId(unified), selected = FirstTable(unified);
        Require(!string.IsNullOrEmpty(id) && !rows.ContainsKey(id), "标准牌谱编号冲突");
        var head = new JObject();
        foreach (var property in entries[0].Properties()) {
            if (property.Name != "record" && property.Name != "match_record" && property.Name != "legacy_cursor_map")
                head.Add(property.Name, property.Value.DeepClone());
        }
        head["game_id"] = id;
        head["selected_table_id"] = selected;
        if ((string)unified["match_format"] == "paired_tables_2v2") {
            head["players"] = new JArray(((JObject)unified["tables"]).Properties().SelectMany(t =>
                ((t.Value["players"] as JArray) ?? new JArray()).OfType<JObject>().Select(player => {
                    var seat = (JObject)player.DeepClone();
                    seat["table_id"] = t.Name;
                    return seat;
                })));
        }
        foreach (var entry in entries.Skip(1)) {
            var extra = new JObject();
            foreach (var p in entry.Properties()) {
                if (new[] { "record", "match_record", "game_id", "players", "selected_table_id", "legacy_cursor_map" }.Contains(p.Name))
                    continue;
                if (!JToken.DeepEquals(p.Value, head[p.Name]))
                    extra[p.Name] = p.Value.DeepClone();
            }
            if (extra.HasValues) {
                if (!(head["local_table_metadata"] is JObject))
                    head["local_table_metadata"] = new JObject();
                head["local_table_metadata"][(string)entry["game_id"]] = extra;
            }
        }
        var detail = head.ToObject<RecordDetail>();
        detail.record = unified;
        head["record"] = unified;
        AddAliases(aliases, detail);
        foreach (var entry in entries) {
            string old = (string)entry["game_id"];
            oldIds.Add(old, id);
            string table = MatchRecordCodec.IsUnified((JObject)entry["record"]) ? (string)entry["selected_table_id"] :
                SourceTable(entry, unified);
            if (old != id && aliases[old] == null)
                aliases[old] = Alias(id, table ?? selected, (JObject)unified["tables"][table ?? selected]);
            if (old != id)
                plan.Deletes.Add(old);
        }
        plan.Writes[id] = head.ToString(Formatting.None);
        rows[id] = JObject.FromObject(LocalRecordStore.ToInfo(detail, detail.match_type));
    }

    internal static Plan Prepare(IDictionary<string, string> store) {
        var plan = new Plan();
        var steps = PrepareSteps(store, plan);
        while (steps.MoveNext()) { }
        return plan;
    }

    // WebGL collects temporary managed allocations between frames. Keep only source
    // IDs across matches and defer every write until all matches have been verified.
    internal static IEnumerator PrepareSteps(IDictionary<string, string> store, Plan plan, bool stream = false) {
        if (stream) foreach (var key in new[] { Index, Aliases, Catalog })
            if (store.ContainsKey(key)) yield return key;
        var index = store.TryGetValue(Index, out var indexText) ? JArray.Parse(indexText) : new JArray();
        var aliases = store.TryGetValue(Aliases, out var aliasText) ? MatchRecordCodec.Parse(aliasText) : new JObject();
        var catalog = store.TryGetValue(Catalog, out var meta) ? MatchRecordCodec.Parse(meta) : null;
        var indexRows = index.OfType<JObject>().ToDictionary(row => (string)row["game_id"]);
        var catalogAliases = catalog?["aliases"] as JObject ?? new JObject();
        var groups = new Dictionary<string, List<string>>();
        var order = indexRows.Keys.Concat(store.Keys.Where(k => !k.StartsWith("__", StringComparison.Ordinal)))
            .Concat(catalogAliases.Properties().Select(p => p.Name)).Distinct().ToArray();
        bool needsMigration = !store.ContainsKey(Index) || !store.ContainsKey(Aliases) ||
            store.Keys.Any(k => k.StartsWith("__match_v2", StringComparison.Ordinal));
        foreach (string sourceId in order) {
            bool readOriginal = stream && !(catalogAliases[sourceId] is JObject) && store.ContainsKey(sourceId);
            if (readOriginal) yield return sourceId;
            var match = LocateMatch(sourceId, store, catalogAliases, out bool unified);
            if (!unified) needsMigration = true;
            Require(!string.IsNullOrEmpty(match), "比赛身份缺失");
            if (!groups.TryGetValue(match, out var ids))
                groups[match] = ids = new List<string>();
            ids.Add(sourceId);
            if (readOriginal) store[sourceId] = null;
            // An uncatalogued record was parsed to discover its identity.
            if (!(catalogAliases[sourceId] is JObject))
                yield return null;
        }
        if (!needsMigration) yield break;
        yield return null;
        var rows = new Dictionary<string, JObject>();
        var oldIds = new Dictionary<string, string>();
        var verifiedArchives = new HashSet<string>();
        foreach (var group in groups) {
            var inputs = new List<string>();
            if (stream) {
                inputs.AddRange(group.Value.Where(store.ContainsKey));
                var archive = (string)catalog?["matches"]?[group.Key];
                if (archive != null && store.ContainsKey("__match_v2/" + archive)) inputs.Add("__match_v2/" + archive);
                foreach (var key in inputs) yield return key;
            }
            var entries = LoadMatch(group.Key, group.Value, store, catalog, catalogAliases, indexRows, verifiedArchives);
            yield return null;
            ConvertMatch(group.Key, entries, rows, oldIds, aliases, plan);
            entries = null;
            if (stream) {
                foreach (var key in inputs) store[key] = null;
                foreach (var output in plan.Writes) yield return output;
                plan.Writes.Clear();
            }
            yield return null;
        }
        var updated = new JArray();
        var seen = new HashSet<string>();
        foreach (var row in index.OfType<JObject>()) {
            string old = (string)row["game_id"];
            Require(old != null && oldIds.TryGetValue(old, out _), "索引引用缺失的本地牌谱：" + old);
            string id = oldIds[old];
            if (!seen.Add(id)) {
                var first = updated.OfType<JObject>().First(r => (string)r["game_id"] == id);
                if ((bool?)row["is_favorite"] == true)
                    first["is_favorite"] = true;
                if (!string.IsNullOrEmpty((string)row["note"]) && !JToken.DeepEquals(row["note"], first["note"])) {
                    if (!(first["table_notes"] is JObject))
                        first["table_notes"] = new JObject();
                    first["table_notes"][old] = row["note"].DeepClone();
                }
                continue;
            }
            var merged = (JObject)row.DeepClone();
            foreach (var field in rows[id].Properties())
                if (field.Value.Type != JTokenType.Null)
                    merged[field.Name] = field.Value.DeepClone();
            if (row["is_favorite"] != null)
                merged["is_favorite"] = row["is_favorite"].DeepClone();
            updated.Add(merged);
        }
        foreach (var a in aliases.Properties())
            Require(rows.ContainsKey((string)a.Value["game_id"]), "本地牌谱别名缺少对应正文");
        if (catalog != null) {
            foreach (var a in ((JObject)catalog["aliases"]).Properties()) {
                var target = aliases[a.Name];
                Require(target is JObject && new[] { "table_id", "round_map", "node_map" }.All(k => JToken.DeepEquals(target[k], a.Value[k])),
                    "旧牌谱位置无法完整迁移：" + a.Name);
            }
            Require(((JObject)catalog["matches"]).Properties().All(p => verifiedArchives.Contains(p.Name)), "存在未核对的旧比赛归档");
        }
        plan.Writes[Index] = updated.ToString(Formatting.None);
        plan.Writes[Aliases] = aliases.ToString(Formatting.None);
        var files = new HashSet<string>((catalog?["matches"] as JObject ?? new JObject()).Properties().Select(p => "__match_v2/" + (string)p.Value));
        foreach (var key in store.Keys.Where(k => k.StartsWith("__match_v2/", StringComparison.Ordinal)))
            Require(files.Contains(key), "存在未核对的旧归档：" + key);
        foreach (var runKey in store.Keys.Where(k => k.StartsWith("__match_v2_run/", StringComparison.Ordinal)).ToArray()) {
            if (stream) yield return runKey;
            var status = (string)MatchRecordCodec.Parse(store[runKey])["status"];
            if (stream) store[runKey] = null;
            Require(status == "published" || status == "rolled_back", "旧浏览器迁移仍在进行：" + runKey);
        }
        plan.Deletes.AddRange(store.Keys.Where(k => k == Catalog || files.Contains(k) || k.StartsWith("__match_v2_run/", StringComparison.Ordinal)));
    }
}
