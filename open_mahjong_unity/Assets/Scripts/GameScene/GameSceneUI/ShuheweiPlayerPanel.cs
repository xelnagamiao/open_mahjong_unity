using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum ShuheweiPanelPosition { Self, Left, Top, Right }

public class ShuheweiPlayerPanel : MonoBehaviour {
    [SerializeField] private ShuheweiPanelPosition position;
    [SerializeField] private TextMeshProUGUI userNameText;
    [SerializeField] private TextMeshProUGUI totalScoreText;
    [SerializeField] private TextMeshProUGUI roundFuText;
    [SerializeField] private TextMeshProUGUI roundFanText;
    [SerializeField] private TextMeshProUGUI roundFuPointText;
    [SerializeField] private Image readyImage;
    [SerializeField] private Transform fanContainer;

    public ShuheweiPanelPosition Position { get { return position; } }
    public TextMeshProUGUI UserNameText { get { return userNameText; } }
    public TextMeshProUGUI TotalScoreText { get { return totalScoreText; } }
    public TextMeshProUGUI RoundFuText { get { return roundFuText; } }
    public TextMeshProUGUI RoundFanText { get { return roundFanText; } }
    public TextMeshProUGUI RoundFuPointText { get { return roundFuPointText; } }
    public Image ReadyImage { get { return readyImage; } }
    public Transform FanContainer { get { return fanContainer; } }

    public void SetUserName(string userName) {
        UserNameText.text = userName;
    }

    public void SetReady(bool isReady) {
        ReadyImage.gameObject.SetActive(isReady);
    }

    public void ResetRoundStats() {
        RoundFuText.text = "0";
        RoundFanText.text = "0";
        RoundFuPointText.text = "0";
        TotalScoreText.text = "";
    }

    public void SetTotalScore(int totalScore, int change) {
        if (change > 0) {
            TotalScoreText.text = $"{totalScore}<color=green>+{change}</color>";
            return;
        }
        if (change < 0) {
            TotalScoreText.text = $"{totalScore}<color=red>{change}</color>";
            return;
        }
        TotalScoreText.text = $"{totalScore}";
    }

    public IEnumerator PlayFuAndFanReveal(string[] fuTypes, string[] fanList, GameObject fanCountPrefab, float intervalSeconds) {
        ClearFanContainer();
        for (int i = 0; i < fuTypes.Length; i++) {
            GameObject fuInstance = Instantiate(fanCountPrefab, FanContainer);
            FanCount fuCount = fuInstance.GetComponent<FanCount>();
            string fuName = fuTypes[i];
            string fuDisplay = FanTextDictionary.GetFuDisplayText(fuName);
            fuCount.SetFanCount(fuName, fuDisplay);
            fuCount.ApplyFuColor();
            yield return new WaitForSeconds(intervalSeconds);
        }
        for (int i = 0; i < fanList.Length; i++) {
            GameObject fanInstance = Instantiate(fanCountPrefab, FanContainer);
            FanCount fanCount = fanInstance.GetComponent<FanCount>();
            string fanName = fanList[i];
            string fanDisplay = FanTextDictionary.GetFanDisplayText("classical/standard", fanName);
            fanCount.SetFanCount(fanName, fanDisplay);
            fanCount.ApplyFanColor();
            yield return new WaitForSeconds(intervalSeconds);
        }
    }

    public IEnumerator PlayRoundStatsAnimation(int targetFu, int targetFan, int targetFuPoint, float duration) {
        float elapsed = 0f;
        while (elapsed < duration) {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            int fuNow = Mathf.RoundToInt(Mathf.Lerp(0f, targetFu, t));
            int fanNow = Mathf.RoundToInt(Mathf.Lerp(0f, targetFan, t));
            int pointNow = Mathf.RoundToInt(Mathf.Lerp(0f, targetFuPoint, t));
            RoundFuText.text = $"{fuNow}";
            RoundFanText.text = $"{fanNow}";
            RoundFuPointText.text = $"{pointNow}";
            yield return null;
        }
        RoundFuText.text = $"{targetFu}";
        RoundFanText.text = $"{targetFan}";
        RoundFuPointText.text = $"{targetFuPoint}";
    }

    public void ClearFanContainer() {
        for (int i = FanContainer.childCount - 1; i >= 0; i--) {
            Destroy(FanContainer.GetChild(i).gameObject);
        }
    }
}
