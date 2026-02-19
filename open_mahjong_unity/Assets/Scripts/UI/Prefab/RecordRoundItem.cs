using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class RecordRoundItem : MonoBehaviour
{
    [SerializeField] private TMP_Text roundNameText;
    [SerializeField] private TMP_Text roundExtraText;
    [SerializeField] private Button roundButton;

    private int targetRoundIndex;

    private static readonly Dictionary<int, string> CurrentRoundTextGB = new Dictionary<int, string>() {
        {1, "东风东"}, {2, "东风南"}, {3, "东风西"}, {4, "东风北"},
        {5, "南风东"}, {6, "南风南"}, {7, "南风西"}, {8, "南风北"},
        {9, "西风东"}, {10, "西风南"}, {11, "西风西"}, {12, "西风北"},
        {13, "北风东"}, {14, "北风南"}, {15, "北风西"}, {16, "北风北"},
    };

    private static readonly Dictionary<int, string> CurrentRoundTextQingque = new Dictionary<int, string>() {
        {1, "东一局"}, {2, "东二局"}, {3, "东三局"}, {4, "东四局"},
        {5, "南一局"}, {6, "南二局"}, {7, "南三局"}, {8, "南四局"},
        {9, "西一局"}, {10, "西二局"}, {11, "西三局"}, {12, "西四局"},
        {13, "北一局"}, {14, "北二局"}, {15, "北三局"}, {16, "北四局"},
    };

    private static readonly Dictionary<int, string> CurrentRoundTextRiichi = new Dictionary<int, string>() {
        {1, "东一局"}, {2, "东二局"}, {3, "东三局"}, {4, "东四局"},
        {5, "南一局"}, {6, "南二局"}, {7, "南三局"}, {8, "南四局"},
        {9, "西一局"}, {10, "西二局"}, {11, "西三局"}, {12, "西四局"},
        {13, "北一局"}, {14, "北二局"}, {15, "北三局"}, {16, "北四局"},
    };

    public void Initialize(string rule, int roundIndex, int currentRound, int honbaCount) {
        targetRoundIndex = roundIndex;
        roundNameText.text = GetRoundName(rule, currentRound);
        roundExtraText.text = rule == "riichi" ? $"{honbaCount}本场" : string.Empty;
        roundButton.onClick.RemoveAllListeners();
        roundButton.onClick.AddListener(OnClickRound);
    }

    public void InitializeFromManager(string payload) {
        if (string.IsNullOrEmpty(payload)) return;
        string[] parts = payload.Split('|');
        if (parts.Length < 4) return;
        string rule = parts[0];
        int roundIndex = int.Parse(parts[1]);
        int currentRound = int.Parse(parts[2]);
        int honbaCount = int.Parse(parts[3]);
        Initialize(rule, roundIndex, currentRound, honbaCount);
    }

    private void OnClickRound() {
        GameRecordManager.Instance.GotoSelectRound(targetRoundIndex);
    }

    private string GetRoundName(string rule, int currentRound) {
        Dictionary<int, string> roundMap = null;
        if (rule == "guobiao") roundMap = CurrentRoundTextGB;
        else if (rule == "qingque") roundMap = CurrentRoundTextQingque;
        else if (rule == "riichi") roundMap = CurrentRoundTextRiichi;
        if (roundMap != null && roundMap.TryGetValue(currentRound, out string roundName)) {
            return roundName;
        }
        return $"第{roundIndexToText(currentRound)}局";
    }

    private string roundIndexToText(int index) {
        return index.ToString();
    }
}
