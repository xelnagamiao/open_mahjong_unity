using System.Collections.Generic;
using UnityEngine;

/// <summary>台湾听牌与和牌张提示：台数比起和线，未达则按自摸重算；听牌资格（听牌/天听）影响台数。</summary>
internal static class TaiwanTips {
    public static HashSet<int> Tingpai(TingpaiQuery q) {
        return TaiwanExternal.TingpaiCheck(q.Hand, q.Melds ?? new List<string>(), q.DetailedConfig);
    }

    public static WaitTileHint Describe(WaitHintQuery q) {
        string readyQualification = q.Record != null
            ? q.Record.ReadyQualification
            : TaiwanGameState.Active?.SelfReadyQualification;
        List<int> flowers = q.SelfFlowers ?? new List<int>();
        int seatWind = 41 + Mathf.Clamp(q.SelfIndex, 0, 3);
        int roundWind = 41 + Mathf.Clamp((q.CurrentRound - 1) / 4, 0, 3);

        var ron = TaiwanExternal.HepaiCheck(
            q.HandWithWin, q.Melds, q.HepaiTile, false, seatWind, roundWind, flowers, q.DetailedConfig, readyQualification);
        if (ron.Item1 >= q.HepaiLimit) {
            return WaitTileHint.Ron($"{ron.Item1}台");
        }
        var zimo = TaiwanExternal.HepaiCheck(
            q.HandWithWin, q.Melds, q.HepaiTile, true, seatWind, roundWind, flowers, q.DetailedConfig, readyQualification);
        return zimo.Item1 >= q.HepaiLimit
            ? WaitTileHint.TsumoOnly("仅自摸")
            : WaitTileHint.None("未起和");
    }
}
