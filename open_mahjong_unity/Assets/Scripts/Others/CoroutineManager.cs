using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局命名协程键。跨场景、跨面板的计时/延迟任务统一在此登记，避免各处私有 Coroutine 字段。
/// </summary>
public static class CoroutineKeys {
    public const string MatchQueueTimer = "match.queue_timer";
    public const string MatchFoundedCountdown = "match.founded_countdown";
    public const string MatchQueueingPanelDelay = "match.queueing_panel_delay";
    public const string CommitmentSaltHide = "ui.commitment_salt_hide";
    public const string BoardScoreDifference = "ui.board_score_difference";
    public const string BoardCurrentFlash = "ui.board_current_flash";
    public const string NetworkReconnect = "network.reconnect";
    public const string NetworkAndroidAutoReconnect = "network.android_auto_reconnect";
}

/// <summary>
/// 常驻协程管理器（DontDestroyOnLoad）。
/// 负责匹配计时、匹配成功倒计时、UI 临时展示、网络重连等不应挂在会被关闭的面板上的协程。
/// 动画/手牌等与具体 MonoBehaviour 生命周期强绑定的协程仍可在本地 StartCoroutine。
/// </summary>
public class CoroutineManager : MonoBehaviour {
    private static CoroutineManager _instance;

    public static CoroutineManager Instance {
        get {
            Ensure();
            return _instance;
        }
    }

    private readonly Dictionary<string, Coroutine> _named = new Dictionary<string, Coroutine>();

    public static void Ensure() {
        if (_instance != null) return;
        var go = new GameObject(nameof(CoroutineManager));
        _instance = go.AddComponent<CoroutineManager>();
        DontDestroyOnLoad(go);
    }

    private void Awake() {
        if (_instance != null && _instance != this) {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy() {
        if (_instance == this) {
            _instance = null;
        }
    }

    /// <summary>运行匿名协程（无命名，调用方自行持有 Coroutine 或不关心停止）。</summary>
    public Coroutine Run(IEnumerator routine) {
        return StartCoroutine(routine);
    }

    /// <summary>
    /// 以 key 运行协程；<paramref name="restartIfRunning"/> 为 true 时会先停止同名任务（用于重置倒计时）。
    /// </summary>
    public void RunNamed(string key, IEnumerator routine, bool restartIfRunning = true) {
        if (routine == null) return;
        if (string.IsNullOrEmpty(key)) {
            Run(routine);
            return;
        }
        if (restartIfRunning) {
            StopNamed(key);
        }
        _named[key] = StartCoroutine(NamedWrapper(key, routine));
    }

    public void StopNamed(string key) {
        if (string.IsNullOrEmpty(key) || !_named.TryGetValue(key, out Coroutine running) || running == null) {
            return;
        }
        StopCoroutine(running);
        _named.Remove(key);
    }

    public bool IsNamedRunning(string key) {
        return !string.IsNullOrEmpty(key) && _named.ContainsKey(key);
    }

    public void StopAllNamed() {
        foreach (KeyValuePair<string, Coroutine> pair in _named) {
            if (pair.Value != null) {
                StopCoroutine(pair.Value);
            }
        }
        _named.Clear();
    }

    /// <summary>下一帧执行回调；可选 key，便于取消或防重复。</summary>
    public void RunNextFrame(Action callback, string key = null, bool restartIfRunning = true) {
        if (callback == null) return;
        string runKey = string.IsNullOrEmpty(key) ? $"anonymous.next_frame.{Guid.NewGuid():N}" : key;
        RunNamed(runKey, NextFrameRoutine(callback), restartIfRunning);
    }

    private IEnumerator NextFrameRoutine(Action callback) {
        yield return null;
        callback?.Invoke();
    }

    private IEnumerator NamedWrapper(string key, IEnumerator routine) {
        yield return routine;
        _named.Remove(key);
    }
}
