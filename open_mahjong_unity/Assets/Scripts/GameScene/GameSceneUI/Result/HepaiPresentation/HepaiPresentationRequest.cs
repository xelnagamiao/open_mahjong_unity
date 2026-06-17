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
}
