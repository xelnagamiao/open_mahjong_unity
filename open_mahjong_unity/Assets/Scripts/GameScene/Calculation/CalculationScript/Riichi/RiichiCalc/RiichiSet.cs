using System.Collections.Generic;

namespace Riichi {
    public enum RiichiSetType { Chi, Pon, Kan, Pair }

    /// <summary>
    /// 解析后的面子/雀头。Chi 的 Tile 表示顺子中张（如 12 代表 11/12/13）。
    /// </summary>
    public class RiichiSet {
        public RiichiSetType Type;
        public int Tile;
        public bool Opened;
        public bool IsClosedKan;

        public int[] Tiles() {
            switch (Type) {
                case RiichiSetType.Chi: return new[] { Tile - 1, Tile, Tile + 1 };
                case RiichiSetType.Pon: return new[] { Tile, Tile, Tile };
                case RiichiSetType.Kan: return new[] { Tile, Tile, Tile, Tile };
                case RiichiSetType.Pair: return new[] { Tile, Tile };
            }
            return new int[0];
        }

        public bool ContainsYaojiu() {
            foreach (int t in Tiles()) {
                if (RiichiTileUtil.IsYaojiu(t)) return true;
            }
            return false;
        }

        public bool ContainsTerminal() {
            foreach (int t in Tiles()) {
                if (RiichiTileUtil.IsTerminal(t)) return true;
            }
            return false;
        }

        public static List<RiichiSet> ParseCombinationList(List<string> combList) {
            var sets = new List<RiichiSet>();
            foreach (string c in combList) {
                if (string.IsNullOrEmpty(c) || c.Length < 2) continue;
                char sign = c[0];
                if (!int.TryParse(c.Substring(1), out int tile)) continue;
                var set = new RiichiSet { Tile = tile };
                switch (sign) {
                    case 's': set.Type = RiichiSetType.Chi; set.Opened = true; break;
                    case 'S': set.Type = RiichiSetType.Chi; set.Opened = false; break;
                    case 'k': set.Type = RiichiSetType.Pon; set.Opened = true; break;
                    case 'K': set.Type = RiichiSetType.Pon; set.Opened = false; break;
                    case 'g': set.Type = RiichiSetType.Kan; set.Opened = true; set.IsClosedKan = false; break;
                    case 'G': set.Type = RiichiSetType.Kan; set.Opened = false; set.IsClosedKan = true; break;
                    case 'q': set.Type = RiichiSetType.Pair; set.Opened = false; break;
                    default: continue;
                }
                sets.Add(set);
            }
            return sets;
        }
    }

    public enum RiichiWaitType {
        None,
        Tanki,      // 单骑
        Kanchan,    // 嵌张
        Penchan,    // 边张
        Shanpon,    // 双碰
        Ryanmen,    // 两面
    }
}
