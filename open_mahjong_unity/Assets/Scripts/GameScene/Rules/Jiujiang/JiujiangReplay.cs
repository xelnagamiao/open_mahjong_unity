using System.Collections.Generic;
using Newtonsoft.Json;

public partial class GameRecordManager {
    internal RuleReplayModule CreateJiujiangReplayModule() => new RuleReplayModule("jiujiang") {
        Reset = ResetJiujiangRecordState,
        Clear = ResetJiujiangRecordState,
        ApplyBeforeMutation = ApplyJiujiangRecordAction,
        AfterSilentMutation = tick => { if (tick.Count > 1 && tick[1] == "reveal_kongs") RefreshGuizhouRecordMelds(); },
    };

    private readonly JiujiangHintsCache[] recordJiujiangHints = {
        new JiujiangHintsCache(),new JiujiangHintsCache(),new JiujiangHintsCache(),new JiujiangHintsCache(),
    };
    private bool IsJiujiangRecord() => RecordRuleManifest?.RuleId == JiujiangGameState.RuleId;
    private void ResetJiujiangRecordState() { foreach(var cache in recordJiujiangHints) cache.Reset(); }
    public HashSet<int> JiujiangWaiting(TingpaiQuery query) {
        var result=new HashSet<int>();
        if (!IsJiujiangRecord() || query==null) return result;
        if (query.RecordPlayerIndex.HasValue) {
            int seat=query.RecordPlayerIndex.Value;
            return seat>=0 && seat<4 ? recordJiujiangHints[seat].Waiting(query) : result;
        }
        foreach(var cache in recordJiujiangHints) result.UnionWith(cache.Waiting(query));
        return result;
    }
    public WaitTileHint DescribeJiujiangWaiting(WaitHintQuery query) {
        if (!IsJiujiangRecord() || query==null) return null;
        int seat=query.Record?.SelfPlayerIndex ?? query.SelfIndex;
        return seat>=0 && seat<4 ? recordJiujiangHints[seat].Describe(query) : null;
    }
    private bool ApplyJiujiangRecordAction(IReadOnlyList<string> tick) {
        if (!IsJiujiangRecord() || tick==null || tick.Count<3 || tick[0]!="jiujiang") return false;
        if (tick[1]=="hints" && tick.Count>=4 && int.TryParse(tick[2],out int seat) && seat>=0 && seat<4)
            recordJiujiangHints[seat].Accept(JsonConvert.DeserializeObject<JiujiangHints>(tick[3]));
        if (tick[1]=="reveal_kongs") {
            var masks=JsonConvert.DeserializeObject<int[][][]>(tick[2]);
            if(masks?.Length==4) foreach(var player in recordPlayerList)
                player.combinationMasks=new List<int[]>(masks[player.playerIndex]);
        }
        // payments 为结算解释；净变化已经由随后 hu 事件统一应用。
        return true;
    }
}
