/// <summary>
/// 和牌结算面板演出时长（与服务器 round_end_timing 保持一致）。
/// </summary>
public static class RoundEndTiming {
    public const float RoundEndPresentationFadeSeconds = 0.35f;
    public const float RoundEndHandRevealSeconds = 1.5f;
    public const float HuFanRevealIntervalSeconds = 0.5f;
    public const float HuBeforeTotalPanelSeconds = 0.5f;
    public const float HuConfirmCountdownSeconds = 8f;

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
