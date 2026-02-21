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
        return RoundTextDictionary.GetRoundName(rule, currentRound);
    }
}
