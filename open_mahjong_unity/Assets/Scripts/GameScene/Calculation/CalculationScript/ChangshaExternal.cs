using System;
using System.Collections.Generic;
using Changsha;

public static class ChangshaExternal {
    private static readonly ChangshaHepaiCheck SharedHepai = new ChangshaHepaiCheck();
    private static readonly ChangshaTingpaiCheck SharedTingpai = new ChangshaTingpaiCheck();

    public static Tuple<int, List<string>> HepaiCheck(
        List<int> handList,
        List<string> tilesCombination,
        List<string> wayToHepai,
        int getTile,
        bool includeSituational = true) {
        return SharedHepai.HepaiCheck(
            handList, tilesCombination, wayToHepai, getTile, includeSituational);
    }

    public static HashSet<int> TingpaiCheck(
        List<int> handTileList,
        List<string> combinationList) {
        return SharedTingpai.TingpaiCheck(handTileList, combinationList);
    }

    public static int BaseFromFans(
        List<string> fanList,
        bool dealerRelated = false,
        int smallHuScore = 2,
        int bigHuScore = 8,
        bool baseScoreNoDealer = false) {
        return ChangshaHepaiCheck.BaseFromFans(
            fanList,
            dealerRelated,
            smallHuScore,
            bigHuScore,
            baseScoreNoDealer);
    }
}
