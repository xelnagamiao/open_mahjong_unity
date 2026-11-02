using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Salasasa.DataLab
{
    [Serializable]
    public sealed class DataRule
    {
        public string key, label, family, familyLabel, subRule, subRuleLabel, unit, winLabel, dealLabel;
        public int players;
        public bool fanStats;
        public static List<DataRule> Read(string json)
        {
            var catalog=JObject.Parse(json);
            return ((JArray)catalog["rules"]).OfType<JObject>().Where(x => (bool?)x["basic_stats"] == true)
                .Select(x => new DataRule { key = (string)x["key"], label = (string)x["stats_name"], family = (string)x["rule"],
                    familyLabel = (string)x["family_name"], subRule = (string)x["sub_rule"], subRuleLabel = (string)x["sub_rule_name"], players = (int?)x["player_count"] ?? 4,
                    unit = Unit((string)x["win_stat_unit"]), fanStats = (bool?)x["fan_stats"] == true,
                    winLabel=(string)catalog["stat_value_units"]?[(string)x["win_stat_unit"]??"fan"]?["win_label"],
                    dealLabel=(string)catalog["stat_value_units"]?[(string)x["deal_in_stat_unit"]??"fan"]?["deal_in_label"] }).ToList();
        }
        static string Unit(string unit) => unit switch { "fan" => "番", "fu" => "符", "tai" => "台", "points" => "点", "base_points" => "基本分", "multiplier" => "倍", _ => "" };
    }

    public sealed class SeatResult
    {
        public string id, name;
        public int rank;
        public double score;
        public bool hasScore;
        public string RankLabel(int playerCount) => rank >= 1 && rank <= playerCount ? "第" + rank + "名" : "—";
        public string ScoreLabel => hasScore ? PlayerData.Format(score, "+0.##;-0.##;0") : "—";
        public static SeatResult Read(JToken token) => new SeatResult
        {
            id = (string)token["user_id"], name = (string)token["username"], rank = (int?)token["rank"] ?? 0,
            score = (double?)token["score"] ?? 0, hasScore = token["score"] != null && token["score"].Type != JTokenType.Null
        };
    }

    public sealed class MatchResult
    {
        public string id, rule, subRule, room, mode, eventName, tier;
        public DateTimeOffset date;
        public List<SeatResult> seats = new List<SeatResult>();
        public SeatResult Own(string playerId) => seats.FirstOrDefault(x => x.id == playerId);
        public string DateLabel => date.ToOffset(TimeSpan.FromHours(8)).ToString("MM.dd  HH:mm");
        public string FullDate => date.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy.MM.dd  HH:mm");
        public string SceneLabel => room == "custom" ? "自定义" : room == "events" ? "赛事" : PlatformData.Label(tier ?? "match");
        public string ModeLabel => rule == "hongque" ? (mode ?? "").Split('/')[0]+" 局" : (mode ?? "").Split('/')[0] switch { "1" => "东风战", "2" => "半庄战", "3" => "东西战", "4" => rule == "hongque" ? "4 局" : "全庄战", _ => mode ?? "—" };
        public string RoomLabel => room == "match" ? "匹配" : room == "custom" ? "自建房" : "对局";
    }

    public sealed class FanOccurrence
    {
        public string name;
        public double count;
    }

    public sealed class PlayerData
    {
        public string id, name;
        public DataRule rule;
        public bool details, supportsFans, hasWinValue;
        public double games, rounds, wins, dealIns, selfDraws, fulu, totalFan, winTurns, analyzed, totalScore;
        public double[] placements = new double[4];
        public List<MatchResult> records = new List<MatchResult>();
        public List<FanOccurrence> fans = new List<FanOccurrence>();
        public int recordTotal;
        public double AverageRank => games <= 0 || placements.Sum() <= 0 ? double.NaN : placements.Select((n, i) => n * (i + 1)).Sum() / games;
        public double Rate(double numerator, double denominator) => details && denominator > 0 ? numerator / denominator : double.NaN;
        public string Percent(double numerator, double denominator) => Format(Rate(numerator, denominator) * 100, "0.0", "%");
        public static string Format(double value, string format = "0", string suffix = "") => double.IsNaN(value) || double.IsInfinity(value) ? "—" : value.ToString(format, CultureInfo.InvariantCulture) + suffix;
        public string Value(string key)
        {
            switch (key)
            {
                case "name": return name;
                case "id": return "ID  " + id;
                case "rule": return rule.label;
                case "games": return Format(games);
                case "rounds": return details ? Format(rounds) : "—";
                case "average": return Format(AverageRank, "0.00");
                case "win": return Percent(wins, rounds);
                case "dealin": return Percent(dealIns, rounds);
                case "selfdraw": return Percent(selfDraws, wins);
                case "fulu": return Percent(fulu, rounds);
                case "fan": return Format(hasWinValue ? Rate(totalFan, wins) : double.NaN, "0.00");
                case "fanWithUnit": return Value("fan") == "—" ? "—" : Value("fan") + (string.IsNullOrEmpty(rule.unit) ? "" : " " + rule.unit);
                case "fanUnit": return rule.unit;
                case "turns": return Format(Rate(winTurns, wins), "0.0");
                case "score": return details ? Format(totalScore, "+0;-0;0") : "—";
                case "wins": return details ? Format(wins) : "—";
                case "dealins": return details ? Format(dealIns) : "—";
                case "top2": return games > 0 ? Format((placements[0] + placements[1]) / games * 100, "0.0", "%") : "—";
                case "coverage": return details ? "已解析 " + Format(analyzed) + " / " + Format(games) + " 场" : "本规则暂未提供逐局统计";
                default: return "—";
            }
        }
        public static PlayerData Parse(string infoJson, string statsJson, string recordsJson, DataRule rule)
        {
            var info = Payload(infoJson); var stats = Payload(statsJson); var rec = Payload(recordsJson);
            var result = new PlayerData { rule = rule, id = (string)info["user_id"], name = (string)info["user_settings"]?["username"] ?? (string)info["username"] ?? "玩家",
                details = (bool?)stats["details_available"] == true, supportsFans = (bool?)stats["supports_fan_stats"] == true,
                hasWinValue = stats["total_fan_score"] != null && !string.IsNullOrEmpty(rule.unit),
                games = N(stats, "total_games"), rounds = N(stats, "total_rounds"), wins = N(stats, "win_count"), dealIns = N(stats, "deal_in_count"),
                selfDraws = N(stats, "self_draw_count"), fulu = N(stats, "fulu_round_count"), totalFan = N(stats, "total_fan_score"), winTurns = N(stats, "total_win_turn"),
                analyzed = N(stats, "analyzed_games"), totalScore = N(stats, "total_round_score"), recordTotal = (int?)rec["total"] ?? 0 };
            string[] names = { "first_place_count", "second_place_count", "third_place_count", "fourth_place_count" };
            for (int i = 0; i < 4; i++) result.placements[i] = N(stats, names[i]);
            var dict = info["fan_dict"]?[rule.subRule] as JObject ?? info["fan_dict"]?[rule.family] as JObject;
            var fanStats = stats["fan_stats"] as JObject;
            if (fanStats != null && result.supportsFans)
                foreach (var p in fanStats.Properties()) if (double.TryParse(p.Value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double n) && n > 0)
                    result.fans.Add(new FanOccurrence { name = (string)dict?[p.Name] ?? p.Name, count = n });
            result.fans = result.fans.OrderByDescending(x => x.count).ThenBy(x => x.name, StringComparer.Ordinal).ToList();
            foreach (var token in rec["items"] as JArray ?? new JArray())
            {
                var m = new MatchResult { id = (string)token["game_id"], rule = (string)token["rule"], subRule = (string)token["sub_rule"], room = (string)token["room_type"],
                    mode = (string)token["match_type"], eventName = (string)token["event_name"], date = ReadDate(token["created_at"]) };
                foreach (var seat in token["players"] as JArray ?? new JArray()) m.seats.Add(SeatResult.Read(seat));
                result.records.Add(m);
            }
            return result;
        }
        public static JObject Payload(string json)
        {
            var root = JObject.Parse(json);
            if ((bool?)root["success"] == false || root["data"] is not JObject data) throw new FormatException("接口未返回可用数据");
            return data;
        }
        static double N(JObject obj, string key) => (double?)obj[key] ?? 0;
        public static DateTimeOffset ReadDate(JToken token)
        {
            if (token is JValue value && value.Value is DateTimeOffset offset) return offset;
            if (token is JValue dateValue && dateValue.Value is DateTime date)
                return new DateTimeOffset(date.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(date,DateTimeKind.Utc) : date);
            return DateTimeOffset.TryParse((string)token,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var parsed) ? parsed : DateTimeOffset.MinValue;
        }
        public static string Definition => "统计范围\n所选规则、房间类型与日期同时作用于战绩和对局列表。\n\n和牌率 / 放铳率\n个人和牌次数 / 放铳次数 ÷ 参与的手数。\n\n自摸占和牌\n自摸次数 ÷ 和牌次数；不是每手自摸率。\n\n副露率\n个人副露手数 ÷ 参与的手数。\n\n平均顺位\n各顺位场次 × 顺位之和 ÷ 总场次。\n\n番种记录\n记录累计出现次数，可重复计数，不是互斥占比。\n\n近期走势\n最近最多 20 场，按时间从旧到新。缺少明细的指标显示“—”。";
    }
}
