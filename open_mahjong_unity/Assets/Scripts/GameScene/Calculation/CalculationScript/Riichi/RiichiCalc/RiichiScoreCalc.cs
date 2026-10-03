using System;

namespace Riichi {
    /// <summary>
    /// 立直麻将点数计算。
    /// base = fu * 2^(han+2)，非役满时对 mangan/haneman/baiman/sanbaiman 封顶。
    /// 役满：8000 base × 倍数。
    /// 返回和牌者从三家收到的总分（不含本场/供托）。
    /// </summary>
    public static class RiichiScoreCalc {
        public static int CalculateTotalScore(int han, int fu, bool isDealer, bool isTsumo, int yakumanMultiplier, bool kiriage = false, string kazoe = "yakuman") {
            int basePoints = yakumanMultiplier > 0
                ? 8000 * yakumanMultiplier
                : GetBasePoints(han, fu, kiriage, kazoe);

            if (isDealer) {
                if (isTsumo) return CeilTo100(basePoints * 2) * 3;
                return CeilTo100(basePoints * 6);
            }
            if (isTsumo) return CeilTo100(basePoints * 2) + CeilTo100(basePoints) * 2;
            return CeilTo100(basePoints * 4);
        }

        /// <summary>
        /// 结算面板「xx点」：番型收分，不含本场棒/场供立直棒。
        /// 有番符时按番符重算；错和或番符缺失时从和牌者总分剥离场供与本场。
        /// </summary>
        public static int ResolveDisplayPoints(
            int han,
            int fu,
            bool isDealer,
            bool isTsumo,
            int winnerScoreDelta,
            int honba,
            int riichiSticksCollected,
            string[] yaku = null, bool kiriage = false, string kazoe = "yakuman") {
            if (ContainsCuohe(yaku)) {
                return StripFieldBonuses(winnerScoreDelta, isTsumo, honba, riichiSticksCollected);
            }
            if (han > 0 && fu > 0) {
                bool trueYakuman = yaku != null && Array.Exists(yaku, IsYakumanName);
                int yakumanMult = trueYakuman ? Math.Max(1, han / 13) : 0;
                return CalculateTotalScore(han, fu, isDealer, isTsumo, yakumanMult, kiriage, kazoe);
            }
            return StripFieldBonuses(winnerScoreDelta, isTsumo, honba, riichiSticksCollected);
        }

        /// <summary>从和牌者本笔 score_changes 中去掉场供与本场。</summary>
        public static int StripFieldBonuses(int winnerDelta, bool isTsumo, int honba, int riichiSticksCollected) {
            int points = winnerDelta - Math.Max(0, riichiSticksCollected) * 1000;
            int honbaBonus = Math.Max(0, honba) * 300;
            if (honbaBonus <= 0) {
                return Math.Max(0, points);
            }
            // 自摸必含本场；荣和多家时非首家不含本场，仅当总分足以覆盖时剥离
            if (isTsumo || points >= honbaBonus) {
                points -= honbaBonus;
            }
            return Math.Max(0, points);
        }

        public static int GetBasePoints(int han, int fu, bool kiriage = false, string kazoe = "yakuman") {
            if (han <= 0) return 0;
            if (han >= 13) return kazoe == "sanbaiman" ? 6000 : kazoe == "unlimited" ? 8000 * (han / 13) : 8000;
            if (han >= 11) return 6000;        // 三倍满
            if (han >= 8) return 4000;         // 倍满
            if (han >= 6) return 3000;         // 跳满
            int basePoints = fu * (int)Math.Pow(2, han + 2);
            if (kiriage && basePoints == 1920) return 2000;
            if (han >= 5 || basePoints > 2000) return 2000; // 满贯
            return basePoints;
        }

        private static int CeilTo100(int value) {
            int r = value % 100;
            return r == 0 ? value : value + (100 - r);
        }

        private static bool ContainsCuohe(string[] yaku) {
            if (yaku == null) return false;
            for (int i = 0; i < yaku.Length; i++) {
                if (yaku[i] == "错和") return true;
            }
            return false;
        }
        private static bool IsYakumanName(string name) => name == "天和" || name == "地和" || name == "大三元"
            || name.StartsWith("四暗刻") || name.StartsWith("国士无双") || name == "字一色" || name == "绿一色"
            || name == "清老头" || name == "小四喜" || name == "大四喜" || name == "四杠子"
            || name == "九莲宝灯" || name == "纯正九莲宝灯";
    }
}
