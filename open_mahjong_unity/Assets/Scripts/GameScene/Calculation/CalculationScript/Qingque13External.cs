using System;
using System.Collections.Generic;
using Qingque13;

/// <summary>
/// 青雀对外统一接口（封装 Qingque13 命名空间内部实现）。
/// 放在 CalculationScript 程序集内，由本程序集引用 QinqueAssembly，避免 GameScene 直接引用外部程序集。
/// </summary>
public static class Qingque13External
{
    /// <summary>
    /// 和牌检查封装。直接转调 Qingque13.Qingque13Hepai.HepaiCheck。
    /// </summary>
    public static Tuple<double, List<string>> HepaiCheck(
        List<int> hand_list,
        List<string> tiles_combination,
        List<string> way_to_hepai,
        int get_tile,
        bool debug = false)
    {
        List<int> handListWithoutTile = new List<int>(hand_list);
        handListWithoutTile.Remove(get_tile);

        return Qingque13Hepai.HepaiCheck(
            handListWithoutTile,
            tiles_combination,
            way_to_hepai,
            get_tile,
            debug
        );
    }

    /// <summary>
    /// 根据番数计算基础点数的封装。转调 Qingque13.Qingque13Hepai.GetBasePoint。
    /// </summary>
    public static int GetBasePoint(double fan)
    {
        return Qingque13Hepai.GetBasePoint(fan);
    }

    /// <summary>
    /// 听牌检查封装。转调 Qingque13.Qingque13Tingpai.TingpaiCheck。
    /// </summary>
    public static HashSet<int> TingpaiCheck(
        List<int> hand_tile_list,
        List<string> combination_list,
        bool debug = false)
    {
        return Qingque13Tingpai.TingpaiCheck(
            hand_tile_list,
            combination_list,
            debug
        );
    }
}
