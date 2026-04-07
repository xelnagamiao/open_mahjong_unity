using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class MatchingWidget : MonoBehaviour {
    public static MatchingWidget Instance { get; private set; }

    [SerializeField] private TMP_Text matchTypeText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private Button cancelButton;

    private float elapsedTime;
    private Coroutine timerCoroutine;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        cancelButton.onClick.AddListener(OnCancelClick);
        gameObject.SetActive(false);
    }

    public void Show(string matchTypeName) {
        gameObject.SetActive(true);
        matchTypeText.text = matchTypeName;
        elapsedTime = 0f;
        timerText.text = "00:00";
        if (timerCoroutine != null) StopCoroutine(timerCoroutine);
        timerCoroutine = StartCoroutine(TimerRoutine());
    }

    public void Hide() {
        if (timerCoroutine != null) {
            StopCoroutine(timerCoroutine);
            timerCoroutine = null;
        }
        gameObject.SetActive(false);
    }

    private IEnumerator TimerRoutine() {
        while (true) {
            yield return new WaitForSeconds(1f);
            elapsedTime += 1f;
            int minutes = Mathf.FloorToInt(elapsedTime / 60f);
            int seconds = Mathf.FloorToInt(elapsedTime % 60f);
            timerText.text = $"{minutes:00}:{seconds:00}";
        }
    }

    private void OnCancelClick() {
        MatchNetworkManager.Instance?.SendLeaveQueue();
    }
}
