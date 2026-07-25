using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    private sealed class DetailedConfigState {
        public readonly DetailedConfigDefinition Definition;
        public readonly Dictionary<string, TMP_Dropdown> Dropdowns = new Dictionary<string, TMP_Dropdown>();
        public readonly Dictionary<string, int> SelectedIndices = new Dictionary<string, int>();
        public readonly Dictionary<string, int> SnapshotIndices = new Dictionary<string, int>();
        public readonly Dictionary<string, object> AdditionalValues = new Dictionary<string, object>();
        public readonly Dictionary<string, object> SnapshotAdditionalValues = new Dictionary<string, object>();
        public GameObject Panel;
        public TMP_Dropdown PresetDropdown;
        public TMP_Text PresetDescription;
        public float LabelColumnWidth;
        public bool HasSnapshot;
        public bool IsApplyingPreset;

        public DetailedConfigState(DetailedConfigDefinition definition) {
            Definition = definition;
            foreach (DetailedConfigOption option in definition.Options) {
                SelectedIndices[option.Key] = FindDetailedConfigValueIndex(option.Values, option.DefaultValue);
            }
            foreach (KeyValuePair<string, object> entry in definition.AdditionalDefaults) {
                AdditionalValues[entry.Key] = entry.Value;
            }
        }
    }

    private readonly Dictionary<string, DetailedConfigState> _detailedConfigStates =
        new Dictionary<string, DetailedConfigState>();
    private Button _detailedConfigDropdownButton;

    private DetailedConfigState GetDetailedConfigState(string ruleKey, bool create) {
        if (string.IsNullOrEmpty(ruleKey)
            || !DetailedConfigRegistry.TryGet(ruleKey, out DetailedConfigDefinition definition)) {
            return null;
        }
        if (_detailedConfigStates.TryGetValue(ruleKey, out DetailedConfigState state) || !create) {
            return state;
        }
        state = new DetailedConfigState(definition);
        _detailedConfigStates[ruleKey] = state;
        ApplyDetailedConfigPreset(state, definition.DefaultPresetIndex);
        return state;
    }

    private void EnsureDetailedConfigControls(string ruleKey) {
        DetailedConfigState state = GetDetailedConfigState(ruleKey, true);
        if (state == null || state.Panel != null
            || HepaiWayPanel == null
            || HepaiWayDropdown == null
            || SubRuleDescriptionText == null
            || createButton == null) return;

        state.LabelColumnWidth = CalculateDetailedConfigLabelColumnWidth(state.Definition);

        Transform headerSource = transform.Find("HeaderPanel");
        Transform bodySource = transform.Find("Create_Panel");
        if (headerSource == null
            || bodySource == null
            || FindDetailedConfigHeaderTextSource(headerSource) == null) return;

        state.Panel = new GameObject(
            $"DetailedConfigPanel_{state.Definition.RuleKey}",
            typeof(RectTransform));
        state.Panel.transform.SetParent(transform, false);
        StretchDetailedConfigRect(
            state.Panel.GetComponent<RectTransform>(),
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);

        GameObject backdropObject = new GameObject(
            "Backdrop",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        backdropObject.transform.SetParent(state.Panel.transform, false);
        StretchDetailedConfigRect(
            backdropObject.GetComponent<RectTransform>(),
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);
        Image backdropImage = backdropObject.GetComponent<Image>();
        backdropImage.sprite = null;
        backdropImage.color = new Color(0.16f, 0.16f, 0.16f, 0.42f);
        backdropImage.raycastTarget = true;

        GameObject dialogObject = new GameObject("Dialog", typeof(RectTransform));
        dialogObject.transform.SetParent(state.Panel.transform, false);
        StretchDetailedConfigRect(
            dialogObject.GetComponent<RectTransform>(),
            new Vector2(0.18f, 0.10f),
            new Vector2(0.82f, 0.90f),
            Vector2.zero,
            Vector2.zero);

        RectTransform sourceHeaderRect = headerSource.GetComponent<RectTransform>();
        float headerHeightRatio = sourceHeaderRect.anchorMax.y - sourceHeaderRect.anchorMin.y;
        if (headerHeightRatio <= 0f || headerHeightRatio >= 1f) headerHeightRatio = 0.16f;

        GameObject bodyObject = Instantiate(bodySource.gameObject, dialogObject.transform, false);
        bodyObject.name = "Create_Panel";
        bodyObject.SetActive(true);
        StretchDetailedConfigRect(
            bodyObject.GetComponent<RectTransform>(),
            Vector2.zero,
            new Vector2(1f, 1f - headerHeightRatio),
            Vector2.zero,
            Vector2.zero);
        SetDetailedConfigChildrenActive(bodyObject.transform, false);
        Image bodyImage = bodyObject.GetComponent<Image>();
        if (bodyImage != null) bodyImage.raycastTarget = true;

        GameObject headerObject = Instantiate(headerSource.gameObject, dialogObject.transform, false);
        headerObject.name = "HeaderPanel";
        headerObject.SetActive(true);
        StretchDetailedConfigRect(
            headerObject.GetComponent<RectTransform>(),
            new Vector2(0f, 1f - headerHeightRatio),
            Vector2.one,
            Vector2.zero,
            Vector2.zero);
        TMP_Text headerTitle = FindDetailedConfigHeaderTextSource(headerObject.transform);
        if (headerTitle == null) {
            Destroy(state.Panel);
            state.Panel = null;
            return;
        }
        RetainDetailedConfigHeaderTitle(headerObject.transform, headerTitle.transform);
        headerTitle.text = state.Definition.DialogTitle;

        GameObject scrollObject = new GameObject("ScrollArea", typeof(RectTransform), typeof(ScrollRect));
        scrollObject.transform.SetParent(bodyObject.transform, false);
        StretchDetailedConfigRect(
            scrollObject.GetComponent<RectTransform>(),
            Vector2.zero,
            Vector2.one,
            new Vector2(54f, 112f),
            new Vector2(-54f, -24f));

        GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewportObject.transform.SetParent(scrollObject.transform, false);
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        StretchDetailedConfigRect(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        GameObject contentObject = new GameObject(
            "Content",
            typeof(RectTransform),
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter));
        contentObject.transform.SetParent(viewportObject.transform, false);
        RectTransform content = contentObject.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 12, 18);
        layout.spacing = 7f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 42f;

        AddDetailedConfigPresetControls(state, content);
        AddDetailedConfigOptionControls(state, content);

        GameObject footerObject = new GameObject("Footer", typeof(RectTransform));
        footerObject.transform.SetParent(bodyObject.transform, false);
        StretchDetailedConfigRect(
            footerObject.GetComponent<RectTransform>(),
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(36f, 18f),
            new Vector2(-36f, 94f));

        Button resetButton = CreateDetailedConfigDialogButton(footerObject.transform, "Reset", "恢复推荐", new Vector2(0.01f, 0.12f), new Vector2(0.31f, 0.88f));
        Button cancelButton = CreateDetailedConfigDialogButton(footerObject.transform, "Cancel", "取消", new Vector2(0.35f, 0.12f), new Vector2(0.65f, 0.88f));
        Button confirmButton = CreateDetailedConfigDialogButton(footerObject.transform, "Confirm", "确定", new Vector2(0.69f, 0.12f), new Vector2(0.99f, 0.88f));
        if (resetButton != null) resetButton.onClick.AddListener(() => ResetDetailedConfig(state));
        if (cancelButton != null) cancelButton.onClick.AddListener(() => CancelDetailedConfigChanges(state));
        if (confirmButton != null) confirmButton.onClick.AddListener(() => ConfirmDetailedConfigChanges(state));

        state.Panel.SetActive(false);
    }

    private void AddDetailedConfigOptionControls(DetailedConfigState state, Transform parent) {
        float sourceRowHeight = GetDetailedConfigSourceRowHeight();
        string previousSection = null;
        foreach (DetailedConfigOption option in state.Definition.Options) {
            if (previousSection != option.Section) {
                AddDetailedConfigSectionHeader(parent, option.Section);
                previousSection = option.Section;
            }
            GameObject row = Instantiate(HepaiWayPanel, parent);
            row.name = $"DetailedConfig_{option.Key}";
            row.SetActive(true);
            SetPanelLabel(row, option.Label);
            LayoutElement rowLayout = row.GetComponent<LayoutElement>();
            if (rowLayout == null) rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.minHeight = sourceRowHeight;
            rowLayout.preferredHeight = sourceRowHeight;
            rowLayout.flexibleWidth = 1f;

            TMP_Dropdown dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
            if (dropdown == null) continue;
            dropdown.onValueChanged.RemoveAllListeners();
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string>(option.Choices));
            state.Dropdowns[option.Key] = dropdown;
            LayoutDetailedConfigRow(state, row, dropdown);

            string optionKey = option.Key;
            dropdown.onValueChanged.AddListener(index => OnDetailedConfigOptionChanged(state, optionKey, index));
            SetDetailedConfigDropdownValue(state, option, state.SelectedIndices[option.Key]);
        }
    }

    private void AddDetailedConfigSectionHeader(Transform parent, string section) {
        TMP_Text source = FindDetailedConfigRowLabel(HepaiWayPanel);
        if (source == null) source = SubRuleDescriptionText;
        TMP_Text header = CloneDetailedConfigText(source, parent, $"Section_{section}", section);
        if (header == null) return;
        header.alignment = TextAlignmentOptions.Center;
        header.margin = new Vector4(8f, 4f, 8f, 2f);
        header.textWrappingMode = TextWrappingModes.NoWrap;
        header.overflowMode = TextOverflowModes.Ellipsis;
        LayoutElement headerLayout = header.GetComponent<LayoutElement>();
        if (headerLayout == null) headerLayout = header.gameObject.AddComponent<LayoutElement>();
        float inheritedRowHeight = GetDetailedConfigSourceRowHeight();
        headerLayout.minHeight = Mathf.Max(30f, inheritedRowHeight * 0.72f);
        headerLayout.preferredHeight = Mathf.Max(34f, inheritedRowHeight * 0.78f);
        headerLayout.flexibleWidth = 1f;
    }

    private void AddDetailedConfigPresetControls(DetailedConfigState state, Transform parent) {
        GameObject row = Instantiate(HepaiWayPanel, parent);
        row.name = "DetailedConfigPreset";
        row.SetActive(true);
        SetPanelLabel(row, state.Definition.PresetLabel);
        LayoutElement rowLayout = row.GetComponent<LayoutElement>();
        if (rowLayout == null) rowLayout = row.AddComponent<LayoutElement>();
        float sourceRowHeight = GetDetailedConfigSourceRowHeight();
        rowLayout.minHeight = sourceRowHeight;
        rowLayout.preferredHeight = sourceRowHeight;
        rowLayout.flexibleWidth = 1f;

        state.PresetDropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
        if (state.PresetDropdown != null) {
            state.PresetDropdown.onValueChanged.RemoveAllListeners();
            state.PresetDropdown.ClearOptions();
            var names = new List<string>();
            foreach (DetailedConfigPreset preset in state.Definition.Presets) names.Add(preset.Name);
            names.Add(state.Definition.CustomPresetName);
            state.PresetDropdown.AddOptions(names);
            state.PresetDropdown.onValueChanged.AddListener(index => ApplyDetailedConfigPreset(state, index));
            LayoutDetailedConfigRow(state, row, state.PresetDropdown);
        }

        string description = state.Definition.Presets.Count > 0
            ? state.Definition.Presets[state.Definition.DefaultPresetIndex].Description
            : state.Definition.CustomDescription;
        state.PresetDescription = CloneDetailedConfigText(
            SubRuleDescriptionText,
            parent,
            "PresetDescription",
            description);
    }

    private void LayoutDetailedConfigRow(DetailedConfigState state, GameObject row, TMP_Dropdown dropdown) {
        TMP_Text rowLabel = FindDetailedConfigRowLabel(row);
        if (rowLabel == null) return;

        rowLabel.textWrappingMode = TextWrappingModes.NoWrap;
        rowLabel.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform labelRect = rowLabel.rectTransform;
        float originalWidth = labelRect.rect.width;
        float widthDelta = Mathf.Max(0f, state.LabelColumnWidth - originalWidth);
        if (widthDelta > 0f) {
            Vector2 size = labelRect.sizeDelta;
            size.x += widthDelta;
            labelRect.sizeDelta = size;
            Vector2 position = labelRect.anchoredPosition;
            position.x += widthDelta * labelRect.pivot.x;
            labelRect.anchoredPosition = position;
        }

        if (dropdown == null) return;
        RectTransform dropdownRect = dropdown.GetComponent<RectTransform>();
        if (dropdownRect != null && widthDelta > 0f) {
            Vector2 offsetMin = dropdownRect.offsetMin;
            offsetMin.x += widthDelta;
            dropdownRect.offsetMin = offsetMin;
        }
        if (dropdown.captionText != null) {
            dropdown.captionText.textWrappingMode = TextWrappingModes.NoWrap;
            dropdown.captionText.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    private float CalculateDetailedConfigLabelColumnWidth(DetailedConfigDefinition definition) {
        TMP_Text sourceLabel = FindDetailedConfigRowLabel(HepaiWayPanel);
        if (sourceLabel == null) return 0f;

        float sourceWidth = sourceLabel.rectTransform.rect.width;
        if (sourceWidth <= 0f) sourceWidth = Mathf.Abs(sourceLabel.rectTransform.sizeDelta.x);
        float sourceTextWidth = sourceLabel.GetPreferredValues(sourceLabel.text).x;
        float inheritedPadding = Mathf.Max(0f, sourceWidth - sourceTextWidth);
        float requiredWidth = sourceWidth;
        foreach (DetailedConfigOption option in definition.Options) {
            requiredWidth = Mathf.Max(
                requiredWidth,
                sourceLabel.GetPreferredValues(option.Label).x + inheritedPadding);
        }
        requiredWidth = Mathf.Max(
            requiredWidth,
            sourceLabel.GetPreferredValues(definition.PresetLabel).x + inheritedPadding);
        return Mathf.Ceil(requiredWidth);
    }

    private float GetDetailedConfigSourceRowHeight() {
        RectTransform source = HepaiWayPanel != null ? HepaiWayPanel.GetComponent<RectTransform>() : null;
        if (source == null) return 1f;
        float height = Mathf.Abs(source.rect.height);
        if (height <= 0f) height = Mathf.Abs(source.sizeDelta.y);
        return Mathf.Max(1f, height);
    }

    private static TMP_Text FindDetailedConfigRowLabel(GameObject row) {
        if (row == null) return null;
        foreach (TMP_Text label in row.GetComponentsInChildren<TMP_Text>(true)) {
            if (label.GetComponentInParent<TMP_Dropdown>() == null) return label;
        }
        return null;
    }

    private static TMP_Text FindDetailedConfigHeaderTextSource(Transform header) {
        if (header == null) return null;
        foreach (TMP_Text text in header.GetComponentsInChildren<TMP_Text>(true)) {
            if (text.GetComponentInParent<TMP_Dropdown>() != null) continue;
            if (text.GetComponentInParent<Button>() != null) continue;
            return text;
        }
        return null;
    }

    private static TMP_Text CloneDetailedConfigText(
        TMP_Text source,
        Transform parent,
        string objectName,
        string value) {
        if (source == null) return null;
        TMP_Text clone = Instantiate(source, parent, false);
        clone.name = objectName;
        clone.gameObject.SetActive(true);
        clone.text = value;
        return clone;
    }

    private static void SetDetailedConfigChildrenActive(Transform parent, bool active) {
        if (parent == null) return;
        foreach (Transform child in parent) child.gameObject.SetActive(active);
    }

    private static void RetainDetailedConfigHeaderTitle(Transform parent, Transform title) {
        if (parent == null || title == null) return;
        foreach (Transform child in parent) {
            bool keep = child == title || title.IsChildOf(child);
            child.gameObject.SetActive(keep);
            if (keep && child != title) RetainDetailedConfigHeaderTitle(child, title);
        }
    }

    private Button CreateDetailedConfigDialogButton(
        Transform parent,
        string objectName,
        string label,
        Vector2 anchorMin,
        Vector2 anchorMax) {
        if (createButton == null) return null;
        Button button = Instantiate(createButton, parent);
        button.name = objectName;
        button.gameObject.SetActive(true);
        button.onClick.RemoveAllListeners();
        StretchDetailedConfigRect(
            button.GetComponent<RectTransform>(),
            anchorMin,
            anchorMax,
            new Vector2(6f, 4f),
            new Vector2(-6f, -4f));
        TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
        if (text != null) text.text = label;
        return button;
    }

    private static void StretchDetailedConfigRect(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax) {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        rect.localScale = Vector3.one;
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
        state.IsApplyingPreset = true;
        try {
            foreach (DetailedConfigOption option in state.Definition.Options) {
                object value = GetDetailedConfigPresetValue(preset, option);
                SetDetailedConfigDropdownValue(
                    state,
                    option,
                    FindDetailedConfigValueIndex(option.Values, value));
            }
            SetDetailedConfigAdditionalValuesFromPreset(state, preset);
            SetDetailedConfigPresetDropdownValue(state.PresetDropdown, presetIndex);
            if (state.PresetDescription != null) state.PresetDescription.text = preset.Description;
        } finally {
            state.IsApplyingPreset = false;
        }
        RefreshDetailedConfigDropdownCaption(state, presetIndex);
    }

    private void OnDetailedConfigOptionChanged(DetailedConfigState state, string optionKey, int index) {
        if (state.IsApplyingPreset) return;
        DetailedConfigOption option = FindDetailedConfigOption(state.Definition, optionKey);
        if (option == null) return;
        SetDetailedConfigDropdownValue(state, option, index);
        RefreshDetailedConfigPresetSelection(state);
    }

    private void RefreshDetailedConfigPresetSelection(DetailedConfigState state) {
        int matchingPreset = FindMatchingDetailedConfigPresetIndex(state);

        int displayedIndex = matchingPreset >= 0 ? matchingPreset : state.Definition.Presets.Count;
        if (matchingPreset >= 0) {
            SetDetailedConfigAdditionalValuesFromPreset(state, state.Definition.Presets[matchingPreset]);
        }
        SetDetailedConfigPresetDropdownValue(state.PresetDropdown, displayedIndex);
        if (state.PresetDescription != null) {
            state.PresetDescription.text = matchingPreset >= 0
                ? state.Definition.Presets[matchingPreset].Description
                : state.Definition.CustomDescription;
        }
        RefreshDetailedConfigDropdownCaption(state, matchingPreset);
    }

    private static int FindMatchingDetailedConfigPresetIndex(DetailedConfigState state) {
        for (int i = 0; i < state.Definition.Presets.Count; i++) {
            if (DetailedConfigPresetMatches(state, state.Definition.Presets[i])) return i;
        }
        return -1;
    }

    private static bool DetailedConfigPresetMatches(DetailedConfigState state, DetailedConfigPreset preset) {
        foreach (DetailedConfigOption option in state.Definition.Options) {
            int selected = state.SelectedIndices.TryGetValue(option.Key, out int index)
                ? Mathf.Clamp(index, 0, option.Values.Length - 1)
                : 0;
            if (!Equals(option.Values[selected], GetDetailedConfigPresetValue(preset, option))) return false;
        }
        return true;
    }

    private static object GetDetailedConfigPresetValue(
        DetailedConfigPreset preset,
        DetailedConfigOption option) {
        return preset.Values.TryGetValue(option.Key, out object value) ? value : option.DefaultValue;
    }

    private static DetailedConfigOption FindDetailedConfigOption(
        DetailedConfigDefinition definition,
        string optionKey) {
        foreach (DetailedConfigOption option in definition.Options) {
            if (option.Key == optionKey) return option;
        }
        return null;
    }

    private static bool IsDetailedConfigOptionKey(
        DetailedConfigDefinition definition,
        string key) {
        return FindDetailedConfigOption(definition, key) != null;
    }

    private static int FindDetailedConfigValueIndex(object[] values, object target) {
        for (int i = 0; i < values.Length; i++) {
            if (Equals(values[i], target)) return i;
        }
        return 0;
    }

    private static void SetDetailedConfigDropdownValue(
        DetailedConfigState state,
        DetailedConfigOption option,
        int index) {
        int clamped = Mathf.Clamp(index, 0, option.Values.Length - 1);
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

    private static void SetDetailedConfigAdditionalValuesFromPreset(
        DetailedConfigState state,
        DetailedConfigPreset preset) {
        state.AdditionalValues.Clear();
        foreach (KeyValuePair<string, object> entry in state.Definition.AdditionalDefaults) {
            state.AdditionalValues[entry.Key] = entry.Value;
        }
        foreach (KeyValuePair<string, object> entry in preset.Values) {
            if (!IsDetailedConfigOptionKey(state.Definition, entry.Key)) {
                state.AdditionalValues[entry.Key] = entry.Value;
            }
        }
    }

    private static void CaptureDetailedConfigSnapshot(DetailedConfigState state) {
        state.SnapshotIndices.Clear();
        foreach (KeyValuePair<string, int> entry in state.SelectedIndices) {
            state.SnapshotIndices[entry.Key] = entry.Value;
        }
        state.SnapshotAdditionalValues.Clear();
        foreach (KeyValuePair<string, object> entry in state.AdditionalValues) {
            state.SnapshotAdditionalValues[entry.Key] = entry.Value;
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
                    : FindDetailedConfigValueIndex(option.Values, option.DefaultValue);
                SetDetailedConfigDropdownValue(state, option, index);
            }
            state.AdditionalValues.Clear();
            foreach (KeyValuePair<string, object> entry in state.SnapshotAdditionalValues) {
                state.AdditionalValues[entry.Key] = entry.Value;
            }
        } finally {
            state.IsApplyingPreset = false;
        }
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
        state.SnapshotAdditionalValues.Clear();
        state.HasSnapshot = false;
    }

    private Dictionary<string, object> BuildDetailedConfigValues(string ruleKey) {
        DetailedConfigState state = GetDetailedConfigState(ruleKey, true);
        var result = new Dictionary<string, object>();
        if (state == null) return result;

        foreach (KeyValuePair<string, object> entry in state.AdditionalValues) {
            result[entry.Key] = entry.Value;
        }
        foreach (DetailedConfigOption option in state.Definition.Options) {
            int selected = state.SelectedIndices.TryGetValue(option.Key, out int index)
                ? Mathf.Clamp(index, 0, option.Values.Length - 1)
                : FindDetailedConfigValueIndex(option.Values, option.DefaultValue);
            result[option.Key] = option.Values[selected];
        }
        return result;
    }

    private bool ShowDetailedConfigPanel(string ruleKey) {
        if (!DetailedConfigRegistry.TryGet(ruleKey, out _)) return false;
        EnsureDetailedConfigControls(ruleKey);
        DetailedConfigState state = GetDetailedConfigState(ruleKey, false);
        if (state == null || state.Panel == null || state.Panel.activeSelf) return true;
        CaptureDetailedConfigSnapshot(state);
        state.Panel.SetActive(true);
        state.Panel.transform.SetAsLastSibling();
        LayoutRebuilder.ForceRebuildLayoutImmediate(state.Panel.GetComponent<RectTransform>());
        return true;
    }

    private static void HideDetailedConfigPanel(DetailedConfigState state) {
        if (state.Panel != null) state.Panel.SetActive(false);
    }

    private void EnsureDetailedConfigDropdownButton() {
        if (_detailedConfigDropdownButton != null || SubRuleDropdown == null) return;
        GameObject buttonObject = new GameObject(
            "DetailedConfigButton",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        buttonObject.transform.SetParent(SubRuleDropdown.transform, false);
        StretchDetailedConfigRect(
            buttonObject.GetComponent<RectTransform>(),
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);
        Image raycastImage = buttonObject.GetComponent<Image>();
        raycastImage.color = Color.clear;
        raycastImage.raycastTarget = true;

        _detailedConfigDropdownButton = buttonObject.GetComponent<Button>();
        _detailedConfigDropdownButton.targetGraphic = SubRuleDropdown.targetGraphic;
        _detailedConfigDropdownButton.transition = SubRuleDropdown.transition;
        _detailedConfigDropdownButton.colors = SubRuleDropdown.colors;
        _detailedConfigDropdownButton.spriteState = SubRuleDropdown.spriteState;
        _detailedConfigDropdownButton.animationTriggers = SubRuleDropdown.animationTriggers;
        _detailedConfigDropdownButton.navigation = SubRuleDropdown.navigation;
        _detailedConfigDropdownButton.onClick.AddListener(
            () => ShowDetailedConfigPanel(_ruleState));
        buttonObject.SetActive(false);
    }

    private void RefreshDetailedConfigDropdownCaption(
        DetailedConfigState state,
        int matchingPreset) {
        if (state == null
            || state.Definition.RuleKey != _ruleState
            || SubRuleDropdown == null
            || _detailedConfigDropdownButton == null) return;

        string displayName = matchingPreset >= 0
            ? state.Definition.Presets[matchingPreset].Name
            : "自定义规则";
        SubRuleDropdown.ClearOptions();
        SubRuleDropdown.AddOptions(new List<string> { displayName });
        SubRuleDropdown.SetValueWithoutNotify(0);
        SubRuleDropdown.RefreshShownValue();
    }

    private void RefreshDetailedConfigEntry() {
        if (SubRuleDropdown == null) return;
        bool hasSubRule = RuleConfigs.TryGetValue(
            _ruleState,
            out Dictionary<string, object> ruleConfig)
            && ruleConfig.ContainsKey(CfgSubRule);
        bool useDetailedConfigDropdown = !hasSubRule
            && DetailedConfigRegistry.TryGet(_ruleState, out _);

        EnsureDetailedConfigDropdownButton();
        SubRuleDropdown.gameObject.SetActive(hasSubRule || useDetailedConfigDropdown);
        SubRuleDropdown.enabled = true;
        _detailedConfigDropdownButton.gameObject.SetActive(useDetailedConfigDropdown);

        if (useDetailedConfigDropdown) {
            DetailedConfigState state = GetDetailedConfigState(_ruleState, true);
            RefreshDetailedConfigDropdownCaption(
                state,
                FindMatchingDetailedConfigPresetIndex(state));
        }
        if (SubRuleDropdown.transform.parent is RectTransform rowRect) {
            LayoutRebuilder.ForceRebuildLayoutImmediate(rowRect);
        }

        foreach (KeyValuePair<string, DetailedConfigState> entry in _detailedConfigStates) {
            if (entry.Key != _ruleState) CancelDetailedConfigChanges(entry.Value);
        }
    }
}
