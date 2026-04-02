using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class EndShuheWeiPanel : MonoBehaviour {
    public static EndShuheWeiPanel Instance { get; private set; }

    [SerializeField] private TextMeshProUGUI titleText;

    [Header("Self")]
    [SerializeField] private TextMeshProUGUI SelfUserName;
    [SerializeField] private TextMeshProUGUI SelfScore;
    [SerializeField] private TextMeshProUGUI SelfFuCount;

    [Header("Left")]
    [SerializeField] private TextMeshProUGUI LeftUserName;
    [SerializeField] private TextMeshProUGUI LeftScore;
    [SerializeField] private TextMeshProUGUI LeftFuCount;

    [Header("Top")]
    [SerializeField] private TextMeshProUGUI TopUserName;
    [SerializeField] private TextMeshProUGUI TopScore;
    [SerializeField] private TextMeshProUGUI TopFuCount;

    [Header("Right")]
    [SerializeField] private TextMeshProUGUI RightUserName;
    [SerializeField] private TextMeshProUGUI RightScore;
    [SerializeField] private TextMeshProUGUI RightFuCount;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void ShowShuhewei(Dictionary<int, int> player_fu, Dictionary<int, int> player_to_score, Dictionary<int, int> score_changes, Dictionary<int, string> indexToPosition, Dictionary<string, PlayerInfoClass> player_to_info) {
        if (titleText != null) titleText.text = "数和尾";

        Dictionary<string, int> posToIndex = new Dictionary<string, int>();
        foreach (var kvp in indexToPosition) {
            posToIndex[kvp.Value] = kvp.Key;
        }

        FillPosition("self", posToIndex, player_to_info, player_to_score, score_changes, player_fu,
            SelfUserName, SelfScore, SelfFuCount);
        FillPosition("left", posToIndex, player_to_info, player_to_score, score_changes, player_fu,
            LeftUserName, LeftScore, LeftFuCount);
        FillPosition("top", posToIndex, player_to_info, player_to_score, score_changes, player_fu,
            TopUserName, TopScore, TopFuCount);
        FillPosition("right", posToIndex, player_to_info, player_to_score, score_changes, player_fu,
            RightUserName, RightScore, RightFuCount);

        gameObject.SetActive(true);
        StartCoroutine(AutoHideAfterDelay());
    }

    private void FillPosition(string pos, Dictionary<string, int> posToIndex,
        Dictionary<string, PlayerInfoClass> player_to_info,
        Dictionary<int, int> player_to_score, Dictionary<int, int> score_changes,
        Dictionary<int, int> player_fu,
        TextMeshProUGUI nameText, TextMeshProUGUI scoreText, TextMeshProUGUI fuText) {
        if (!posToIndex.ContainsKey(pos)) return;
        int playerIndex = posToIndex[pos];

        nameText.text = player_to_info.ContainsKey(pos) ? player_to_info[pos].username : "";

        int score = player_to_score.ContainsKey(playerIndex) ? player_to_score[playerIndex] : 0;
        int change = score_changes.ContainsKey(playerIndex) ? score_changes[playerIndex] : 0;
        if (change > 0) {
            scoreText.text = score.ToString() + $"<color=green>+{change}</color>";
        } else if (change < 0) {
            scoreText.text = score.ToString() + $"<color=red>{change}</color>";
        } else {
            scoreText.text = score.ToString();
        }

        int fu = player_fu.ContainsKey(playerIndex) ? player_fu[playerIndex] : 0;
        fuText.text = $"{fu}副";
    }

    public void ClearEndShuheWeiPanel() {
        gameObject.SetActive(false);
    }

    private IEnumerator AutoHideAfterDelay() {
        yield return new WaitForSeconds(5f);
        gameObject.SetActive(false);
    }
}
