using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Salasasa.DataLab
{
    public sealed class PlatformData
    {
        public static readonly string[] ScopeKeys={"all","beginner","intermediate","advanced","custom","mcrpl","elo","events"};
        public static readonly string[] ScopeLabels={"全场次","初级场","中级场","高级场","自定义","MCRPL","Elo 匹配","赛事"};
        public static readonly string[] MainTiers={"beginner","intermediate","advanced","custom"};
        public static readonly string[] ModeKeys={"dongfeng","banzhuang","xifeng","quanzhuang"};
        public static readonly string[] ModeLabels={"东风战","半庄战","东西战","全庄战"};
        public DataRule rule;
        public string scope, asOf, from, to, source, statsError, recordsError;
        public double games, matchGames, tableHands, wins, selfDraws, fanScore, turns;
        public bool statsAvailable, supportsFans;
        public readonly Dictionary<string,double> tiers=new Dictionary<string,double>();
        public readonly Dictionary<string,double> modes=new Dictionary<string,double>();
        public readonly Dictionary<string,Dictionary<string,double>> tierModes=new Dictionary<string,Dictionary<string,double>>();
        public readonly List<MatchResult> records=new List<MatchResult>();
        public readonly List<FanOccurrence> fans=new List<FanOccurrence>();
        public float[] daily=Array.Empty<float>();
        public int recordTotal, recordOffset;
        public bool PublicRecords => scope!="custom";
        public string ScopeLabel => Label(scope);
        public string DetailScope => scope=="all"?"匹配场":ScopeLabel;
        public string RecordScope => scope=="all"?"匹配场":ScopeLabel;
        public string RecordEmpty => recordsError ?? (PublicRecords ? "暂无公开对局记录" : "自定义房间\n暂未提供公共牌谱列表");
        public string DailyCaption => scope=="custom"?"自定义房间暂未提供每日汇总":statsError!=null?"每日汇总暂时不可用":from+" — "+to+"  ·  "+DetailScope;
        public string DailyNote => scope=="custom"?"仅展示可核验的累计对局":statsError!=null?statsError:daily.Count(x=>!float.IsNaN(x))+" / "+daily.Length+" 天有记录 · 缺失日期留空";
        public static string Label(string key){int i=Array.IndexOf(ScopeKeys,key);return i<0?key:ScopeLabels[i];}
        public double Tier(string key)=>tiers.TryGetValue(key,out var n)?n:0;
        public double Mode(string key)=>modes.TryGetValue(key,out var n)?n:0;
        public double TierMode(string tier,string mode)=>tierModes.TryGetValue(tier,out var m)&&m.TryGetValue(mode,out var n)?n:0;
        static double N(JToken obj,string key)=>(double?)obj?[key]??0;
        static string F(double n,string format="#,0")=>PlayerData.Format(n,format);
        public string Value(string key)
        {
            if(key.StartsWith("modeLabel.")){int i=int.Parse(key.Substring(10));return rule.family=="hongque"?new[]{"4 局","8 局","12 局","16 局"}[i]:ModeLabels[i];}
            if(key.StartsWith("tier."))return F(Tier(key.Substring(5)));
            if(key.StartsWith("mode."))return F(Mode(key.Substring(5)));
            if(key.StartsWith("matrix.")){var p=key.Split('.');return F(TierMode(p[1],p[2]));}
            switch(key)
            {
                case "games":return F(games);
                case "scope":return ScopeLabel;
                case "rule":return rule.label;
                case "matchGames":return statsAvailable?F(matchGames):"—";
                case "rounds":return statsAvailable?F(tableHands):"—";
                case "wins":return statsAvailable?F(wins):"—";
                case "selfdraw":return statsAvailable&&wins>0?F(selfDraws/wins*100,"0.0")+"%":"—";
                case "fan":return statsAvailable&&wins>0&&!string.IsNullOrEmpty(rule.unit)?F(fanScore/wins,"0.00"):"—";
                case "fanWithUnit":return Value("fan")=="—"?"—":Value("fan")+" "+rule.unit;
                case "turns":return statsAvailable&&wins>0?F(turns/wins,"0.0"):"—";
                case "dailySum":return daily.Any(x=>!float.IsNaN(x))?F(daily.Where(x=>!float.IsNaN(x)).Sum()):"—";
                case "dailyPeak":return daily.Any(x=>!float.IsNaN(x))?"单日峰值  "+F(daily.Where(x=>!float.IsNaN(x)).Max()):"暂无日汇总";
                case "dailyCaption":return DailyCaption;
                case "dailyNote":return DailyNote;
                case "detailScope":return DetailScope+" · 累计统计";
                case "recordsScope":return RecordScope+" · 近期公开牌谱";
                case "recentTitle":return "近期对局 · "+RecordScope;
                case "fansScope":return DetailScope+" · 番种记录";
                case "cutoff":return "日汇总截止 "+asOf;
                case "other":return F(tiers.Where(x=>!MainTiers.Contains(x.Key)).Sum(x=>x.Value));
                default:return "—";
            }
        }
        public static PlatformData Parse(string homeJson,string statsJson,DataRule rule,string scope)
        {
            var p=new PlatformData{rule=rule,scope=scope};
            var home=PlayerData.Payload(homeJson);
            var rows=(home["rows"] as JArray??new JArray()).Where(x=>(string)x["rule"]==rule.key && ((string)x["sub_rule"]==rule.subRule || x["sub_rule"]==null)).ToArray();
            foreach(var row in rows)
            {
                string room=(string)row["room_type"],tier=room=="match"?(string)row["match_tier"]:room;
                if(string.IsNullOrEmpty(tier))tier="other";
                double games=N(row,"total_games");
                p.tiers[tier]=p.Tier(tier)+games;
                string mode=(string)row["game_type"]??"other";
                if(rule.family=="hongque")mode=mode switch{"hands4"=>"dongfeng","hands8"=>"banzhuang","hands12"=>"xifeng","hands16"=>"quanzhuang",_=>mode};
                if(!p.tierModes.ContainsKey(tier))p.tierModes[tier]=new Dictionary<string,double>();
                p.tierModes[tier][mode]=p.TierMode(tier,mode)+games;
                if(scope=="all" || tier==scope){p.games+=games;p.modes[mode]=p.Mode(mode)+games;}
            }
            if(scope=="custom" || string.IsNullOrEmpty(statsJson))return p;
            var root=JObject.Parse(statsJson);var stats=PlayerData.Payload(statsJson);var meta=root["meta"];
            p.asOf=(string)meta?["as_of_date"]??"—";p.from=(string)meta?["date_from"]??"";p.to=(string)meta?["date_to"]??"";
            string statTier=scope=="all"||scope=="events"?"total":scope;
            var totals=(stats["totals"] as JArray??new JArray()).FirstOrDefault(x=>(string)x["match_tier"]==statTier);
            p.statsAvailable=totals!=null;p.matchGames=N(totals,"total_games");p.tableHands=N(totals,"total_rounds");p.wins=N(totals,"win_count");p.selfDraws=N(totals,"self_draw_count");p.fanScore=N(totals,"total_fan_score");p.turns=N(totals,"total_win_turn");
            p.supportsFans=(bool?)stats["supports_fan_stats"]==true;
            var dict=stats["fan_dict"] as JObject;var fanStats=stats["fans"]?[statTier] as JObject;
            if(p.supportsFans&&fanStats!=null)foreach(var item in fanStats.Properties())if(double.TryParse(item.Value.ToString(),NumberStyles.Any,CultureInfo.InvariantCulture,out double count)&&count>0)p.fans.Add(new FanOccurrence{name=(string)dict?[item.Name]??item.Name,count=count});
            p.fans.Sort((a,b)=>b.count.CompareTo(a.count));
            var days=new Dictionary<string,float>();
            foreach(var group in (stats["daily"] as JArray??new JArray()).GroupBy(x=>(string)x["stat_date"]))
            {
                var filtered=group.Where(x=>(string)x["match_tier"]==statTier).ToArray();
                if(filtered.Length>0)days[group.Key]=(float)filtered.Sum(x=>N(x,"total_games"));
                else if(statTier=="total")days[group.Key]=(float)group.Where(x=>(string)x["match_tier"]!="total").Sum(x=>N(x,"total_games"));
            }
            if(DateTime.TryParse(p.from,out var start)&&DateTime.TryParse(p.to,out var end)&&end>=start&&(end-start).TotalDays<366)
            {
                p.daily=Enumerable.Range(0,(end-start).Days+1).Select(i=>days.TryGetValue(start.AddDays(i).ToString("yyyy-MM-dd"),out float n)?n:float.NaN).ToArray();
            }
            return p;
        }
        public void ReadRecords(string json,int offset=0)
        {
            records.Clear();recordOffset=offset;
            if(string.IsNullOrEmpty(json)){recordTotal=0;return;}
            var obj=PlayerData.Payload(json);recordTotal=(int?)obj["total"]??0;
            foreach(var token in obj["items"] as JArray??new JArray())
            {
                var m=new MatchResult{id=(string)token["game_id"],rule=(string)token["rule"],subRule=(string)token["sub_rule"],room=(string)token["room_type"],mode=(string)token["match_type"],tier=(string)token["match_tier"],eventName=(string)token["event_name"],date=PlayerData.ReadDate(token["created_at"])};
                foreach(var s in token["players"] as JArray??new JArray())m.seats.Add(SeatResult.Read(s));
                m.seats=m.seats.OrderBy(s=>s.rank>=1&&s.rank<=m.seats.Count?s.rank:int.MaxValue).ToList();records.Add(m);
            }
        }
        public static string Definition=>"平台累计\n按当前规则及场次读取公开牌谱统计，对局按 game_id 去重。全场次含匹配、自定义和赛事；四张场次卡始终展示当前规则的累计数量。\n\n匹配场行为数据\n累计手数为全桌手数；平均和牌 = 总和牌番值 ÷ 和牌次数；自摸占和牌 = 自摸次数 ÷ 和牌次数。不展示个人胜率和个人顺位。\n\n每日对局量\n时间窗口只作用于日汇总，不改变累计指标与最近牌谱。统计日以北京时间 04:00 切日，截止最近已完成聚合日。缺少记录的日期留空，不补零。\n\n场次与局制\n初级、中级、高级、自定义的累计计数来自网站首页的原始牌谱归口接口。自定义部分缺少完整行为指标，只展示已提供的对局计数。\n\n最近对局\n全场次时展示匹配场公共牌谱，可按等级场筛选、翻页并查看同桌结算。赛事单独查询；现有接口未提供自定义房间公共牌谱列表。\n\n番种记录\n累计出现次数可重复，不是互斥占比。所选场次无支持时显示不可用。";
    }
}
