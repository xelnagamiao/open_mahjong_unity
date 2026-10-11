using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>中庸提示回归：对应服务端中庸 scoring.py 的代表牌例。</summary>
public static class ZhongyongTipsValidation {
    public static object RunAll() {
        int checks = 0;
        List<int> H(params int[] tiles) => tiles.ToList();
        List<string> M(params string[] codes) => codes.ToList();
        var cases = new (List<int> hand, List<string> melds, int tile, int seat, int ron, int tsumo)[] {
            (H(11,12,13,24,25,26,37,37), M("k19","s32"), 37, 0, 1, 1),
            (H(11,11,11,11,22,22,25,25,37,37,41,41,47,47), M(), 47, 0, 30, 30),
            (H(12,12,12,15,15,15,23,23,23,37,37,37,41,41), M(), 37, 0, 65, 160),
            (H(41,41,41,12,13,14,25,26,27,36,37,38,29,29), M(), 29, 0, 15, 15),
            (H(41,41,41,12,13,14,25,26,27,36,37,38,29,29), M(), 29, 1, 5, 5),
            (H(41,41), M("G11","g22","g33","G45"), 41, 0, 480, 480),
            (H(11,11,11,12,13,14,15,16,17,18,19,19,19,15), M(), 15, 0, 480, 480),
            (H(12,12,12,13,13,13,14,14,14,15,15,15,16,16), M(), 16, 0, 320, 320),
        };
        foreach (var c in cases) {
            var before = c.hand.ToArray();
            var q = new WaitHintQuery { HandWithWin = c.hand, Melds = c.melds, HepaiTile = c.tile, SelfIndex = c.seat };
            string expected = c.ron + "分" + (c.tsumo != c.ron ? "\n自摸" + c.tsumo + "分" : "");
            var hint = ZhongyongTips.Describe(q);
            if (hint.Label != expected || hint.Kind != WaitTileHint.KindRon) throw new Exception($"中庸提示应为 {expected}，实际 {hint.Label}");
            checks++;
            q.Record = new RecordTipsContext { RoomRule = "zhongyong", SubRule = "zhongyong/standard", SelfPlayerIndex = c.seat };
            if (ZhongyongTips.Describe(q).Label != expected || !before.SequenceEqual(c.hand)) throw new Exception("回放提示或手牌不变性失败");
            checks++;
        }
        if (ZhongyongTips.Points(H(11,11,12,13,14,25,26,27,35,36,37), M("k11"), 11, 41) != 0)
            throw new Exception("不应接受第五张同牌");
        checks++;
        return new { cases = cases.Length, checks, passed = true };
    }
}
