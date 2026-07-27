using System.Collections;
using UnityEngine;

public partial class GameCanvas : MonoBehaviour {
    // 显示倒计时
    public void LoadingRemianTime(int remainingTime, int cuttime){
        if (VotePanel.Instance != null && VotePanel.Instance.IsGameTimerSuppressed) {
            StopTimeRunning();
            return;
        }

        // 停止可能正在运行的倒计时协程
        if (_countdownCoroutine != null)
            StopCoroutine(_countdownCoroutine);

        // 保存初始时间值
        _currentRemainingTime = remainingTime;
        _currentCutTime = cuttime;

        // 设置倒计时初始值
        if (remianTimeText == null) return;
        RefreshRemainTimeDisplay();
        TryPlayCountdownTickSound();

        // 启动倒计时协程
        _countdownCoroutine = StartCoroutine(CountdownTimer());
    }

    // 倒计时协程
    private IEnumerator CountdownTimer(){
        // 使用WaitForSeconds缓存，提高性能
        WaitForSeconds oneSecondWait = new WaitForSeconds(1.0f);

        while (_currentCutTime > 0 || _currentRemainingTime > 0){
            if (VotePanel.Instance != null && VotePanel.Instance.IsGameTimerSuppressed) {
                StopTimeRunning();
                yield break;
            }

            // 等待1秒
            yield return oneSecondWait;
            if (VotePanel.Instance != null && VotePanel.Instance.IsGameTimerSuppressed) {
                StopTimeRunning();
                yield break;
            }

            // 先扣步时，再扣储备
            if (_currentCutTime > 0){
                _currentCutTime--;
            }
            else if (_currentRemainingTime > 0){
                _currentRemainingTime--;
            }

            if (remianTimeText == null) yield break;
            RefreshRemainTimeDisplay();
            TryPlayCountdownTickSound();

            // 剩余时间为0 结束协程
            if (_currentRemainingTime <= 0 && _currentCutTime <= 0){
                remianTimeText.text = "";
                NormalGameStateManager.Instance.SwitchCurrentPlayer("self","TimeOut",0);
                break;
            }
        }
    }

    public void StopTimeRunning(){
        if (_countdownCoroutine != null) {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null; // 设置为null以避免重复停止
        }
        _currentRemainingTime = 0;
        _currentCutTime = 0;
        if (remianTimeText == null) return;
        remianTimeText.text = "";
        remianTimeText.color = Color.white;
    }

    private int TotalRemainSeconds => _currentRemainingTime + _currentCutTime;

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
