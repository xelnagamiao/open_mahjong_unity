/// <summary>和牌张展示方式（局终倒牌 / 四川血战中途和牌补花区）。</summary>
public enum HepaiWinTilePresentMode {
    /// <summary>自摸：摸牌位生成后 0.2s 移入末张槽。</summary>
    TsumoTravel,
    /// <summary>国标荣和：从河牌最后一张 0.2s 移入末张槽。</summary>
    GuobiaoRonTravelFromRiver,
    /// <summary>荣和（含国标错和）：末张与其它牌一并摆好，仅等待 0.2s 后展开。</summary>
    RonInstantThenPause,

    /// <summary>四川血战·自摸：摸入张移入补花区暗面，和牌者手牌区退场。</summary>
    SichuanZimoToBuhuaFaceDown,
    /// <summary>四川血战·点炮单家：河牌最后一张移入和牌者补花区亮面。</summary>
    SichuanRonSingleToBuhua,
    /// <summary>四川血战·一炮多响：河牌分裂，每家补花区 1 张变暗亮面。</summary>
    SichuanRonMultiToBuhua,
}
