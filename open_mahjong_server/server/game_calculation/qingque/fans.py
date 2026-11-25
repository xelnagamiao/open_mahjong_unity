"""Fan enum, Chinese names, occasional fan tags, and derepellenise coverage rules.

Pure Python port of QingqueFan.cs and QingqueDerepellenise.cs.
"""

from __future__ import annotations

from enum import IntEnum
from typing import Dict, List, Mapping, Sequence, Tuple


class QingqueFan(IntEnum):
    Trivial = 0
    HeavenlyHand = 1
    EarthlyHand = 2
    OutWithReplacementTile = 3
    LastTileDraw = 4
    LastTileClaim = 5
    RobbingTheKong = 6
    ThirteenOrphans = 7
    SevenPairs = 8
    ConcealedHand = 9
    FourConcealedKongs = 10
    ThreeConcealedKongs = 11
    TwoConcealedKongs = 12
    ConcealedKong = 13
    FourKongs = 14
    ThreeKongs = 15
    TwoKongs = 16
    FourConcealedTriplets = 17
    ThreeConcealedTriplets = 18
    AllTriplets = 19
    TwelveHog = 20
    EightHog = 21
    ThreeDoublePairs = 22
    TwoDoublePairs = 23
    DoublePair = 24
    AllHonours = 25
    BigFourWinds = 26
    LittleFourWinds = 27
    FourWindPairs = 28
    ThreeWindTriplets = 29
    SevenWindPairs = 30
    SixWindPairs = 31
    BigThreeDragons = 32
    LittleThreeDragons = 33
    SixDragonPairs = 34
    ThreeDragonPairs = 35
    FanTile4T = 36
    FanTile3T = 37
    FanTile2T = 38
    FanTile1T = 39
    FanTile7P = 40
    FanTile6P = 41
    FanTile5P = 42
    FanTile4P = 43
    FanTile3P = 44
    FanTile2P = 45
    FanTile1P = 46
    AllTerminals = 47
    AllTerminalsAndHonours = 48
    PureOutsideHand = 49
    MixedOutsideHand = 50
    NineGates = 51
    FullFlush = 52
    HalfFlush = 53
    AllTypes = 54
    MixedOneNumber = 55
    TwoNumbers = 56
    TwoConsecutiveNumbers = 57
    ThreeConsecutiveNumbers = 58
    FourConsecutiveNumbers = 59
    ConnectedNumbers = 60
    GappedNumbers = 61
    ReflectedHand = 62
    ReflectedHand2 = 63
    CommonNumber = 64
    QuadrupleSequence = 65
    TripleSequence = 66
    TwoDoubleSequences = 67
    DoubleSequence = 68
    FourShiftedTriplets = 69
    ThreeShiftedTriplets = 70
    FourShiftedSequences = 71
    ThreeShiftedSequences = 72
    FourChainedSequences = 73
    ThreeChainedSequences = 74
    PureStraight = 75
    SevenShiftedPairs = 76
    SixShiftedPairs = 77
    FiveShiftedPairs = 78
    FourShiftedPairs = 79
    MixedTripleTriplet = 80
    MixedTripleSequence = 81
    TwoTriplePairs = 82
    MixedTriplePair = 83
    MixedShiftedTriplets = 84
    MixedStraight = 85
    MirroredHand = 86
    ThreeMirroredPairs = 87
    TwoMirroredPairs = 88
    TwoShortStraights = 89


CHINESE_NAMES: Mapping[QingqueFan, str] = {
    QingqueFan.Trivial: "和牌",
    QingqueFan.HeavenlyHand: "天和",
    QingqueFan.EarthlyHand: "地和",
    QingqueFan.OutWithReplacementTile: "岭上开花",
    QingqueFan.LastTileDraw: "海底捞月",
    QingqueFan.LastTileClaim: "河底捞鱼",
    QingqueFan.RobbingTheKong: "抢杠",
    QingqueFan.ThirteenOrphans: "十三幺",
    QingqueFan.SevenPairs: "七对",
    QingqueFan.ConcealedHand: "门前清",
    QingqueFan.FourConcealedKongs: "四暗杠",
    QingqueFan.ThreeConcealedKongs: "三暗杠",
    QingqueFan.TwoConcealedKongs: "双暗杠",
    QingqueFan.ConcealedKong: "暗杠",
    QingqueFan.FourKongs: "四杠",
    QingqueFan.ThreeKongs: "三杠",
    QingqueFan.TwoKongs: "双杠",
    QingqueFan.FourConcealedTriplets: "四暗刻",
    QingqueFan.ThreeConcealedTriplets: "三暗刻",
    QingqueFan.AllTriplets: "对对和",
    QingqueFan.TwelveHog: "十二归",
    QingqueFan.EightHog: "八归",
    QingqueFan.ThreeDoublePairs: "三叠对",
    QingqueFan.TwoDoublePairs: "二叠对",
    QingqueFan.DoublePair: "叠对",
    QingqueFan.AllHonours: "字一色",
    QingqueFan.BigFourWinds: "大四喜",
    QingqueFan.LittleFourWinds: "小四喜",
    QingqueFan.FourWindPairs: "四喜对",
    QingqueFan.ThreeWindTriplets: "风牌三刻",
    QingqueFan.SevenWindPairs: "风牌七对",
    QingqueFan.SixWindPairs: "风牌六对",
    QingqueFan.BigThreeDragons: "大三元",
    QingqueFan.LittleThreeDragons: "小三元",
    QingqueFan.SixDragonPairs: "三元六对",
    QingqueFan.ThreeDragonPairs: "三元对",
    QingqueFan.FanTile4T: "番牌四刻",
    QingqueFan.FanTile3T: "番牌三刻",
    QingqueFan.FanTile2T: "番牌二刻",
    QingqueFan.FanTile1T: "番牌刻",
    QingqueFan.FanTile7P: "番牌七对",
    QingqueFan.FanTile6P: "番牌六对",
    QingqueFan.FanTile5P: "番牌五对",
    QingqueFan.FanTile4P: "番牌四副",
    QingqueFan.FanTile3P: "番牌三副",
    QingqueFan.FanTile2P: "番牌二副",
    QingqueFan.FanTile1P: "番牌",
    QingqueFan.AllTerminals: "清幺九",
    QingqueFan.AllTerminalsAndHonours: "混幺九",
    QingqueFan.PureOutsideHand: "清带幺",
    QingqueFan.MixedOutsideHand: "混带幺",
    QingqueFan.NineGates: "九莲宝灯",
    QingqueFan.FullFlush: "清一色",
    QingqueFan.HalfFlush: "混一色",
    QingqueFan.AllTypes: "五门齐",
    QingqueFan.MixedOneNumber: "混一数",
    QingqueFan.TwoNumbers: "二数",
    QingqueFan.TwoConsecutiveNumbers: "二聚",
    QingqueFan.ThreeConsecutiveNumbers: "三聚",
    QingqueFan.FourConsecutiveNumbers: "四聚",
    QingqueFan.ConnectedNumbers: "连数",
    QingqueFan.GappedNumbers: "间数",
    QingqueFan.ReflectedHand: "镜数",
    QingqueFan.ReflectedHand2: "映数",
    QingqueFan.CommonNumber: "满庭芳",
    QingqueFan.QuadrupleSequence: "四同顺",
    QingqueFan.TripleSequence: "三同顺",
    QingqueFan.TwoDoubleSequences: "二般高",
    QingqueFan.DoubleSequence: "一般高",
    QingqueFan.FourShiftedTriplets: "四连刻",
    QingqueFan.ThreeShiftedTriplets: "三连刻",
    QingqueFan.FourShiftedSequences: "四步高",
    QingqueFan.ThreeShiftedSequences: "三步高",
    QingqueFan.FourChainedSequences: "四连环",
    QingqueFan.ThreeChainedSequences: "三连环",
    QingqueFan.PureStraight: "一气贯通",
    QingqueFan.SevenShiftedPairs: "七连对",
    QingqueFan.SixShiftedPairs: "六连对",
    QingqueFan.FiveShiftedPairs: "五连对",
    QingqueFan.FourShiftedPairs: "四连对",
    QingqueFan.MixedTripleTriplet: "三色同刻",
    QingqueFan.MixedTripleSequence: "三色同顺",
    QingqueFan.TwoTriplePairs: "三色二对",
    QingqueFan.MixedTriplePair: "三色同对",
    QingqueFan.MixedShiftedTriplets: "三色连刻",
    QingqueFan.MixedStraight: "三色贯通",
    QingqueFan.MirroredHand: "镜同",
    QingqueFan.ThreeMirroredPairs: "镜同三对",
    QingqueFan.TwoMirroredPairs: "镜同二对",
    QingqueFan.TwoShortStraights: "双龙会",
}

OCCASIONAL_FAN_VALUES: Mapping[QingqueFan, int] = {
    QingqueFan.HeavenlyHand: 16,
    QingqueFan.EarthlyHand: 16,
    QingqueFan.OutWithReplacementTile: 4,
    QingqueFan.LastTileDraw: 4,
    QingqueFan.LastTileClaim: 4,
    QingqueFan.RobbingTheKong: 4,
}

# Coverage rules: (covering_fan, (covered_fans...))
_COVERAGE_RULES: Tuple[Tuple[QingqueFan, Tuple[QingqueFan, ...]], ...] = (
    # Heavenly/Earthly/Seven Pairs cover Concealed Hand
    (QingqueFan.HeavenlyHand, (QingqueFan.ConcealedHand,)),
    (QingqueFan.EarthlyHand, (QingqueFan.ConcealedHand,)),
    (QingqueFan.SevenPairs, (QingqueFan.ConcealedHand,)),
    # Thirteen Orphans
    (
        QingqueFan.ThirteenOrphans,
        (QingqueFan.ConcealedHand, QingqueFan.AllTerminalsAndHonours),
    ),
    # Concealed Kongs coverage
    (
        QingqueFan.FourConcealedKongs,
        (
            QingqueFan.ThreeConcealedKongs,
            QingqueFan.TwoConcealedKongs,
            QingqueFan.ConcealedKong,
            QingqueFan.FourKongs,
            QingqueFan.ThreeKongs,
            QingqueFan.TwoKongs,
            QingqueFan.FourConcealedTriplets,
            QingqueFan.ThreeConcealedTriplets,
            QingqueFan.AllTriplets,
            QingqueFan.ConcealedHand,
        ),
    ),
    (
        QingqueFan.ThreeConcealedKongs,
        (
            QingqueFan.TwoConcealedKongs,
            QingqueFan.ConcealedKong,
            QingqueFan.ThreeKongs,
            QingqueFan.TwoKongs,
            QingqueFan.ThreeConcealedTriplets,
        ),
    ),
    (
        QingqueFan.TwoConcealedKongs,
        (QingqueFan.ConcealedKong, QingqueFan.TwoKongs),
    ),
    # Kongs coverage
    (
        QingqueFan.FourKongs,
        (QingqueFan.ThreeKongs, QingqueFan.TwoKongs, QingqueFan.AllTriplets),
    ),
    (QingqueFan.ThreeKongs, (QingqueFan.TwoKongs,)),
    # Concealed Triplets coverage
    (
        QingqueFan.FourConcealedTriplets,
        (
            QingqueFan.ThreeConcealedTriplets,
            QingqueFan.AllTriplets,
            QingqueFan.ConcealedHand,
        ),
    ),
    # Hog coverage
    (QingqueFan.TwelveHog, (QingqueFan.EightHog,)),
    # All Honours coverage
    (
        QingqueFan.AllHonours,
        (
            QingqueFan.HalfFlush,
            QingqueFan.MixedOneNumber,
            QingqueFan.AllTerminalsAndHonours,
            QingqueFan.MixedOutsideHand,
            QingqueFan.FanTile1P,
        ),
    ),
    # Wind coverage
    (
        QingqueFan.BigFourWinds,
        (
            QingqueFan.ThreeWindTriplets,
            QingqueFan.AllTriplets,
            QingqueFan.MixedOneNumber,
            QingqueFan.HalfFlush,
            QingqueFan.FanTile1T,
        ),
    ),
    (
        QingqueFan.LittleFourWinds,
        (
            QingqueFan.ThreeWindTriplets,
            QingqueFan.HalfFlush,
            QingqueFan.FanTile1P,
        ),
    ),
    (QingqueFan.FourWindPairs, (QingqueFan.FanTile1P,)),
    (
        QingqueFan.SevenWindPairs,
        (
            QingqueFan.SixWindPairs,
            QingqueFan.FourWindPairs,
            QingqueFan.FanTile1P,
            QingqueFan.AllHonours,
            QingqueFan.TwelveHog,
            QingqueFan.ThreeDoublePairs,
        ),
    ),
    (
        QingqueFan.SixWindPairs,
        (
            QingqueFan.HalfFlush,
            QingqueFan.MixedOneNumber,
            QingqueFan.EightHog,
            QingqueFan.TwoDoublePairs,
        ),
    ),
    # Dragon coverage
    (QingqueFan.BigThreeDragons, (QingqueFan.FanTile3T,)),
    (
        QingqueFan.LittleThreeDragons,
        (QingqueFan.FanTile2T, QingqueFan.FanTile3P),
    ),
    (
        QingqueFan.SixDragonPairs,
        (
            QingqueFan.ThreeDragonPairs,
            QingqueFan.FanTile6P,
            QingqueFan.HalfFlush,
            QingqueFan.MixedOneNumber,
            QingqueFan.TwelveHog,
            QingqueFan.ThreeDoublePairs,
        ),
    ),
    (QingqueFan.ThreeDragonPairs, (QingqueFan.FanTile3P,)),
    # Fan tile coverage (triplets)
    (
        QingqueFan.FanTile4T,
        (
            QingqueFan.FanTile3T,
            QingqueFan.FanTile2T,
            QingqueFan.FanTile1T,
            QingqueFan.FanTile4P,
            QingqueFan.FanTile3P,
            QingqueFan.FanTile2P,
            QingqueFan.FanTile1P,
            QingqueFan.HalfFlush,
            QingqueFan.MixedOneNumber,
            QingqueFan.AllTriplets,
        ),
    ),
    (
        QingqueFan.FanTile3T,
        (
            QingqueFan.FanTile2T,
            QingqueFan.FanTile1T,
            QingqueFan.FanTile3P,
            QingqueFan.FanTile2P,
            QingqueFan.FanTile1P,
        ),
    ),
    (
        QingqueFan.FanTile2T,
        (
            QingqueFan.FanTile1T,
            QingqueFan.FanTile2P,
            QingqueFan.FanTile1P,
        ),
    ),
    (QingqueFan.FanTile1T, (QingqueFan.FanTile1P,)),
    # Fan tile coverage (pairs)
    (
        QingqueFan.FanTile7P,
        (
            QingqueFan.FanTile6P,
            QingqueFan.FanTile5P,
            QingqueFan.FanTile4P,
            QingqueFan.FanTile3P,
            QingqueFan.FanTile2P,
            QingqueFan.FanTile1P,
            QingqueFan.AllHonours,
            QingqueFan.TwelveHog,
            QingqueFan.ThreeDoublePairs,
            QingqueFan.ThreeDragonPairs,
        ),
    ),
    (
        QingqueFan.FanTile6P,
        (
            QingqueFan.FanTile5P,
            QingqueFan.FanTile4P,
            QingqueFan.FanTile3P,
            QingqueFan.FanTile2P,
            QingqueFan.FanTile1P,
            QingqueFan.HalfFlush,
            QingqueFan.MixedOneNumber,
            QingqueFan.EightHog,
            QingqueFan.TwoDoublePairs,
        ),
    ),
    (
        QingqueFan.FanTile5P,
        (
            QingqueFan.FanTile4P,
            QingqueFan.FanTile3P,
            QingqueFan.FanTile2P,
            QingqueFan.FanTile1P,
            QingqueFan.DoublePair,
        ),
    ),
    (
        QingqueFan.FanTile4P,
        (QingqueFan.FanTile3P, QingqueFan.FanTile2P, QingqueFan.FanTile1P),
    ),
    (QingqueFan.FanTile3P, (QingqueFan.FanTile2P, QingqueFan.FanTile1P)),
    (QingqueFan.FanTile2P, (QingqueFan.FanTile1P,)),
    # Terminal coverage
    (
        QingqueFan.AllTerminals,
        (
            QingqueFan.AllTerminalsAndHonours,
            QingqueFan.PureOutsideHand,
            QingqueFan.MixedOutsideHand,
            QingqueFan.TwoNumbers,
        ),
    ),
    (QingqueFan.AllTerminalsAndHonours, (QingqueFan.MixedOutsideHand,)),
    (QingqueFan.PureOutsideHand, (QingqueFan.MixedOutsideHand,)),
    # Flush coverage
    (
        QingqueFan.NineGates,
        (QingqueFan.FullFlush, QingqueFan.ConcealedHand),
    ),
    (QingqueFan.FullFlush, (QingqueFan.HalfFlush,)),
    (QingqueFan.AllTypes, (QingqueFan.FanTile1P,)),
    # Consecutive numbers coverage
    (QingqueFan.TwoConsecutiveNumbers, (QingqueFan.TwoNumbers,)),
    # Sequence coverage
    (
        QingqueFan.QuadrupleSequence,
        (
            QingqueFan.TripleSequence,
            QingqueFan.TwoDoubleSequences,
            QingqueFan.DoubleSequence,
            QingqueFan.TwelveHog,
        ),
    ),
    (QingqueFan.TripleSequence, (QingqueFan.DoubleSequence,)),
    (QingqueFan.TwoDoubleSequences, (QingqueFan.DoubleSequence,)),
    # Shifted patterns coverage
    (
        QingqueFan.FourShiftedTriplets,
        (QingqueFan.ThreeShiftedTriplets, QingqueFan.AllTriplets),
    ),
    (QingqueFan.FourShiftedSequences, (QingqueFan.ThreeShiftedSequences,)),
    (QingqueFan.FourChainedSequences, (QingqueFan.ThreeChainedSequences,)),
    # Shifted pairs coverage
    (
        QingqueFan.SevenShiftedPairs,
        (
            QingqueFan.SixShiftedPairs,
            QingqueFan.FiveShiftedPairs,
            QingqueFan.FourShiftedPairs,
            QingqueFan.FullFlush,
            QingqueFan.ReflectedHand,
            QingqueFan.ConnectedNumbers,
        ),
    ),
    (
        QingqueFan.SixShiftedPairs,
        (QingqueFan.FiveShiftedPairs, QingqueFan.FourShiftedPairs),
    ),
    (QingqueFan.FiveShiftedPairs, (QingqueFan.FourShiftedPairs,)),
    # Triple pairs coverage
    (
        QingqueFan.TwoTriplePairs,
        (QingqueFan.MixedTriplePair, QingqueFan.TwoMirroredPairs),
    ),
    # Mirrored pairs coverage
    (QingqueFan.ThreeMirroredPairs, (QingqueFan.TwoMirroredPairs,)),
    # Consecutive numbers coverage
    (
        QingqueFan.TwoConsecutiveNumbers,
        (
            QingqueFan.ThreeConsecutiveNumbers,
            QingqueFan.FourConsecutiveNumbers,
        ),
    ),
    (QingqueFan.ThreeConsecutiveNumbers, (QingqueFan.FourConsecutiveNumbers,)),
    # Double pairs coverage
    (
        QingqueFan.ThreeDoublePairs,
        (
            QingqueFan.TwoDoublePairs,
            QingqueFan.DoublePair,
            QingqueFan.TwelveHog,
            QingqueFan.EightHog,
        ),
    ),
    (
        QingqueFan.TwoDoublePairs,
        (QingqueFan.DoublePair, QingqueFan.EightHog),
    ),
)


def derepellenise(fans: Sequence[QingqueFan]) -> List[QingqueFan]:
    """Remove covered/redundant fans while preserving insertion order."""
    fan_set = set(fans)
    removed = {QingqueFan.Trivial}

    for covering, covered_tuple in _COVERAGE_RULES:
        if covering in fan_set:
            removed.update(covered_tuple)

    if (
        QingqueFan.ReflectedHand in fan_set
        and QingqueFan.ReflectedHand2 in fan_set
        and QingqueFan.FullFlush in fan_set
    ):
        removed.add(QingqueFan.ReflectedHand2)

    return [f for f in fans if f not in removed]
