using System;
using System.Collections.Generic;
using TMPro;
using Taiwan;
using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    private sealed class DetailedConfigState {
        public readonly DetailedConfigDefinition Definition;
        public readonly Dictionary<string, TMP_Dropdown> Dropdowns = new Dictionary<string, TMP_Dropdown>();
        public readonly Dictionary<string, int> SelectedIndices = new Dictionary<string, int>();
        public readonly Dictionary<string, int> SnapshotIndices = new Dictionary<string, int>();
        public readonly Dictionary<string, int> FanTaiOverrides =
            new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> SnapshotFanTaiOverrides =
            new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> FanEditorSnapshot =
            new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, TMP_Dropdown> FanTaiDropdowns =
            new Dictionary<string, TMP_Dropdown>(StringComparer.Ordinal);
        public readonly Dictionary<string, GameObject> FanRows =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);
        public readonly Dictionary<string, GameObject> FanSectionHeaders =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);
        public GameObject Panel;
        public GameObject FanTablePanel;
        public RectTransform Content;
        public RectTransform FanContent;
        public ScrollRect Scroll;
        public ScrollRect FanScroll;
        public TMP_Dropdown PresetDropdown;
        public TMP_Text PresetDescription;
        public TMP_Dropdown FanTableEntryDropdown;
        public TMP_Dropdown FanFilterDropdown;
        public bool HasSnapshot;
        public bool HasFanEditorSnapshot;
        public bool ShowAllFans;
        public bool IsApplyingPreset;
        public bool IsRefreshingFanTable;

        public DetailedConfigState(DetailedConfigDefinition definition) {
            Definition = definition;
            foreach (DetailedConfigOption option in definition.Options) {
                SelectedIndices[option.Key] = option.DefaultIndex;
            }
        }
    }

    private readonly Dictionary<string, DetailedConfigState> _detailedConfigStates =
        new Dictionary<string, DetailedConfigState>();

    private DetailedConfigState GetDetailedConfigState(string ruleKey) {
        return !string.IsNullOrEmpty(ruleKey)
            && _detailedConfigStates.TryGetValue(ruleKey, out DetailedConfigState state)
                ? state
                : null;
    }

    private void BindDetailedConfigControls(string ruleKey) {
        if (!DetailedConfigRegistry.TryGet(
                ruleKey,
                out DetailedConfigDefinition definition)) return;

        Transform panelTransform = transform.Find($"DetailedConfigPanel_{ruleKey}");
        if (panelTransform == null) {
            Debug.LogError($"Missing prebuilt detailed config panel for rule '{ruleKey}'.", this);
            return;
        }

        var state = new DetailedConfigState(definition) {
            Panel = panelTransform.gameObject,
            Content = panelTransform.Find(
                "Dialog/Create_Panel/ScrollArea/Viewport/Content") as RectTransform,
            Scroll = panelTransform.Find(
                "Dialog/Create_Panel/ScrollArea")?.GetComponent<ScrollRect>(),
            FanTablePanel = panelTransform.Find("FanTablePanel")?.gameObject,
            FanContent = panelTransform.Find(
                "FanTablePanel/Dialog/Create_Panel/ScrollArea/Viewport/Content") as RectTransform,
            FanScroll = panelTransform.Find(
                "FanTablePanel/Dialog/Create_Panel/ScrollArea")?.GetComponent<ScrollRect>(),
        };
        _detailedConfigStates[ruleKey] = state;

        if (state.Content == null || state.Scroll == null) {
            Debug.LogError($"Prebuilt detailed config panel '{ruleKey}' is incomplete.", this);
            return;
        }
        ConfigureDetailedConfigScrollViewport(state.Scroll);
        ConfigureDetailedConfigScrollViewport(state.FanScroll);

        Transform presetRow = state.Content.Find("DetailedConfigPreset");
        state.PresetDropdown = presetRow?.GetComponentInChildren<TMP_Dropdown>(true);
        state.PresetDescription = state.Content.Find(
            "PresetDescription")?.GetComponent<TMP_Text>();
        if (state.PresetDropdown != null) {
            state.PresetDropdown.onValueChanged.RemoveAllListeners();
            state.PresetDropdown.onValueChanged.AddListener(
                index => ApplyDetailedConfigPreset(state, index));
        }

        foreach (DetailedConfigOption option in definition.Options) {
            Transform row = state.Content.Find($"DetailedConfig_{option.Key}");
            TMP_Dropdown dropdown = row?.GetComponentInChildren<TMP_Dropdown>(true);
            if (dropdown == null) continue;
            state.Dropdowns[option.Key] = dropdown;
            string optionKey = option.Key;
            dropdown.onValueChanged.RemoveAllListeners();
            dropdown.onValueChanged.AddListener(
                index => OnDetailedConfigOptionChanged(state, optionKey, index));
        }

        DetailedConfigFanTable table = definition.FanTable;
        if (table != null) {
            Transform fanEntryRow = state.Content.Find($"DetailedConfig_{table.Key}");
            state.FanTableEntryDropdown =
                fanEntryRow?.GetComponentInChildren<TMP_Dropdown>(true);
            Button openFanTableButton = fanEntryRow
                ?.Find("Dropdown/OpenFanTableEditor")
                ?.GetComponent<Button>();
            if (openFanTableButton != null) {
                openFanTableButton.onClick.RemoveAllListeners();
                openFanTableButton.onClick.AddListener(
                    () => ShowDetailedConfigFanTablePanel(state));
            }

            if (state.FanContent != null) {
                Transform filterRow = state.FanContent.Find("FanTableFilter");
                state.FanFilterDropdown =
                    filterRow?.GetComponentInChildren<TMP_Dropdown>(true);
                if (state.FanFilterDropdown != null) {
                    state.FanFilterDropdown.onValueChanged.RemoveAllListeners();
                    state.FanFilterDropdown.onValueChanged.AddListener(index => {
                        if (state.IsRefreshingFanTable) return;
                        state.ShowAllFans = index == 1;
                        RefreshDetailedConfigFanTable(state);
                    });
                }

                foreach (DetailedConfigFanValue fan in table.Fans) {
                    Transform row = state.FanContent.Find($"FanTai_{fan.Id}");
                    TMP_Dropdown dropdown =
                        row?.GetComponentInChildren<TMP_Dropdown>(true);
                    if (row == null || dropdown == null) continue;
                    state.FanRows[fan.Id] = row.gameObject;
                    state.FanTaiDropdowns[fan.Id] = dropdown;
                    string fanId = fan.Id;
                    dropdown.onValueChanged.RemoveAllListeners();
                    dropdown.onValueChanged.AddListener(
                        index => OnDetailedConfigFanTaiChanged(state, fanId, index));

                    Transform section = state.FanContent.Find($"Section_{fan.Section}");
                    if (section != null) {
                        state.FanSectionHeaders[fan.Section] = section.gameObject;
                    }
                }
            }
        }

        BindDetailedConfigDialogButtons(state);
        ApplyDetailedConfigPreset(state, definition.DefaultPresetIndex);
        if (state.FanTablePanel != null) state.FanTablePanel.SetActive(false);
        state.Panel.SetActive(false);
    }

    private static void ConfigureDetailedConfigScrollViewport(ScrollRect scroll) {
        if (scroll == null || scroll.viewport == null) return;
        Graphic viewportGraphic = scroll.viewport.GetComponent<Graphic>();
        if (viewportGraphic != null) viewportGraphic.raycastTarget = true;
        scroll.horizontal = false;
        scroll.vertical = true;
    }

    private void BindDetailedConfigDialogButtons(DetailedConfigState state) {
        Transform footer = state.Panel.transform.Find("Dialog/Create_Panel/Footer");
        Button resetButton = footer?.Find("Reset")?.GetComponent<Button>();
        Button cancelButton = footer?.Find("Cancel")?.GetComponent<Button>();
        Button confirmButton = footer?.Find("Confirm")?.GetComponent<Button>();
        if (resetButton != null) {
            resetButton.onClick.RemoveAllListeners();
            resetButton.onClick.AddListener(() => ResetDetailedConfig(state));
        }
        if (cancelButton != null) {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(() => CancelDetailedConfigChanges(state));
        }
        if (confirmButton != null) {
            confirmButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(() => ConfirmDetailedConfigChanges(state));
        }

        Transform fanFooter = state.FanTablePanel != null
            ? state.FanTablePanel.transform.Find("Dialog/Create_Panel/Footer")
            : null;
        Button fanResetButton = fanFooter?.Find("Reset")?.GetComponent<Button>();
        Button fanCancelButton = fanFooter?.Find("Cancel")?.GetComponent<Button>();
        Button fanConfirmButton = fanFooter?.Find("Confirm")?.GetComponent<Button>();
        if (fanResetButton != null) {
            fanResetButton.onClick.RemoveAllListeners();
            fanResetButton.onClick.AddListener(() => ResetDetailedConfigFanTable(state));
        }
        if (fanCancelButton != null) {
            fanCancelButton.onClick.RemoveAllListeners();
            fanCancelButton.onClick.AddListener(() => CancelDetailedConfigFanTable(state));
        }
        if (fanConfirmButton != null) {
            fanConfirmButton.onClick.RemoveAllListeners();
            fanConfirmButton.onClick.AddListener(() => ConfirmDetailedConfigFanTable(state));
        }
    }


    private static Dictionary<string, object> GetDetailedConfigSelectedValues(
        DetailedConfigState state) {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (DetailedConfigOption option in state.Definition.Options) {
            int selected = state.SelectedIndices.TryGetValue(option.Key, out int index)
                ? Mathf.Clamp(index, 0, option.ValueCount - 1)
                : option.DefaultIndex;
            values[option.Key] = option.GetValue(selected);
        }
        return values;
    }

    private static string GetDetailedConfigScoringPreset(
        DetailedConfigState state,
        DetailedConfigFanTable table) {
        if (!state.Definition.TryGetOption(
                table.PresetKey,
                out DetailedConfigOption presetOption)) {
            return string.Empty;
        }
        int selected = state.SelectedIndices.TryGetValue(table.PresetKey, out int index)
            ? Mathf.Clamp(index, 0, presetOption.ValueCount - 1)
            : presetOption.DefaultIndex;
        return presetOption.GetValue(selected)?.ToString() ?? string.Empty;
    }

    private static void RefreshDetailedConfigFanTableEntry(
        DetailedConfigState state) {
        TMP_Dropdown dropdown = state.FanTableEntryDropdown;
        if (dropdown == null) return;
        string summary = state.FanTaiOverrides.Count == 0
            ? "使用基础台表"
            : $"已自定义 {state.FanTaiOverrides.Count} 项";
        dropdown.ClearOptions();
        dropdown.AddOptions(new List<string> { summary });
        dropdown.SetValueWithoutNotify(0);
        dropdown.RefreshShownValue();
    }

    private void RefreshDetailedConfigFanTable(DetailedConfigState state) {
        DetailedConfigFanTable table = state.Definition.FanTable;
        if (table == null) return;
        string scoringPreset = GetDetailedConfigScoringPreset(state, table);
        Dictionary<string, object> values = GetDetailedConfigSelectedValues(state);

        state.IsRefreshingFanTable = true;
        try {
            if (state.FanFilterDropdown != null) {
                state.FanFilterDropdown.SetValueWithoutNotify(state.ShowAllFans ? 1 : 0);
                state.FanFilterDropdown.RefreshShownValue();
            }
            foreach (DetailedConfigFanValue fan in table.Fans) {
                int baseTai = table.GetPresetTai(scoringPreset, fan.Id);
                bool hasOverride = state.FanTaiOverrides.TryGetValue(
                    fan.Id,
                    out int customTai);
                int tai = hasOverride
                    ? customTai
                    : baseTai;
                if (state.FanTaiDropdowns.TryGetValue(
                        fan.Id,
                        out TMP_Dropdown dropdown)
                    && dropdown != null
                    && dropdown.options.Count > 0) {
                    int index = Mathf.Clamp(
                        tai - table.MinimumTai,
                        0,
                        dropdown.options.Count - 1);
                    dropdown.SetValueWithoutNotify(index);
                    dropdown.RefreshShownValue();
                    if (hasOverride && dropdown.captionText != null) {
                        dropdown.captionText.text = $"{tai}台（基础{baseTai}台）";
                    }
                }
                if (state.FanRows.TryGetValue(fan.Id, out GameObject row)
                    && row != null) {
                    row.SetActive(state.ShowAllFans || fan.IsEnabled(values));
                }
            }
            foreach (KeyValuePair<string, GameObject> entry in state.FanSectionHeaders) {
                bool hasVisibleFan = false;
                foreach (DetailedConfigFanValue fan in table.Fans) {
                    if (fan.Section == entry.Key
                        && state.FanRows.TryGetValue(fan.Id, out GameObject row)
                        && row != null
                        && row.activeSelf) {
                        hasVisibleFan = true;
                        break;
                    }
                }
                if (entry.Value != null) entry.Value.SetActive(hasVisibleFan);
            }
        } finally {
            state.IsRefreshingFanTable = false;
        }
        RefreshDetailedConfigFanTableEntry(state);
        if (state.FanContent != null) {
            LayoutRebuilder.ForceRebuildLayoutImmediate(state.FanContent);
        }
    }

    private void OnDetailedConfigFanTaiChanged(
        DetailedConfigState state,
        string fanId,
        int index) {
        if (state.IsRefreshingFanTable) return;
        DetailedConfigFanTable table = state.Definition.FanTable;
        if (table == null) return;
        int tai = Mathf.Clamp(
            table.MinimumTai + index,
            table.MinimumTai,
            table.MaximumTai);
        int baseTai = table.GetPresetTai(
            GetDetailedConfigScoringPreset(state, table),
            fanId);
        if (tai == baseTai) state.FanTaiOverrides.Remove(fanId);
        else state.FanTaiOverrides[fanId] = tai;
        RefreshDetailedConfigFanTable(state);
        RefreshDetailedConfigPresetSelection(state);
    }

    private void ShowDetailedConfigFanTablePanel(DetailedConfigState state) {
        if (state.FanTablePanel == null || state.FanTablePanel.activeSelf) return;
        state.FanEditorSnapshot.Clear();
        foreach (KeyValuePair<string, int> entry in state.FanTaiOverrides) {
            state.FanEditorSnapshot[entry.Key] = entry.Value;
        }
        state.HasFanEditorSnapshot = true;
        RefreshDetailedConfigFanTable(state);
        state.FanTablePanel.SetActive(true);
        state.FanTablePanel.transform.SetAsLastSibling();
        Canvas.ForceUpdateCanvases();
        if (state.FanScroll != null) state.FanScroll.verticalNormalizedPosition = 1f;
    }

    private void ResetDetailedConfigFanTable(DetailedConfigState state) {
        state.FanTaiOverrides.Clear();
        RefreshDetailedConfigFanTable(state);
        RefreshDetailedConfigPresetSelection(state);
    }

    private void CancelDetailedConfigFanTable(DetailedConfigState state) {
        if (state.HasFanEditorSnapshot) {
            state.FanTaiOverrides.Clear();
            foreach (KeyValuePair<string, int> entry in state.FanEditorSnapshot) {
                state.FanTaiOverrides[entry.Key] = entry.Value;
            }
        }
        ClearDetailedConfigFanEditorSnapshot(state);
        RefreshDetailedConfigFanTable(state);
        RefreshDetailedConfigPresetSelection(state);
        if (state.FanTablePanel != null) state.FanTablePanel.SetActive(false);
    }

    private void ConfirmDetailedConfigFanTable(DetailedConfigState state) {
        ClearDetailedConfigFanEditorSnapshot(state);
        if (state.FanTablePanel != null) state.FanTablePanel.SetActive(false);
    }

    private static void ClearDetailedConfigFanEditorSnapshot(
        DetailedConfigState state) {
        state.FanEditorSnapshot.Clear();
        state.HasFanEditorSnapshot = false;
    }

    private void ResetDetailedConfig(DetailedConfigState state) {
        ApplyDetailedConfigPreset(state, state.Definition.DefaultPresetIndex);
    }

    private void ApplyDetailedConfigPreset(DetailedConfigState state, int presetIndex) {
        if (presetIndex < 0 || presetIndex >= state.Definition.Presets.Count) {
            RefreshDetailedConfigPresetSelection(state);
            return;
        }

        DetailedConfigPreset preset = state.Definition.Presets[presetIndex];
        state.FanTaiOverrides.Clear();
        // Applying a base preset starts a new sparse-difference session.  Do
        // not let an editor that was open before the preset change restore
        // overrides belonging to the previous base table on Cancel.
        ClearDetailedConfigFanEditorSnapshot(state);
        state.IsApplyingPreset = true;
        try {
            foreach (DetailedConfigOption option in state.Definition.Options) {
                object value = state.Definition.GetPresetValue(preset, option);
                SetDetailedConfigDropdownValue(
                    state,
                    option,
                    option.FindValueIndex(value));
            }
            SetDetailedConfigPresetDropdownValue(state.PresetDropdown, presetIndex);
            if (state.PresetDescription != null) state.PresetDescription.text = preset.Description;
        } finally {
            state.IsApplyingPreset = false;
        }
        RefreshDetailedConfigFanTable(state);
    }

    private void OnDetailedConfigOptionChanged(DetailedConfigState state, string optionKey, int index) {
        if (state.IsApplyingPreset) return;
        if (!state.Definition.TryGetOption(optionKey, out DetailedConfigOption option)) return;
        SetDetailedConfigDropdownValue(state, option, index);
        if (state.Definition.FanTable != null
            && optionKey == state.Definition.FanTable.PresetKey) {
            // 基础台表一旦切换，旧差异不再具有可推断语义，直接丢弃。
            state.FanTaiOverrides.Clear();
            ClearDetailedConfigFanEditorSnapshot(state);
        }
        RefreshDetailedConfigFanTable(state);
        RefreshDetailedConfigPresetSelection(state);
    }

    private void RefreshDetailedConfigPresetSelection(DetailedConfigState state) {
        int matchingPreset = FindMatchingDetailedConfigPresetIndex(state);

        int displayedIndex = matchingPreset >= 0 ? matchingPreset : state.Definition.Presets.Count;
        SetDetailedConfigPresetDropdownValue(state.PresetDropdown, displayedIndex);
        if (state.PresetDescription != null) {
            state.PresetDescription.text = matchingPreset >= 0
                ? state.Definition.Presets[matchingPreset].Description
                : state.Definition.Presentation.CustomDescription;
        }
    }

    private static int FindMatchingDetailedConfigPresetIndex(DetailedConfigState state) {
        for (int i = 0; i < state.Definition.Presets.Count; i++) {
            if (DetailedConfigPresetMatches(state, state.Definition.Presets[i])) return i;
        }
        return -1;
    }

    private static bool DetailedConfigPresetMatches(DetailedConfigState state, DetailedConfigPreset preset) {
        if (state.FanTaiOverrides.Count > 0) return false;
        foreach (DetailedConfigOption option in state.Definition.Options) {
            int selected = state.SelectedIndices.TryGetValue(option.Key, out int index)
                ? Mathf.Clamp(index, 0, option.ValueCount - 1)
                : option.DefaultIndex;
            if (!DetailedConfigOption.ValuesEqual(
                    option.GetValue(selected),
                    state.Definition.GetPresetValue(preset, option))) {
                return false;
            }
        }
        return true;
    }

    private static void SetDetailedConfigDropdownValue(
        DetailedConfigState state,
        DetailedConfigOption option,
        int index) {
        int clamped = Mathf.Clamp(index, 0, option.ValueCount - 1);
        state.SelectedIndices[option.Key] = clamped;
        if (!state.Dropdowns.TryGetValue(option.Key, out TMP_Dropdown dropdown)
            || dropdown == null
            || dropdown.options.Count == 0) return;
        dropdown.SetValueWithoutNotify(Mathf.Clamp(clamped, 0, dropdown.options.Count - 1));
        dropdown.RefreshShownValue();
    }

    private static void SetDetailedConfigPresetDropdownValue(TMP_Dropdown dropdown, int index) {
        if (dropdown == null || dropdown.options.Count == 0) return;
        dropdown.SetValueWithoutNotify(Mathf.Clamp(index, 0, dropdown.options.Count - 1));
        dropdown.RefreshShownValue();
    }

    private static void CaptureDetailedConfigSnapshot(DetailedConfigState state) {
        state.SnapshotIndices.Clear();
        state.SnapshotFanTaiOverrides.Clear();
        foreach (KeyValuePair<string, int> entry in state.SelectedIndices) {
            state.SnapshotIndices[entry.Key] = entry.Value;
        }
        foreach (KeyValuePair<string, int> entry in state.FanTaiOverrides) {
            state.SnapshotFanTaiOverrides[entry.Key] = entry.Value;
        }
        state.HasSnapshot = true;
    }

    private void RestoreDetailedConfigSnapshot(DetailedConfigState state) {
        if (!state.HasSnapshot) return;
        state.IsApplyingPreset = true;
        try {
            foreach (DetailedConfigOption option in state.Definition.Options) {
                int index = state.SnapshotIndices.TryGetValue(option.Key, out int snapshotIndex)
                    ? snapshotIndex
                    : option.DefaultIndex;
                SetDetailedConfigDropdownValue(state, option, index);
            }
            state.FanTaiOverrides.Clear();
            foreach (KeyValuePair<string, int> entry in state.SnapshotFanTaiOverrides) {
                state.FanTaiOverrides[entry.Key] = entry.Value;
            }
        } finally {
            state.IsApplyingPreset = false;
        }
        RefreshDetailedConfigFanTable(state);
        RefreshDetailedConfigPresetSelection(state);
    }

    private void CancelDetailedConfigChanges(DetailedConfigState state) {
        RestoreDetailedConfigSnapshot(state);
        ClearDetailedConfigSnapshot(state);
        HideDetailedConfigPanel(state);
    }

    private void CancelDetailedConfigChanges() {
        foreach (DetailedConfigState state in _detailedConfigStates.Values) {
            CancelDetailedConfigChanges(state);
        }
    }

    private void ConfirmDetailedConfigChanges(DetailedConfigState state) {
        ClearDetailedConfigSnapshot(state);
        HideDetailedConfigPanel(state);
    }

    private static void ClearDetailedConfigSnapshot(DetailedConfigState state) {
        state.SnapshotIndices.Clear();
        state.SnapshotFanTaiOverrides.Clear();
        state.HasSnapshot = false;
    }

    private Dictionary<string, object> BuildDetailedConfigValues(string ruleKey) {
        DetailedConfigState state = GetDetailedConfigState(ruleKey);
        var result = new Dictionary<string, object>();
        if (state == null) return result;

        foreach (DetailedConfigOption option in state.Definition.Options) {
            int selected = state.SelectedIndices.TryGetValue(option.Key, out int index)
                ? Mathf.Clamp(index, 0, option.ValueCount - 1)
                : option.DefaultIndex;
            result[option.Key] = option.GetValue(selected);
        }
        if (state.Definition.FanTable != null) {
            result[state.Definition.FanTable.Key] =
                new Dictionary<string, int>(state.FanTaiOverrides, StringComparer.Ordinal);
        }
        return result;
    }

    private bool ShowDetailedConfigPanel(string ruleKey) {
        if (!DetailedConfigRegistry.TryGet(ruleKey, out _)) return false;
        DetailedConfigState state = GetDetailedConfigState(ruleKey);
        if (state == null || state.Panel == null || state.Panel.activeSelf) return true;
        CaptureDetailedConfigSnapshot(state);
        state.Panel.SetActive(true);
        state.Panel.transform.SetAsLastSibling();
        Canvas.ForceUpdateCanvases();
        if (state.Scroll != null) state.Scroll.verticalNormalizedPosition = 1f;
        return true;
    }

    private static void HideDetailedConfigPanel(DetailedConfigState state) {
        ClearDetailedConfigFanEditorSnapshot(state);
        if (state.FanTablePanel != null) state.FanTablePanel.SetActive(false);
        if (state.Panel != null) state.Panel.SetActive(false);
    }

    private void RefreshDetailedConfigEntry() {
        if (SubRuleDropdown == null) return;
        bool hasSubRule = RuleConfigs.TryGetValue(
            _ruleState,
            out Dictionary<string, object> ruleConfig)
            && ruleConfig.ContainsKey(CfgSubRule);
        bool showTaiwanDetailedConfig = _ruleState == "taiwan"
            && DetailedConfigRegistry.TryGet(_ruleState, out _);

        if (SubRuleText != null) {
            SubRuleText.text = showTaiwanDetailedConfig ? "设置馆规" : "子规则";
            SubRuleText.gameObject.SetActive(hasSubRule || showTaiwanDetailedConfig);
        }
        SubRuleDropdown.gameObject.SetActive(hasSubRule);
        SubRuleDropdown.enabled = true;
        if (DetailedConfigButton != null) {
            DetailedConfigButton.gameObject.SetActive(showTaiwanDetailedConfig);
        }
        if (SubRuleDropdown.transform.parent != null) {
            LayoutHierarchyRebuilder.RebuildUpwards(
                SubRuleDropdown.transform.parent,
                transform);
        }

        foreach (KeyValuePair<string, DetailedConfigState> entry in _detailedConfigStates) {
            if (entry.Key != _ruleState) CancelDetailedConfigChanges(entry.Value);
        }
    }
}
