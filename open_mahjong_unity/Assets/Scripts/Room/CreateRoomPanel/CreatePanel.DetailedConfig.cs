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
        public TMP_Dropdown PresetDropdown;
        public TMP_Text PresetDescription;
        public TMP_Dropdown FanTableEntryDropdown;
        public TMP_Dropdown FanFilterDropdown;
        public float LabelColumnWidth;
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
        headerTitle.text = state.Definition.Presentation.DialogTitle;

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
        AddDetailedConfigFanTableEntry(state, content);

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

        EnsureDetailedConfigFanTablePanel(state, headerSource, bodySource, headerHeightRatio);
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

    private void AddDetailedConfigFanTableEntry(
        DetailedConfigState state,
        Transform parent) {
        DetailedConfigFanTable table = state.Definition.FanTable;
        if (table == null) return;

        AddDetailedConfigSectionHeader(parent, table.Section);
        GameObject row = Instantiate(HepaiWayPanel, parent);
        row.name = $"DetailedConfig_{table.Key}";
        row.SetActive(true);
        SetPanelLabel(row, table.Label);
        LayoutElement rowLayout = row.GetComponent<LayoutElement>();
        if (rowLayout == null) rowLayout = row.AddComponent<LayoutElement>();
        float sourceRowHeight = GetDetailedConfigSourceRowHeight();
        rowLayout.minHeight = sourceRowHeight;
        rowLayout.preferredHeight = sourceRowHeight;
        rowLayout.flexibleWidth = 1f;

        TMP_Dropdown dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
        if (dropdown == null) return;
        dropdown.onValueChanged.RemoveAllListeners();
        dropdown.ClearOptions();
        dropdown.AddOptions(new List<string> { "使用基础台表" });
        dropdown.SetValueWithoutNotify(0);
        dropdown.RefreshShownValue();
        dropdown.interactable = true;
        state.FanTableEntryDropdown = dropdown;
        LayoutDetailedConfigRow(state, row, dropdown);

        GameObject overlay = new GameObject(
            "OpenFanTableEditor",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        overlay.transform.SetParent(dropdown.transform, false);
        StretchDetailedConfigRect(
            overlay.GetComponent<RectTransform>(),
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);
        Image overlayImage = overlay.GetComponent<Image>();
        overlayImage.color = Color.clear;
        overlayImage.raycastTarget = true;
        Button button = overlay.GetComponent<Button>();
        button.targetGraphic = overlayImage;
        button.onClick.AddListener(() => ShowDetailedConfigFanTablePanel(state));
        RefreshDetailedConfigFanTableEntry(state);
    }

    private void EnsureDetailedConfigFanTablePanel(
        DetailedConfigState state,
        Transform headerSource,
        Transform bodySource,
        float headerHeightRatio) {
        DetailedConfigFanTable table = state.Definition.FanTable;
        if (table == null || state.FanTablePanel != null) return;

        state.FanTablePanel = new GameObject(
            "FanTablePanel",
            typeof(RectTransform));
        state.FanTablePanel.transform.SetParent(state.Panel.transform, false);
        StretchDetailedConfigRect(
            state.FanTablePanel.GetComponent<RectTransform>(),
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);

        GameObject backdropObject = new GameObject(
            "Backdrop",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        backdropObject.transform.SetParent(state.FanTablePanel.transform, false);
        StretchDetailedConfigRect(
            backdropObject.GetComponent<RectTransform>(),
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);
        Image backdrop = backdropObject.GetComponent<Image>();
        backdrop.sprite = null;
        backdrop.color = new Color(0.10f, 0.10f, 0.10f, 0.58f);
        backdrop.raycastTarget = true;

        GameObject dialogObject = new GameObject("Dialog", typeof(RectTransform));
        dialogObject.transform.SetParent(state.FanTablePanel.transform, false);
        StretchDetailedConfigRect(
            dialogObject.GetComponent<RectTransform>(),
            new Vector2(0.18f, 0.08f),
            new Vector2(0.82f, 0.92f),
            Vector2.zero,
            Vector2.zero);

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
            Destroy(state.FanTablePanel);
            state.FanTablePanel = null;
            return;
        }
        RetainDetailedConfigHeaderTitle(headerObject.transform, headerTitle.transform);
        headerTitle.text = "台种设置";

        GameObject scrollObject = new GameObject(
            "ScrollArea",
            typeof(RectTransform),
            typeof(ScrollRect));
        scrollObject.transform.SetParent(bodyObject.transform, false);
        StretchDetailedConfigRect(
            scrollObject.GetComponent<RectTransform>(),
            Vector2.zero,
            Vector2.one,
            new Vector2(54f, 112f),
            new Vector2(-54f, -24f));

        GameObject viewportObject = new GameObject(
            "Viewport",
            typeof(RectTransform),
            typeof(RectMask2D));
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
        contentObject.GetComponent<ContentSizeFitter>().verticalFit =
            ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 42f;

        AddDetailedConfigFanFilter(state, content);
        AddDetailedConfigFanRows(state, content);

        GameObject footerObject = new GameObject("Footer", typeof(RectTransform));
        footerObject.transform.SetParent(bodyObject.transform, false);
        StretchDetailedConfigRect(
            footerObject.GetComponent<RectTransform>(),
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(36f, 18f),
            new Vector2(-36f, 94f));
        Button resetButton = CreateDetailedConfigDialogButton(
            footerObject.transform,
            "Reset",
            "恢复基础台表",
            new Vector2(0.01f, 0.12f),
            new Vector2(0.31f, 0.88f));
        Button cancelButton = CreateDetailedConfigDialogButton(
            footerObject.transform,
            "Cancel",
            "取消",
            new Vector2(0.35f, 0.12f),
            new Vector2(0.65f, 0.88f));
        Button confirmButton = CreateDetailedConfigDialogButton(
            footerObject.transform,
            "Confirm",
            "确定",
            new Vector2(0.69f, 0.12f),
            new Vector2(0.99f, 0.88f));
        if (resetButton != null) {
            resetButton.onClick.AddListener(() => ResetDetailedConfigFanTable(state));
        }
        if (cancelButton != null) {
            cancelButton.onClick.AddListener(() => CancelDetailedConfigFanTable(state));
        }
        if (confirmButton != null) {
            confirmButton.onClick.AddListener(() => ConfirmDetailedConfigFanTable(state));
        }
        state.FanTablePanel.SetActive(false);
    }

    private void AddDetailedConfigFanFilter(
        DetailedConfigState state,
        Transform parent) {
        GameObject row = Instantiate(HepaiWayPanel, parent);
        row.name = "FanTableFilter";
        row.SetActive(true);
        SetPanelLabel(row, "显示范围");
        LayoutElement rowLayout = row.GetComponent<LayoutElement>();
        if (rowLayout == null) rowLayout = row.AddComponent<LayoutElement>();
        float sourceRowHeight = GetDetailedConfigSourceRowHeight();
        rowLayout.minHeight = sourceRowHeight;
        rowLayout.preferredHeight = sourceRowHeight;
        rowLayout.flexibleWidth = 1f;

        state.FanFilterDropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
        if (state.FanFilterDropdown == null) return;
        state.FanFilterDropdown.onValueChanged.RemoveAllListeners();
        state.FanFilterDropdown.ClearOptions();
        state.FanFilterDropdown.AddOptions(
            new List<string> { "仅显示已启用", "显示全部台种" });
        state.FanFilterDropdown.SetValueWithoutNotify(state.ShowAllFans ? 1 : 0);
        state.FanFilterDropdown.onValueChanged.AddListener(index => {
            if (state.IsRefreshingFanTable) return;
            state.ShowAllFans = index == 1;
            RefreshDetailedConfigFanTable(state);
        });
        LayoutDetailedConfigRow(state, row, state.FanFilterDropdown);
    }

    private void AddDetailedConfigFanRows(
        DetailedConfigState state,
        Transform parent) {
        DetailedConfigFanTable table = state.Definition.FanTable;
        if (table == null) return;
        var taiChoices = new List<string>();
        for (int tai = table.MinimumTai; tai <= table.MaximumTai; tai++) {
            taiChoices.Add($"{tai}台");
        }

        string previousSection = null;
        foreach (DetailedConfigFanValue fan in table.Fans) {
            if (previousSection != fan.Section) {
                GameObject header = AddDetailedConfigSectionHeader(parent, fan.Section);
                if (header != null) state.FanSectionHeaders[fan.Section] = header;
                previousSection = fan.Section;
            }

            GameObject row = Instantiate(HepaiWayPanel, parent);
            row.name = $"FanTai_{fan.Id}";
            row.SetActive(true);
            string label = string.IsNullOrEmpty(fan.Unit)
                ? fan.Label
                : $"{fan.Label}（{fan.Unit}）";
            SetPanelLabel(row, label);
            LayoutElement rowLayout = row.GetComponent<LayoutElement>();
            if (rowLayout == null) rowLayout = row.AddComponent<LayoutElement>();
            float sourceRowHeight = GetDetailedConfigSourceRowHeight();
            rowLayout.minHeight = sourceRowHeight;
            rowLayout.preferredHeight = sourceRowHeight;
            rowLayout.flexibleWidth = 1f;

            TMP_Dropdown dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
            if (dropdown == null) continue;
            dropdown.onValueChanged.RemoveAllListeners();
            dropdown.ClearOptions();
            dropdown.AddOptions(taiChoices);
            state.FanRows[fan.Id] = row;
            state.FanTaiDropdowns[fan.Id] = dropdown;
            LayoutDetailedConfigRow(state, row, dropdown);

            string fanId = fan.Id;
            dropdown.onValueChanged.AddListener(
                index => OnDetailedConfigFanTaiChanged(state, fanId, index));
        }
    }

    private GameObject AddDetailedConfigSectionHeader(Transform parent, string section) {
        TMP_Text source = FindDetailedConfigRowLabel(HepaiWayPanel);
        if (source == null) source = SubRuleDescriptionText;
        TMP_Text header = CloneDetailedConfigText(source, parent, $"Section_{section}", section);
        if (header == null) return null;
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
        return header.gameObject;
    }

    private void AddDetailedConfigPresetControls(DetailedConfigState state, Transform parent) {
        GameObject row = Instantiate(HepaiWayPanel, parent);
        row.name = "DetailedConfigPreset";
        row.SetActive(true);
        SetPanelLabel(row, state.Definition.Presentation.PresetLabel);
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
            names.Add(state.Definition.Presentation.CustomPresetName);
            state.PresetDropdown.AddOptions(names);
            state.PresetDropdown.onValueChanged.AddListener(index => ApplyDetailedConfigPreset(state, index));
            LayoutDetailedConfigRow(state, row, state.PresetDropdown);
        }

        string description = state.Definition.DefaultPreset.Description;
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
            sourceLabel.GetPreferredValues(definition.Presentation.PresetLabel).x + inheritedPadding);
        if (definition.FanTable != null) {
            requiredWidth = Mathf.Max(
                requiredWidth,
                sourceLabel.GetPreferredValues(definition.FanTable.Label).x + inheritedPadding);
            foreach (DetailedConfigFanValue fan in definition.FanTable.Fans) {
                string label = string.IsNullOrEmpty(fan.Unit)
                    ? fan.Label
                    : $"{fan.Label}（{fan.Unit}）";
                requiredWidth = Mathf.Max(
                    requiredWidth,
                    sourceLabel.GetPreferredValues(label).x + inheritedPadding);
            }
        }
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
        LayoutRebuilder.ForceRebuildLayoutImmediate(
            state.FanTablePanel.GetComponent<RectTransform>());
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
        RefreshDetailedConfigDropdownCaption(state, presetIndex);
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
        RefreshDetailedConfigDropdownCaption(state, matchingPreset);
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
        DetailedConfigState state = GetDetailedConfigState(ruleKey, true);
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
        ClearDetailedConfigFanEditorSnapshot(state);
        if (state.FanTablePanel != null) state.FanTablePanel.SetActive(false);
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
