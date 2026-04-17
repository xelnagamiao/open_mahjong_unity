using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 匹配排队中面板（与 <see cref="MatchFoundedPanel"/> 区分）。显示/收起使用 <see cref="PanelPopupTransition"/>，取消匹配时渐出后再发离开队列。
/// </summary>
public class MatchQueueingPanel : MonoBehaviour {
    public static MatchQueueingPanel Instance { get; private set; }

    [SerializeField] private PanelPopupTransition panelPopup;
    [SerializeField] private TMP_Text queueingMatchTypeText;
    [SerializeField] private TMP_Text queueingElapsedText;
    [SerializeField] private Button cancelQueueButton;

    private float elapsedTime;
    private Coroutine timerCoroutine;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        cancelQueueButton.onClick.AddListener(OnCancelClick);
        gameObject.SetActive(false);
    }

    public void Show(string matchTypeName) {
        queueingMatchTypeText.text = matchTypeName;
        elapsedTime = 0f;
        queueingElapsedText.text = "00:00";
        if (timerCoroutine != null) StopCoroutine(timerCoroutine);
        timerCoroutine = null;
        panelPopup.Show(() => {
            timerCoroutine = StartCoroutine(TimerRoutine());
        });
    }

    public void Hide() {
        if (timerCoroutine != null) {
            StopCoroutine(timerCoroutine);
            timerCoroutine = null;
        }
        panelPopup.Hide();
    }

    private IEnumerator TimerRoutine() {
        while (true) {
            yield return new WaitForSeconds(1f);
            elapsedTime += 1f;
            int minutes = Mathf.FloorToInt(elapsedTime / 60f);
            int seconds = Mathf.FloorToInt(elapsedTime % 60f);
            queueingElapsedText.text = $"{minutes:00}:{seconds:00}";
        }
    }

    private void OnCancelClick() {
        if (timerCoroutine != null) {
            StopCoroutine(timerCoroutine);
            timerCoroutine = null;
        }
        panelPopup.Hide(() => MatchNetworkManager.Instance?.SendLeaveQueue());
    }
}
