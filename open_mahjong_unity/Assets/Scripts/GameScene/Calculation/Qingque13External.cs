using System;
using System.Collections.Generic;
using Qingque13;

/// <summary>
/// 青雀对外统一接口（封装 Qingque13 命名空间内部实现）。
/// 不修改 Qingque13 命名空间，只在 Calculation 目录提供调用入口。
/// </summary>
public static class Qingque13External
{
    /// <summary>
    /// 和牌检查封装。直接转调 Qingque13.Qingque13Hepai.HepaiCheck。
    /// </summary>
    /// <param name="hand_list">手牌列表（应包含和牌张）</param>
    /// <param name="tiles_combination">已组成的组合列表</param>
    /// <param name="way_to_hepai">和牌方式列表（自摸、海底、岭上等关键字）</param>
    /// <param name="get_tile">和牌张 ID</param>
    /// <param name="debug">是否开启调试输出</param>
    /// <returns>返回元组：(番数, 番种名称列表)</returns>
    public static Tuple<double, List<string>> HepaiCheck(
        List<int> hand_list,
        List<string> tiles_combination,
        List<string> way_to_hepai,
        int get_tile,
        bool debug = false)
    {
        // 创建手牌列表副本，并移除和牌张（避免修改原列表）
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
    /// <param name="hand_tile_list">手牌列表</param>
    /// <param name="combination_list">已组成的组合列表</param>
    /// <param name="debug">是否开启调试</param>
    /// <returns>听牌集合（可和的牌 ID 集合）</returns>
    public static HashSet<int> TingpaiCheck(
        List<int> hand_tile_list,
        List<string> combination_list,
        bool debug = true)
    {
        return Qingque13Tingpai.TingpaiCheck(
            hand_tile_list,
            combination_list,
            debug
        );
    }
}


