#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public static class GuobiaoSixteenValidation {
    public static object Run() {
        var checks = new List<string>();
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks.Add(label); }
        const string sub = GuobiaoGameState.SixteenSubRule;
        Check(GuobiaoLobby.SubRules.Count(r => r.Key == sub) == 1, "one sixteeen-tile subrule in Guobiao lobby");
        Check(GuobiaoLobby.SubRules[0].Key == "guobiao/standard", "standard remains the default");
        Check(!GuobiaoGameState.IsJokerSubRule(sub), "sixteen tiles do not enable joker settings");
        Check(GuobiaoGameState.UsesAsyncLocalTips(sub), "local asynchronous scoring and waits");
        Check(MahjongPlayerCount.ForSubRule(sub) == 4, "four seats");
        var structure = HandStructures.Resolve("guobiao", sub);
        Check(structure.BaseHandTileCount == 16 && structure.CompleteHandTileCount == 17, "sixteen and seventeen tile layouts");
        for (int melds = 0; melds <= 5; melds++) {
            Check(structure.ConcealedTileCount(melds, false) == 16 - 3 * melds, "waiting hand after " + melds + " melds");
            Check(structure.ConcealedTileCount(melds, true) == 17 - 3 * melds, "complete hand after " + melds + " melds");
        }
        Check(HandStructures.Resolve("guobiao", "guobiao/standard").BaseHandTileCount == 13, "thirteen-tile layout regression");
        RuleRegistry.TryResolve("guobiao", sub, out RuleManifest manifest);
        Check(manifest != null && manifest.GameStateFactory() is GuobiaoGameState, "existing authoritative state machine family");
        Check(manifest.RulebookPath(sub, null) == "/rulebook/guobiao-sixteen", "dedicated extension rulebook");
        Check(!manifest.HandJokerTiles(sub).Any(), "ordinary physical tiles");
        Check(PlayerInfoRuleCatalog.SubRule("guobiao_sixteen") == sub && PlayerInfoRuleCatalog.HasFanStats("guobiao_sixteen"), "independent statistics");
        Check(RuleNameDictionary.WholeName[sub].Contains("十六张"), "replay and room name");
        var response = JsonConvert.DeserializeObject<Response>("{\"game_info\":{\"sub_rule\":\"guobiao/sixteen\",\"four_jokers_hints\":{\"sub_rule\":\"guobiao/sixteen\",\"hand\":[11,12,13,21,22,23,31,32,33,41,41,41,45,45,45,46],\"melds\":[],\"waits\":[46],\"discard_waits\":{}}}}");
        var snapshot = response.game_info.four_jokers_hints;
        FourJokersTips.Accept(snapshot);
        var query = new TingpaiQuery { Hand=snapshot.hand.ToList(), Melds=new List<string>(), SubRule=sub };
        Check(LocalHintValidation.WaitForLocal(query).SetEquals(new[] {46}), "live and reconnect calculate sixteen-tile waits locally");
        var returned = GuobiaoTips.Tingpai(query); returned.Clear();
        Check(GuobiaoTips.Tingpai(query).Count == 1, "immutable cached waits");
        query.Hand.Add(46);
        Check(GuobiaoTips.Tingpai(query).Count == 0, "seventeen tiles require discard candidates");
        var session = new GameSession();
        session.LoadFrom(response.game_info, 0);
        Check(session.SubRule == sub, "session preserves subrule");
        var config = new GB_Create_RoomConfig { SubRule=sub, DuplicateKey="ordinary-wall" };
        Check(!config.Validate(out _, false, false), "incompatible fourteen-tile wall rejected");
        return new { count=checks.Count, checks };
    }
}
#endif
