using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// 匹配已成功（found）后的进入游戏倒计时面板，与 <see cref="MatchQueueingPanel"/> 区分。显示时使用 <see cref="CanvasGroupFadeIn"/> 渐入。
/// </summary>
public class MatchFoundedPanel : MonoBehaviour {
    public static MatchFoundedPanel Instance { get; private set; }

    [SerializeField] private CanvasGroupFadeIn fadeIn;
    [SerializeField] private TMP_Text foundedMatchTypeText;
    [SerializeField] private TMP_Text foundedCountdownText;

    private Coroutine countdownCoroutine;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        gameObject.SetActive(false);
    }

    public void Show(string matchTypeName) {
        MatchQueueingPanel.Instance?.Hide();
        gameObject.SetActive(true);
        foundedMatchTypeText.text = matchTypeName;
        fadeIn.PlayFadeIn();
        if (countdownCoroutine != null) StopCoroutine(countdownCoroutine);
        countdownCoroutine = StartCoroutine(CountdownRoutine());
    }

    public void StopCountdownAndHide() {
        if (countdownCoroutine != null) {
            StopCoroutine(countdownCoroutine);
            countdownCoroutine = null;
        }
        gameObject.SetActive(false);
    }

    private IEnumerator CountdownRoutine() {
        for (int i = 5; i > 0; i--) {
            foundedCountdownText.text = $"{i} 秒后进入游戏...";
            yield return new WaitForSeconds(1f);
        }
        gameObject.SetActive(false);
        countdownCoroutine = null;
    }
}
