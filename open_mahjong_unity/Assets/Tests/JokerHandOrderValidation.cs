#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

/// <summary>手牌表现排序回归；不进入场景、不改服务器手牌或玩家设置。</summary>
public static class JokerHandOrderValidation {
    public static string Run() {
        int checks = 0;
        void Check(bool ok, string label) {
            if (!ok) throw new Exception("JokerHandOrder: " + label);
            checks++;
        }
        var saved = new List<Action>();
        void SaveProperty(Type type, string name) {
            var property = type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            object value = property.GetValue(null);
            saved.Add(() => property.SetValue(null, value));
        }
        void Set(Type type, string name, object value) => type.GetProperty(name).SetValue(null, value);
        foreach (string name in new[] { "Current", "CurrentSubRule", "ActiveGameState" }) SaveProperty(typeof(RuleRegistry), name);
        SaveProperty(typeof(NanchangTableIndicators), "DisplayedInfo");
        SaveProperty(typeof(WenzhouTableIndicators), "DisplayedInfo");
        SaveProperty(typeof(WuhanPresentation), "DisplayedInfo");
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var groups = (int[])typeof(TileIdOrder).GetField("groupRank", flags).GetValue(null);
        var oldGroups = (int[])groups.Clone();
        var dragon = typeof(TileIdOrder).GetField("dragonOrderMode", flags);
        var riichiDragon = typeof(TileIdOrder).GetField("riichiDragonOrderMode", flags);
        object oldDragon = dragon.GetValue(null), oldRiichiDragon = riichiDragon.GetValue(null);
        try {
            // EditMode does not run SubsystemRegistration; register the production manifests.
            foreach (Type type in new[] { typeof(HangzhouRuleBootstrap), typeof(HongzhongRuleBootstrap),
                typeof(NanchangRuleBootstrap), typeof(WenzhouRuleBootstrap), typeof(WuhanRuleBootstrap), typeof(TuidaoRuleBootstrap) })
                type.GetMethod("Register", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, null);
            Set(typeof(NanchangTableIndicators), "DisplayedInfo", new NanchangInfo { indicator = 47, secondary = 46 });
            Set(typeof(WenzhouTableIndicators), "DisplayedInfo", new WenzhouInfo { caishen = 28, white_natural = 28 });
            Set(typeof(WuhanPresentation), "DisplayedInfo", new WuhanInfo { joker = 39, skins = new[] {37,38,45} });
            var cases = new[] {
                (rule: "hangzhou", sub: "hangzhou/mil2025", jokers: new[] {46}),
                (rule: "hongzhong", sub: "hongzhong/mil2024", jokers: new[] {45}),
                (rule: "nanchang", sub: "nanchang/mil2026", jokers: new[] {47,46}),
                (rule: "wenzhou", sub: "wenzhou/mil2024", jokers: new[] {28}),
                (rule: "wuhan", sub: "wuhan/mil2025", jokers: new[] {39}),
                (rule: "guangdong", sub: "guangdong/mil2023", jokers: new[] {55,56,57,58}),
            };
            int[] sample = { 11, 29, 46, 55, 39, 28, 45, 47, 12, 58, 56, 57, 46, 28, 38 };
            foreach (var test in cases) {
                var manifest = RuleRegistry.Resolve(test.rule, test.sub);
                Check(manifest != null, test.rule + " registered");
                Check(new HashSet<int>(manifest.HandJokerTiles(test.sub)).SetEquals(test.jokers), test.rule + " physical identities");
                Set(typeof(RuleRegistry), "Current", manifest);
                Set(typeof(RuleRegistry), "CurrentSubRule", test.sub);
                for (int suit = 0; suit < 6; suit++) for (int honor = 0; honor < 4; honor++) for (int d = 0; d < 6; d++) {
                    TileIdOrder.SetSortRule(suit, honor, d, 2);
                    var hand = (int[])sample.Clone();
                    TileIdOrder.SortHand(hand);
                    int count = sample.Count(test.jokers.Contains);
                    Check(hand.Take(count).All(test.jokers.Contains) && hand.Skip(count).All(t => !test.jokers.Contains(t)), test.rule + " joker prefix");
                    Check(hand.Skip(count).SequenceEqual(sample.Where(t => !test.jokers.Contains(t)).OrderBy(t => t, TileIdOrder.Comparer)), test.rule + " ordinary preference");
                    Check(hand.OrderBy(t => t).SequenceEqual(sample.OrderBy(t => t)), test.rule + " physical multiset");
                    var again = (int[])hand.Clone(); TileIdOrder.SortHand(again);
                    Check(again.SequenceEqual(hand), test.rule + " idempotence");
                    var objects = sample.Select((id, index) => new KeyValuePair<int,int>(index,id)).ToList();
                    TileIdOrder.SortHand(objects, value => value.Value);
                    Check(objects.Select(value => value.Value).SequenceEqual(hand), test.rule + " object overload");
                    var list = sample.ToList(); TileIdOrder.SortHand(list);
                    Check(list.SequenceEqual(hand), test.rule + " list overload");
                }
            }
            TileIdOrder.SetSortRule(0, 0, 0, 2);
            Set(typeof(RuleRegistry), "Current", RuleRegistry.Resolve("wuhan"));
            Set(typeof(RuleRegistry), "CurrentSubRule", "wuhan/mil2025");
            var oldComparer = TileIdOrder.HandComparer(sample);
            Set(typeof(WuhanPresentation), "DisplayedInfo", new WuhanInfo());
            Check(oldComparer.Compare(39, 11) < 0, "comparer captures joker identity");
            Check(TileIdOrder.HandComparer(sample).Compare(39, 11) > 0, "unrevealed next hand clears joker");
            Set(typeof(WuhanPresentation), "DisplayedInfo", new WuhanInfo {joker = 19, skins = new[] {17,18,45}});
            Check(TileIdOrder.HandComparer(sample).Compare(19, 11) < 0, "new indicator");
            Check(TileIdOrder.HandComparer(sample).Compare(45, 11) > 0, "Wuhan red skin is not joker");
            Set(typeof(RuleRegistry), "Current", RuleRegistry.Resolve("wenzhou"));
            Check(TileIdOrder.HandComparer(sample).Compare(46, 11) > 0, "Wenzhou white natural substitute is not joker");
            Set(typeof(WenzhouTableIndicators), "DisplayedInfo", new WenzhouInfo {caishen = 46, white_natural = 46});
            Check(TileIdOrder.HandComparer(sample).Compare(46, 11) < 0, "Wenzhou white can be real joker");
            Set(typeof(RuleRegistry), "Current", RuleRegistry.Resolve("guangdong"));
            Set(typeof(RuleRegistry), "CurrentSubRule", "guangdong/tuidao");
            Check(TileIdOrder.HandComparer(sample).Compare(55, 11) > 0, "Tuidao has no flower joker");
            Set(typeof(RuleRegistry), "Current", null);
            Set(typeof(RuleRegistry), "CurrentSubRule", null);
            int[] red = {105, 16, 15, 14}; TileIdOrder.SortHand(red);
            Check(red.SequenceEqual(new[] {14,15,105,16}), "red fives keep physical ids and adjacency");
            int[] ordinary = (int[])sample.Clone(); TileIdOrder.SortHand(ordinary);
            Check(ordinary.SequenceEqual(sample.OrderBy(t => t, TileIdOrder.Comparer)), "rule exit has no leaked joker");
            int[] rainbow = {1001, 1131, 1121};
            Check(rainbow.OrderBy(t => t, TileIdOrder.HandComparer(rainbow, new[] {1131})).SequenceEqual(HongqueDisplayOrder.Hand(rainbow)), "Hongque colour circle unchanged");
            Check(TileIdOrder.Compare(46, 11) > 0, "non-hand comparer stays ordinary");
            var historicalRound = new Round {nanchang=new NanchangInfo {indicator=42,secondary=43},
                wenzhou=new WenzhouInfo {caishen=19},actionTicks=new List<List<string>> {new List<string>{"wuhan","indicator","29"}}};
            Check(TileIdOrder.RecordHandJokers("nanchang","nanchang/mil2026",historicalRound).SequenceEqual(new[]{42,43}),"history uses saved Nanchang jing");
            Check(TileIdOrder.RecordHandJokers("wenzhou","wenzhou/mil2024",historicalRound).SequenceEqual(new[]{19}),"history uses saved Wenzhou caishen");
            Check(TileIdOrder.RecordHandJokers("wuhan","wuhan/mil2025",historicalRound).SequenceEqual(new[]{21}),"history uses saved Wuhan indicator");
            Set(typeof(RuleRegistry),"Current",RuleRegistry.Resolve("nanchang"));
            var snapshot=ScoreHistorySettlementHelper.CreateFromShowResult("nanchang/mil2026","hu_self",0,"test",1,new[]{"test"},new[]{11,46,47},null,null,null,null,null);
            Set(typeof(NanchangTableIndicators),"DisplayedInfo",new NanchangInfo{indicator=12,secondary=13});
            Check(snapshot.handJokerTiles.SequenceEqual(new[]{47,46}),"live history keeps immutable joker snapshot across rounds");
            Check(new[]{11,46,47}.OrderBy(t=>t,TileIdOrder.HandComparer(new[]{11,46,47},snapshot.handJokerTiles)).First()!=11,"historical tooltip comparator ignores current round");
            int[] empty = {}; TileIdOrder.SortHand(empty); Check(empty.Length == 0, "empty hand");
            return JsonConvert.SerializeObject(new {passed = true, checks, rules = cases.Select(t => t.rule), preferenceCombinations = 144});
        } finally {
            foreach (Action restore in saved) restore();
            Array.Copy(oldGroups, groups, groups.Length);
            dragon.SetValue(null, oldDragon); riichiDragon.SetValue(null, oldRiichiDragon);
        }
    }
}
#endif
