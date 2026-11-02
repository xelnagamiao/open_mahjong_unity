using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    [Header("共享建房界面")]
    [SerializeField] private RectTransform roomPageParent;
    [SerializeField] private RectTransform venueOverlayParent;

    private enum CreationContext { Room, Venue, Preset }
    private CreationContext context;
    private readonly Dictionary<CreationContext, CreationDraft> contextDrafts = new Dictionary<CreationContext, CreationDraft>();
    private CreationDraft initialDraft;
    private TMP_Dropdown[] basicDropdowns;
    private TMP_InputField[] basicInputs;
    private Toggle[] basicToggles;
    private ScrollRect[] basicScrolls;

    private void CacheBasicDraftControls() {
        var basic = transform;
        basicDropdowns = basic.GetComponentsInChildren<TMP_Dropdown>(true);
        basicInputs = basic.GetComponentsInChildren<TMP_InputField>(true);
        basicToggles = basic.GetComponentsInChildren<Toggle>(true);
        basicScrolls = basic.GetComponentsInChildren<ScrollRect>(true);
    }

    private sealed class DetailedDraft {
        public Dictionary<string, int> indices, fanValues;
        public bool showAllFans;
    }

    // The shared view keeps independent in-memory drafts; it never persists room passwords or seeds.
    private sealed class CreationDraft {
        public int rule, subRule, userId, riichiPreset, fuzhouBaseScore;
        public string defaultName;
        public bool duplicateEnabled, spectatorBeforeDuplicate;
        public Dictionary<TMP_Dropdown, int> dropdowns;
        public Dictionary<TMP_InputField, string> inputs;
        public Dictionary<Toggle, bool> toggles;
        public Dictionary<ScrollRect, Vector2> scrolls;
        public Dictionary<string, DetailedDraft> details;
    }

    public void PrepareForRoom() => PrepareContext(CreationContext.Room, null);

    private void PrepareContext(CreationContext next, string eventId) {
        EnsureInitialized();
        if (initialDraft == null) initialDraft = CaptureDraft();
        bool changing = context != next;
        if (changing) {
            CancelDetailedConfigChanges();
            contextDrafts[context] = CaptureDraft();
            foreach (var dropdown in GetComponentsInChildren<TMP_Dropdown>(true)) dropdown.Hide();
            gameObject.SetActive(false);
            if (context == CreationContext.Preset) ClearEventPresetState();
        }
        _venueEventId = next == CreationContext.Room ? null : eventId;
        context = next;
        var parent = next == CreationContext.Room ? roomPageParent : venueOverlayParent;
        if (parent && transform.parent != parent) transform.SetParent(parent, false);
        if (venueOverlayParent) venueOverlayParent.gameObject.SetActive(next != CreationContext.Room);
        if (changing) RestoreDraft(contextDrafts.TryGetValue(next, out var saved) ? saved : initialDraft);
        ApplyDefaultRoomNameForCurrentUser();
        RefreshVisibility();
        RefreshRoomPresentation();
    }

    private CreationDraft CaptureDraft() {
        return new CreationDraft {
            rule = chooseRule.value, subRule = SubRuleDropdown.value,
            userId = _roomNameBoundUserId, defaultName = _lastDefaultRoomName, riichiPreset = selectedRiichiPresetIndex,
            fuzhouBaseScore = fuzhouBaseScoreIndex,
            duplicateEnabled = _duplicateWasEnabled, spectatorBeforeDuplicate = _spectatorBeforeDuplicate,
            dropdowns = basicDropdowns.ToDictionary(x => x, x => x.value),
            inputs = basicInputs.ToDictionary(x => x, x => x.text),
            toggles = basicToggles.ToDictionary(x => x, x => x.isOn),
            scrolls = basicScrolls.ToDictionary(x => x, x => x.normalizedPosition),
            details = _detailedConfigStates.ToDictionary(pair => pair.Key, pair => new DetailedDraft {
                showAllFans = pair.Value.ShowAllFans,
                indices = new Dictionary<string, int>(pair.Value.SelectedIndices),
                fanValues = new Dictionary<string, int>(pair.Value.FanTaiOverrides)
            })
        };
    }

    private void RestoreDraft(CreationDraft draft) {
        chooseRule.SetValueWithoutNotify(draft.rule);
        OnRuleDropdownChanged(draft.rule);
        SubRuleDropdown.SetValueWithoutNotify(draft.subRule);
        OnSubRuleChanged(draft.subRule);
        foreach (var pair in draft.dropdowns) if (pair.Key) pair.Key.SetValueWithoutNotify(pair.Value);
        foreach (var pair in draft.inputs) if (pair.Key) pair.Key.SetTextWithoutNotify(pair.Value);
        foreach (var pair in draft.toggles) if (pair.Key && !pair.Value) pair.Key.SetIsOnWithoutNotify(false);
        foreach (var pair in draft.toggles) if (pair.Key && pair.Value) pair.Key.SetIsOnWithoutNotify(true);
        foreach (var pair in draft.details) {
            var state = GetDetailedConfigState(pair.Key);
            if (state == null) continue;
            state.SelectedIndices.Clear();
            foreach (var value in pair.Value.indices) state.SelectedIndices[value.Key] = value.Value;
            state.FanTaiOverrides.Clear();
            foreach (var value in pair.Value.fanValues) state.FanTaiOverrides[value.Key] = value.Value;
            state.HasSnapshot = state.HasFanEditorSnapshot = false;
            state.ShowAllFans = pair.Value.showAllFans;
            SyncDetailedConfigView(state);
        }
        fuzhouBaseScoreIndex = draft.fuzhouBaseScore;
        if (_fuzhouBaseScore) _fuzhouBaseScore.SetValueWithoutNotify(fuzhouBaseScoreIndex);
        selectedRiichiPresetIndex = draft.riichiPreset;
        _roomNameBoundUserId = draft.userId;
        _lastDefaultRoomName = draft.defaultName;
        _duplicateWasEnabled = draft.duplicateEnabled;
        _spectatorBeforeDuplicate = draft.spectatorBeforeDuplicate;
        TogglePassword(passwordToggle.isOn);
        ToggleSetRandomSeed(SetRandomSeedToggle.isOn);
        RefreshVisibility();
        RefreshSubRuleDescription();
        foreach (var pair in draft.scrolls) if (pair.Key) {
            pair.Key.StopMovement(); pair.Key.normalizedPosition = pair.Value;
        }
        ShowRoomHelp("");
    }
}
