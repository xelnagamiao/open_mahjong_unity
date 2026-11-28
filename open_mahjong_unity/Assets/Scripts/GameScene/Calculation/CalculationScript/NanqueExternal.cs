using System;
using System.Collections.Generic;
using Nanque;

/// <summary>
/// Client-side Nanque calculation entry points. The server remains
/// authoritative; this facade follows the same shape as other rule adapters.
/// </summary>
public static class NanqueExternal {
    private static readonly NanqueFanCalculator SharedHepai = new NanqueFanCalculator();
    private static readonly NanqueTingpaiCheck SharedTingpai = new NanqueTingpaiCheck();

    /// <summary>
    /// Static discard-win fan count for tingpai tips. Situational fans are
    /// intentionally absent; the server remains authoritative at settlement.
    /// </summary>
    public static Tuple<int, List<string>> HepaiCheck(
        List<int> handList,
        List<string> combinationList,
        int winningTile) {
        return SharedHepai.HepaiCheck(
            handList,
            combinationList,
            winningTile);
    }

    public static HashSet<int> TingpaiCheck(
        List<int> handTileList,
        List<string> combinationList) {
        return SharedTingpai.TingpaiCheck(
            handTileList,
            combinationList);
    }
}
