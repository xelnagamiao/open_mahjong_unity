#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

/// <summary>上海与血流普通听牌提示、牌谱提示和点和危险牌回归；不启动对局。</summary>
public static class ShanghaiBloodflowTipsValidation {
    public static string Run() {
        var checks = new List<string>();
        void Check(string name, bool valid) {
            if (!valid) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        var shanghai = new RuleManifest {
            RuleId = "shanghai", DefaultSubRule = "shanghai/qiaoma", DefaultHepaiLimit = 0,
            RecordDangerUsesWaitHint = true,
            Tingpai = q => q.SubRule == "shanghai/qinghunpeng"
                ? QinghunpengHandCalculator.Waits(q.Hand, q.Melds) : ShanghaiHandCalculator.Waits(q.Hand, q.Melds),
            DescribeWaitingTile = q => q.SubRule == "shanghai/qinghunpeng"
                ? QinghunpengHandCalculator.Describe(q) : ShanghaiHandCalculator.Describe(q),
        };
        var sichuan = new RuleManifest {
            RuleId = "sichuan", DefaultSubRule = "sichuan/standard", DefaultHepaiLimit = 0,
            RecordDangerUsesWaitHint = true, Tingpai = SichuanTips.Tingpai, DescribeWaitingTile = SichuanTips.Describe,
        };
        var registry = (Dictionary<string, RuleManifest>)typeof(RuleRegistry)
            .GetField("ManifestsByRule", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var saved = new Dictionary<string, RuleManifest>(registry);
        try {
            RuleRegistry.Register(shanghai);
            RuleRegistry.Register(sichuan);
            var plain = new List<int> { 11,12,13,21,22,23,31,32,33,37,38,39,41 };
            var mixed = new List<int> { 11,12,13,12,13,14,15,16,17,17,18,19,41 };
            var open = new List<int> { 21,22,23,31,32,33,37,38,39,41 };
            var exchange = new List<int> { 11,12,13,14,15,16,21,22,23,24,25,26,27 };
            var discard = new List<int> { 11,12,13,21,22,23,24,25,26,27 };

            RecordTipsContext Context(string subRule, List<string> melds = null, List<int> flowers = null, int minimum = 0, int dingque = 0, int debt = 0) {
                return new RecordTipsContext {
                    RoomRule = subRule.Split('/')[0], SubRule = subRule, HepaiLimit = minimum,
                    CurrentRound = 1, SelfPlayerIndex = 0, SelfDingqueSuit = dingque,
                    SelfHuapaiList = flowers ?? new List<int>(), SelfCombinationMasks = new List<int[]>(),
                    DetailedConfig = new Dictionary<string, object> { { "huangfan_count", debt }, { "xueliu_exchange_scoring", true } },
                    PlayersByPosition = new Dictionary<string, RecordTipsPlayerVisible> {
                        { "self", new RecordTipsPlayerVisible { CombinationTiles = melds ?? new List<string>(), DiscardTiles = new List<int>() } },
                    },
                };
            }
            WaitTileHint Verify(string name, List<int> hand, int tile, RecordTipsContext ctx, string label, string kind) {
                RuleManifest manifest = ctx.RoomRule == "shanghai" ? shanghai : sichuan;
                List<string> melds = ctx.PlayersByPosition["self"].CombinationTiles;
                var waiting = RuleTips.ComputeWaiting(manifest, new TingpaiQuery {
                    Hand = hand, Melds = melds, SubRule = ctx.SubRule, ExcludedSuit = ctx.SelfDingqueSuit, DetailedConfig = ctx.DetailedConfig,
                });
                Check(name + "保留基本听张", waiting.Contains(tile));
                var live = RuleTips.DescribeWaitingTile(manifest, new WaitHintQuery {
                    HandWithWin = new List<int>(hand) { tile }, HepaiTile = tile, Melds = melds,
                    SelfFlowers = ctx.SelfHuapaiList, HepaiLimit = ctx.HepaiLimit,
                    SubRule = ctx.SubRule, ExcludedSuit = ctx.SelfDingqueSuit, DetailedConfig = ctx.DetailedConfig,
                });
                Check(name + "普通提示", live.Kind == kind && live.Label == label);
                var recordQuery = RecordWaitHintCalculator.BuildQueries(ctx, hand, waiting.ToList()).Single(q => q.HepaiTile == tile);
                var record = RuleTips.DescribeWaitingTile(manifest, recordQuery);
                Check(name + "牌谱提示一致", record.Kind == kind && record.Label == label);
                var preview = RecordWaitHintCalculator.BuildQueries(ctx, hand, waiting.ToList(), 28).Single(q => q.HepaiTile == tile);
                var previewHint = RuleTips.DescribeWaitingTile(manifest, preview);
                Check(name + "切牌预览一致", previewHint.Kind == kind && previewHint.Label == label);

                var player = new GameRecordManager.RecordPlayer {
                    playerIndex = 0, tileList = hand, combinationTiles = melds,
                    huapaiList = ctx.SelfHuapaiList, dingqueSuit = ctx.SelfDingqueSuit,
                };
                var players = new Dictionary<string, GameRecordManager.RecordPlayer> { { "right", player } };
                var danger = RecordChongHintCalculator.ComputeDangerTiles(players, ctx.RoomRule, ctx.DetailedConfig, ctx.SubRule, _ => ctx);
                Check(name + "牌山只标普通点和", danger.Contains(tile) == (kind == WaitTileHint.KindRon));
                var handDanger = RecordChongHintCalculator.ComputeRonDangerForHandOwner(players, "self", ctx.RoomRule, ctx.DetailedConfig, ctx.SubRule, _ => ctx);
                Check(name + "手牌只标普通点和", handDanger.Contains(tile) == (kind == WaitTileHint.KindRon));
                Check(name + "危险牌不包含自身听张", RecordChongHintCalculator.ComputeRonDangerForHandOwner(
                    players, "right", ctx.RoomRule, ctx.DetailedConfig, ctx.SubRule, _ => ctx).Count == 0);
                return live;
            }

            Verify("清混碰无起和番", plain, 41, Context("shanghai/qinghunpeng"), "未满足", WaitTileHint.KindNone);
            Verify("清混碰混一色无花", mixed, 41, Context("shanghai/qinghunpeng"), "仅自摸 1花（1分）", WaitTileHint.KindTsumoOnly);
            Verify("清混碰混一色一花", mixed, 41, Context("shanghai/qinghunpeng", flowers: new List<int> { 51 }), "2花（2分）", WaitTileHint.KindRon);
            Verify("清混碰荒番不叠倍数", mixed, 41, Context("shanghai/qinghunpeng", flowers: new List<int> { 51 }, debt: 3), "2花（4分） 荒番", WaitTileHint.KindRon);
            Verify("敲麻花数不足", open, 41, Context("shanghai/qiaoma", new List<string> { "s12" }, new List<int> { 51 }), "未满足", WaitTileHint.KindNone);
            Verify("敲麻二花仅自摸", open, 41, Context("shanghai/qiaoma", new List<string> { "s12" }, new List<int> { 51,52 }), "敲牌后仅自摸 3分", WaitTileHint.KindTsumoOnly);
            Verify("敲麻三花可点和", open, 41, Context("shanghai/qiaoma", new List<string> { "s12" }, new List<int> { 51,52,53 }), "敲牌后 4分", WaitTileHint.KindRon);
            Verify("敲麻一番门槛不足", open, 41, Context("shanghai/qiaoma", new List<string> { "s12" }, new List<int> { 51,52,53 }, minimum: 1), "未起和", WaitTileHint.KindNone);
            Verify("敲麻门清普通估分", plain, 41, Context("shanghai/qiaoma"), "敲牌后 22分", WaitTileHint.KindRon);
            Verify("换三张定缺未清", exchange, 27, Context("sichuan/xueliu_exchange", dingque: 2), "未满足", WaitTileHint.KindNone);
            Verify("换三张普通计番", exchange, 27, Context("sichuan/xueliu_exchange", dingque: 3), "3番", WaitTileHint.KindRon);
            Verify("弃三张点和自摸分开", discard, 27, Context("sichuan/xueliu"), "点和1番\n自摸2番", WaitTileHint.KindRon);
            Verify("弃三张仅自摸过门槛", discard, 27, Context("sichuan/xueliu", minimum: 2), "仅自摸 2番", WaitTileHint.KindTsumoOnly);
            Verify("弃三张均未过门槛", discard, 27, Context("sichuan/xueliu", minimum: 3), "未起和", WaitTileHint.KindNone);
            var threeSuits = new List<int> { 11,12,13,21,22,23,31,32,33,35 };
            Verify("弃三张三门基本听张", threeSuits, 35, Context("sichuan/xueliu"), "未满足", WaitTileHint.KindNone);
            var honorPairs = new List<int> { 41,41,42,42,43,43,44,44,45,45,46,46,47 };
            Verify("清混碰乱风向", honorPairs, 47, Context("shanghai/qinghunpeng"), "20花（20分）", WaitTileHint.KindRon);
            var sevenPairs = new List<int> { 12,12,14,14,16,16,22,22,24,24,26,26,28 };
            Verify("换三张七对", sevenPairs, 28, Context("sichuan/xueliu_exchange", dingque: 3), "7番", WaitTileHint.KindRon);
            var invalid = new List<int> { 11,11,11,11,12,13,21,22,23,31,32,33,41 };
            Check("清混碰不提示第五张同牌", !QinghunpengHandCalculator.Waits(invalid, null).Contains(11));
            Check("清混碰不听牌不显示", QinghunpengHandCalculator.Waits(new List<int> { 11,12,13 }, null).Count == 0);
            var ordinary = Context("sichuan/xueliu_exchange", dingque: 3);
            var incidentalQuery = new WaitHintQuery {
                HandWithWin = new List<int>(exchange) { 27 }, HepaiTile = 27, Melds = new List<string>(),
                SubRule = ordinary.SubRule, ExcludedSuit = 3, DetailedConfig = ordinary.DetailedConfig,
                WayToHepai = new List<string> { "抢杠" }, SingleTileWay = new List<string>(), MergedWay = new List<string> { "抢杠", "点和" },
            };
            Check("偶然番仍不加入普通预估", SichuanTips.Describe(incidentalQuery).Label == "3番");
            return JsonConvert.SerializeObject(new { passed = checks.Count, checks }, Formatting.Indented);
        } finally {
            registry.Clear();
            foreach (var item in saved) registry[item.Key] = item.Value;
        }
    }
}
#endif
