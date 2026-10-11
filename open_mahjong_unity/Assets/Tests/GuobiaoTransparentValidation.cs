#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

/// <summary>Transparent MCR calculation and protocol regressions, also runnable in a Player.</summary>
public static class GuobiaoTransparentValidation {
    public static object Run() {
        var checks = new List<string>();
        void Check(bool condition, string label) {
            if (!condition) throw new InvalidOperationException("Transparent MCR: " + label);
            checks.Add(label);
        }
        string rule = GuobiaoGameState.TransparentSubRule;
        Check(GuobiaoLobby.SubRules.Any(r => r.Key == rule), "room subrule registered");
        Check(WashizuTile.UsesTransparentHands(rule), "glass presentation enabled");
        Check(!WashizuTile.UsesTransparentHands("guobiao/standard"), "standard presentation unchanged");
        Check(MahjongPlayerCount.ForSubRule(rule) == 4, "four players");
        RuleRegistry.TryResolve("guobiao", rule, out RuleManifest manifest);
        Check(manifest != null && manifest.RuleId == "guobiao", "uses MCR manifest");
        Check(manifest.NormalizeTileId(2015) == 15, "remaining tile counts normalize glass");
        var hand = new List<int> {11,11,11,21,22,23,31,32,33,44,44,44,45};
        var glass = hand.Select(t => t+2000).ToList();
        foreach (string sub in new[] {"guobiao/standard", rule}) {
            var plainWaiting = GuobiaoTips.Tingpai(new TingpaiQuery {Hand=hand, Melds=new List<string>(), SubRule=sub});
            var glassWaiting = GuobiaoTips.Tingpai(new TingpaiQuery {Hand=glass, Melds=new List<string>(), SubRule=sub});
            Check(plainWaiting.SetEquals(glassWaiting) && glassWaiting.Contains(45), "waiting parity " + sub);
            foreach (int limit in new[] {8,16,64}) {
                WaitHintQuery Query(List<int> cards, int win) => new WaitHintQuery {
                    HandWithWin = new List<int>(cards) {win}, HepaiTile=win,
                    Melds=new List<string>(), SubRule=sub, HepaiLimit=limit,
                    WayToHepai=new List<string> {"场风东","自风东","和单张"},
                    SingleTileWay=new List<string>(), MergedWay=new List<string> {"场风东","自风东","和单张","点和"},
                };
                var p = GuobiaoTips.Describe(Query(hand,45));
                var g = GuobiaoTips.Describe(Query(glass,2045));
                Check(p.Kind==g.Kind && p.Label==g.Label, "hint parity " + sub + " limit " + limit);
            }
        }
        Check(glass.All(WashizuTile.IsTransparent), "calculators preserve physical input");
        Check(WashizuTile.VisibleMeldTiles(new[]{2,0,2,2022,2,2022,2,2022}).Select(WashizuTile.Face).SequenceEqual(new[]{22,22,22,22}), "glass concealed kong exhausts four copies");
        Check(!WashizuTile.VisibleMeldTiles(new[]{2,0,2,0,2,0,2,0}).Any(), "opaque concealed kong stays unknown");
        Check(HeJuezhangTableCounter.CountShowTilesOnTable(45,
            new[] {new List<int>{2045,45}}, new List<string>(), 2045)==3, "last-copy fan counts physical river");
        foreach (int tile in Enumerable.Range(11,9).Concat(Enumerable.Range(21,9)).Concat(Enumerable.Range(31,9)).Concat(Enumerable.Range(41,7))) {
            Check(manifest.NormalizeTileId(tile+2000)==tile, "normalize " + tile);
            Check(!WashizuTile.SameCopyKind(tile,tile+2000), "physical removal distinction " + tile);
        }
        foreach (int flower in Enumerable.Range(51,8)) {
            Check(!WashizuTile.IsTransparent(flower) && manifest.NormalizeTileId(flower)==flower, "flower preserved " + flower);
        }
        var response=JsonConvert.DeserializeObject<Response>("{\"success\":true,\"type\":\"gamestate/guobiao/do_action\",\"do_action_info\":{\"action_list\":[\"peng\"],\"action_player\":1,\"combination_mask\":[1,2015,0,15,0,2015],\"washizu_hands\":{\"1\":[2011,2012,0]}}}");
        Check(response.do_action_info.washizu_hands[1].SequenceEqual(new[]{2011,2012,0}), "public payload preserves opaque zero");
        Check(response.do_action_info.combination_mask.SequenceEqual(new[]{1,2015,0,15,0,2015}), "meld physical IDs survive transport");
        return new {count=checks.Count, checks};
    }
}
#endif
