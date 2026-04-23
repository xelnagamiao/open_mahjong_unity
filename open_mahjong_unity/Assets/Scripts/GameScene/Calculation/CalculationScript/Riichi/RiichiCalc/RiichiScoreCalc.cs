using System;

namespace Riichi {
    /// <summary>
    /// 立直麻将点数计算。
    /// base = fu * 2^(han+2)，非役满时对 mangan/haneman/baiman/sanbaiman 封顶。
    /// 役满：8000 base × 倍数。
    /// 返回和牌者从三家收到的总分（不含本场/供托）。
    /// </summary>
    public static class RiichiScoreCalc {
        public static int CalculateTotalScore(int han, int fu, bool isDealer, bool isTsumo, int yakumanMultiplier) {
            int basePoints = yakumanMultiplier > 0
                ? 8000 * yakumanMultiplier
                : GetBasePoints(han, fu);

            if (isDealer) {
                if (isTsumo) return CeilTo100(basePoints * 2) * 3;
                return CeilTo100(basePoints * 6);
            }
            if (isTsumo) return CeilTo100(basePoints * 2) * 2 + CeilTo100(basePoints);
            return CeilTo100(basePoints * 4);
        }

        public static int GetBasePoints(int han, int fu) {
            if (han >= 13) return 8000;        // 数役满
            if (han >= 11) return 6000;        // 三倍满
            if (han >= 8) return 4000;         // 倍满
            if (han >= 6) return 3000;         // 跳满
            int basePoints = fu * (int)Math.Pow(2, han + 2);
            if (han >= 5 || basePoints > 2000) return 2000; // 满贯
            return basePoints;
        }

        private static int CeilTo100(int value) {
            int r = value % 100;
            return r == 0 ? value : value + (100 - r);
        }
    }
}
