using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 战术鸣牌等待面板：吃牌申请阶段固定 1.5 秒倒计时。
/// 服务端在切牌后/抢杠询问阶段，吃牌会广播 is_claim=true 的 do_action 并启动 0→100 滑动条；
/// 碰/和/杠/加杠仅在有更高优先级竞争者时才进入该阶段。
/// 若 1.5 秒内有更高优先级行为打断，服务端会重新广播 is_claim 申请，本面板会重新归零；
/// 期间没有打断时，服务端会下发 silent=true 的实际行为，此时面板隐藏。
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
