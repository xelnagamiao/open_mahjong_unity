using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>推倒和基础提示回归；不读取活动对局，不模拟声明、过水或临时加番。</summary>
public static class TuidaoTipsValidation {
    public static object RunAll() {
        int checks = 0;
        List<int> H(params int[] tiles) => tiles.ToList();
        List<string> M(params string[] codes) => codes.ToList();
        List<int> P(int a, int b, int c, int d, int pair) =>
            new[] { a, b, c, d }.SelectMany(tile => Enumerable.Repeat(tile, 3)).Concat(new[] { pair, pair }).ToList();
        void Expect(bool condition, string message) {
            if (!condition) throw new Exception("推倒和提示：" + message);
            checks++;
        }
        var plain = H(11,12,13,14,15,16,21,22,23,34,35,36,28,28);
        var cases = new (string name, List<int> hand, List<string> melds, int tile, int ron, int tsumo)[] {
            ("门清平和", plain, M(), 28, 4, 4),
            ("副露平和", H(14,15,16,21,22,23,34,35,36,28,28), M("s12"), 28, 2, 2),
            ("合法零番", H(14,15,16,21,22,23,34,35,36,41,41), M("s12"), 41, 0, 0),
            ("断幺平和", H(22,23,24,25,26,27,32,33,34,35,36,37,28,28), M(), 28, 6, 6),
            ("全带幺", H(11,12,13,17,18,19,21,22,23,41,41,41,47,47), M(), 47, 8, 8),
            ("大吊车", H(28,28), M("s12","s15","s22","s35"), 28, 8, 8),
            ("混一色清龙", H(11,12,13,14,15,16,17,18,19,41,41,41,47,47), M(), 47, 16, 16),
            ("清龙", H(11,12,13,14,15,16,17,18,19,21,21,21,32,32), M(), 32, 10, 10),
            ("七对", H(11,11,22,22,33,33,44,44,45,45,46,46,47,47), M(), 47, 16, 16),
            ("豪华七对", H(11,11,11,11,22,22,33,33,44,44,45,45,46,46), M(), 46, 24, 24),
            ("全不靠", H(11,14,17,22,25,28,33,36,41,42,43,44,45,46), M(), 46, 8, 8),
            ("十三幺", H(11,19,21,29,31,39,41,42,43,44,45,46,47,47), M(), 47, 32, 32),
            ("清一色平和清龙", H(11,12,13,14,15,16,17,18,19,12,13,14,18,18), M(), 18, 28, 28),
            ("清一色七对封顶", H(11,11,12,12,13,13,14,14,15,15,16,16,19,19), M(), 19, 32, 32),
            ("字一色七对封顶", H(41,41,42,42,43,43,44,44,45,45,46,46,47,47), M(), 47, 32, 32),
            ("大三元封顶", H(45,45,45,46,46,46,47,47,47,11,12,13,22,22), M(), 22, 32, 32),
            ("大四喜不重复碰碰和", P(41,42,43,44,47), M(), 47, 32, 32),
            ("四暗刻单骑点和", P(11,22,33,44,47), M(), 47, 32, 32),
            ("点和补刻与自摸不同", P(11,22,33,44,47), M(), 44, 8, 32),
            ("暗杠仍是暗刻", H(22,22,22,33,33,33,44,44,44,47,47), M("G11"), 47, 32, 32),
            ("明杠不算暗刻", H(22,22,22,33,33,33,44,44,44,47,47), M("g11"), 47, 6, 6),
            ("多拆分取高番", P(11,12,13,14,15), M(), 15, 32, 32),
        };
        foreach (var row in cases) {
            int[] before = row.hand.ToArray();
            string[] callsBefore = row.melds.ToArray();
            var ron = TuidaoHandCalculator.Score(row.hand, row.melds, row.tile, false);
            var tsumo = TuidaoHandCalculator.Score(row.hand, row.melds, row.tile, true);
            Expect(ron != null && ron.Fan == row.ron && ron.Points == row.ron + 2, row.name + "点和基本分");
            Expect(tsumo != null && tsumo.Fan == row.tsumo && tsumo.Points == row.tsumo + 2, row.name + "自摸基本分");
            var query = new WaitHintQuery { HandWithWin = row.hand, Melds = row.melds, HepaiTile = row.tile };
            string label = $"基础{row.ron}番{row.ron + 2}分";
            if (row.ron != row.tsumo) label += $"\n自摸{row.tsumo}番{row.tsumo + 2}分";
            var live = TuidaoHandCalculator.Describe(query);
            Expect(live.Kind == WaitTileHint.KindRon && live.Label == label, row.name + "实战提示");
            var waitingHand = new List<int>(row.hand);
            waitingHand.Remove(row.tile);
            Expect(TuidaoHandCalculator.Waits(waitingHand, row.melds).Contains(row.tile), row.name + "基础听口");
            var ctx = new RecordTipsContext {
                RoomRule = "guangdong", SubRule = "guangdong/tuidao_mil2024", CurrentRound = 1,
                SelfPlayerIndex = 1, SelfHuapaiList = new List<int>(), SelfCombinationMasks = new List<int[]>(),
                PlayersByPosition = new Dictionary<string, RecordTipsPlayerVisible> {
                    { "self", new RecordTipsPlayerVisible { DiscardTiles = new List<int>(), CombinationTiles = row.melds } }
                }
            };
            var recordQuery = RecordWaitHintCalculator.BuildQueries(ctx, waitingHand, new List<int> { row.tile }).Single();
            var record = TuidaoHandCalculator.Describe(recordQuery);
            Expect(record.Kind == live.Kind && record.Label == live.Label, row.name + "牌谱同口径");
            // 提示只采用基础牌型，查询中的临时条件和声明不改变这个口径。
            query.MergedWay = new List<string> { "报听", "天听", "抢杠", "海底", "杠上开花", "天和" };
            ctx.ReadyQualification = "heavenly";
            query.Record = ctx;
            Expect(TuidaoHandCalculator.Describe(query).Label == live.Label, row.name + "基础口径不引入偶然加番");
            Expect(row.hand.SequenceEqual(before) && row.melds.SequenceEqual(callsBefore), row.name + "查询不能改动手牌");
        }
        var four = TuidaoHandCalculator.Score(P(11,22,33,44,47), M(), 47, false);
        Expect(four.FanNames.Contains("四暗刻") && !four.FanNames.Contains("碰碰和") && !four.FanNames.Contains("门清"), "四暗刻不重复计番");
        var luxury = TuidaoHandCalculator.Score(cases[9].hand, M(), 46, false);
        Expect(luxury.FanNames.SequenceEqual(new[] { "豪华七对" }), "豪华七对不重复计七对或门清");
        var honors = TuidaoHandCalculator.Score(P(41,42,43,44,47), M(), 47, false);
        Expect(honors.RawFan > 32 && honors.Fan == 32 && honors.Points == 34 && !honors.FanNames.Contains("全带幺"), "叠加封顶且字一色不重复全带幺");
        Expect(TuidaoHandCalculator.Score(plain, null, 28, false)?.Fan == 4, "null副露按门清处理");
        Expect(TuidaoHandCalculator.Score(plain, M(), 19, false) == null, "不在手牌内的和牌张");
        Expect(TuidaoHandCalculator.Describe(null).Label == "未起和", "空查询不属于硬条件未满足");
        Expect(TuidaoHandCalculator.Describe(new WaitHintQuery { HandWithWin = H(11,11), Melds = M(), HepaiTile = 11 }).Kind == WaitTileHint.KindNone, "不成和不给绿色提示");
        Expect(TuidaoHandCalculator.Describe(new WaitHintQuery { HandWithWin = H(11,11), Melds = M(), HepaiTile = 11 }).Label == "未起和", "未成和不属于硬条件未满足");
        return new { passed = true, cases = cases.Length, checks };
    }
}
