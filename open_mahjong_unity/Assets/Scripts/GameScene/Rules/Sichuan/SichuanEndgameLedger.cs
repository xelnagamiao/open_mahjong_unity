using System.Collections.Generic;

/// <summary>
/// 血战终局分步结算（reveal_hu → settle_hu×N → chajiao×N）的分差累加器：终局只在计分板写一行。
/// 对局内由 SichuanGameState 持有；牌谱回放（GameRecordManager.Sichuan）持有自己的实例。
/// </summary>
public sealed class SichuanEndgameLedger {
    private Dictionary<int, int> accum;
    private int huCount;
    private bool hadChajiao;

    public static bool IsEndgameScoreStep(string liujuStep) {
        return liujuStep == "reveal_hu" || liujuStep == "settle_hu" || liujuStep == "chajiao";
    }

    public void Reset() {
        accum = null;
        huCount = 0;
        hadChajiao = false;
    }

    public void Begin() {
        accum = new Dictionary<int, int>();
        huCount = 0;
        hadChajiao = false;
    }

    public void Accumulate(Dictionary<int, int> deltas) {
        if (deltas == null || deltas.Count == 0) return;
        accum ??= new Dictionary<int, int>();
        foreach (var kvp in deltas) {
            accum.TryGetValue(kvp.Key, out int cur);
            accum[kvp.Key] = cur + kvp.Value;
        }
    }

    public void RecordHu() {
        accum ??= new Dictionary<int, int>();
        huCount++;
    }

    public void MarkChajiaoStep() {
        hadChajiao = true;
    }

    /// <summary>终局末步：把累积分差写成一行计分板（主番仅 三家和/查叫/流局）。未开始累计时返回 false。</summary>
    public bool TryFlushToHistory(string subRule, List<RoundSettlementSnapshot> history) {
        if (accum == null) return false;
        var merged = new Dictionary<int, int>(accum);
        string roundLabel = ScoreHistorySettlementHelper.ResolveSichuanEndgameRoundLabel(huCount, hadChajiao);
        RoundSettlementSnapshot snap = ScoreHistorySettlementHelper.CreateSichuanScoreboardSnapshot(subRule, roundLabel);
        history.Add(snap);
        SettlementPresenter.Current.ApplyLocalScoreHistory(snap, merged);
        Reset();
        return true;
    }

    /// <summary>非血战终局或即时和牌：四川计分板不写番种/手牌，主番留空。</summary>
    public static void AppendSimpleScoreboard(string subRule, List<RoundSettlementSnapshot> history, string huClass, Dictionary<int, int> scoreChanges) {
        string roundLabel = huClass == "liuju" ? SichuanRoundLabel.Liuju : null;
        RoundSettlementSnapshot snap = ScoreHistorySettlementHelper.CreateSichuanScoreboardSnapshot(subRule, roundLabel);
        history.Add(snap);
        SettlementPresenter.Current.ApplyLocalScoreHistory(snap, scoreChanges);
    }
}
