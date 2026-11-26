using System;
using System.Collections.Generic;

/// <summary>
/// 虹雀听牌与切牌预览提示渲染器：把虹雀的唯一牌可见性与分值描述装配成通用行数据，
/// 交由 TipsContainer.RenderCustomHints 统一绘制。
/// </summary>
public static class HongqueTipsRenderer {
    public static List<int> ExtractWaitingTileIds(HongqueScoreHintInfo[] waitHints, HongqueScoreHintInfo winHint) {
        var list = new List<int>();
        if (waitHints != null) {
            foreach (HongqueScoreHintInfo hint in waitHints) {
                if (hint == null || string.IsNullOrEmpty(hint.tile)) continue;
                int tileId = HongqueTileVisual.FromCode(hint.tile);
                if (tileId != 0) list.Add(tileId);
            }
        }
        if (winHint != null && !string.IsNullOrEmpty(winHint.tile)) {
            int tileId = HongqueTileVisual.FromCode(winHint.tile);
            if (tileId != 0 && !list.Contains(tileId)) list.Add(tileId);
        }
        return list;
    }

    public static void Render(TipsContainer container, HongqueScoreHintInfo[] waitHints, HongqueScoreHintInfo winHint) {
        if (container == null) return;
        string subRule = HongqueWTips.IsActive ? HongqueWScoring.SubRule : "hongque";
        HongqueScoreHintInfo[] hints = winHint != null
            ? new[] { winHint }
            : (waitHints ?? Array.Empty<HongqueScoreHintInfo>());
        HashSet<int> unavailableTiles = winHint == null ? CollectUnavailableTiles() : null;
        var rows = new List<(int tileId, WaitTileHint hint, int remaining)>(hints.Length);
        foreach (HongqueScoreHintInfo hint in hints) {
            if (hint == null) continue;
            int tileId = string.IsNullOrEmpty(hint.tile) ? 0 : HongqueTileVisual.FromCode(hint.tile);
            string label = $"{hint.points}分";
            WaitTileHint waitHint = HongqueWTips.IsActive
                ? HongqueWTips.Describe(hint)
                : (hint.self_draw_only ? WaitTileHint.TsumoOnly(label) : WaitTileHint.Ron(label));
            int remaining = HongqueWTips.IsActive
                ? (winHint == null ? HongqueWTips.Remaining(tileId) : 1)
                : (unavailableTiles != null && unavailableTiles.Contains(tileId) ? 0 : 1);
            rows.Add((tileId, waitHint, remaining));
        }
        container.RenderCustomHints(rows, subRule);
    }

    public static void RenderCutPreview(TipsContainer container, HongqueScoreHintInfo[] waitHints, int pendingCutTileId) {
        if (container == null) return;
        string subRule = HongqueWTips.IsActive ? HongqueWScoring.SubRule : "hongque";
        HashSet<int> unavailableTiles = CollectUnavailableTiles();
        unavailableTiles.Add(pendingCutTileId);
        HongqueScoreHintInfo[] hints = waitHints ?? Array.Empty<HongqueScoreHintInfo>();
        var rows = new List<(int tileId, WaitTileHint hint, int remaining)>(hints.Length);
        foreach (HongqueScoreHintInfo hint in hints) {
            if (hint == null || string.IsNullOrEmpty(hint.tile)) continue;
            int tileId = HongqueTileVisual.FromCode(hint.tile);
            if (tileId == 0) continue;
            string label = $"{hint.points}分";
            WaitTileHint waitHint = HongqueWTips.IsActive
                ? HongqueWTips.Describe(hint)
                : (hint.self_draw_only ? WaitTileHint.TsumoOnly(label) : WaitTileHint.Ron(label));
            int remaining = HongqueWTips.IsActive
                ? HongqueWTips.Remaining(tileId)
                : (unavailableTiles.Contains(tileId) ? 0 : 1);
            rows.Add((tileId, waitHint, remaining));
        }
        container.RenderCustomHints(rows, subRule, pendingCutTileId);
    }

    /// <summary>收集全部玩家牌河与副露中的牌（虹雀每张唯一，已公开的听牌张无法再获得）。</summary>
    public static HashSet<int> CollectUnavailableTiles() {
        var unavailable = new HashSet<int>();
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        if (gsm == null || gsm.player_to_info == null) return unavailable;
        foreach (KeyValuePair<string, PlayerInfoClass> kv in gsm.player_to_info) {
            PlayerInfoClass info = kv.Value;
            if (info == null) continue;
            if (info.discard_tiles != null) {
                foreach (int tileId in info.discard_tiles) {
                    if (tileId > 0) unavailable.Add(tileId);
                }
            }
            if (info.combination_masks == null) continue;
            foreach (int[] meldMask in info.combination_masks) {
                if (meldMask == null) continue;
                for (int i = 1; i < meldMask.Length; i += 2) {
                    if (meldMask[i] > 0) unavailable.Add(meldMask[i]);
                }
            }
        }
        return unavailable;
    }
}
