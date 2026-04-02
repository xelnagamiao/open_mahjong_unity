using System;
using System.Collections.Generic;
using Classical;

/// <summary>
/// 古典麻将对外统一接口（封装 Classical 命名空间内部实现）。
/// 放在 CalculationScript 程序集内，由本程序集引用 ClassicalAssembly。
/// </summary>
public static class ClassicalExternal {
    /// <summary>
    /// 和牌检查。hand_list 应包含和牌张（14张）。
    /// 返回 (基础副, 总副, 副番名列表, 番名列表)。
    /// </summary>
    public static Tuple<int, int, List<string>, List<string>> HepaiCheck(
        List<int> handList,
        List<string> tilesCombination,
        List<string> wayToHepai,
        int getTile,
        bool debug = false) {
        return new ClassicalHepaiCheck(debug).HepaiCheck(
            handList, tilesCombination, wayToHepai, getTile);
    }

    /// <summary>
    /// 仅计算基础副数（不含番倍），供提示使用。
    /// </summary>
    public static int FushuCheck(
        List<int> handList,
        List<string> tilesCombination,
        List<string> wayToHepai,
        int getTile,
        bool debug = false) {
        return new ClassicalHepaiCheck(debug).FushuCheck(
            handList, tilesCombination, wayToHepai, getTile);
    }

    /// <summary>
    /// 听牌检查。hand_tile_list 为不含和牌张的手牌。
    /// </summary>
    public static HashSet<int> TingpaiCheck(
        List<int> handTileList,
        List<string> combinationList,
        bool debug = false) {
        return ClassicalTingpaiCheck.TingpaiCheckStatic(handTileList, combinationList, debug);
    }
}
