using System;
using System.Collections.Generic;
using System.Linq;

public static class HongqueWTips {
    public static bool IsActive => RuleRegistry.CurrentSubRule == HongqueWScoring.SubRule;
    private static Dictionary<string, object> Config {
        get {
            var record = GameRecordManager.Instance;
            return record != null && record.TryGetActiveRecordRuleContext(out _, out _)
                ? record.GetDetailedConfigSnapshot() : GameSession.Current.DetailedConfig;
        }
    }
    private static int IntOption(string key) => Config != null && Config.TryGetValue(key,out var value) ? Convert.ToInt32(value) : 0;
    public static bool Colourless => Config == null || !Config.TryGetValue("hongque_colourless",out var value) || Convert.ToBoolean(value);
    public static int MinimumFan => IntOption("hongque_min_fan");
    public static int MinimumPoints => IntOption("hongque_min_points");
    private static bool Meets(HongqueWinScore score) => score != null && score.FanTotal >= MinimumFan && score.Points >= MinimumPoints;

    public static HongqueScoreHintInfo[] BuildScoreHints(List<int> hand, List<int[]> melds) {
        if (hand == null) return Array.Empty<HongqueScoreHintInfo>();
        var owned = hand.Concat((melds ?? new List<int[]>()).SelectMany(mask => mask.Where((tile,index) => index%2 == 1)))
            .GroupBy(tile => tile).ToDictionary(group => group.Key,group => group.Count());
        var hints = new List<HongqueScoreHintInfo>();
        foreach (int tile in HongqueWScoring.Faces(Colourless)) {
            if (owned.TryGetValue(tile,out int count) && count >= 2) continue;
            var complete = new List<int>(hand) {tile};
            var ron = HongqueWScoring.BestWinResult(complete,melds);
            var tsumo = HongqueWScoring.BestWinResult(complete,melds,true,false,false,true);
            var legalRon = MinimumFan == 0 && MinimumPoints == 0 ? ron : HongqueWScoring.BestWinResult(complete,melds,minimumFan:MinimumFan,minimumPoints:MinimumPoints);
            var legalTsumo = MinimumFan == 0 && MinimumPoints == 0 ? tsumo : HongqueWScoring.BestWinResult(complete,melds,true,false,false,true,MinimumFan,MinimumPoints);
            var score = legalRon ?? legalTsumo ?? tsumo ?? ron;
            if (score == null) continue;
            hints.Add(ToHint(tile,score,legalRon == null && legalTsumo != null,legalRon == null && legalTsumo == null ? "未起和" : null));
        }
        return hints.ToArray();
    }
    private static HongqueScoreHintInfo ToHint(int tile,HongqueWinScore score,bool selfOnly=false,string status=null) => new HongqueScoreHintInfo {
        tile=HongqueTileVisual.ToCode(tile), @base=score.Base, fan_total=score.FanTotal, points=score.Points,
        fans=score.Fans, self_draw_only=selfOnly, status_label=status
    };
    public static HongqueScoreHintInfo BuildWinHint(List<int> hand,List<int[]> melds,HongqueWinHintContext context) {
        if (context == null || hand == null) return null;
        int tile=HongqueTileVisual.FromCode(context.tile); var complete=new List<int>(hand);
        if (!context.self_draw) complete.Add(tile);
        var score=HongqueWScoring.BestWinResult(complete,melds,context.self_draw,context.before_first_discard,context.wall_empty,context.self_draw,MinimumFan,MinimumPoints)
            ?? HongqueWScoring.BestWinResult(complete,melds,context.self_draw,context.before_first_discard,context.wall_empty,context.self_draw);
        return score == null ? null : ToHint(tile,score,false,Meets(score) ? null : "未起和");
    }
    public static WaitTileHint Describe(HongqueScoreHintInfo hint) {
        if (!string.IsNullOrEmpty(hint.status_label)) return WaitTileHint.None(hint.status_label);
        return hint.self_draw_only ? WaitTileHint.TsumoOnly("仅自摸") : WaitTileHint.Ron(hint.points+"分");
    }
    public static int Remaining(int tile) {
        int seen=0; var mirror=TableMirror.Current;
        if (mirror == null) return 2;
        seen += mirror.SelfHandTiles.Count(item => item == tile);
        var gsm=NormalGameStateManager.Instance;
        if (gsm?.player_to_info != null) foreach (var player in gsm.player_to_info.Values) {
            seen += player.discard_tiles?.Count(item => item == tile) ?? 0;
            if (player.combination_masks != null) foreach (var mask in player.combination_masks)
                for (int index=1; mask != null && index < mask.Length; index+=2) if (mask[index] == tile) seen++;
        }
        return Math.Max(0,2-seen);
    }
}
