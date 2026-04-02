using System.Collections;
using UnityEngine;
using TMPro;

public class EndLiujuPanel : MonoBehaviour {
    public static EndLiujuPanel Instance { get; private set; }

    [SerializeField] private TextMeshProUGUI liujuText;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void ShowLiujuPanel(string displayText = "流局") {
        if (liujuText != null) liujuText.text = displayText;
        gameObject.SetActive(true);
        StartCoroutine(AutoHideAfterDelay());
    }

    public void ClearEndLiujuPanel() {
        gameObject.SetActive(false);
    }

    private IEnumerator AutoHideAfterDelay() {
        yield return new WaitForSeconds(2f);
        gameObject.SetActive(false);
    }
}
