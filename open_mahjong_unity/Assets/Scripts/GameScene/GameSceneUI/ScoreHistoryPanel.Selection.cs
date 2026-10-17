using System.Collections.Generic;
using TMPro;
using UnityEngine;

public partial class ScoreHistoryPanel {
    private readonly Dictionary<int, UnityEngine.UI.Toggle> playerSelections = new();
    private readonly List<(UnityEngine.UI.Toggle toggle, int roundNumber, int recordRoundIndex)> roundSelections = new();
    private UnityEngine.UI.ToggleGroup playerSelectionGroup;
    private UnityEngine.UI.ToggleGroup roundSelectionGroup;
    private bool isRecordScoreboard;

    private void EnsureSelectionGroups() {
        if (playerSelectionGroup == null && player0UserName != null)
            playerSelectionGroup = ScoreHistoryCellVisuals.EnsureToggleGroup(player0UserName.transform.parent, false);
        if (roundSelectionGroup == null && RoundIndexContainer != null)
            roundSelectionGroup = ScoreHistoryCellVisuals.EnsureToggleGroup(RoundIndexContainer.transform, false);
    }

    private void BindPlayerSelection(TMP_Text text, int originalIndex) {
        var toggle = ScoreHistoryCellVisuals.CreateToggle(text, playerSelectionGroup);
        if (playerSelections.TryGetValue(originalIndex, out var existing) && existing == toggle) return;
        playerSelections[originalIndex] = toggle;
        toggle.onValueChanged.AddListener(selected => {
            if (!selected) return;
            var record = ActiveRecord();
            if (record != null && !record.BlocksRecordNavigation) {
                fanTooltip?.Hide();
                record.SwitchRecordPerspectiveToOriginalPlayerIndex(originalIndex);
            }
            RefreshSelections();
        });
    }

    private void BindRoundSelection(TMP_Text text, int roundNumber, int recordRoundIndex) {
        var toggle = ScoreHistoryCellVisuals.CreateToggle(text, roundSelectionGroup);
        roundSelections.Add((toggle, roundNumber, recordRoundIndex));
        toggle.onValueChanged.AddListener(selected => {
            if (!selected) return;
            fanTooltip?.Hide();
            ActiveRecord()?.TryGotoScoreHistoryRound(recordRoundIndex);
            RefreshSelections();
        });
    }

    private GameRecordManager ActiveRecord() {
        var record = GameRecordManager.Instance;
        return isRecordScoreboard && record != null && record.isActiveAndEnabled && record.gameRecord != null
            ? record : null;
    }

    // 视角也可由四角玩家菜单切换；这里只同步轻量选中状态，不重建表格或改变滚动位置。
    private void LateUpdate() => RefreshSelections();

    private void RefreshSelections() {
        var record = ActiveRecord();
        var live = !isRecordScoreboard ? NormalGameStateManager.Instance : null;
        int selectedPlayer = record != null ? record.selectedPlayerOriginalIndex : -1;
        if (live?.player_to_info != null && live.player_to_info.TryGetValue("self", out var self) && self != null)
            selectedPlayer = self.original_player_index;
        bool canSwitchPlayer = record != null && !record.BlocksRecordNavigation;
        UnityEngine.UI.Toggle selectedPlayerToggle = null;
        foreach (var pair in playerSelections) {
            var toggle = pair.Value;
            if (toggle == null) continue;
            SetInteractable(toggle, canSwitchPlayer);
            if (pair.Key == selectedPlayer && toggle.gameObject.activeInHierarchy) selectedPlayerToggle = toggle;
        }
        SelectToggle(playerSelectionGroup, selectedPlayerToggle);

        UnityEngine.UI.Toggle selectedRoundToggle = null;
        foreach (var row in roundSelections) {
            if (row.toggle == null) continue;
            bool canGoto = record != null && record.CanGotoScoreHistoryRound(row.recordRoundIndex);
            SetInteractable(row.toggle, canGoto);
            if (record != null) {
                // 连庄可拥有相同局名，选择必须使用真实 round_index；同局多次结算只标识一个入口。
                if (selectedRoundToggle == null && row.recordRoundIndex == record.currentRoundIndex)
                    selectedRoundToggle = row.toggle;
            } else if (live != null && row.roundNumber == live.currentRound) {
                selectedRoundToggle = row.toggle;
            }
        }
        SelectToggle(roundSelectionGroup, selectedRoundToggle);
    }

    private static void SelectToggle(UnityEngine.UI.ToggleGroup group, UnityEngine.UI.Toggle selected) {
        if (group == null) return;
        group.allowSwitchOff = selected == null;
        if (selected != null) selected.SetIsOnWithoutNotify(true);
        else if (group.AnyTogglesOn()) group.SetAllTogglesOff(false);
    }

    private static void SetInteractable(UnityEngine.UI.Toggle toggle, bool interactable) {
        if (toggle.interactable == interactable) return;
        toggle.interactable = interactable;
        // 实时表头和未来局仅展示状态；点击仍交给面板关闭已固定的主番详情。
        toggle.GetComponent<TMP_Text>().raycastTarget = interactable;
    }
}
