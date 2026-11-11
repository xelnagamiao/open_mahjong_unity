using System.Collections;
using UnityEngine;

public partial class GameCanvas : MonoBehaviour {
    private double _timerStepDeadline;
    private double _timerDeadline;
    private bool _retainClaimActionsAtZero;
    private TMPro.FontStyles _timerFontStyleBeforeWait;
    private TMPro.FontWeight _timerFontWeightBeforeWait;
    private bool _timerAutoSizeBeforeWait;
    private TMPro.TextWrappingModes _timerWrappingBeforeWait;
    private float _timerFontSizeBeforeWait, _timerFontMinBeforeWait, _timerFontMaxBeforeWait;
    public bool IsWaitingForDraw { get; private set; }
    public bool IsCountdownRunning => _countdownCoroutine != null && remianTimeText != null
        && _timerDeadline > Time.realtimeSinceStartupAsDouble;

    public void CorrectCountdownBudget(double remainingTime, double stepTime) {
        double now = Time.realtimeSinceStartupAsDouble;
        _timerStepDeadline = now + System.Math.Max(0, stepTime);
        _timerDeadline = _timerStepDeadline + System.Math.Max(0, remainingTime);
        _currentRemainingTime = (int)System.Math.Ceiling(System.Math.Max(0, remainingTime));
        _currentCutTime = (int)System.Math.Ceiling(System.Math.Max(0, stepTime));
        if (remianTimeText != null) RefreshRemainTimeDisplay();
    }
    // 显示倒计时
    public void LoadingRemianTime(int remainingTime, int cuttime) => LoadingRemianTime((double)remainingTime, cuttime);

    // Families with an exact server budget retain fractions until display.
    public void LoadingRemianTime(double remainingTime, double cuttime) => StartDecisionTimer(remainingTime, cuttime, false);

    /// <summary>独立短等待：零秒后保留按钮，直到服务器推进弃牌窗口。</summary>
    public void LoadingClaimWaitTime(double seconds) => StartDecisionTimer(seconds, 0, true);

    private void StartDecisionTimer(double remainingTime, double cuttime, bool retainClaimActionsAtZero){
        if (VotePanel.Instance != null && VotePanel.Instance.IsGameTimerSuppressed) {
            StopTimeRunning();
            return;
        }

        StopTimeRunning();
        _retainClaimActionsAtZero = retainClaimActionsAtZero;

        // 保存初始时间值
        _currentRemainingTime = (int)System.Math.Ceiling(System.Math.Max(0, remainingTime));
        _currentCutTime = (int)System.Math.Ceiling(System.Math.Max(0, cuttime));
        double now = Time.realtimeSinceStartupAsDouble;
        _timerStepDeadline = now + System.Math.Max(0, cuttime);
        _timerDeadline = _timerStepDeadline + System.Math.Max(0, remainingTime);

        // 设置倒计时初始值
        if (remianTimeText == null) return;
        if (_retainClaimActionsAtZero && remainingTime <= 0) {
            _countdownCoroutine = StartCoroutine(WaitingForDraw());
            return;
        }
        RefreshRemainTimeDisplay();
        TryPlayCountdownTickSound();

        // 启动倒计时协程
        _countdownCoroutine = StartCoroutine(CountdownTimer());
    }

    /// <summary>仅修正已开启的手动操作时钟；自动动作或已停止的窗口不会被重开。</summary>
    public void RebaseDecisionClock(double bankSeconds, double stepSeconds) {
        if (!IsCountdownRunning) return;
        CorrectCountdownBudget(bankSeconds, stepSeconds);
    }

    // 倒计时协程
    private IEnumerator CountdownTimer(){
        int lastSoundSecond = TotalRemainSeconds;
        while (true){
            if (VotePanel.Instance != null && VotePanel.Instance.IsGameTimerSuppressed) {
                StopTimeRunning();
                yield break;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            _currentCutTime = (int)System.Math.Ceiling(System.Math.Max(0, _timerStepDeadline - now));
            _currentRemainingTime = (int)System.Math.Ceiling(System.Math.Max(0,
                _timerDeadline - System.Math.Max(now, _timerStepDeadline)));

            if (remianTimeText == null) yield break;
            RefreshRemainTimeDisplay();
            if (TotalRemainSeconds != lastSoundSecond) {
                lastSoundSecond = TotalRemainSeconds;
                TryPlayCountdownTickSound();
            }

            // 剩余时间为0 结束协程
            if (_currentRemainingTime <= 0 && _currentCutTime <= 0){
                if (_retainClaimActionsAtZero) {
                    yield return WaitingForDraw();
                    break;
                }
                remianTimeText.text = "";
                NormalGameStateManager.Instance?.SwitchCurrentPlayer("self","TimeOut",0);
                break;
            }
            yield return null; // unscaled deadline also advances through timeScale=0 and long frames
        }
    }

    private IEnumerator WaitingForDraw() {
        IsWaitingForDraw = true;
        _timerFontStyleBeforeWait = remianTimeText.fontStyle;
        _timerFontWeightBeforeWait = remianTimeText.fontWeight;
        _timerAutoSizeBeforeWait = remianTimeText.enableAutoSizing;
        _timerWrappingBeforeWait = remianTimeText.textWrappingMode;
        _timerFontSizeBeforeWait = remianTimeText.fontSize;
        _timerFontMinBeforeWait = remianTimeText.fontSizeMin;
        _timerFontMaxBeforeWait = remianTimeText.fontSizeMax;
        remianTimeText.fontStyle &= ~TMPro.FontStyles.Bold;
        remianTimeText.fontWeight = TMPro.FontWeight.Regular;
        remianTimeText.fontSizeMin = Mathf.Min(18, _timerFontSizeBeforeWait);
        remianTimeText.fontSizeMax = _timerFontSizeBeforeWait;
        remianTimeText.enableAutoSizing = true;
        remianTimeText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        remianTimeText.color = Color.white;
        double started = Time.realtimeSinceStartupAsDouble;
        int lastDots = 0;
        AppLanguage? lastLanguage = null;
        string[] frames = null;
        while (_retainClaimActionsAtZero && remianTimeText != null) {
            if (VotePanel.Instance != null && VotePanel.Instance.IsGameTimerSuppressed) {
                StopTimeRunning();
                yield break;
            }
            // 语言/主题刷新也不能把这段中文等待提示重新加粗。
            if ((remianTimeText.fontStyle & TMPro.FontStyles.Bold) != 0)
                remianTimeText.fontStyle &= ~TMPro.FontStyles.Bold;
            if (remianTimeText.fontWeight != TMPro.FontWeight.Regular)
                remianTimeText.fontWeight = TMPro.FontWeight.Regular;
            int dots = ClaimWaitDotCount(Time.realtimeSinceStartupAsDouble - started);
            AppLanguage language = ConfigManager.CurrentLanguage;
            if (lastLanguage != language) {
                string message = PlatformText.GetContextual("等待下一位玩家摸牌", "sichuan");
                frames = new[] { message + ".", message + "..", message + "..." };
                lastLanguage = language;
                lastDots = 0;
            }
            if (dots != lastDots) {
                remianTimeText.text = frames[dots - 1];
                lastDots = dots;
            }
            yield return null;
        }
    }

    internal static int ClaimWaitDotCount(double elapsed) => 1 + (int)(System.Math.Max(0, elapsed) / 0.5 % 3);

    public void StopTimeRunning(){
        if (_countdownCoroutine != null) {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null; // 设置为null以避免重复停止
        }
        _currentRemainingTime = 0;
        _currentCutTime = 0;
        _retainClaimActionsAtZero = false;
        if (IsWaitingForDraw && remianTimeText != null) {
            remianTimeText.fontStyle = _timerFontStyleBeforeWait;
            remianTimeText.fontWeight = _timerFontWeightBeforeWait;
            remianTimeText.enableAutoSizing = _timerAutoSizeBeforeWait;
            remianTimeText.fontSizeMin = _timerFontMinBeforeWait;
            remianTimeText.fontSizeMax = _timerFontMaxBeforeWait;
            remianTimeText.fontSize = _timerFontSizeBeforeWait;
            remianTimeText.textWrappingMode = _timerWrappingBeforeWait;
        }
        IsWaitingForDraw = false;
        if (remianTimeText == null) return;
        remianTimeText.text = "";
        remianTimeText.color = Color.white;
    }

    private int TotalRemainSeconds => (int)System.Math.Ceiling(System.Math.Max(0,
        _timerDeadline - Time.realtimeSinceStartupAsDouble));

    private void RefreshRemainTimeDisplay() {
        if (_currentCutTime > 0){
            remianTimeText.text = $"{_currentRemainingTime}+{_currentCutTime}";
        } else {
            remianTimeText.text = $"{_currentRemainingTime}";
        }
        // 本巡总剩余 ≤5 秒变红（含仍在扣步时、储备不足的情况）
        remianTimeText.color = TotalRemainSeconds <= 5 ? Color.red : Color.white;
    }

    /// <summary>本巡总剩余为 3/2/1 秒时各播一次提示音。</summary>
    private void TryPlayCountdownTickSound() {
        int total = TotalRemainSeconds;
        if (total < 1 || total > 3) return;
        if (SoundManager.Instance == null) return;
        SoundManager.Instance.PlayCountdownTickSound();
    }
}
