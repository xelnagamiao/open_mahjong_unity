using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;

public partial class EndGamePanel {
    Action recordReturnAction;
    string liveReturnSource;

    public bool IsRecordResult => recordReturnAction != null;
    public bool IsRecordResultVisible => IsRecordResult && gameObject.activeInHierarchy;

    /// <summary>Use the live end-game view with saved results and a replay-only return action.</summary>
    public void ShowRecordedGameEndPanel(RecordMatchResult result, Dictionary<string, object> gameTitle, Action onReturn) {
        if (onReturn == null) throw new ArgumentNullException(nameof(onReturn));
        SetRecordReturnAction(onReturn);
        ShowGameEndPanelContent(
            CommitmentSaltDisplay.ReadMasterSeedFromGameTitle(gameTitle),
            CommitmentSaltDisplay.ReadCommitmentFromGameTitle(gameTitle),
            CommitmentSaltDisplay.ReadSaltFromGameTitle(gameTitle),
            result.ToFinalPlayerData(), result.sichuan_match);
    }

    void SetRecordReturnAction(Action onReturn) {
        var caption = goHomeButton.GetComponentInChildren<TMP_Text>(true);
        if (liveReturnSource == null) {
            var binding = caption.GetComponent<LocalizedPlatformLabel>();
            liveReturnSource = binding != null && !string.IsNullOrEmpty(binding.Source) ? binding.Source : caption.text;
        }
        recordReturnAction = onReturn;
        TableText.Set(caption, IsRecordResult ? "返回大厅" : liveReturnSource);
        if (returnToTableButton) returnToTableButton.gameObject.SetActive(IsRecordResult);
    }

    static string FinalRatingText(Dictionary<string, object> player) {
        if (player.TryGetValue("rating_status", out var status) && status?.ToString() == "missing") return "— PT";
        bool elo = player.TryGetValue("rating_system", out var system) && system?.ToString() == "elo";
        // Live Qingque packets also identify Elo through their rule.
        elo |= player.TryGetValue("rating_rule", out var rule) && rule?.ToString() == "qingque"
            && player.TryGetValue("rating_completed_hands", out var hands) && hands != null;
        if (elo) {
            if (!player.TryGetValue("elo_delta", out var delta) || delta == null) return "-";
            return Convert.ToDouble(delta).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " Elo";
        }
        object points = player["pt"];
        if (player.TryGetValue("rating_system", out system) && system?.ToString() == "grade"
            && player.TryGetValue("rating_pt", out var saved) && saved != null) points = saved;
        return Convert.ToDouble(points).ToString("+0.00;-0.00;+0.00", CultureInfo.InvariantCulture) + " PT";
    }
}
