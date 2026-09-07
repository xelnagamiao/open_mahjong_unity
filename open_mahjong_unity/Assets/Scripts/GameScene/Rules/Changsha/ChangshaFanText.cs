using System.Collections.Generic;

/// <summary>长沙番文本：小胡/大胡分类 + 鸟牌/中鸟条目的牌名格式化。</summary>
internal static class ChangshaFanText {
    public static readonly Dictionary<string, string> FanToDisplayChangsha = new Dictionary<string, string> {
        {"小胡", "基础"},
        {"碰碰胡", "大胡"},
        {"将将胡", "大胡"},
        {"清一色", "大胡"},
        {"全求人", "大胡"},
        {"七小对", "大胡"},
        {"豪华七小对", "大胡"},
        {"天胡", "大胡"},
        {"地胡", "大胡"},
        {"海底", "大胡"},
        {"杠上开花", "大胡"},
        {"杠上炮", "大胡"},
        {"抢杠胡", "大胡"},
    };

    public static string FormatChangshaTileName(int tileId) {
        int suit = tileId / 10;
        int rank = tileId % 10;
        string rankName = rank switch {
            1 => "一",
            2 => "二",
            3 => "三",
            4 => "四",
            5 => "五",
            6 => "六",
            7 => "七",
            8 => "八",
            9 => "九",
            _ => null,
        };
        string suitName = suit switch {
            1 => "万",
            2 => "筒",
            3 => "条",
            _ => null,
        };
        return rankName != null && suitName != null ? $"{rankName}{suitName}" : tileId.ToString();
    }

    public static bool IsChangshaTileId(int tileId) {
        int suit = tileId / 10;
        int rank = tileId % 10;
        return suit >= 1 && suit <= 3 && rank >= 1 && rank <= 9;
    }

    public static bool IsChangshaRule(string rule) {
        return !string.IsNullOrEmpty(rule)
            && rule.StartsWith("changsha", System.StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParseChangshaTileId(string text, out int tileId) {
        tileId = 0;
        if (string.IsNullOrEmpty(text)) return false;
        string item = text.Trim();
        int equalsIndex = item.IndexOf('=');
        if (equalsIndex >= 0) item = item.Substring(0, equalsIndex);
        if (int.TryParse(item, out int parsed) && IsChangshaTileId(parsed)) {
            tileId = parsed;
            return true;
        }
        if (item.Length != 2) return false;
        int rank = item[0] switch {
            '一' => 1, '二' => 2, '三' => 3, '四' => 4, '五' => 5,
            '六' => 6, '七' => 7, '八' => 8, '九' => 9, _ => 0,
        };
        int suit = item[1] switch {
            '万' => 1, '筒' => 2, '条' => 3, _ => 0,
        };
        if (rank == 0 || suit == 0) return false;
        tileId = suit * 10 + rank;
        return true;
    }

    /// <summary>长沙扎鸟指示牌：只读协议 / 牌谱 tick 里的 ID，不再从「鸟牌:」番种反解。</summary>
    public static int[] ResolveBirdTiles(IReadOnlyList<int> recordedBirds) {
        List<int> birds = CopyValidBirds(recordedBirds);
        return birds.Count == 0 ? null : birds.ToArray();
    }

    private static List<int> CopyValidBirds(IReadOnlyList<int> tiles) {
        var result = new List<int>();
        if (tiles == null) return result;
        for (int i = 0; i < tiles.Count; i++) {
            if (IsChangshaTileId(tiles[i])) result.Add(tiles[i]);
        }
        return result;
    }

    public static string FormatChangshaBirdFanName(string fanName) {
        if (string.IsNullOrEmpty(fanName)) return fanName;
        string prefix = null;
        string payload = null;
        if (fanName.StartsWith("鸟牌:")) {
            prefix = "鸟牌:";
            payload = fanName.Substring("鸟牌:".Length);
        } else if (fanName.StartsWith("中鸟:")) {
            prefix = "中鸟:";
            payload = fanName.Substring("中鸟:".Length);
        }
        if (prefix == null || string.IsNullOrEmpty(payload) || payload == "无") return fanName;

        string[] parts = payload.Split(',');
        for (int i = 0; i < parts.Length; i++) {
            string item = parts[i].Trim();
            int equalsIndex = item.IndexOf('=');
            if (equalsIndex >= 0) item = item.Substring(0, equalsIndex);
            if (int.TryParse(item, out int tileId)) {
                parts[i] = FormatChangshaTileName(tileId);
            } else {
                parts[i] = item;
            }
        }
        return prefix + string.Join(",", parts);
    }

    public static bool IsBirdEntry(string fanName) {
        return !string.IsNullOrEmpty(fanName)
            && (fanName.StartsWith("鸟牌:")
                || fanName.StartsWith("中鸟:")
                || fanName.StartsWith("中鸟x")
                || fanName.StartsWith("扎鸟倍数:"));
    }

    public static string FanName(string subRule, string fanName) => FormatChangshaBirdFanName(fanName);

    /// <summary>总计栏：长沙按分计，两栏都是"x分"。</summary>
    public static SettlementTotalDisplay SettlementTotal(SettlementTotalQuery q) {
        return new SettlementTotalDisplay { FanText = $"{q.HuScore}分", ScoreText = $"{q.HuScore}分" };
    }

    public static string FanValue(string subRule, string fanName) {
        if (IsBirdEntry(fanName)) return "结算";
        return FanToDisplayChangsha.TryGetValue(fanName, out string display) ? display : "0番";
    }

    public static string ScoreboardFanText(SettlementTotalQuery q) => $"{q.HuScore}分";

    public static string RoundName(int round) => $"第{round}局";

    /// <summary>长沙房间 game_round 为风圈数，展示成实际局数。</summary>
    public static string MaxRoundText(int gameRound) {
        switch (gameRound) {
            case 1: return "4局";
            case 2: return "8局";
            case 4: return "16局";
            default: return $"未知({gameRound})";
        }
    }
}
