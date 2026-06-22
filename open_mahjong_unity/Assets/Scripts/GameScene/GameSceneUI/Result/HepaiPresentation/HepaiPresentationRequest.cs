/// <summary>和牌倒牌演出输入（由 <see cref="HepaiRevealDirector"/> 从对局状态组装）。</summary>
public sealed class HepaiPresentationRequest {
    public int HepaiPlayerIndex;
    public string WinnerPosition;
    public string HuClass;
    public int[] HepaiPlayerHand;
    public HepaiWinTilePresentMode WinTileMode;
    /// <summary>国标荣和错和结束后需恢复为继续对局的手牌/河牌画面。</summary>
    public bool RestoreMidGameAfterReveal;
    /// <summary>荣和时供牌方座位（用于校验河牌对象）。</summary>
    public string DiscardPlayerPosition;
    /// <summary>四川血战：和牌张 tile id（自摸时仅和牌者可见真实 id，他人为 0）。</summary>
    public int HepaiTile;
    /// <summary>四川血战·一炮多响：本家和牌动画结束后是否回收河牌（仅最后一家为 true）。</summary>
    public bool RecycleDiscardAfterPresent;
    /// <summary>四川血战·抢杠和：和牌张来自加杠牌，spawn/回收优先使用 lastCutJiagang3DObject。</summary>
    public bool IsQianggang;

    /// <summary>牌谱/观战回放：展开明牌模式（仅 left/top/right）。</summary>
    public bool IsRecordShowCardsExpanded;
    /// <summary>牌谱规则键（如 guobiao / riichi），用于与对局一致的倒牌策略。</summary>
    public string RecordRule;
    /// <summary>牌谱荣和错和（与 WinTileMode 配合）。</summary>
    public bool IsCuoheRon;
}
