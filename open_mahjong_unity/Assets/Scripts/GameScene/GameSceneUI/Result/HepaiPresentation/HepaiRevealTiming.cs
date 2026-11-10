/// <summary>
/// 和牌倒牌演出时长（与 open_mahjong_server round_end_timing 保持一致）。
/// </summary>
public static class HepaiRevealTiming {
    /// <summary>就位/河牌抓取等位移动画时长。</summary>
    public const float TravelSeconds = 0.2f;

    /// <summary>展开动画触发后的停留（Animator Expand 含在此时长内）。</summary>
    public const float ExpandHoldSeconds = RoundEndTiming.RoundEndHandRevealSeconds;

    /// <summary>倒牌阶段总时长 = 位移动画 + 展开停留；服务端 pre_panel_delay 与此对齐。</summary>
    public static float PrePanelTotalSeconds => TravelSeconds + ExpandHoldSeconds;

    /// <summary>牌谱展开明牌：和牌 display 后等待再弹出结算面板。</summary>
    public const float RecordShowCardsPanelDelaySeconds = 1f;

    /// <summary>虹雀末次杠宣言到和牌宣言的最短间隔，与杠牌三段动画总时长一致。</summary>
    public const float HongqueKongDeclarationIntervalSeconds = 0.64f;

    /// <summary>虹雀空手和没有倒牌动画，宣言后仍保留可辨认的停顿。</summary>
    public const float HongqueEmptyHandDeclarationHoldSeconds = 1f;
}
