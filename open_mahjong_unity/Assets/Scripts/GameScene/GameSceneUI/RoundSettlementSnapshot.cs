using System;

/// <summary>
/// 单局结算快照，供计分板主番列与悬停详情使用。
/// </summary>
[Serializable]
public class RoundSettlementSnapshot {
    public bool hasWin;
    public bool isLiuju;
    public string huClass;
    public int hepaiPlayerIndex = -1;
    public string winnerUsername;
    public int huScore;
    public int winnerScoreDelta;
    public string[] huFan;
    public string[] fuFanList;
    public int? baseFu;
    public int? han;
    public int? fu;
    public int[] hepaiPlayerHand;
    public int[][] combinationMask;
    public string subRule;
    /// <summary>四川：计分板主番 simplified 类型（three_hu / chajiao / liuju）。</summary>
    public string sichuanRoundLabel;

    public bool CanShowTooltip =>
        string.IsNullOrEmpty(sichuanRoundLabel)
        && hasWin
        && huFan != null
        && huFan.Length > 0;

    public bool CanShowHandPreview =>
        hepaiPlayerHand != null && hepaiPlayerHand.Length > 0;
}
