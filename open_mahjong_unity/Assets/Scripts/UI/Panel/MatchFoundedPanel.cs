using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// 匹配已成功（found）后的进入游戏倒计时面板，与 <see cref="MatchQueueingPanel"/> 区分。显示时使用 <see cref="CanvasGroupFadeIn"/> 渐入。
/// 倒计时协程由 <see cref="CoroutineManager"/> 统一管理。
/// </summary>
public class MatchFoundedPanel : MonoBehaviour {
    public static MatchFoundedPanel Instance { get; private set; }

    [SerializeField] private CanvasGroupFadeIn fadeIn;
    [SerializeField] private TMP_Text foundedMatchTypeText;
    [SerializeField] private TMP_Text foundedCountdownText;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        gameObject.SetActive(false);
    }

    public void Show(string matchTypeName) {
        MatchStateManager.Instance.MarkMatchFound();
        MatchQueueingPanel.Instance?.HideImmediately();
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        foundedMatchTypeText.text = matchTypeName;
        fadeIn.PlayFadeIn();
        StartFoundedCountdown();
    }

    /// <summary>
    /// 切回匹配窗口时若已匹配成功，恢复展示该面板（标题取自常驻 <see cref="MatchStateManager"/>）。
    /// 由 <see cref="MatchPanel.OnEnable"/> 调用。
    /// </summary>
    public void RestoreIfMatchFound() {
        MatchStateManager manager = MatchStateManager.Instance;
        if (manager == null || !manager.IsMatchFound) return;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        foundedMatchTypeText.text = manager.QueueTitle;
        fadeIn.PlayFadeIn();
        StartFoundedCountdown();
    }

    public void StopCountdownAndHide() {
        StopFoundedCountdown();
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 立即隐藏面板，但不结束匹配成功状态（供离开匹配窗口时隐藏视图用）。
    /// </summary>
    public void HideImmediately() {
        StopFoundedCountdown();
        gameObject.SetActive(false);
    }

    private void StartFoundedCountdown() {
        CoroutineManager.Ensure();
        CoroutineManager.Instance.RunNamed(
            CoroutineKeys.MatchFoundedCountdown,
            CountdownRoutine(),
            restartIfRunning: true
        );
    }

    private void StopFoundedCountdown() {
        CoroutineManager.Instance?.StopNamed(CoroutineKeys.MatchFoundedCountdown);
    }

    private IEnumerator CountdownRoutine() {
        for (int i = 5; i > 0; i--) {
            if (foundedCountdownText != null) {
                foundedCountdownText.text = $"{i} 秒后进入游戏...";
            }
            yield return new WaitForSeconds(1f);
        }
        gameObject.SetActive(false);
    }
}
