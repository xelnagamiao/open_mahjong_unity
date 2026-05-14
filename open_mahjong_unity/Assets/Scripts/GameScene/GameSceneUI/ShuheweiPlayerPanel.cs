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

    private void Awake() {
        HideReadyIndicators();
        readyImage.gameObject.SetActive(false);
    }

    public void SetUserName(string userName) {
        UserNameText.text = userName;
    }

    public void SetReady(bool isReady) {
        Image indicator = ResolveReadyImage();
        if (indicator != null) {
            indicator.gameObject.SetActive(isReady);
            return;
        }
        SetReadyByName(isReady);
    }

    public void ResetRoundStats() {
        RoundFuText.text = "副 0";
        RoundFanText.text = "番 0";
        RoundFuPointText.text = "本局副数 0*1 = 0";
        TotalScoreText.text = "本场总副数 0";
    }

    public void SetRoundStats(int fuValue, int fanValue, int fuPointValue) {
        RoundFuText.text = $"副 {fuValue}";
        RoundFanText.text = $"番 {fanValue}";
        int multiplier = 1;
        for (int i = 0; i < fanValue; i++) {
            multiplier *= 2;
        }
        RoundFuPointText.text = $"本局副数 {fuValue}*{multiplier} = {fuPointValue}";
    }

    public IEnumerator AnimateRoundStats(
        int fromFu,
        int toFu,
        int fromFan,
        int toFan,
        int fromFuPoint,
        int toFuPoint,
        float duration
    ) {
        float elapsed = 0f;
        while (elapsed < duration) {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            int fuNow = Mathf.RoundToInt(Mathf.Lerp(fromFu, toFu, t));
            int fanNow = Mathf.RoundToInt(Mathf.Lerp(fromFan, toFan, t));
            int pointNow = Mathf.RoundToInt(Mathf.Lerp(fromFuPoint, toFuPoint, t));
            SetRoundStats(fuNow, fanNow, pointNow);
            yield return null;
        }
        SetRoundStats(toFu, toFan, toFuPoint);
    }

    public void AppendFuType(string fuType, GameObject fanCountPrefab) {
        GameObject fuInstance = Instantiate(fanCountPrefab, FanContainer);
        FanCount fuCount = fuInstance.GetComponent<FanCount>();
        string fuDisplay = FanTextDictionary.GetFuDisplayText(fuType);
        string fuNameDisplay = FanTextDictionary.GetFuNameDisplayText(fuType);
        fuCount.SetFanCount(fuNameDisplay, fuDisplay);
        fuCount.ApplyFuColor();
    }

    public void AppendFan(string fanName, GameObject fanCountPrefab) {
        string fanDisplay = FanTextDictionary.GetFanDisplayText("classical/standard", fanName);
        if (fanDisplay == "0番") {
            return;
        }
        GameObject fanInstance = Instantiate(fanCountPrefab, FanContainer);
        FanCount fanCount = fanInstance.GetComponent<FanCount>();
        fanCount.SetFanCount(fanName, fanDisplay);
        fanCount.ApplyFanColor();
    }

    public static int ParseFuValue(string fuType) {
        string fuDisplay = FanTextDictionary.GetFuDisplayText(fuType);
        if (fuDisplay.EndsWith("副") && int.TryParse(fuDisplay.Replace("副", ""), out int val)) {
            return val;
        }
        return 0;
    }

    public static int ParseFanValue(string fanName) {
        string fanDisplay = FanTextDictionary.GetFanDisplayText("classical/standard", fanName);
        if (fanDisplay == "0番") {
            return 0;
        }
        if (fanDisplay.EndsWith("翻") && int.TryParse(fanDisplay.Replace("翻", ""), out int val)) {
            return val;
        }
        return 0;
    }

    public static int SumFuValues(string[] fuTypes) {
        int total = 0;
        for (int i = 0; i < fuTypes.Length; i++) {
            total += ParseFuValue(fuTypes[i]);
        }
        return total;
    }

    public static int SumFanValues(string[] fanList) {
        int total = 0;
        for (int i = 0; i < fanList.Length; i++) {
            total += ParseFanValue(fanList[i]);
        }
        return total;
    }

    public static int CalculateRoundFuPoint(int fu, int fan) {
        int point = fu;
        for (int i = 0; i < fan; i++) {
            point *= 2;
        }
        return Mathf.Min(300, point);
    }

    public IEnumerator PlayFuAndFanReveal(
        string[] fuTypes,
        string[] fanList,
        GameObject fanCountPrefab,
        float numberAnimationSeconds,
        float pauseSeconds
    ) {
        int currentFu = 0;
        int currentFan = 0;
        int currentFuPoint = 0;
        SetRoundStats(0, 0, 0);

        for (int i = 0; i < fuTypes.Length; i++) {
            string fuType = fuTypes[i];
            int fuAdd = ParseFuValue(fuType);
            int targetFu = currentFu + fuAdd;
            int targetFan = currentFan;
            int targetFuPoint = CalculateRoundFuPoint(targetFu, targetFan);
            AppendFuType(fuType, fanCountPrefab);
            yield return AnimateRoundStats(currentFu, targetFu, currentFan, targetFan, currentFuPoint, targetFuPoint, numberAnimationSeconds);
            currentFu = targetFu;
            currentFuPoint = targetFuPoint;
            yield return new WaitForSeconds(pauseSeconds);
        }

        for (int i = 0; i < fanList.Length; i++) {
            string fanName = fanList[i];
            int fanAdd = ParseFanValue(fanName);
            if (fanAdd <= 0) {
                continue;
            }
            int targetFu = currentFu;
            int targetFan = currentFan + fanAdd;
            int targetFuPoint = CalculateRoundFuPoint(targetFu, targetFan);
            AppendFan(fanName, fanCountPrefab);
            yield return AnimateRoundStats(currentFu, targetFu, currentFan, targetFan, currentFuPoint, targetFuPoint, numberAnimationSeconds);
            currentFan = targetFan;
            currentFuPoint = targetFuPoint;
            yield return new WaitForSeconds(pauseSeconds);
        }
    }

    public float EstimateRevealSeconds(string[] fuTypes, string[] fanList, float numberAnimationSeconds, float pauseSeconds) {
        return (fuTypes.Length + fanList.Length) * (numberAnimationSeconds + pauseSeconds);
    }

    public void SetTotalScore(int totalScore, int change) {
        if (change > 0) {
            TotalScoreText.text = $"本场总副数 {totalScore} <color=green>+{change}</color>";
            return;
        }
        if (change < 0) {
            TotalScoreText.text = $"本场总副数 {totalScore} <color=red>{change}</color>";
            return;
        }
        TotalScoreText.text = $"本场总副数 {totalScore}";
    }

    public void ClearFanContainer() {
        for (int i = FanContainer.childCount - 1; i >= 0; i--) {
            Destroy(FanContainer.GetChild(i).gameObject);
        }
    }

    public void HideReadyIndicators() {
        if (readyImage != null) {
            readyImage.gameObject.SetActive(false);
        }
        SetReadyByName(false);
    }

    private Image ResolveReadyImage() {
        if (readyImage != null) {
            return readyImage;
        }
        Image[] images = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++) {
            Image img = images[i];
            if (img != null && img.name.ToLower().Contains("ready")) {
                readyImage = img;
                return readyImage;
            }
        }
        return null;
    }

    private void SetReadyByName(bool isReady) {
        Image[] images = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++) {
            Image img = images[i];
            if (img != null && img.name.ToLower().Contains("ready")) {
                img.gameObject.SetActive(isReady);
            }
        }
    }
}
