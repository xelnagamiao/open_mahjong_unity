using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// 匹配已成功（found）后的进入游戏倒计时面板，与 <see cref="MatchQueueingPanel"/> 区分。
/// 应挂在 OverlayCanvas 上，不随 <see cref="MatchPanel"/> 窗口切换而隐藏。
/// </summary>
public class MatchFoundedPanel : MonoBehaviour {
    public static MatchFoundedPanel Instance { get; private set; }

    [SerializeField] private CanvasGroupFadeIn fadeIn;
    [SerializeField] private TMP_Text foundedMatchTypeText;
    [SerializeField] private TMP_Text foundedCountdownText;

    private CanvasGroup _canvasGroup;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        _canvasGroup = GetComponent<CanvasGroup>();
        gameObject.SetActive(false);
    }

    public void Show(string matchTypeName) {
        MatchStateManager.Instance.MarkMatchFound(matchTypeName);
        MatchQueueingPanel.Instance?.HideImmediately();
        Present(matchTypeName);
    }

    public void StopCountdownAndHide() {
        StopFoundedCountdown();
        ResetCanvasGroupBeforeHide();
        gameObject.SetActive(false);
    }

    private void Present(string matchTypeName) {
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        EnsureCanvasGroupInteractive();
        if (foundedMatchTypeText != null) {
            foundedMatchTypeText.text = matchTypeName;
        }
        if (fadeIn != null) {
            fadeIn.PlayFadeIn();
        } else {
            EnsureCanvasGroupOpaque();
        }
        StartFoundedCountdown();
    }

    private CanvasGroup GetCanvasGroup() {
        if (_canvasGroup == null) {
            _canvasGroup = GetComponent<CanvasGroup>();
        }
        return _canvasGroup;
    }

    private void EnsureCanvasGroupInteractive() {
        CanvasGroup cg = GetCanvasGroup();
        if (cg == null) return;
        cg.interactable = true;
        cg.blocksRaycasts = true;
    }

    private void EnsureCanvasGroupOpaque() {
        CanvasGroup cg = GetCanvasGroup();
        if (cg == null) return;
        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;
    }

    private void ResetCanvasGroupBeforeHide() {
        CanvasGroup cg = GetCanvasGroup();
        if (cg == null) return;
        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;
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
        ResetCanvasGroupBeforeHide();
        gameObject.SetActive(false);
    }
}
