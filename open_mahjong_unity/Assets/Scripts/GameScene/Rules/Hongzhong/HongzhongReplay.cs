using System.Collections.Generic;
using Newtonsoft.Json;

public partial class GameRecordManager {
    public HongzhongInfo HongzhongRecordInfo { get; private set; }
    private readonly HongzhongHintsCache[] recordHongzhongHints = {
        new HongzhongHintsCache(),new HongzhongHintsCache(),new HongzhongHintsCache(),new HongzhongHintsCache(),
    };
    private bool IsHongzhongRecord() => RecordRuleManifest?.RuleId == HongzhongGameState.RuleId;
    private void ResetHongzhongRecordState() {
        HongzhongRecordInfo = null;
        foreach (var cache in recordHongzhongHints) cache.Reset();
    }
    public HashSet<int> HongzhongWaiting(TingpaiQuery query) {
        var result = new HashSet<int>();
        if (!IsHongzhongRecord() || query == null) return result;
        if (query.RecordPlayerIndex.HasValue) {
            int seat = query.RecordPlayerIndex.Value;
            return seat >= 0 && seat < recordHongzhongHints.Length ? recordHongzhongHints[seat].Waiting(query) : result;
        }
        foreach (var cache in recordHongzhongHints) result.UnionWith(cache.Waiting(query));
        return result;
    }
    public WaitTileHint DescribeHongzhongWaiting(WaitHintQuery query) {
        if (!IsHongzhongRecord() || query == null) return null;
        int seat = query.Record?.SelfPlayerIndex ?? query.SelfIndex;
        return seat >= 0 && seat < recordHongzhongHints.Length ? recordHongzhongHints[seat].Describe(query) : null;
    }
    private bool ApplyHongzhongRecordAction(IReadOnlyList<string> tick) {
        if (!IsHongzhongRecord() || tick == null || tick.Count<3 || tick[0]!="hongzhong") return false;
        if (tick[1] == "kong_score" || tick[1] == "kong_refund") {
            int[] changes = JsonConvert.DeserializeObject<int[]>(tick[2]);
            if (changes?.Length == 4) {
                var deltas = new Dictionary<int,int>();
                MapTickScoreChangesToDeltas(changes,deltas); ApplyScoreDeltas(deltas,out _,out _);
            }
        } else if (tick[1] == "birds") {
            var birds = JsonConvert.DeserializeObject<HongzhongBirdEvent>(tick[2]);
            if (birds != null) {
                HongzhongRecordInfo = new HongzhongInfo { bird_tiles=birds.tiles,bird_hits=birds.hits,detail=birds.detail };
                foreach (int _ in birds.tiles ?? System.Array.Empty<int>()) {
                    if (currentTilesList.Count == 0) break;
                    currentTilesList.RemoveAt(0); currentOriginalIndices.RemoveAt(0); consumedFromFront++;
                }
            }
        } else if (tick[1] == "hints" && tick.Count>=4 && int.TryParse(tick[2],out int seat) && seat>=0 && seat<4) {
            recordHongzhongHints[seat].Accept(JsonConvert.DeserializeObject<HongzhongHints>(tick[3]));
        }
        return true;
    }
}
