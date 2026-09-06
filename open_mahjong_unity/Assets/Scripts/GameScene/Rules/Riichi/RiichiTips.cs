using System.Collections.Generic;
using Riichi;
using UnityEngine;

/// <summary>
/// 日麻听牌与和牌张提示：本地完整计番，先按荣和上下文算，不成立再按自摸重算；
/// 标签只展示番数（役满直接显示"役满 / x倍役满"）。
/// </summary>
internal static class RiichiTips {
    public static HashSet<int> Tingpai(TingpaiQuery q) {
        return RiichiExternal.TingpaiCheck(q.Hand, q.Melds ?? new List<string>(), false);
    }

    public static WaitTileHint Describe(WaitHintQuery q) {
        RiichiHandResult ron = RiichiExternal.FullHepaiCheck(
            q.HandWithWin, q.Melds, q.HepaiTile, BuildContext(q, isTsumo: false));
        if (ron.IsValid && ron.Score > 0) {
            return WaitTileHint.Ron(FormatLabel(ron));
        }
        RiichiHandResult tsumo = RiichiExternal.FullHepaiCheck(
            q.HandWithWin, q.Melds, q.HepaiTile, BuildContext(q, isTsumo: true));
        return tsumo.IsValid && tsumo.Score > 0
            ? WaitTileHint.TsumoOnly(FormatLabel(tsumo))
            : WaitTileHint.None(FormatLabel(null));
    }

    private static RiichiHandContext BuildContext(WaitHintQuery q, bool isTsumo) {
        var ctx = new RiichiHandContext {
            IsTsumo = isTsumo,
            HasOpenTanyao = true,
            CombinationMasks = q.MeldMasks,
            PlayerWind = RiichiTileUtil.East + q.SelfIndex,
            RoundWind = RiichiTileUtil.East + Mathf.Clamp((q.CurrentRound - 1) / 4, 0, 3),
            // 里宝仅立直者在和牌时才能看到；提示阶段不计（服务端仍以结算为准）
            UraDoraIndicators = new List<int>(),
        };

        if (q.Record != null) {
            ctx.IsRiichi = q.Record.SelfIsRiichi;
            ctx.DoraIndicators = q.Record.DoraIndicators != null ? new List<int>(q.Record.DoraIndicators) : new List<int>();
            return ctx;
        }

        TableMirror mirror = TableMirror.Current;
        string[] selfTags = mirror.Info("self")?.tag_list;
        if (selfTags != null) {
            foreach (string tag in selfTags) {
                if (tag == "riichi") ctx.IsRiichi = true;
                else if (tag == "daburu_riichi") { ctx.IsDaburuRiichi = true; ctx.IsRiichi = true; }
            }
        }
        if (RiichiCutSelectionController.Instance != null && RiichiCutSelectionController.Instance.IsActive) {
            ctx.IsPendingRiichi = true;
            if (!ctx.IsRiichi) {
                ctx.IsRiichi = true;
                ctx.IsDaburuRiichi = IsDaburuRiichiCandidate(mirror);
            }
        }

        RiichiGameState state = RiichiGameState.Active;
        ctx.DoraIndicators = new List<int>();
        if (state != null) {
            ctx.DoraIndicators.AddRange(state.DoraIndicators);
            ctx.DoraIndicators.AddRange(state.KanDoraIndicators);
        }
        return ctx;
    }

    /// <summary>两立直条件：自家尚无理论弃牌，且其他玩家均无吃碰明杠。</summary>
    private static bool IsDaburuRiichiCandidate(TableMirror mirror) {
        var selfOrigin = mirror.Info("self")?.discard_origin_tiles;
        if (selfOrigin != null && selfOrigin.Count > 0) return false;
        foreach (string pos in new[] { "left", "top", "right" }) {
            var combos = mirror.Info(pos)?.combination_tiles;
            if (combos == null) continue;
            foreach (string combo in combos) {
                if (combo.Length == 0) continue;
                char sign = combo[0];
                if (sign == 's' || sign == 'k' || sign == 'g') return false;
            }
        }
        return true;
    }

    private static string FormatLabel(RiichiHandResult result) {
        if (result == null || !result.IsValid) return "无役";
        if (result.YakumanMultiplier >= 2) return $"{result.YakumanMultiplier}倍役满";
        if (result.YakumanMultiplier == 1) return "役满";
        if (result.Han <= 0) return "无役";
        return $"{result.Han}番";
    }
}
