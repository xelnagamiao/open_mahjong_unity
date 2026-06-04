/// <summary>和牌张进入手牌区的展示方式。</summary>
public enum HepaiWinTilePresentMode {
    /// <summary>自摸：摸牌位生成后 0.2s 移入末张槽。</summary>
    TsumoTravel,
    /// <summary>国标荣和：从河牌最后一张 0.2s 移入末张槽。</summary>
    GuobiaoRonTravelFromRiver,
    /// <summary>荣和（含国标错和）：末张与其它牌一并摆好，仅等待 0.2s 后展开。</summary>
    RonInstantThenPause,
}
