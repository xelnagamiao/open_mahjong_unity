using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 战术鸣牌等待面板（已弃用进度条；保留组件以免场景引用丢失，不再主动显示）。
/// 战术等待窗口由服务端 ask_other 的 remaining_time 驱动。
/// </summary>
public class TacticalCallPanel : MonoBehaviour {
    public static TacticalCallPanel Instance { get; private set; }

    [SerializeField] private Slider countdownSlider;
    [SerializeField] private float durationSeconds = 1.5f;

    private Coroutine activeCoroutine;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 显示战术鸣牌面板并从 0 滑动到 100，1.5 秒后自动隐藏。
    /// 若上一段倒计时仍在播放（出现连续打断），会停止旧倒计时并重新开始。
    /// </summary>
    public void ShowClaim() {
        StopActiveCoroutine();
        gameObject.SetActive(true);
        countdownSlider.minValue = 0f;
        countdownSlider.maxValue = 100f;
        countdownSlider.value = 0f;
        activeCoroutine = StartCoroutine(CoCountdown());
    }

    public void HidePanel() {
        StopActiveCoroutine();
        gameObject.SetActive(false);
    }

    private IEnumerator CoCountdown() {
        float elapsed = 0f;
        while (elapsed < durationSeconds) {
            elapsed += Time.deltaTime;
            float ratio = Mathf.Clamp01(elapsed / durationSeconds);
            countdownSlider.value = ratio * 100f;
            yield return null;
        }
        countdownSlider.value = 100f;
        activeCoroutine = null;
        gameObject.SetActive(false);
    }

    private void StopActiveCoroutine() {
        if (activeCoroutine != null) {
            StopCoroutine(activeCoroutine);
            activeCoroutine = null;
        }
    }
}
