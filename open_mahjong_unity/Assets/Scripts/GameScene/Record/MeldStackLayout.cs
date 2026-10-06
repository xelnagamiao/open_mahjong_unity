using System.Collections.Generic;

/// <summary>
/// 备用显示 sign：101 明面叠牌，102 暗面叠牌。沿原始 mask 顺序叠在最近的 0/1/2/3 牌上；
/// 连续叠牌依次为第 1、2…层，不占横向槽，也不属于须从手牌移除的基础副露牌。
/// 旧 sign 0~4 的生成和含义不变，当前行牌规则不生成 101/102。
/// </summary>
public static class MeldStackLayout {
    public const int FaceUp = 101;
    public const int FaceDown = 102;

    public readonly struct StackTile {
        public readonly int PairIndex;
        public readonly int AnchorIndex;
        public readonly int Layer;
        public readonly int Sign;
        public readonly int TileId;
        public StackTile(int pairIndex, int anchorIndex, int layer, int sign, int tileId) {
            PairIndex = pairIndex;
            AnchorIndex = anchorIndex;
            Layer = layer;
            Sign = sign;
            TileId = tileId;
        }
    }

    public static bool IsStack(int sign) => sign == FaceUp || sign == FaceDown;

    public static bool HasStacks(int[] mask) {
        if (mask == null) return false;
        for (int i = 0; i + 1 < mask.Length; i += 2)
            if (IsStack(mask[i])) return true;
        return false;
    }

    public static List<StackTile> Build(int[] mask) {
        var stacks = new List<StackTile>();
        if (mask == null) return stacks;
        int anchor = -1, layer = 0;
        for (int i = 0; i + 1 < mask.Length; i += 2) {
            int sign = mask[i], tile = mask[i + 1];
            if (IsStack(sign)) {
                // 未知牌 0 可以显示牌背；孤立叠牌及非法牌值不回退为普通牌。
                if (anchor >= 0 && (tile == 0 || tile > 10))
                    stacks.Add(new StackTile(i / 2, anchor, ++layer, sign, tile));
            } else if (sign >= 0 && sign <= 3) {
                anchor = tile == 0 || tile > 10 ? i / 2 : -1;
                layer = 0;
            } else if (sign != 4) {
                anchor = -1;
                layer = 0;
            }
        }
        return stacks;
    }
}
