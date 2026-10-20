#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class ShangraoClientValidation {
    public static string Run(string fixturePath = null) {
        var checks = new List<string>();
        void Check(bool ok, string label) { if (!ok) throw new Exception("Shangrao: " + label); checks.Add(label); }
        ShangraoRuleBootstrap.Register();
        var manifest = RuleRegistry.Resolve("shangrao", "shangrao/standard");
        Check(manifest?.OutboundChannel == "shangrao" && manifest.GameStateFactory() is ShangraoGameState, "rule and independent state registered");
        Check(!manifest.HasFlowerReplacement && manifest.PeekAnkan && !manifest.ShowsRonDangerHints, "136 tiles and self draw only");
        Check(manifest.VisibleMeldTiles("G11", false).SequenceEqual(new[] {11,11,11,11}), "all four concealed kong faces visible");
        Check(GameRecordMeldCodec.BuildAngangMaskFromRemoved(new[] {11,11,11,11}, "shangrao/standard").SequenceEqual(new[] {0,11,0,11,0,11,0,11}), "replay concealed kong mask");
        Check(manifest.MaxRoundText(4) == "16副" && manifest.RulebookKey == "shangrao", "hand count and rulebook");
        var room = manifest.BuildRoom(new RoomCreationInput {Rule="shangrao",SubRule="shangrao/standard",RoomName="上饶测试",GameRound=1,RoundTimer=20,StepTimer=5,Tips=true,CountTips=true,PointerTips=true,AllowSpectator=true});
        Check(room.IsValid, "create room defaults");
        var packet = JObject.FromObject(RoomNetworkManager.BuildShangraoRequest((Shangrao_Create_RoomConfig)room.Configuration, "1"));
        Check((string)packet["detailed_config"]["rule_version"] == "shangrao-v0.2", "room rule version");
        foreach (string key in new[] {"qingyise","ziyise","allow_special_bao","four_bao_reward","flying_seven_stars"})
            Check((bool?)packet["detailed_config"][key] == false, "default off: " + key);
        Check(PlayerInfoRuleCatalog.Supports("shangrao") && !PlayerInfoRuleCatalog.HasFanStats("shangrao"), "basic statistics enabled, fan distribution disabled");
        var response = JsonConvert.DeserializeObject<Response>("{\"do_action_info\":{\"action_player\":2,\"action\":\"shangrao_payment\",\"gang_score_changes\":{\"0\":-1,\"1\":-1,\"2\":3,\"3\":-1},\"shangrao_burnt_tile\":31},\"game_info\":{\"shangrao_info\":{\"start_scores\":[0,0,0,0],\"burnt_river_indices\":[[],[],[1],[]]}}}");
        Check(response.do_action_info.gang_score_changes[2] == 3 && response.do_action_info.shangrao_burnt_tile == 31, "immediate payments and burn codec");
        Check(response.game_info.shangrao_info.start_scores.Length == 4 && response.game_info.shangrao_info.burnt_river_indices[2][0] == 1, "reconnect baseline and river marks codec");
        var visible = ShangraoTableIndicators.CountedIndicators(new ShangraoInfo {indicator=45,bao=47,burnt_tiles=new[] {31,47}}).ToList();
        Check(visible.SequenceEqual(new[] {45,47}) && 4-1-visible.Count(t=>t==47)==2, "burnt bao counted once outside river, displayed bao excluded");
        var context = new JObject {["indicator"]=45,["config"]=new JObject()};
        var solver = new ShangraoLocal(new List<string>(), context);
        var ready = new List<int> {11,11,13,13,15,15,21,21,23,23,25,25,47};
        Check(solver.Waiting(ready).Length == 34, "held bao seven pairs waits on every physical type");
        Check((string)solver.Describe(ready.Concat(new[] {31}).ToList(),31)["label"] == "仅自摸\n2分", "self only points when allowed");
        var firstBao = ready.Take(12).Concat(new[] {31,47}).ToList();
        Check(solver.Score(firstBao,47) == null, "first drawn bao cannot complete seven pairs");
        Check((string)solver.Describe(firstBao,47)["label"] == "未满足", "blocked shape has no numeric quote");
        var snapshot = RegionalLocalCalculator.Snapshot(new JObject {["rule"]="shangrao",["hand"]=new JArray(ready),["melds"]=new JArray(),["context"]=context});
        var cache = new ShangraoHintsCache();cache.Accept(snapshot.ToObject<ShangraoHints>());
        Check(cache.Waiting(new TingpaiQuery {Hand=ready,Melds=new List<string>()}).Count == 34, "native cache consumes local result");
        Check(cache.Waiting(new TingpaiQuery {Hand=ready.Skip(1).Concat(new[] {29}).ToList(),Melds=new List<string>()}).Count == 0, "changed hand rejects stale waits");
        Check(cache.Waiting(new TingpaiQuery {Hand=ready,Melds=new List<string>{"k11"}}).Count == 0, "changed meld rejects stale waits");
        var cutHand = ready.Concat(new[] {31}).ToList();
        var cuts = RegionalLocalCalculator.Snapshot(new JObject {["rule"]="shangrao",["hand"]=new JArray(cutHand),["melds"]=new JArray(),["context"]=context});
        Check((int)cuts["waiting_details_by_discard"]["31"]["29"]["points"] == 2, "seven pairs discard preview");
        var blocked = new ShangraoLocal(new List<string>(),new JObject {["indicator"]=45,["blocked"]=true});
        Check((int)blocked.Describe(cutHand,31)["points"] == 0, "win restriction suppresses points");
        var round = new Round();
        GameRecordJsonDecoder.ApplyRoundHeader(round,JObject.Parse("{\"dealer_index\":0,\"start_player_index\":0,\"seats\":[2,3,0,1],\"shangrao\":{\"indicator\":45,\"bao\":47,\"start_scores\":[10,20,-20,-10]}}"),1);
        GameRecordJsonDecoder.AccumulateScoreChangesFromTick(round,new List<string>{"shangrao","state","{}","[15,17,-19,-13]"});
        Check(round.scoreChanges.SequenceEqual(new[] {1,-3,5,-3}), "round net changes map to original seats");
        var clock = new ShangraoAskClock();
        clock.Project(1,5000,1000,10,10,out double bank,out double step);
        Check(bank == 4 && step == 1, "fractional ask clock");
        clock.Project(1,5000,1000,11,11,out bank,out step);
        Check(bank == 4 && step == 0, "duplicate ask cannot extend time");
        clock.Close(); Check(clock.IsClosed(1), "closed ask stays closed");
        int fixtures = 0;
        if (fixturePath != null) foreach (var item in JArray.Parse(File.ReadAllText(fixturePath)).Cast<JObject>()) {
            var input = new JObject {["indicator"]=item["indicator"],["config"]=item["config"], ["flying_bao"]=item["flying_bao"] ?? 0, ["flying_seven_stars"]=item["flying_seven_stars"] ?? false};
            var actual = new ShangraoLocal(item["melds"].ToObject<List<string>>(),input).Score(item["hand"].ToObject<List<int>>(),(int)item["win"],opening:(bool?)item["opening"]??false,afterKong:(bool?)item["after_kong"]??false);
            if (!JToken.DeepEquals((JToken)actual ?? JValue.CreateNull(),item["expected"])) throw new Exception("Shangrao parity fixture " + fixtures + ": " + actual + " expected " + item["expected"]);
            fixtures++;
        }
        return JsonConvert.SerializeObject(new {checks=checks.Count,fixtures,passed=true,details=checks});
    }
}
#endif
