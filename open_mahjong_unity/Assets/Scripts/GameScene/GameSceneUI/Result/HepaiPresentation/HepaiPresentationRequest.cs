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
}
