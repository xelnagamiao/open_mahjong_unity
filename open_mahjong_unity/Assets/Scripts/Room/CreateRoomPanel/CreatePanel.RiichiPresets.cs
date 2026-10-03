using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    // Keep the chosen baseline when its individual options are customized.
    private int selectedRiichiPresetIndex;

    private void RefreshDetailedConfigButtonLabel() {
        if (!DetailedConfigButton) return;
        var label = DetailedConfigButton.GetComponentInChildren<TMP_Text>(true);
        if (!label) return;
        if (_ruleState != "riichi") { label.text = "馆规设置"; return; }
        var state = GetDetailedConfigState("riichi");
        int baseline = state != null && state.PendingRoomPreset >= 0
            ? state.PendingRoomPreset : selectedRiichiPresetIndex;
        bool changed = state != null
            && !DetailedConfigPresetMatches(state, state.Definition.Presets[baseline]);
        label.text = changed ? "高级设置 <color=#00AFC1>（已更改）</color>" : "高级设置";
    }

    public void ApplyRiichiPreset(int index) {
        if (_ruleState != "riichi" || index < 0 || index >= RiichiRoomRules.Presets.Length) return;
        var state = GetDetailedConfigState("riichi");
        if (state == null) return;
        ApplyDetailedConfigPreset(state, index);
        ApplyRiichiCommonPreset(index);
        state.PendingRoomPreset = -1;
    }

    private void ApplyRiichiCommonPreset(int index) {
        selectedRiichiPresetIndex = index;
        var scrollPosition = RoomContent.anchoredPosition;
        var values = RiichiRoomRules.Presets[index].Room;
        SubRuleDropdown.SetValueWithoutNotify(0);
        SelectGameTime((int)values["game_round"]);
        RiichiStartingScoreInput.text = values["starting_score"].ToString();
        RedDoraToggle.SetIsOnWithoutNotify((bool)values["red_dora"]);
        KuikaeToggle.SetIsOnWithoutNotify(!(bool)values["allow_kuikae"]);
        XiruToggle.SetIsOnWithoutNotify((bool)values["open_xiru"]);
        TobiToggle.SetIsOnWithoutNotify((bool)values["open_tobi"]);
        CuoHeheToggle.SetIsOnWithoutNotify((bool)values["open_cuohe"]);
        InputHepaiLimitToggle.SetIsOnWithoutNotify(false);
        HepaiLimitInput.text = values["hepai_limit"].ToString();
        HepaiWayDropdown.SetValueWithoutNotify(Array.IndexOf(new[] { "multi_ron", "three_ron_abort", "head_bump" }, (string)values["hepai_way"]));
        RefreshVisibility(); RefreshRoomPresentation(true); RefreshSubRuleDescription();
        Canvas.ForceUpdateCanvases();
        RoomContent.GetComponentInParent<ScrollRect>().StopMovement();
        RoomContent.anchoredPosition = scrollPosition;
    }

    private bool RiichiPresetMatches(int index) {
        var state = GetDetailedConfigState("riichi");
        if (state == null || SubRuleDropdown.value != 0) return false;
        if (!DetailedConfigPresetMatches(state, state.Definition.Presets[index])) return false;
        var values = RiichiRoomRules.Presets[index].Room;
        return GetSelectedGameTime() == (int)values["game_round"]
            && RiichiStartingScoreInput.text == values["starting_score"].ToString()
            && RedDoraToggle.isOn == (bool)values["red_dora"] && KuikaeToggle.isOn != (bool)values["allow_kuikae"]
            && XiruToggle.isOn == (bool)values["open_xiru"] && TobiToggle.isOn == (bool)values["open_tobi"]
            && CuoHeheToggle.isOn == (bool)values["open_cuohe"] && !InputHepaiLimitToggle.isOn
            && HepaiWayDropdown.value == Array.IndexOf(new[] { "multi_ron", "three_ron_abort", "head_bump" }, (string)values["hepai_way"]);
    }

}
