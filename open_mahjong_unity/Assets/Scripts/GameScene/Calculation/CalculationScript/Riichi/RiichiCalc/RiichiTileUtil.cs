using System.Collections.Generic;

namespace Riichi {
    /// <summary>
    /// 立直麻将牌 id 工具。
    /// 牌 id 约定：
    ///   11-19 万 / 21-29 饼 / 31-39 条
    ///   41 东 / 42 南 / 43 西 / 44 北
    ///   45 中 / 46 白 / 47 发
    ///   105 / 205 / 305 = 赤 5m / 赤 5p / 赤 5s（点数按普通 5 结算，额外计 赤宝牌 han）
    /// </summary>
    public static class RiichiTileUtil {
        public static readonly HashSet<int> Terminals = new HashSet<int> { 11, 19, 21, 29, 31, 39 };
        public static readonly HashSet<int> Honors = new HashSet<int> { 41, 42, 43, 44, 45, 46, 47 };
        public static readonly HashSet<int> Winds = new HashSet<int> { 41, 42, 43, 44 };
        public static readonly HashSet<int> Dragons = new HashSet<int> { 45, 46, 47 };
        public static readonly HashSet<int> Yaojiu;
        public static readonly HashSet<int> GreenTiles = new HashSet<int> { 32, 33, 34, 36, 38, 47 };

        public const int East = 41;
        public const int South = 42;
        public const int West = 43;
        public const int North = 44;
        public const int Chun = 45;
        public const int Haku = 46;
        public const int Hatsu = 47;

        static RiichiTileUtil() {
            Yaojiu = new HashSet<int>(Terminals);
            foreach (int h in Honors) Yaojiu.Add(h);
        }

        public static int Normalize(int tileId) {
            if (tileId == 105) return 15;
            if (tileId == 205) return 25;
            if (tileId == 305) return 35;
            return tileId;
        }

        public static bool IsRedFive(int tileId) {
            return tileId == 105 || tileId == 205 || tileId == 305;
        }

        /// <summary>
        /// 数牌花色：0 万 / 1 饼 / 2 条；字牌返回 -1。
        /// </summary>
        public static int Suit(int tileId) {
            int t = Normalize(tileId);
            if (t >= 11 && t <= 19) return 0;
            if (t >= 21 && t <= 29) return 1;
            if (t >= 31 && t <= 39) return 2;
            return -1;
        }

        public static int NumberInSuit(int tileId) {
            int t = Normalize(tileId);
            if (t >= 11 && t <= 39) return t % 10;
            return 0;
        }

        public static bool IsTerminal(int tileId) {
            return Terminals.Contains(Normalize(tileId));
        }

        public static bool IsHonor(int tileId) {
            return Honors.Contains(Normalize(tileId));
        }

        public static bool IsWind(int tileId) {
            return Winds.Contains(Normalize(tileId));
        }

        public static bool IsDragon(int tileId) {
            return Dragons.Contains(Normalize(tileId));
        }

        public static bool IsYaojiu(int tileId) {
            return Yaojiu.Contains(Normalize(tileId));
        }

        /// <summary>
        /// 根据宝牌指示牌计算宝牌本体：
        ///   数牌：n → n+1（9→1，按花色循环）
        ///   风牌：东→南→西→北→东
        ///   三元：白→发→中→白（对应本项目 id：46→47→45→46）
        /// </summary>
        public static int DoraFromIndicator(int indicatorId) {
            int t = Normalize(indicatorId);
            if (t >= 11 && t <= 19) return 11 + (t - 11 + 1) % 9;
            if (t >= 21 && t <= 29) return 21 + (t - 21 + 1) % 9;
            if (t >= 31 && t <= 39) return 31 + (t - 31 + 1) % 9;
            if (t >= 41 && t <= 44) return 41 + (t - 41 + 1) % 4;
            if (t == Haku) return Hatsu;
            if (t == Hatsu) return Chun;
            if (t == Chun) return Haku;
            return t;
        }

        public static bool IsGreen(int tileId) {
            return GreenTiles.Contains(Normalize(tileId));
        }

        public static List<int> TilesFromMask(int[] mask) {
            var list = new List<int>();
            if (mask == null) return list;
            for (int i = 1; i < mask.Length; i += 2) {
                if (mask[i] >= 10) list.Add(mask[i]);
            }
            return list;
        }

        public static int CountAkaInTiles(IEnumerable<int> tiles) {
            int count = 0;
            foreach (int t in tiles) {
                if (IsRedFive(t)) count++;
            }
            return count;
        }
    }
}
