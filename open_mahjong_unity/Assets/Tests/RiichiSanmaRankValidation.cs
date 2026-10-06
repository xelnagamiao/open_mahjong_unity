#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Explicitly invoked contracts for the independent Sanma rating pool and authored UI.</summary>
public static class RiichiSanmaRankValidation {
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,Flags).GetValue(target);
    static T Find<T>() where T:Component=>Resources.FindObjectsOfTypeAll<T>().First(x=>x.gameObject.scene.IsValid());
    public static string Run(){
        var checks=new List<string>();
        void Check(bool condition,string name){if(!condition)throw new Exception(name);checks.Add(name);}
        Check(RankedRules.Ids.Length>=5&&RankedRules.Ids[4]==RiichiSanmaRankConfig.Rule,"rating pool appended without changing existing IDs");
        Check(RankedRules.Supports(RiichiSanmaRankConfig.Rule)&&RankedRules.IsGrade(RiichiSanmaRankConfig.Rule),"three player grade pool supported");
        var rating=RankedRules.Get(null,RiichiSanmaRankConfig.Rule);
        Check(rating.rank_name=="10级"&&rating.rank_score==0&&rating.system=="grade","new pool starts at 10级 and zero PT");
        Check(RankedRules.RankCaption(rating)=="立直三麻 · 10级"&&RankedRules.ScoreCaption(rating).Contains("PT"),"grade captions use independent pool");
        Check(RankedRules.Next("sichuan")==RiichiSanmaRankConfig.Rule&&RankedRules.Ids.Select(RankedRules.Next).Distinct().Count()==RankedRules.Ids.Length,"home cycle includes new pool");
        Check(RankedRules.QueueRule("riichi_beginner_dongfeng")=="riichi","four player queue prefix unchanged");
        var lobby=Find<MatchLobbyView>();
        Check(Field<GameObject[]>(lobby,"rulePages").Length==RankedRules.Ids.Length&&Field<Button[]>(lobby,"ruleButtons").Length==RankedRules.FamilyNames.Length,"six rating pages grouped under four authored match tabs");
        Check(Field<TMP_Text[]>(lobby,"rulePlayerCounts").Length==RankedRules.FamilyNames.Length&&Field<TMP_Text[]>(lobby,"variantPlayerCounts").Length==2,"family and independent variant online counts authored");
        Check(RankedRules.FamilyIndex(1)==RankedRules.FamilyIndex(4)&&RankedRules.FamilyRuleIndices[1].SequenceEqual(new[]{1,4}),"four and three player Riichi share navigation with distinct pool indices");
        var entries=Field<MatchButton[]>(lobby,"entries");
        int expected=RankedRules.Ids.Sum(r=>r=="guobiao"?12:r=="riichi"||r==RiichiSanmaRankConfig.Rule?6:1);
        Check(entries.Length==expected&&entries.Select(x=>x.QueueType).Distinct().Count()==expected,"all ranked entries are unique");
        Check(entries.Count(x=>x.RuleId=="riichi")==6&&entries.Count(x=>x.RuleId==RiichiSanmaRankConfig.Rule)==6,"six three player and six four player queues");
        foreach(var card in entries.Where(x=>x.RuleId==RiichiSanmaRankConfig.Rule)){
            string q=card.QueueType;var parts=q.Substring(RiichiSanmaRankConfig.Rule.Length+1).Split('_');
            int tier=Array.IndexOf(RiichiRankConfig.Tiers,parts[0]);
            int pt=(int)(RiichiSanmaRankConfig.EastPoints[tier]*(parts[1]=="dongfeng"?1f:1.5f));
            string description=RiichiSanmaRankConfig.DescribeQueue(q);
            Check(card.IsAvailable&&RankedRules.QueueRule(q)==RiichiSanmaRankConfig.Rule,"queue prefix and availability "+q);
            Check(description.Contains("第一名 +"+pt)&&description.Contains("第二名 0")&&description.Contains("第三名 -"+pt),"placement PT description "+q);
            Check(MatchQueueDisplayText.GetQueueTitle(q).Contains("立直三麻"),"queue title "+q);
        }
        foreach(string invalid in new[]{null,"riichi_beginner_dongfeng","riichi_sanma_bad_dongfeng","riichi_sanma_beginner_quanzhuang","riichi_sanma_beginner_dongfeng_extra"})
            Check(RiichiSanmaRankConfig.DescribeQueue(invalid).Contains("暂未开放"),"invalid sanma description "+invalid);
        Check(Field<TMP_Dropdown>(Find<DataPanel>(),"ruleDropdown").options.Count==RankedRules.Ids.Length,"leaderboard selector includes Sanma");
        Check(Field<Button[]>(Find<PlayerInfoPanel>(),"ruleButtons").Length==PlayerInfoRuleCatalog.PrimaryRules.Length
            && PlayerInfoRuleCatalog.OtherRules(true).Any(option=>option.Key==RiichiSanmaRankConfig.Rule),
            "profile keeps four main buttons and exposes Sanma in the other-rule dropdown");
        var labels=Field<TMP_Text[]>(Field<PlayerInfoTrendChart>(Find<PlayerInfoPanel>(),"trendChart"),"rankLabels");
        Check(labels.Length==4&&labels.All(x=>x!=null)&&labels.Distinct().Count()==4,"trend labels authored for three and four player views");
        string rule=RiichiSanmaRankConfig.Rule;
        var modes=PlayerInfoStatsFormatter.Modes(rule,true);
        Check(modes.SequenceEqual(new[]{"2/4_sanma_rank","1/4_sanma_rank"}),"profile queries only two three player modes");
        Check(PlayerInfoStatsFormatter.ModeCaption(rule,modes[0],true).Contains("半庄"),"three player half game caption");
        var stats=new RuleStatsResponse{history_stats=new PlayerStatsInfo[]{
            new PlayerStatsInfo{rule=rule,mode=modes[0],total_games=2,riichi_details=new Dictionary<string,int>{{"riichi_round_count",3}}},
            new PlayerStatsInfo{rule=rule,mode=modes[1],total_games=1,riichi_details=new Dictionary<string,int>{{"riichi_round_count",1}}},
            new PlayerStatsInfo{rule="riichi",mode="2/4_rank",total_games=99,riichi_details=new Dictionary<string,int>{{"riichi_round_count",99}}}}};
        var total=PlayerInfoStatsFormatter.Aggregate(stats,rule,modes);
        Check(total.total_games==3&&total.riichi_details["riichi_round_count"]==4,"three player profile excludes four player games and retains details");
        Check(RankedRules.Policy.Contains("三人立直")&&RankedRules.Policy.Contains("第二名不变"),"policy explains three player placement scoring");
        return JsonConvert.SerializeObject(new{success=true,count=checks.Count,checks});
    }
}
#endif
