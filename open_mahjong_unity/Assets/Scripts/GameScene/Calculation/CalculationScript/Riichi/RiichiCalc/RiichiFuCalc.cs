using System.Collections.Generic;
using System.Linq;

namespace Riichi {
    /// <summary>
    /// 立直麻将符数计算。
    /// 规则（与 Python mahjong 库一致的核心部分）：
    ///   底符 20
    ///   门前清荣和 +10（门前清且荣和，非平和成立时）
    ///   自摸 +2（非平和自摸）
    ///   七对子 / 国士无双固定 25 符（不再叠加其他符）
    ///   雀头役牌 +2（三元/自风/场风；同为自风+场风双倍 +4）
    ///   刻子/杠：
    ///     明刻中张 2 / 明刻幺九 4
    ///     暗刻中张 4 / 暗刻幺九 8
    ///     明杠中张 8 / 明杠幺九 16
    ///     暗杠中张 16/ 暗杠幺九 32
    ///   待牌：边张/嵌张/单骑 各 +2（两面、双碰 0）
    ///   平和荣和固定 30 符（=20+10）；平和自摸 20 符（门清自摸不加 2）
    ///   其余向上十位取整（ceil 到 10）
    /// </summary>
    public static class RiichiFuCalc {
        public static int Calculate(List<RiichiSet> sets, int winTile, RiichiWaitType wait,
                                     RiichiHandContext ctx, RiichiYakuDetector.DetectResult detect,
                                     HandShape shape) {
            if (shape == HandShape.Chiitoitsu) return 25;
            if (shape == HandShape.Kokushi) return 25;

            bool hasPinfu = detect.Yaku.Any(y => y.Name == "平和");
            if (hasPinfu) return ctx.IsTsumo ? 20 : 30;

            int fu = 20;
            bool isClosed = sets.All(s => !s.Opened);

            if (isClosed && !ctx.IsTsumo) fu += 10;
            if (ctx.IsTsumo) fu += 2;

            var pair = sets.FirstOrDefault(s => s.Type == RiichiSetType.Pair);
            if (pair != null) {
                if (RiichiTileUtil.IsDragon(pair.Tile)) fu += 2;
                if (pair.Tile == ctx.PlayerWind) fu += 2;
                if (pair.Tile == ctx.RoundWind) fu += 2;
            }

            int winNorm = RiichiTileUtil.Normalize(winTile);
            foreach (var s in sets) {
                if (s.Type != RiichiSetType.Pon && s.Type != RiichiSetType.Kan) continue;
                bool yaojiu = RiichiTileUtil.IsYaojiu(s.Tile);
                bool opened = s.Opened;
                // 荣和完成的刻子视为明刻
                if (!ctx.IsTsumo && s.Type == RiichiSetType.Pon && s.Tile == winNorm && !opened) {
                    opened = true;
                }
                if (s.Type == RiichiSetType.Pon) {
                    fu += opened ? (yaojiu ? 4 : 2) : (yaojiu ? 8 : 4);
                } else {
                    bool closedKan = s.IsClosedKan;
                    if (closedKan) fu += yaojiu ? 32 : 16;
                    else fu += yaojiu ? 16 : 8;
                }
            }

            if (wait == RiichiWaitType.Tanki || wait == RiichiWaitType.Kanchan || wait == RiichiWaitType.Penchan) {
                fu += 2;
            }

            // 门前清副露单吊特殊：开局只有副露+雀头，荣和只加 10 + 2 符 = 30 最低
            if (!isClosed && !ctx.IsTsumo && fu == 20) fu = 30;

            // 向上取整到 10
            int remainder = fu % 10;
            if (remainder != 0) fu += (10 - remainder);
            if (fu < 20) fu = 20;
            return fu;
        }
    }
}
