using System;
using System.Collections.Generic;
using Jiandan;

/// <summary>
/// Client-side Jiandan calculation entry points. The server remains
/// authoritative; this facade follows the same shape as other rule adapters.
/// </summary>
public static class JiandanExternal {
    /// <summary>
    /// Static discard-win fan count for tingpai tips. Situational fans are
    /// intentionally absent; the server remains authoritative at settlement.
    /// </summary>
    public static Tuple<int, List<string>> HepaiCheck(
        List<int> handList,
        List<string> combinationList,
        int winningTile) {
        return new JiandanFanCalculator().HepaiCheck(
            handList,
            combinationList,
            winningTile);
    }

    public static HashSet<int> TingpaiCheck(
        List<int> handTileList,
        List<string> combinationList) {
        return new JiandanTingpaiCheck().TingpaiCheck(
            handTileList,
            combinationList);
    }
}
