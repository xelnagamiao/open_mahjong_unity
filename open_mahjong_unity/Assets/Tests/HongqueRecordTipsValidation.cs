#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

public static class HongqueRecordTipsValidation {
    public static string Run() {
        var registry = (Dictionary<string, RuleManifest>)typeof(RuleRegistry)
            .GetField("ManifestsByRule", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        registry.TryGetValue("hongque", out var previous);
        try {
            typeof(HongqueRuleBootstrap).GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            return RunChecks();
        } finally {
            if (previous == null) registry.Remove("hongque"); else registry["hongque"] = previous;
        }
    }
    private static string RunChecks() {
        int passed = 0;
        void Check(bool condition, string message) {
            if (!condition) throw new InvalidOperationException(message);
            passed++;
        }
        var rule = RuleRegistry.Resolve("hongque", "hongque/v1.6");
        Check(rule?.Tingpai != null && rule.DescribeWaitingTile != null, "Hongque replay hint registration");
        for (int count = 1; count <= 14; count++) {
            var player = new GameRecordManager.RecordPlayer { tileList = Enumerable.Range(1, count).Select(i => 1000+i).ToList() };
            Check(RecordChongHintCalculator.NormalizeHandForTingpai(player, "hongque").SequenceEqual(player.tileList),
                "Variable hand length must not imply a drawn tile: " + count);
            player.showHandDrawSlotActive = true;
            Check(RecordChongHintCalculator.NormalizeHandForTingpai(player, "hongque").Count == count-1,
                "Only the explicit drawn tile is excluded: " + count);
            Check(player.tileList.Count == count, "Hint queries must not alter the hand");
        }
        var hand = new List<int> {1002,1003,1024,1025,1026,1044,1045,1046,1064,1065,1066};
        var waiting = rule.Tingpai(new TingpaiQuery { Hand=hand, MeldMasks=new List<int[]>() });
        Check(waiting.Contains(1001), "AX1 completes the known winning hand");
        var hint = rule.DescribeWaitingTile(new WaitHintQuery {
            HepaiTile=1001, HandWithWin=new List<int>(hand) {1001}, MeldMasks=new List<int[]>() });
        Check(hint?.Kind == WaitTileHint.KindRon, "Normal Hongque waiting tile qualifies for ron");
        waiting.Clear();
        Check(rule.Tingpai(new TingpaiQuery { Hand=hand }).Contains(1001), "Caller mutation cannot corrupt cached waits");
        hand.Reverse();
        Check(rule.Tingpai(new TingpaiQuery { Hand=hand }).Contains(1001), "Reordering a hand preserves cached waits");
        var kongMelds = new List<int[]> { new[] {0,1001,0,1002,0,1003} };
        var kongPlayer = new GameRecordManager.RecordPlayer { tileList=new List<int>(), combinationMasks=kongMelds };
        Check(RecordChongHintCalculator.ComputeWaitingTilesForPlayer(kongPlayer,"hongque").Contains(1004),
            "An empty hand can wait for a kong win");
        Check(rule.DescribeWaitingTile(new WaitHintQuery { HepaiTile=1004, HandWithWin=new List<int>{1004},
            MeldMasks=kongMelds })?.Kind == WaitTileHint.KindTsumoOnly, "Kong-only wait is not ron");
        Check(!RecordChongHintCalculator.ComputeDangerTiles(new Dictionary<string,GameRecordManager.RecordPlayer> {
            ["self"]=kongPlayer }, "hongque").Contains(1004), "Kong-only wait is not a danger tile");
        kongMelds[0][1]=1002; kongMelds[0][3]=1003; kongMelds[0][5]=1004;
        Check(RecordChongHintCalculator.ComputeWaitingTilesForPlayer(kongPlayer,"hongque").Contains(1001),
            "Changing a meld in place invalidates the cached result");
        var melds = new List<int[]> { new[] {0,1001,0,1002,0,1003,0,1004} };
        var context = new RecordTipsContext { RoomRule="hongque", SubRule="hongque/v1.6",
            PlayersByPosition=new Dictionary<string,RecordTipsPlayerVisible> {
                ["left"]=new RecordTipsPlayerVisible { CombinationMasks=melds, CombinationTiles=new List<string>{"s1002"} }
            }
        };
        var visible=RecordWaitHintCalculator.CountVisibleTiles(context, new List<int>());
        Check(visible.Count == 4 && visible.Values.All(count=>count==1), "Extended meld counts every physical tile once");
        Check(RecordWaitHintCalculator.Remaining(1004,visible,rule)==0, "Unique revealed tile has no remaining copy");
        Check(RecordWaitHintCalculator.Remaining(1005,visible,rule)==1, "Unseen Hongque tile has one copy");
        Check(RecordChongHintCalculator.NormalizeHandForTingpai(Enumerable.Range(1,13).ToList()).Count==13,
            "Ordinary thirteen-tile normalization is unchanged");
        Check(RecordChongHintCalculator.NormalizeHandForTingpai(Enumerable.Range(1,14).ToList()).Count==13,
            "Ordinary fourteen-tile normalization is unchanged");
        Check(RecordWaitHintCalculator.Remaining(11,new Dictionary<int,int>(),RuleRegistry.Resolve("guobiao"))==4,
            "Ordinary tile supply is unchanged");
        return JsonConvert.SerializeObject(new {passed});
    }
}
#endif
