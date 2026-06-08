using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 匹配排队状态与计时的持久化管理器。
/// 计时协程不再挂在会被关闭的面板上，而是放在常驻（DontDestroyOnLoad）对象上，
/// 这样在排队过程中切换到其它一级窗口再切回来，排队计时不会中断。
/// <para>用法：把本脚本拖到一个常驻 GameObject 上即可；若场景中不存在，也会在首次访问 <see cref="Instance"/> 时自动创建。</para>
/// 面板（<see cref="MatchQueueingPanel"/> / <see cref="MatchFoundedPanel"/>）仅作为视图，
/// 从这里读取状态并订阅 <see cref="OnElapsedTick"/> 刷新显示。
/// </summary>
public class MatchStateManager : MonoBehaviour {
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
    /// <summary>已排队秒数。</summary>
    public float ElapsedTime { get; private set; }

    /// <summary>每秒触发一次，参数为最新的已排队秒数，供面板刷新文本。</summary>
    public event Action<float> OnElapsedTick;

    private Coroutine timerCoroutine;

    private void Awake() {
        if (_instance != null && _instance != this) {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>开始（重新）排队：重置计时并启动持久计时协程。</summary>
    public void StartQueueing(string queueTitle) {
        QueueTitle = queueTitle;
        IsQueueing = true;
        IsMatchFound = false;
        ElapsedTime = 0f;
        OnElapsedTick?.Invoke(ElapsedTime);
        RestartTimer();
    }

    /// <summary>匹配成功：停止计时但保留排队标记，便于面板恢复显示。</summary>
    public void MarkMatchFound() {
        IsMatchFound = true;
        StopTimer();
    }

    /// <summary>结束排队（取消 / 进入对局 / 离队）：清空状态并停止计时。</summary>
    public void StopQueueing() {
        IsQueueing = false;
        IsMatchFound = false;
        ElapsedTime = 0f;
        StopTimer();
    }

    private void RestartTimer() {
        StopTimer();
        timerCoroutine = StartCoroutine(TimerRoutine());
    }

    private void StopTimer() {
        if (timerCoroutine != null) {
            StopCoroutine(timerCoroutine);
            timerCoroutine = null;
        }
    }

    private IEnumerator TimerRoutine() {
        while (true) {
            yield return new WaitForSeconds(1f);
            ElapsedTime += 1f;
            OnElapsedTick?.Invoke(ElapsedTime);
        }
    }
}
