/// <summary>
/// 和牌结算面板演出时长（与服务器 round_end_timing 保持一致）。
/// </summary>
public static class RoundEndTiming {
    public const float RoundEndPresentationFadeSeconds = 0.35f;
    public const float RoundEndHandRevealSeconds = 1.5f;
    /// <summary>与 <see cref="HepaiRevealTiming.TravelSeconds"/> 一致，供注释/对照服务端。</summary>
    public const float HepaiTravelSeconds = 0.2f;
    public const float HuFanRevealIntervalSeconds = 0.5f;
    public const float HuBeforeTotalPanelSeconds = 0.5f;
    public const float HuConfirmCountdownSeconds = 8f;
    /// <summary>四川终局非末步面板停留（由服务端步间 sleep 控制，客户端不再倒计时关面板）。</summary>
    public const float SichuanMidPanelConfirmSeconds = 3f;
    /// <summary>四川查叫面板：有叫/没叫/花猪状态展示停留。</summary>
    public const float SichuanChajiaoStatusHoldSeconds = 0.5f;
    /// <summary>四川查叫面板含刮风下雨退税时额外停留。</summary>
    public const float SichuanChajiaoRefundExtraSeconds = 0.5f;
    /// <summary>四川流局查叫非末步面板停留（不含 0.35s 渐显）。</summary>
    public const float SichuanLiujuPanelHoldSeconds = 2f;

    public static float GetSichuanSettleHuPanelDuration(int fanCount, bool isFinalPanel) {
        float duration = RoundEndPresentationFadeSeconds;
        duration += fanCount * HuFanRevealIntervalSeconds;
        duration += HuBeforeTotalPanelSeconds;
        duration += isFinalPanel ? HuConfirmCountdownSeconds : SichuanMidPanelConfirmSeconds;
        return duration;
    }

    public static float GetSichuanChajiaoPanelDuration(bool isFinalPanel) {
        float duration = RoundEndPresentationFadeSeconds + SichuanChajiaoStatusHoldSeconds;
        if (isFinalPanel) {
            return duration;
        }
        return duration + SichuanMidPanelConfirmSeconds;
    }

    public static float GetHuResultPanelDuration(int fanCount, int fuFanCount, float delayBeforeVisible, bool includePanelFade = true) {
        float duration = delayBeforeVisible;
        if (includePanelFade) {
            duration += RoundEndPresentationFadeSeconds;
        }
        duration += fuFanCount * HuFanRevealIntervalSeconds;
        duration += fanCount * HuFanRevealIntervalSeconds;
        duration += HuBeforeTotalPanelSeconds + HuConfirmCountdownSeconds;
        return duration;
    }
}
