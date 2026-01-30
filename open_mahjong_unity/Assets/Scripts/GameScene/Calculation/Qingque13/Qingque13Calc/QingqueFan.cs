using System.Collections.Generic;

namespace Qingque13
{
    /// <summary>
    /// Qingque mahjong fan (番) enumeration.
    /// Matches C++ enum indices in qingque.h.
    /// Total: 92 fans
    /// </summary>
    public enum QingqueFan
    {
        Trivial = 0,                    // 和牌
        HeavenlyHand,                   // 天和
        EarthlyHand,                    // 地和
        OutWithReplacementTile,         // 岭上开花
        LastTileDraw,                   // 海底捞月
        LastTileClaim,                  // 河底捞鱼
        RobbingTheKong,                 // 抢杠
        SevenPairs,                     // 七对
        ConcealedHand,                  // 门前清
        FourConcealedKongs,             // 四暗杠
        ThreeConcealedKongs,            // 三暗杠
        TwoConcealedKongs,              // 双暗杠
        ConcealedKong,                  // 暗杠
        FourKongs,                      // 四杠
        ThreeKongs,                     // 三杠
        TwoKongs,                       // 双杠
        FourConcealedTriplets,          // 四暗刻
        ThreeConcealedTriplets,         // 三暗刻
        AllTriplets,                    // 对对和
        TwelveHog,                      // 十二归
        EightHog,                       // 八归
        ThreeDoublePairs,               // 三叠对
        TwoDoublePairs,                 // 二叠对
        DoublePair,                     // 叠对
        AllHonours,                     // 字一色
        BigFourWinds,                   // 大四喜
        LittleFourWinds,                // 小四喜
        FourWindPairs,                  // 四喜对
        ThreeWindTriplets,              // 风牌三刻
        SevenWindPairs,                 // 风牌七对
        SixWindPairs,                   // 风牌六对
        FiveWindPairs,                  // 风牌五对
        FourWindPairs2,                 // 风牌四对
        BigThreeDragons,                // 大三元
        LittleThreeDragons,             // 小三元
        SixDragonPairs,                 // 三元六对
        ThreeDragonPairs,               // 三元对
        FanTile4T,                      // 番牌四刻
        FanTile3T,                      // 番牌三刻
        FanTile2T,                      // 番牌二刻
        FanTile1T,                      // 番牌刻
        FanTile7P,                      // 番牌七对
        FanTile6P,                      // 番牌六对
        FanTile5P,                      // 番牌五对
        FanTile4P,                      // 番牌四副
        FanTile3P,                      // 番牌三副
        FanTile2P,                      // 番牌二副
        FanTile1P,                      // 番牌
        AllTerminals,                   // 清幺九
        AllTerminalsAndHonours,         // 混幺九
        PureOutsideHand,                // 清带幺
        MixedOutsideHand,               // 混带幺
        NineGates,                      // 九莲宝灯
        FullFlush,                      // 清一色
        HalfFlush,                      // 混一色
        AllTypes,                       // 五门齐
        MixedOneNumber,                 // 混一数
        TwoNumbers,                     // 二数
        TwoConsecutiveNumbers,          // 二聚
        ThreeConsecutiveNumbers,        // 三聚
        FourConsecutiveNumbers,         // 四聚
        ConnectedNumbers,               // 连数
        GappedNumbers,                  // 间数
        ReflectedHand,                  // 镜数
        ReflectedHand2,                 // 映数
        CommonNumber,                   // 满庭芳
        QuadrupleSequence,              // 四同顺
        TripleSequence,                 // 三同顺
        TwoDoubleSequences,             // 二般高
        DoubleSequence,                 // 一般高
        FourShiftedTriplets,            // 四连刻
        ThreeShiftedTriplets,           // 三连刻
        FourShiftedSequences,           // 四步高
        ThreeShiftedSequences,          // 三步高
        FourChainedSequences,           // 四连环
        ThreeChainedSequences,          // 三连环
        PureStraight,                   // 一气贯通
        SevenShiftedPairs,              // 七连对
        SixShiftedPairs,                // 六连对
        FiveShiftedPairs,               // 五连对
        FourShiftedPairs,               // 四连对
        MixedTripleTriplet,             // 三色同刻
        MixedTripleSequence,            // 三色同顺
        TwoTriplePairs,                 // 三色二对
        MixedTriplePair,                // 三色同对
        MixedShiftedTriplets,           // 三色连刻
        MixedStraight,                  // 三色贯通
        MirroredHand,                   // 镜同
        ThreeMirroredPairs,             // 镜同三对
        TwoMirroredPairs,               // 镜同二对
        TwoShortStraights               // 双龙会
    }

    /// <summary>
    /// Fan metadata (tag information).
    /// Matches C++ struct tag.
    /// </summary>
    public struct QingqueFanTag
    {
        public bool SpecialCompatible;   // Can coexist with special hands
        public bool IsSpecial;            // Is a special hand (七对)
        public bool IsOccasional;         // Is an occasional fan (天和/地和/etc)
        public byte FanValue;             // Base fan value (for scoring)

        public QingqueFanTag(bool specialCompatible, bool isSpecial, bool isOccasional, byte fanValue)
        {
            SpecialCompatible = specialCompatible;
            IsSpecial = isSpecial;
            IsOccasional = isOccasional;
            FanValue = fanValue;
        }
    }

    /// <summary>
    /// Fan metadata collection.
    /// Maps each fan to its Chinese name and tag information.
    /// </summary>
    public static class QingqueFanMetadata
    {
        public static readonly Dictionary<QingqueFan, string> ChineseNames = new Dictionary<QingqueFan, string>
        {
            { QingqueFan.Trivial, "和牌" },
            { QingqueFan.HeavenlyHand, "天和" },
            { QingqueFan.EarthlyHand, "地和" },
            { QingqueFan.OutWithReplacementTile, "岭上开花" },
            { QingqueFan.LastTileDraw, "海底捞月" },
            { QingqueFan.LastTileClaim, "河底捞鱼" },
            { QingqueFan.RobbingTheKong, "抢杠" },
            { QingqueFan.SevenPairs, "七对" },
            { QingqueFan.ConcealedHand, "门前清" },
            { QingqueFan.FourConcealedKongs, "四暗杠" },
            { QingqueFan.ThreeConcealedKongs, "三暗杠" },
            { QingqueFan.TwoConcealedKongs, "双暗杠" },
            { QingqueFan.ConcealedKong, "暗杠" },
            { QingqueFan.FourKongs, "四杠" },
            { QingqueFan.ThreeKongs, "三杠" },
            { QingqueFan.TwoKongs, "双杠" },
            { QingqueFan.FourConcealedTriplets, "四暗刻" },
            { QingqueFan.ThreeConcealedTriplets, "三暗刻" },
            { QingqueFan.AllTriplets, "对对和" },
            { QingqueFan.TwelveHog, "十二归" },
            { QingqueFan.EightHog, "八归" },
            { QingqueFan.ThreeDoublePairs, "三叠对" },
            { QingqueFan.TwoDoublePairs, "二叠对" },
            { QingqueFan.DoublePair, "叠对" },
            { QingqueFan.AllHonours, "字一色" },
            { QingqueFan.BigFourWinds, "大四喜" },
            { QingqueFan.LittleFourWinds, "小四喜" },
            { QingqueFan.FourWindPairs, "四喜对" },
            { QingqueFan.ThreeWindTriplets, "风牌三刻" },
            { QingqueFan.SevenWindPairs, "风牌七对" },
            { QingqueFan.SixWindPairs, "风牌六对" },
            { QingqueFan.FiveWindPairs, "风牌五对" },
            { QingqueFan.FourWindPairs2, "风牌四对" },
            { QingqueFan.BigThreeDragons, "大三元" },
            { QingqueFan.LittleThreeDragons, "小三元" },
            { QingqueFan.SixDragonPairs, "三元六对" },
            { QingqueFan.ThreeDragonPairs, "三元对" },
            { QingqueFan.FanTile4T, "番牌四刻" },
            { QingqueFan.FanTile3T, "番牌三刻" },
            { QingqueFan.FanTile2T, "番牌二刻" },
            { QingqueFan.FanTile1T, "番牌刻" },
            { QingqueFan.FanTile7P, "番牌七对" },
            { QingqueFan.FanTile6P, "番牌六对" },
            { QingqueFan.FanTile5P, "番牌五对" },
            { QingqueFan.FanTile4P, "番牌四副" },
            { QingqueFan.FanTile3P, "番牌三副" },
            { QingqueFan.FanTile2P, "番牌二副" },
            { QingqueFan.FanTile1P, "番牌" },
            { QingqueFan.AllTerminals, "清幺九" },
            { QingqueFan.AllTerminalsAndHonours, "混幺九" },
            { QingqueFan.PureOutsideHand, "清带幺" },
            { QingqueFan.MixedOutsideHand, "混带幺" },
            { QingqueFan.NineGates, "九莲宝灯" },
            { QingqueFan.FullFlush, "清一色" },
            { QingqueFan.HalfFlush, "混一色" },
            { QingqueFan.AllTypes, "五门齐" },
            { QingqueFan.MixedOneNumber, "混一数" },
            { QingqueFan.TwoNumbers, "二数" },
            { QingqueFan.TwoConsecutiveNumbers, "二聚" },
            { QingqueFan.ThreeConsecutiveNumbers, "三聚" },
            { QingqueFan.FourConsecutiveNumbers, "四聚" },
            { QingqueFan.ConnectedNumbers, "连数" },
            { QingqueFan.GappedNumbers, "间数" },
            { QingqueFan.ReflectedHand, "镜数" },
            { QingqueFan.ReflectedHand2, "映数" },
            { QingqueFan.CommonNumber, "满庭芳" },
            { QingqueFan.QuadrupleSequence, "四同顺" },
            { QingqueFan.TripleSequence, "三同顺" },
            { QingqueFan.TwoDoubleSequences, "二般高" },
            { QingqueFan.DoubleSequence, "一般高" },
            { QingqueFan.FourShiftedTriplets, "四连刻" },
            { QingqueFan.ThreeShiftedTriplets, "三连刻" },
            { QingqueFan.FourShiftedSequences, "四步高" },
            { QingqueFan.ThreeShiftedSequences, "三步高" },
            { QingqueFan.FourChainedSequences, "四连环" },
            { QingqueFan.ThreeChainedSequences, "三连环" },
            { QingqueFan.PureStraight, "一气贯通" },
            { QingqueFan.SevenShiftedPairs, "七连对" },
            { QingqueFan.SixShiftedPairs, "六连对" },
            { QingqueFan.FiveShiftedPairs, "五连对" },
            { QingqueFan.FourShiftedPairs, "四连对" },
            { QingqueFan.MixedTripleTriplet, "三色同刻" },
            { QingqueFan.MixedTripleSequence, "三色同顺" },
            { QingqueFan.TwoTriplePairs, "三色二对" },
            { QingqueFan.MixedTriplePair, "三色同对" },
            { QingqueFan.MixedShiftedTriplets, "三色连刻" },
            { QingqueFan.MixedStraight, "三色贯通" },
            { QingqueFan.MirroredHand, "镜同" },
            { QingqueFan.ThreeMirroredPairs, "镜同三对" },
            { QingqueFan.TwoMirroredPairs, "镜同二对" },
            { QingqueFan.TwoShortStraights, "双龙会" }
        };

        public static readonly Dictionary<QingqueFan, QingqueFanTag> Tags = new Dictionary<QingqueFan, QingqueFanTag>
        {
            { QingqueFan.HeavenlyHand, new QingqueFanTag(true, false, true, 16) },
            { QingqueFan.EarthlyHand, new QingqueFanTag(true, false, true, 16) },
            { QingqueFan.OutWithReplacementTile, new QingqueFanTag(false, false, true, 4) },
            { QingqueFan.LastTileDraw, new QingqueFanTag(true, false, true, 4) },
            { QingqueFan.LastTileClaim, new QingqueFanTag(true, false, true, 4) },
            { QingqueFan.RobbingTheKong, new QingqueFanTag(true, false, true, 4) }
        };
    }
}
