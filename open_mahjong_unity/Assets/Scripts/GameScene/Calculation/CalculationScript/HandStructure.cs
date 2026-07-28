using System;

/// <summary>用标准面子数描述一类麻将的手牌结构。</summary>
public sealed class HandStructure {
    public int MeldCount { get; }

    /// <summary>未取得和牌张时的标准手牌张数。</summary>
    public int BaseHandTileCount => MeldCount * 3 + 1;

    /// <summary>取得和牌张后的标准手牌张数。</summary>
    public int CompleteHandTileCount => BaseHandTileCount + 1;

    /// <summary>主手牌、一个牌宽的间隔和摸牌张合计占用的牌宽单位。</summary>
    public int DisplayWidthUnits => BaseHandTileCount + 2;

    public HandStructure(int meldCount) {
        if (meldCount <= 0) throw new ArgumentOutOfRangeException(nameof(meldCount));
        MeldCount = meldCount;
    }

    public int ConcealedMeldCount(int externalMeldCount) {
        return MeldCount - externalMeldCount;
    }

    public int ConcealedTileCount(int externalMeldCount, bool complete) {
        return ConcealedMeldCount(externalMeldCount) * 3 + (complete ? 2 : 1);
    }
}

/// <summary>客户端已支持的标准手牌结构及规则映射。</summary>
public static class HandStructures {
    public static readonly HandStructure ThirteenTile = new HandStructure(4);
    public static readonly HandStructure SixteenTile = new HandStructure(5);

    public static HandStructure Resolve(string roomRule, string subRule = null) {
        if (IsRule(roomRule, "taiwan") || IsRule(roomRule, "taiwan/standard")
            || IsRule(subRule, "taiwan/standard")) {
            return SixteenTile;
        }
        return ThirteenTile;
    }

    private static bool IsRule(string value, string expected) {
        return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    }
}
