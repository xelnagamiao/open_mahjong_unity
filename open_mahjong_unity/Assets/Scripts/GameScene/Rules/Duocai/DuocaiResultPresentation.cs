using System.Linq;

internal static class DuocaiResultPresentation {
    internal static RiichiEndResultExtras BuildExtras(ShowResultInfo info)=>info?.han==null&&info?.fu==null?null:RiichiGameState.BuildExtras(info);
    internal static SettlementTotalDisplay Total(SettlementTotalQuery q) {
        var extras=q.ExtrasAs<RiichiEndResultExtras>();
        if(extras==null)return new SettlementTotalDisplay{FanText=$"和牌 {q.HuScore}点",ScoreText=q.WinnerPointDelta.HasValue?$"{q.WinnerPointDelta.Value:+#;-#;0}点":""};
        var display=RiichiFanText.SettlementTotal(q);
        if(extras.Han>=13) {
            bool named=(q.HuFan??System.Array.Empty<string>()).Any(label=>label.StartsWith("纯赖子")
                ||RiichiFanText.FanToDisplayRiichi.TryGetValue(label.Split('（')[0],out var value)&&value.Contains("役满"));
            display.FuText=null;
            display.LimitText=named?(extras.Han==13?"役满":$"{extras.Han/13}倍役满"):"累计役满";
        }
        return display;
    }
    internal static string Scoreboard(SettlementTotalQuery q)=>q.Han.HasValue||q.Extras is RiichiEndResultExtras
        ?RiichiFanText.ScoreboardFanText(q):$"{q.HuScore}点";
}
