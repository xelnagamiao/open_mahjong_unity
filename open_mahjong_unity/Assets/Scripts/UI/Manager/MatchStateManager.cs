using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 匹配排队状态与计时的持久化管理器。
/// 计时协程由 <see cref="CoroutineManager"/> 统一驱动，不挂在会被关闭的面板上。
/// <para>用法：把本脚本拖到一个常驻 GameObject 上即可；若场景中不存在，也会在首次访问 <see cref="Instance"/> 时自动创建。</para>
/// 面板仅作为视图：<see cref="MatchQueueingPanel"/> 随匹配页显隐；
/// <see cref="MatchFoundedPanel"/> 在 OverlayCanvas 上独立显示。计时状态由此处维护。
/// 排队已用时间和匹配成功倒计时都按 UTC 墙钟计算，不受 timeScale / 掉帧影响。
/// </summary>
public class MatchStateManager : MonoBehaviour {
    public const float MatchFoundDurationSeconds = 5f;

    public static MatchStateManager Instance {
        get {
            if (_instance == null) {
                var go = new GameObject("MatchStateManager");
                _instance = go.AddComponent<MatchStateManager>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    private static MatchStateManager _instance;

    /// <summary>是否正在排队（已加入队列且尚未取消 / 匹配成功 / 进入对局）。</summary>
    public bool IsQueueing { get; private set; }
    /// <summary>是否已匹配成功（进入倒计时进场阶段）。</summary>
    public bool IsMatchFound { get; private set; }
    /// <summary>当前排队的展示标题（已本地化），供面板恢复显示。</summary>
    public string QueueTitle { get; private set; }

    /// <summary>已排队秒数（墙钟）。</summary>
    public float ElapsedTime {
        get {
            if (elapsedFrozen) return frozenElapsed;
            if (!IsQueueing) return 0f;
            return (float)(DateTime.UtcNow - queueStartedUtc).TotalSeconds;
        }
    }

    /// <summary>匹配成功后距离进桌还剩多少秒（墙钟，下限 0）。</summary>
    public float MatchFoundRemaining {
        get {
            if (!IsMatchFound) return 0f;
            return Mathf.Max(
                0f,
                MatchFoundDurationSeconds - (float)(DateTime.UtcNow - matchFoundUtc).TotalSeconds
            );
        }
    }

    /// <summary>每秒触发一次，参数为最新的已排队秒数，供面板刷新文本。</summary>
    public event Action<float> OnElapsedTick;

    private DateTime queueStartedUtc;
    private DateTime matchFoundUtc;
    private float frozenElapsed;
    private bool elapsedFrozen = true;

    private void Awake() {
        if (_instance != null && _instance != this) {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        CoroutineManager.Ensure();
    }

    /// <summary>开始（重新）排队：重置计时并启动持久计时协程。</summary>
    public void StartQueueing(string queueTitle) {
        QueueTitle = queueTitle;
        IsQueueing = true;
        IsMatchFound = false;
        elapsedFrozen = false;
        frozenElapsed = 0f;
        queueStartedUtc = DateTime.UtcNow;
        OnElapsedTick?.Invoke(0f);
        RestartTimer();
    }

    /// <summary>确认仍在排队：已在排则只更新标题、不重置墙钟起点。</summary>
    public void EnsureQueueing(string queueTitle) {
        if (IsQueueing) {
            if (!string.IsNullOrEmpty(queueTitle)) {
                QueueTitle = queueTitle;
            }
            return;
        }
        StartQueueing(queueTitle);
    }

    /// <summary>匹配成功：停止排队计时但保留状态；仅首次记录墙钟起点。</summary>
    public void MarkMatchFound(string queueTitle = null) {
        if (!string.IsNullOrEmpty(queueTitle)) {
            QueueTitle = queueTitle;
        }
        if (!IsMatchFound) {
            frozenElapsed = ElapsedTime;
            elapsedFrozen = true;
            matchFoundUtc = DateTime.UtcNow;
        }
        IsMatchFound = true;
        StopTimer();
    }

    /// <summary>结束排队（取消 / 进入对局 / 离队）：清空状态并停止计时。</summary>
    public void StopQueueing() {
        IsQueueing = false;
        IsMatchFound = false;
        elapsedFrozen = true;
        frozenElapsed = 0f;
        StopTimer();
    }

    private void RestartTimer() {
        CoroutineManager.Instance.RunNamed(CoroutineKeys.MatchQueueTimer, TimerRoutine(), restartIfRunning: true);
    }

    private void StopTimer() {
        CoroutineManager.Instance.StopNamed(CoroutineKeys.MatchQueueTimer);
    }

    private IEnumerator TimerRoutine() {
        while (IsQueueing && !IsMatchFound) {
            yield return new WaitForSecondsRealtime(0.25f);
            OnElapsedTick?.Invoke(ElapsedTime);
        }
    }
}
