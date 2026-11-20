using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

/// <summary>Rebuild a device's unsynchronized V1 replay using only evidence in its saved tables.</summary>
internal static class DeviceRecordConversion {
    public static string TableId(JObject title) {
        if (!SichuanLobby.IsDuplicate((string)title["sub_rule"]))
            return "main";
        string id = (string)title["duplicate_table"];
        if ((id != "open" && id != "closed") || string.IsNullOrEmpty((string)title["duplicate_match_id"]))
            throw new InvalidDataException("复式牌谱缺少比赛编号或桌号，原始文件已保留");
        return id;
    }

    static JToken Value(JToken token) => token?.DeepClone() ?? JValue.CreateNull();
    static JObject Ref(string instance, int seat, int offset) => new JObject {
        ["board_instance_id"] = instance,
        ["zone"] = seat == -1 ? "common_wall" : "seat_wall",
        ["source_original_seat"] = seat == -1 ? JValue.CreateNull() : new JValue(seat),
        ["offset"] = offset
    };

    static JObject Board(JObject title, int number, bool xueliu) {
        var flat = (title["duplicate_round_tiles"] as JArray)?.ElementAtOrDefault(number - 1) as JArray;
        if (flat == null && number == 1)
            flat = title["duplicate_tiles"] as JArray;
        if (flat == null)
            return null;
        if (flat.Count != 108 || flat.Any(x => x.Type != JTokenType.Integer))
            throw new InvalidDataException("原始牌山无效");
        var faces = flat.Values<int>().GroupBy(value => value).ToArray();
        if (faces.Length != 27 || faces.Any(face => face.Count() != 4 || face.Key / 10 < 1
            || face.Key / 10 > 3 || face.Key % 10 < 1 || face.Key % 10 > 9))
            throw new InvalidDataException("原始牌山的 108 张川麻数牌无效");
        int section = xueliu ? 21 : 27;
        var hands = new JArray();
        var walls = new JArray();
        for (int seat = 0; seat < 4; seat++) {
            hands.Add(new JArray(flat.Skip(seat * section).Take(13)));
            walls.Add(new JArray(flat.Skip(seat * section + 13).Take(section - 13)));
        }
        var board = new JObject { ["number"] = number, ["dealer"] = (number - 1) % 4, ["hands"] = hands, ["walls"] = walls };
        if (xueliu) { board["wall_layout"] = "xueliu"; board["common_wall"] = new JArray(flat.Skip(84)); }
        return board;
    }

    static void Origins(JObject rd, JObject meta, JObject board, bool xueliu) {
        string instance = (string)meta["board_instance_id"];
        int[] seats = rd["seats"]?.ToObject<int[]>();
        if (seats == null || !seats.OrderBy(s => s).SequenceEqual(new[] { 0, 1, 2, 3 }))
            throw new InvalidDataException("复式牌谱座位无效");
        int Original(int seat) {
            int original = Array.IndexOf(seats, seat);
            if (original < 0)
                throw new InvalidDataException("复式牌谱行动座位无效");
            return original;
        }
        int current = (int)rd["start_player_index"], dealer = Original((int)rd["dealer_index"]);
        int[] used = new int[4];
        used[dealer] = 1;
        var retired = new HashSet<int>();
        var visited = new HashSet<(int, int)> { (dealer, 0) };
        Queue<(int, int)> common = null;
        int size = xueliu ? 8 : 14;
        int Next(int seat) {
            for (int i = 1; i <= 4; i++)
                if (!retired.Contains(Original((seat + i) % 4)))
                    return (seat + i) % 4;
            return seat;
        }
        (int, int)? NextTile(int actor) {
            if (common != null)
                return common.Count > 0 ? common.Peek() : ((int, int)?)null;
            if (used[actor] < size)
                return (actor, used[actor]);
            int partner = (actor + 2) % 4;
            return !xueliu && retired.Contains(partner) && used[partner] < size ? (partner, used[partner]) : ((int, int)?)null;
        }
        meta["initial_anchors"] = new JArray(new JObject {
            ["kind"] = "dealer_initial_draw",
            ["wall_ref"] = Ref(instance, dealer, 0),
            ["actor_original_seat"] = dealer,
            ["node"] = 0,
            ["phase"] = "after_draw"
        });
        bool reliable = true, missing = false;
        var ticks = (JArray)rd["action_ticks"];
        for (int i = 0; i < ticks.Count; i++) {
            var t = (JArray)ticks[i];
            string code = (string)t[0];
            int actor = Original(current);
            var m = (JObject)meta["action_meta"][i];
            m["actor_original_seat"] = actor;
            m["wall_ref_basis"] = "not_applicable";
            if (code == "d" || code == "gd") {
                (int, int)? source = null;
                if (t.Count >= 6 && (string)t[2] == "dw") {
                    current = (int)t[5];
                    actor = Original(current);
                    source = ((int)t[3], (int)t[4]);
                    m["wall_ref_basis"] = "recorded";
                } else if (board != null && reliable) {
                    source = NextTile(actor);
                    for (int j = 0; source == null && j < 4; j++) { current = Next(current); actor = Original(current); source = NextTile(actor); }
                    m["wall_ref_basis"] = source.HasValue ? "reconstructed" : "unavailable";
                } else { m["wall_ref_basis"] = "unavailable"; reliable = false; }
                m["actor_original_seat"] = actor;
                if (!source.HasValue) { missing = true; reliable = false; continue; }
                var (s, offset) = source.Value;
                if (s < -1 || s > 3 || offset < 0 || offset >= (s == -1 ? (xueliu ? 24 : 0) : size)
                    || !visited.Add((s, offset)))
                    throw new InvalidDataException("重复或无效的牌山位置");
                if (board != null && (int)(s == -1 ? board["common_wall"][offset] : board["walls"][s][offset]) != (int)t[1])
                    throw new InvalidDataException("摸牌与原始牌山冲突");
                if (reliable) {
                    if (source != NextTile(actor))
                        throw new InvalidDataException("摸牌与行牌顺序冲突");
                    if (common != null)
                        common.Dequeue();
                    else if (++used[s] == size && xueliu) {
                        common = new Queue<(int, int)>();
                        for (int seat = 0; seat < 4; seat++)
                            for (int pos = used[seat]; pos < size; pos++)
                                common.Enqueue((seat, pos));
                        for (int pos = 0; pos < 24; pos++)
                            common.Enqueue((-1, pos));
                    }
                }
                m["wall_ref"] = Ref(instance, s, offset);
            } else if (new[] { "p", "g", "cl", "cm", "cr", "bh" }.Contains(code)) {
                if (t.Count > 2 && t[2].Type == JTokenType.Integer && seats.Contains((int)t[2])) {
                    current = (int)t[2];
                    m["actor_original_seat"] = Original(current);
                }
            } else if (code == "c")
                current = Next(current);
            else if (code == "reset") { current = (int)t[1]; Original(current); m["actor_original_seat"] = null; } else if (code.StartsWith("hu_", StringComparison.Ordinal)) {
                if (t.Count > 1 && t[1].Type == JTokenType.Integer && seats.Contains((int)t[1])) {
                    int winner = (int)t[1];
                    m["actor_original_seat"] = Original(winner);
                    if (!xueliu)
                        retired.Add(Original(winner));
                    if (code == "hu_self")
                        current = Next(winner);
                    else if (retired.Contains(Original(current)))
                        current = Next(current);
                }
            } else if (code != "ag" && code != "jg" && code != "ca")
                m["actor_original_seat"] = null;
        }
        meta["alignment_status"] = missing ? "partial" : "exact";
        meta["alignment_reason"] = missing ? (JToken)"missing_draw_origin" : null;
    }

    public static JObject Build(string matchId, Dictionary<string, JObject> details) {
        var first = details.TryGetValue("open", out var opened) ? opened : details.Values.First();
        var firstBody = (JObject)first["record"];
        var title = (JObject)firstBody["game_title"];
        bool paired = !details.ContainsKey("main"), xueliu = (string)title["sub_rule"] == "sichuan/xueliu_doubles_duplicate";
        var boards = new SortedDictionary<int, JObject>();
        var all = details.SelectMany(entry => ((JObject)entry.Value["record"]["game_round"]).Properties()
            .Select(r => (table: entry.Key, rd: (JObject)r.Value, number: (int?)r.Value["current_round"] ?? int.Parse(r.Name.Substring(12)))));
        if (paired)
            foreach (var group in all.GroupBy(r => r.number)) {
                int n = group.Key;
                var instances = group.Select(r => (string)r.rd["board_instance_id"]).Where(x => x != null).Distinct().ToList();
                var attempts = group.Select(r => (int?)r.rd["board_attempt"]).Where(x => x.HasValue).Distinct().ToList();
                var walls = group.Select(r => Board((JObject)details[r.table]["record"]["game_title"], n, xueliu)).Where(w => w != null).ToList();
                if (instances.Count > 1 || attempts.Count > 1 || attempts.Any(value => value < 1) || walls.Any(w => !JToken.DeepEquals(walls[0], w)))
                    throw new InvalidDataException("同一副牌的两桌来源冲突");
                string instance = instances.FirstOrDefault();
                if (instance == null && walls.Count == 2 && group.Select(r => r.table).Distinct().Count() == 2)
                    instance = MatchRecordCodec.Identity("legacy-effective-board", matchId, n);
                if (instance != null)
                    boards.Add(n, new JObject {
                        ["board_no"] = n,
                        ["board_instance_id"] = instance,
                        ["attempt"] = attempts.FirstOrDefault(),
                        ["wall_layout"] = xueliu ? "xueliu" : "standard",
                        ["status"] = group.Count() == 2 ? "paired" : "playing",
                        ["wall_definition"] = walls.FirstOrDefault()
                    });
            }
        var tables = new JObject();
        var origins = new JArray();
        var teams = new SortedDictionary<int, JArray>();
        foreach (string table in paired ? new[] { "open", "closed" } : new[] { "main" }) {
            if (!details.TryGetValue(table, out var detail)) {
                tables[table] = new JObject { ["table_id"] = table, ["table_record_id"] = null, ["record_state"] = "missing", ["payload"] = null, ["round_meta"] = new JObject(), ["players"] = new JArray() };
                continue;
            }
            var source = (JObject)detail["record"];
            string sid = (string)detail["game_id"];
            var payload = (JObject)source.DeepClone();
            var metadata = new JObject();
            var extras = new JObject();
            var fields = new JObject();
            string hash = MatchRecordCodec.Digest(source);
            var tTitle = (JObject)payload["game_title"];
            if (paired && (!JToken.DeepEquals(tTitle["duplicate_profile"], title["duplicate_profile"]) ||
                !JToken.DeepEquals(tTitle["sub_rule"], title["sub_rule"]) || !JToken.DeepEquals(tTitle["rule"], title["rule"]) ||
                (string)tTitle["duplicate_match_id"] != matchId || (string)tTitle["duplicate_table"] != table))
                throw new InvalidDataException("复式比赛身份或规则不一致");
            if (tTitle["team_by_player_index"] is JArray ids)
                for (int seat = 0; seat < ids.Count; seat++) {
                    int team = (int)ids[seat];
                    if (!teams.TryGetValue(team, out var members))
                        teams[team] = members = new JArray();
                    members.Add(new JObject { ["table_id"] = table, ["original_player_index"] = seat, ["user_id"] = Value(tTitle["p" + seat + "_uid"]) });
                }
            foreach (var entry in ((JObject)payload["game_round"]).Properties()) {
                if (!entry.Name.StartsWith("round_index_", StringComparison.Ordinal) || !int.TryParse(entry.Name.Substring(12), out int ri) || ri < 1)
                    throw new InvalidDataException("牌谱局号无效");
                var rd = (JObject)entry.Value;
                if ((int?)rd["round_index"] is int explicitIndex && explicitIndex != ri)
                    throw new InvalidDataException("牌谱局号冲突");
                boards.TryGetValue((int?)rd["current_round"] ?? ri, out var board);
                string instance = (string)rd["board_instance_id"] ?? (string)board?["board_instance_id"];
                string roundId = (string)rd["table_round_id"] ?? MatchRecordCodec.Identity("round", matchId, table, instance ?? entry.Name);
                var map = new JArray(0);
                var actions = new JArray();
                var meta = new JArray();
                var removed = new JArray();
                var keptTimes = new JArray();
                var ticks = rd["action_ticks"] as JArray ?? new JArray();
                var times = rd["action_times_ms"] as JArray;
                var sourceFields = new JObject();
                if (rd["action_ticks"] == null)
                    sourceFields["no_action_ticks"] = true;
                if (times != null && times.Count != ticks.Count) {
                    sourceFields["time_count"] = times.Count;
                    if (times.Count > ticks.Count)
                        sourceFields["time_tail"] = new JArray(times.Skip(ticks.Count));
                }
                if (sourceFields.HasValues)
                    fields[entry.Name] = sourceFields;
                for (int i = 0; i < ticks.Count; i++) {
                    var tick = ticks[i] as JArray;
                    if (tick == null || tick.Count == 0 || tick[0].Type != JTokenType.String)
                        throw new InvalidDataException("无效的牌谱动作");
                    var time = times?.ElementAtOrDefault(i);
                    if ((string)tick[0] == "ask_hand" || (string)tick[0] == "ask_other") {
                        var item = new JObject { ["node"] = i + 1, ["tick"] = tick.DeepClone() };
                        if (time != null)
                            item["time_ms"] = Value(time);
                        removed.Add(item);
                    } else {
                        actions.Add(tick.DeepClone());
                        keptTimes.Add(Value(time));
                        meta.Add(new JObject {
                            ["actor_original_seat"] = null,
                            ["occurred_at_ms"] = time?.Type == JTokenType.Integer && (long)time >= 0 ? Value(time) : null,
                            ["wall_ref"] = null,
                            ["wall_ref_basis"] = paired ? "unavailable" : "not_applicable"
                        });
                    }
                    map.Add(actions.Count);
                }
                rd["action_ticks"] = actions;
                if (times != null)
                    rd["action_times_ms"] = keptTimes;
                if (removed.HasValues)
                    extras[entry.Name] = removed;
                var m = new JObject {
                    ["table_round_id"] = roundId,
                    ["round_index"] = ri,
                    ["board_instance_id"] = instance,
                    ["legacy_node_map"] = map,
                    ["action_meta"] = meta,
                    ["initial_anchors"] = new JArray(),
                    ["alignment_status"] = paired ? "unavailable" : "not_applicable",
                    ["alignment_reason"] = paired ? (JToken)"board_instance_unverified" : null
                };
                if (paired && instance != null)
                    Origins(rd, m, board?["wall_definition"] as JObject, xueliu);
                metadata[entry.Name] = m;
            }
            var tableBody = new JObject {
                ["table_id"] = table,
                ["table_record_id"] = sid,
                ["record_state"] = "complete",
                ["payload_codec"] = "om.action_ticks.v1",
                ["players"] = Value(detail["players"] ?? source["players_settings"] ?? new JArray()),
                ["payload"] = payload,
                ["source_sha256"] = hash,
                ["round_meta"] = metadata
            };
            if (extras.HasValues)
                tableBody["transport_actions"] = extras;
            if (fields.HasValues)
                tableBody["source_round_fields"] = fields;
            tables[table] = tableBody;
            origins.Add(new JObject { ["table_id"] = table, ["game_id"] = sid, ["sha256"] = hash });
        }
        var sync = details.Values.SelectMany(d => new[] { d["record"]["game_title"]["duplicate_sync_mode"], d["record"]["duplicate_match"]?["sync_mode"] })
            .Where(x => x != null && x.Type != JTokenType.Null).Select(x => x.Type == JTokenType.Boolean ? (bool)x : throw new InvalidDataException("同步模式无效")).Distinct().ToList();
        if (sync.Count > 1)
            throw new InvalidDataException("两桌同步模式不一致");
        return new JObject {
            ["format"] = "open_mahjong.match_record",
            ["schema_version"] = 2,
            ["match_id"] = matchId,
            ["record_revision"] = 1,
            ["match_format"] = paired ? "paired_tables_2v2" : "single_table",
            ["completeness"] = paired && details.Count < 2 ? "partial" : "complete",
            ["rule"] = Value(title["rule"]),
            ["sub_rule"] = Value(title["sub_rule"]),
            ["sync_mode"] = sync.Count > 0 ? (JToken)sync[0] : null,
            ["rule_profile"] = Value(title["duplicate_profile"] ?? new JObject { ["version"] = Value(title["rule_version"]) }),
            ["tables"] = tables,
            ["board_catalog"] = new JArray(boards.Values),
            ["teams"] = new JArray(teams.Select(t => new JObject { ["team_id"] = t.Key, ["members"] = t.Value })),
            ["settlements"] = Value(firstBody["duplicate_match"]?["boards"] ?? new JArray()),
            ["result"] = Value(firstBody["doubles_result"] ?? firstBody["match_result"]),
            ["provenance"] = new JObject { ["converter"] = "match-record-v2.1", ["sources"] = new JArray(origins.OrderBy(o => (string)o["table_id"], StringComparer.Ordinal)) }
        };
    }
}
