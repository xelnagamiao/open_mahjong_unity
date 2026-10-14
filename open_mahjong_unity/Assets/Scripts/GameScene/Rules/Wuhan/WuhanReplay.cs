using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public partial class GameRecordManager {
    internal RuleReplayModule CreateWuhanReplayModule() => new RuleReplayModule("wuhan") {
        ReservedWallTiles = 10,
        Reset = ResetWuhanRecordState, Clear = ResetWuhanRecordState,
        InitializePlayers = () => {
            foreach (var player in recordPlayerList)
                player.showHandDrawSlotActive = player.playerIndex == gameRecord.gameRound.rounds[currentRoundIndex].startPlayerIndex && player.tileList.Count == 14;
        },
        ApplyBeforeMutation = ApplyWuhanRecordAction,
        ConsumeWallTile = ConsumeWuhanRecordWallTile,
        RemainingTiles = () => Math.Max(0, currentTilesList.Count - recordDeadWallCount),
        RefreshAfterAction = () => WuhanPresentation.Apply(WuhanRecordInfo, false),
        RefreshAfterSeek = () => WuhanPresentation.Apply(WuhanRecordInfo, true),
        AfterSilentMutation = tick => {
            SyncRecordBoardScores();
            RefreshCurrentRecordTexts();
            WuhanPresentation.Apply(WuhanRecordInfo, true);
            RefreshTileListViewIfVisible();
        }
    };
    public WuhanInfo WuhanRecordInfo { get; private set; }
    private bool wuhanTailSingle;
    private readonly WuhanHintsCache[] recordWuhanHints = { new WuhanHintsCache(), new WuhanHintsCache(), new WuhanHintsCache(), new WuhanHintsCache() };
    private void ResetWuhanRecordState() { WuhanRecordInfo = new WuhanInfo { skins = new[] {45} }; WuhanPresentation.Apply(WuhanRecordInfo, false); wuhanTailSingle = false; foreach (var cache in recordWuhanHints) cache.Reset(); }
    private bool ConsumeWuhanRecordWallTile(string action) {
        if (action != "d" && action != "gd") return false;
        if (currentTilesList.Count == 0) return true;
        int index = action == "d" ? 0 : currentTilesList.Count - (wuhanTailSingle ? 1 : 2);
        index = Math.Max(0, index);
        int original = currentOriginalIndices[index];
        currentTilesList.RemoveAt(index); currentOriginalIndices.RemoveAt(index);
        if (action == "d") consumedFromFront++;
        else { consumedBackIndices.Add(original); wuhanTailSingle = !wuhanTailSingle; if (WuhanRecordInfo?.sea != true) recordDeadWallCount = wuhanTailSingle ? 9 : 10; }
        return true;
    }
    public HashSet<int> WuhanWaiting(TingpaiQuery query) {
        var result = new HashSet<int>();
        if (RecordRuleManifest?.RuleId != "wuhan" || query == null) return result;
        if (query.RecordPlayerIndex.HasValue) {
            int seat = query.RecordPlayerIndex.Value;
            return seat >= 0 && seat < 4 ? recordWuhanHints[seat].Waiting(query) : result;
        }
        foreach (var cache in recordWuhanHints) result.UnionWith(cache.Waiting(query));
        return result;
    }
    public WaitTileHint DescribeWuhanWaiting(WaitHintQuery query) {
        int seat = query?.Record?.SelfPlayerIndex ?? query?.SelfIndex ?? -1;
        return seat >= 0 && seat < 4 ? recordWuhanHints[seat].Describe(query) : null;
    }
    private bool ApplyWuhanRecordAction(IReadOnlyList<string> tick) {
        if (RecordRuleManifest?.RuleId != "wuhan" || tick == null || tick.Count < 3 || tick[0] != "wuhan") return false;
        if (tick[1] == "bonus") {
            var changes = JsonConvert.DeserializeObject<int[]>(tick[2]);
            if (changes?.Length == 4) {
                var deltas = new Dictionary<int,int>(); MapTickScoreChangesToDeltas(changes, deltas); ApplyScoreDeltas(deltas, out _, out _);
            }
        } else if (tick[1] == "indicator" && int.TryParse(tick[2], out int indicator)) {
            WuhanRecordInfo = WuhanGameState.InfoFromIndicator(indicator);
            if (currentTilesList.Count > 0) { currentTilesList.RemoveAt(0); currentOriginalIndices.RemoveAt(0); consumedFromFront++; }
        } else if (tick[1] == "sea" && WuhanRecordInfo != null) {
            WuhanRecordInfo.sea = true;
            if (int.TryParse(tick[2], out int count)) recordDeadWallCount = Math.Max(0, count - 4);
        } else if (tick[1] == "state") WuhanRecordInfo = JsonConvert.DeserializeObject<WuhanInfo>(tick[2]);
        else if (tick[1] == "hints" && tick.Count >= 4 && int.TryParse(tick[2], out int seat) && seat >= 0 && seat < 4)
            recordWuhanHints[seat].Accept(JsonConvert.DeserializeObject<WuhanHints>(tick[3]));
        return true;
    }
}
