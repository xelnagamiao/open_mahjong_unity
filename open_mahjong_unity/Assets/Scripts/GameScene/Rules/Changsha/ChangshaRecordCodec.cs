using System;
using System.Collections.Generic;

internal sealed class ChangshaRonRecord {
    public int HepaiTile;
    public bool MultiRon;
    public int Discarder;
    public bool RecycleDiscard;
}

internal sealed class ChangshaOpenKongRecord {
    public int Player;
    public List<int> PublicTiles = new List<int>();
    public List<int> DrawnHandTiles = new List<int>();
}

internal static class ChangshaRecordCodec {
    /// <summary>跳转时逐步扣除已摸切牌，保留同批尚未处理的亮牌及实际已记入手牌的部分。</summary>
    public static ChangshaOpenKongRecord CurrentOpenKongBatch(IReadOnlyList<List<string>> ticks, int node, Func<List<string>, int[]> readTiles) {
        ChangshaOpenKongRecord batch = null;
        bool previousWasPublicDraw = false;
        for (int i = 0; i < node; i++) {
            List<string> tick = ticks[i];
            if (tick == null || tick.Count == 0) continue;
            if (IsPublicKongDraw(tick)) {
                int player = int.Parse(tick[2]);
                if (!previousWasPublicDraw || batch == null || batch.Player != player) {
                    batch = new ChangshaOpenKongRecord { Player = player, PublicTiles = new List<int>(readTiles(tick)) };
                }
                batch.DrawnHandTiles.Add(int.Parse(tick[1]));
                // 批次一开始已全公开，但逐张gd回放时只有部分牌真正写入手牌镜像。
                previousWasPublicDraw = true;
                continue;
            }
            previousWasPublicDraw = false;
            if (batch == null) continue;
            if (tick[0] == "c") {
                int tile = int.Parse(tick[1]);
                batch.PublicTiles.Remove(tile);
                batch.DrawnHandTiles.Remove(tile);
            } else if (tick[0] == "d" || tick[0] == "gd" || tick[0] == "ag" || tick[0] == "jg" || tick[0] == "g"
                || tick[0] == "p" || tick[0] == "cl" || tick[0] == "cm" || tick[0] == "cr" || tick[0].StartsWith("hu_") || tick[0] == "end") {
                batch = null;
            }
        }
        return batch != null && batch.PublicTiles.Count > 0 ? batch : null;
    }

    public static bool IsPublicKongDraw(IReadOnlyList<string> tick) {
        return tick != null && tick.Count > 5 && tick[0] == "gd" && tick[3] == "changsha_open_kong";
    }

    /// <summary>长沙专用自摸快照扩展固定放在鸟之后，不影响普通自摸及批次接炮格式。</summary>
    public static int OpenKongSelfDrawHandIndex(IReadOnlyList<string> tick) {
        return tick != null && tick.Count > 8 && tick[0] == "hu_self" && tick[7] == "changsha_open_kong_win" ? 8 : -1;
    }

    /// <summary>海底的另一张被开走后，后续补张与回放按尾部半墩继续计数。</summary>
    public static string TailTypeAfterPublicDraw(IReadOnlyList<string> tick, int wallCount, string currentType) {
        return IsPublicKongDraw(tick) && currentType == "double" && int.Parse(tick[4]) == wallCount - 2 ? "single" : currentType;
    }

    /// <summary>骰子补牌按服务端记录的当前墙索引移除，不回退到旧墙尾补牌算法。</summary>
    public static bool ConsumePublicKongDraw(IReadOnlyList<string> tick, List<int> wall, List<int> originalIndices, HashSet<int> consumedIndices) {
        if (!IsPublicKongDraw(tick)) return false;
        int position = int.Parse(tick[4]);
        consumedIndices.Add(originalIndices[position]);
        wall.RemoveAt(position);
        originalIndices.RemoveAt(position);
        return true;
    }

    /// <summary>长沙批次接炮复用现有多响扩展，旧普通长沙记录的第6项仍是鸟数组。</summary>
    public static bool HasBatchRon(IReadOnlyList<string> tick) {
        return tick != null && tick.Count > 8 && int.TryParse(tick[6], out int flag) && (flag == 0 || flag == 1);
    }

    public static int BirdIndex(IReadOnlyList<string> tick) { return HasBatchRon(tick) ? 9 : 6; }

    public static ChangshaRonRecord ParseBatchRon(IReadOnlyList<string> tick) {
        if (!HasBatchRon(tick)) return null;
        return new ChangshaRonRecord {
            HepaiTile = int.Parse(tick[5]), MultiRon = tick[6] == "1",
            Discarder = int.Parse(tick[7]), RecycleDiscard = tick[8] == "1",
        };
    }

    /// <summary>同一赢家再次胡另一张补牌时替换前一次附加牌，保留原来的听牌手牌。</summary>
    public static int[] BuildWinningHand(IReadOnlyList<int> hand, bool hasPreviousRonTile, int tile) {
        int count = hand.Count - (hasPreviousRonTile && hand.Count > 0 ? 1 : 0);
        bool append = count % 3 == 1;
        int[] result = new int[count + (append ? 1 : 0)];
        for (int i = 0; i < count; i++) result[i] = hand[i];
        if (append) result[count] = tile;
        return result;
    }
}
