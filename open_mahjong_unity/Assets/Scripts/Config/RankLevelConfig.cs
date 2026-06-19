using System.Collections.Generic;

/// <summary>
/// 段位等级与分数阈值配置
/// </summary>
public static class RankLevelConfig {
    public static readonly (string name, int startScore, int promoteScore)[] RankTable = {
        ("10级", 0, 20), ("9级", 0, 20), ("8级", 0, 20), ("7级", 0, 20),
        ("6级", 0, 40), ("5级", 0, 40), ("4级", 0, 60),
        ("3级", 0, 60), ("2级", 0, 80), ("1级", 1, 100),
        ("初段", 200, 400), ("二段", 400, 800), ("三段", 600, 1200),
        ("四段", 800, 1600), ("五段", 1000, 2000), ("六段", 1200, 2400),
        ("七段", 1400, 2800), ("八段", 1600, 3200), ("九段", 2000, 4000),
    };

    public static readonly Dictionary<string, int> RankLevelMap = new Dictionary<string, int> {
        {"10级", 0}, {"9级", 1}, {"8级", 2}, {"7级", 3},
        {"6级", 4}, {"5级", 5}, {"4级", 6}, {"3级", 7}, {"2级", 8},
        {"1级", 9},
        {"初段", 10}, {"二段", 11}, {"三段", 12},
        {"四段", 13}, {"五段", 14}, {"六段", 15},
        {"七段", 16}, {"八段", 17}, {"九段", 18},
    };

    // 场次基础均得分（全庄）
    public static readonly Dictionary<string, float> TierBaseGainPt = new Dictionary<string, float> {
        {"beginner", 30f},
        {"intermediate", 65f},
        {"advanced", 95f},
        {"mcrpl", 135f},
    };

    // 各段位均失pt（三四名扣分基准）
    public static readonly Dictionary<string, float> RankAvgLossPt = new Dictionary<string, float> {
        {"10级", 0f},
        {"9级", 0f},
        {"8级", 0f},
        {"7级", 0f},
        {"6级", 0f},
        {"5级", 0f},
        {"4级", 0f},
        {"3级", 0f},
        {"2级", 15f},
        {"1级", 35f},
        {"初段", 45f},
        {"二段", 55f},
        {"三段", 65f},
        {"四段", 90f},
        {"五段", 100f},
        {"六段", 110f},
        {"七段", 150f},
        {"八段", 175f},
        {"九段", 200f},
    };

    public static int GetRankIndex(string rankName) {
        for (int i = 0; i < RankTable.Length; i++) {
            if (RankTable[i].name == rankName) return i;
        }
        return 0;
    }

    public static int GetRankLevel(string rankName) {
        return RankLevelMap.TryGetValue(rankName, out int level) ? level : 0;
    }
}
