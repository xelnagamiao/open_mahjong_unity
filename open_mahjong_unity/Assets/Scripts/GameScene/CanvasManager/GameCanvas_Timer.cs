using System.Collections;
using UnityEngine;
using UnityEngine.UI;

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
        remianTimeText.color = Color.white;
        if (_currentCutTime > 0){
            remianTimeText.text = $"{_currentRemainingTime}+{_currentCutTime}";
        } else {
            remianTimeText.text = $"{_currentRemainingTime}";
        }
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

            // 减少切牌时间
            if (_currentCutTime > 0){
                _currentCutTime--;
            }
            else if (_currentRemainingTime > 0){
                _currentRemainingTime--;
            }
            // 更新文本内容
            if (remianTimeText == null) yield break;
            if (_currentCutTime > 0){
                remianTimeText.text = $"{_currentRemainingTime}+{_currentCutTime}";
            } else {
                remianTimeText.text = $"{_currentRemainingTime}";
            }
            TryPlayCountdownTickSound();
            // 决定文本颜色 低于5秒时显示红色
            if (_currentRemainingTime <= 5 && _currentCutTime <= 0) {
                remianTimeText.color = Color.red;
            } else {
                remianTimeText.color = Color.white;
            }
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

    /// <summary>切牌时结束后，剩余秒数显示为 3/2/1 时各播一次提示音。</summary>
    private void TryPlayCountdownTickSound() {
        if (_currentCutTime > 0) return;
        if (_currentRemainingTime < 1 || _currentRemainingTime > 3) return;
        if (SoundManager.Instance == null) return;
        SoundManager.Instance.PlayCountdownTickSound();
    }
}
