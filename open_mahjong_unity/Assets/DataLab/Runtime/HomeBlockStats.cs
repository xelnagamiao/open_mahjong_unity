using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Salasasa.DataLab
{
    public sealed class HomeBlockStats
    {
        public readonly List<KeyValuePair<string,string>> cells=new List<KeyValuePair<string,string>>();
        public double games;
        public bool details;
        public static string[] SceneKeys(string rule)=>rule=="guobiao"?new[]{"beginner","intermediate","advanced","mcrpl","custom","events"}:
            rule=="riichi"||rule=="riichi_sanma"?new[]{"beginner","intermediate","advanced","custom","events"}:
            new[]{"sichuan","sichuan_xueliu_exchange","hongque","qingque"}.Contains(rule)?new[]{"rank","custom","events"}:new[]{"custom","events"};
        public static string SceneLabel(string key)=>key=="beginner"?"初级":key=="intermediate"?"中级":key=="advanced"?"高级":key=="mcrpl"?"mcrpl":key=="rank"?"段位场":key=="events"?"比赛场":PlatformData.Label(key);
        public static string GameType(JToken row,DataRule rule)
        {
            if(rule.family!="hongque"&&!string.IsNullOrEmpty((string)row["game_type"]))return (string)row["game_type"];
            int.TryParse(((string)row["mode"]??"").Split('/')[0],out int n);
            if(rule.family=="hongque")n/=4;
            return n==4?"quanzhuang":n==3?"xifeng":n==2?"banzhuang":n==1?"dongfeng":"";
        }
        public static HomeBlockStats Read(string json,DataRule rule,string scene,string mode)
        {
            var result=new HomeBlockStats();
            var all=PlayerData.Payload(json)["rows"] as JArray??new JArray();
            var rows=all.Where(x=>(string)x["rule"]==rule.family&&((string)x["sub_rule"]==rule.subRule||string.IsNullOrEmpty((string)x["sub_rule"])&&rule.key==rule.family))
                .Where(x=>string.IsNullOrEmpty(mode)||GameType(x,rule)==mode)
                .Where(x=>scene=="custom"||scene=="events"?(string)x["room_type"]==scene:
                    (string)x["room_type"]=="match"&&(scene=="rank"||(string)x["match_tier"]==scene)).ToArray();
            bool record=rows.Any(x=>(string)x["source"]=="record_metrics");
            if(rule.family!="riichi"&&!record){
                if(scene=="custom"||scene=="events")rows=rows.Where(x=>(string)x["source"]!="metrics").ToArray();
                else{var metrics=rows.Where(x=>(string)x["source"]=="metrics"&&!string.IsNullOrEmpty((string)x["match_tier"])).ToArray();rows=metrics.Length>0?metrics:rows.Where(x=>(string)x["source"]=="history").ToArray();}
            }
            double Sum(string key)=>rows.Sum(x=>(double?)x[key]??0);
            double D(string key)=>rows.Sum(x=>(double?)x["riichi_details"]?[key]??0);
            string Avg(double n,double d)=>d>0?(n/d).ToString("0.00",CultureInfo.InvariantCulture):"0.00";
            string Rate(double n,double d)=>Avg(n*100,d)+"%";
            void Add(string label,string value,bool needsDetails=false)=>result.cells.Add(new KeyValuePair<string,string>(label,needsDetails&&!result.details?"—":value));
            bool riichi=rule.family=="riichi";
            result.details=rows.Length>0&&rows.All(x=>(bool?)x["details_available"]==true&&(!riichi||x["riichi_details"] is JObject));
            result.games=Sum("total_games");
            double rounds=Sum("total_rounds"),wins=Sum("win_count"),deals=Sum("deal_in_count");
            string[] rankKeys={"first_place_count","second_place_count","third_place_count","fourth_place_count"};
            double ranks=rankKeys.Sum(Sum),weighted=rankKeys.Select((key,i)=>Sum(key)*(i+1)).Sum();
            Add("总对局",result.games.ToString("#,0",CultureInfo.InvariantCulture));
            Add("总回合",rounds.ToString("#,0",CultureInfo.InvariantCulture),true);
            Add("平均顺位",Avg(weighted,ranks));
            Add("局均点",Avg(Sum("total_round_score"),riichi?Sum("player_game_count"):result.games));
            string[] rankLabels={"一位率","二位率","三位率","四位率"};
            for(int i=0;i<rule.players&&i<4;i++)Add(rankLabels[i],Rate(Sum(rankKeys[i]),ranks));
            bool sichuan=rule.family=="sichuan";
            Add(sichuan?"局均和牌次数":"和牌率",sichuan?Avg(wins,rounds):Rate(wins,rounds),true);
            Add("自摸率",Rate(Sum("self_draw_count"),wins),true);
            Add(sichuan?"局均放铳次数":"放铳率",sichuan?Avg(deals,rounds):Rate(deals,rounds),true);
            if(!riichi)Add("错和率",Rate(Sum("cuohe_count"),rounds),true);
            Add("副露率",Rate(Sum("fulu_round_count"),rounds*(riichi?1:rule.players)),true);
            if(riichi){
                Add("被飞率",Rate(D("busted_count"),D("final_score_count")),true);
                Add("立直率",Rate(D("riichi_round_count"),rounds),true);
                Add("平均和了打点",Avg(D("total_win_points"),wins),true);
                Add("平均和了巡目",Avg(Sum("total_win_turn"),wins),true);
            }else{
                if(rule.winLabel!=null)Add(rule.winLabel,Avg(Sum("total_fan_score"),wins),true);
                Add("平均和巡",Avg(Sum("total_win_turn"),wins),true);
                if(rule.dealLabel!=null)Add(rule.dealLabel,Avg(Sum("total_fangchong_score"),deals),true);
            }
            return result;
        }
    }
}
