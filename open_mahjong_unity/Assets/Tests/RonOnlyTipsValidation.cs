#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Real scoring regressions for the distinction between ron-only and self-draw-only waits.</summary>
public static class RonOnlyTipsValidation {
    private static WaitHintQuery Query(int[] hand, string[] melds, int win, string sub = "guobiao/standard", int minimum = 8, bool single = false) {
        var way = new List<string> { "场风东", "自风东" };
        if (single) way.Add("和单张");
        return new WaitHintQuery { HandWithWin = hand.ToList(), Melds = melds.ToList(), HepaiTile = win,
            SubRule = sub, HepaiLimit = minimum, CurrentRound = 1, SelfFlowers = new List<int>(),
            WayToHepai = way, SingleTileWay = new List<string>(), MergedWay = way.Concat(new[] { "点和" }).ToList(),
            Record = new RecordTipsContext { SubRule = sub, HepaiLimit = minimum, CurrentRound = 1 } };
    }
    public static object Run() {
        if (Application.isPlaying) throw new InvalidOperationException("Run outside Play Mode.");
        var checks = new List<string>();
        void Check(string name, bool okay) { if (!okay) throw new InvalidOperationException(name); checks.Add(name); }
        void Hint(string name, WaitTileHint hint, string kind, string label = null) {
            Check(name, hint != null && hint.Kind == kind && (label == null || hint.Label == label));
        }
        var chicken = Query(new[] {23,24,25,35,36,37,17,17,17,42,42}, new[] {"s12"}, 37);
        var begging = Query(new[] {25,25}, new[] {"s12","s24","s36","s17"}, 25, single: true);
        foreach (var q in new[] { chicken, begging }) {
            string before = JsonConvert.SerializeObject(q);
            Hint("国标仅点和/" + q.HepaiTile, GuobiaoTips.Describe(q), WaitTileHint.KindRonOnly, "仅点和");
            Check("查询输入保持不变/" + q.HepaiTile, before == JsonConvert.SerializeObject(q));
            var first = GuobiaoTips.Describe(q); first.Label = "changed"; first.Kind = WaitTileHint.KindNone;
            Hint("缓存返回独立副本/" + q.HepaiTile, GuobiaoTips.Describe(q), WaitTileHint.KindRonOnly, "仅点和");
            q.HepaiLimit = 1;
            Hint("双方满足起和/" + q.HepaiTile, GuobiaoTips.Describe(q), WaitTileHint.KindRon);
            q.HepaiLimit = 9;
            Hint("双方未起和/" + q.HepaiTile, GuobiaoTips.Describe(q), WaitTileHint.KindNone, "未起和");
            q.HepaiLimit = 8;
        }
        foreach (string sub in new[] { "guobiao/blood_battle", "guobiao/transparent", "guobiao/transparent_realtime" }) {
            chicken.SubRule = sub;
            Hint(sub, GuobiaoTips.Describe(chicken), WaitTileHint.KindRonOnly);
        }
        chicken.SubRule = "guobiao/standard";
        chicken.HuapaiCount = 7;
        chicken.WayToHepai.AddRange(Enumerable.Repeat("花牌", 7)); chicken.MergedWay.AddRange(Enumerable.Repeat("花牌", 7));
        Hint("花牌不凑起和", GuobiaoTips.Describe(chicken), WaitTileHint.KindRonOnly);
        var sanma = Query(new[] {25,25}, new[] {"k19","k45","s24","s37"}, 25, GuobiaoGameState.SanmaSubRule, 8, true);
        Hint("三人国标全求人点和九番自摸五番", GuobiaoTips.Describe(sanma), WaitTileHint.KindRonOnly);
        begging.SubRule = "guobiao/xiaolin";
        Hint("小林自定义八番起和", GuobiaoTips.Describe(begging), WaitTileHint.KindRonOnly);
        begging.HepaiLimit = 1;
        Hint("小林默认起和不误标", GuobiaoTips.Describe(begging), WaitTileHint.KindRon);
        var blue = Query(new[] {37,37}, new[] {"k14","k45","k21","s16"}, 37, "guobiao/lanshi", 5, true);
        Hint("蓝十点和五分自摸四分", GuobiaoTips.Describe(blue), WaitTileHint.KindRonOnly);
        var onlySelf = Query(new[] {11,11,13,14,15,23,23,24,24,25,25,32,33,34}, Array.Empty<string>(), 34, "guobiao/lanshi", 5);
        Hint("仅自摸保持不变", GuobiaoTips.Describe(onlySelf), WaitTileHint.KindTsumoOnly, "仅自摸");
        begging.SubRule = "guobiao/kshen"; begging.HepaiLimit = 8;
        Hint("K神不沿用国标全求人番数", GuobiaoTips.Describe(begging), WaitTileHint.KindNone);

        foreach (string sub in new[] { GuobiaoGameState.FourJokersSubRule, GuobiaoGameState.HuahunSubRule }) {
            begging.SubRule = sub;
            Hint(sub, GuobiaoJokerLocal.Describe(begging), WaitTileHint.KindRonOnly);
        }
        var sixteen = Query(new[] {25,25}, new[] {"s12","s24","s36","s17","s27"}, 25, GuobiaoGameState.SixteenSubRule, 8, true);
        Hint("十六张全求人", GuobiaoSixteenLocal.Describe(sixteen), WaitTileHint.KindRonOnly);
        var joker = Query(new[] {25,59}, new[] {"s12","s24","s36","s17"}, 59, GuobiaoGameState.FourJokersSubRule, 1, true);
        Hint("癞子只能自摸", GuobiaoJokerLocal.Describe(joker), WaitTileHint.KindTsumoOnly);

        sixteen.SubRule = "taiwan/standard"; sixteen.HepaiLimit = 2;
        Hint("台湾全求人二台自摸一台", TaiwanTips.Describe(sixteen), WaitTileHint.KindRonOnly);
        var taiwan = Query(new[] {35,36,37,25,25}, new[] {"s12","s24","s17","s27"}, 37, "taiwan/standard", 2);
        Hint("台湾平胡二台自摸一台", TaiwanTips.Describe(taiwan), WaitTileHint.KindRonOnly);
        taiwan.DetailedConfig = new Dictionary<string,object> { { "all_chows_self_draw_allowed", true } };
        Hint("台湾允许自摸平胡后双方均可", TaiwanTips.Describe(taiwan), WaitTileHint.KindRon);
        taiwan.DetailedConfig = null; taiwan.HepaiLimit = 0;
        Hint("台湾零台起和不误标", TaiwanTips.Describe(taiwan), WaitTileHint.KindRon);

        var hk = new HongKongLocal(new List<string>{"g19","s24","s36","k17"}, new JObject {
            ["profile"] = HongKongGameState.QingzhangRemix, ["seat"] = 0, ["round_wind"] = 41, ["dealer_streak"] = 2 });
        var hkRow = hk.Describe(new List<int>{41,41},41).ToObject<HongKongWait>();
        Check("港麻实际资格为点和三十分自摸未起和", hkRow.ron && !hkRow.self_draw && hkRow.ron_fan == 30);
        Hint("港麻显示仅点和", HongKongGameState.DescribeWait(hkRow,HongKongGameState.QingzhangRemix), WaitTileHint.KindRonOnly);

        var record = new RecordTipsContext { RoomRule="guobiao", SubRule="guobiao/standard", HepaiLimit=8, CurrentRound=1,
            SelfPlayerIndex=0, PlayersByPosition=new Dictionary<string,RecordTipsPlayerVisible> {
                {"self",new RecordTipsPlayerVisible {CombinationTiles=new List<string>{"s12"},DiscardTiles=new List<int>()}} } };
        var queries = RecordWaitHintCalculator.BuildQueries(record,new List<int>{23,24,25,35,36,17,17,17,42,42},new List<int>{34,37},47);
        Hint("牌谱和试切预览使用相同资格", GuobiaoTips.Describe(queries.Single(q=>q.HepaiTile==37)), WaitTileHint.KindRonOnly);

        const string probeRule="ron_only_validation";
        WaitTileHint dangerHint=WaitTileHint.RonOnly();
        var registry=(Dictionary<string,RuleManifest>)typeof(RuleRegistry).GetField("ManifestsByRule",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        try {
            RuleRegistry.Register(new RuleManifest { RuleId=probeRule, DefaultSubRule=probeRule, RecordDangerUsesWaitHint=true,
                Tingpai=q=>new HashSet<int>{25}, DescribeWaitingTile=q=>dangerHint });
            var players=new Dictionary<string,GameRecordManager.RecordPlayer> {{"self",new GameRecordManager.RecordPlayer {
                tileList=new List<int>{25}, combinationTiles=new List<string>{"s12","s24","s36","s17"} }}};
            Check("仅点和仍属于放铳危险牌", RecordChongHintCalculator.ComputeDangerTiles(players,probeRule).Contains(25));
            dangerHint=WaitTileHint.TsumoOnly("仅自摸");
            Check("仅自摸不属于放铳危险牌", !RecordChongHintCalculator.ComputeDangerTiles(players,probeRule).Contains(25));
        } finally { registry.Remove(probeRule); }
        return new { passed=checks.Count,checks };
    }
}
#endif
