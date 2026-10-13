using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    private Toggle ChangshaKamoToggle;
    private GameObject ChangshaCapRow;
    private GameObject ChangshaCapTypePanel;
    private GameObject ChangshaCapPresetPanel;
    private GameObject ChangshaCapValuePanel;
    private TMP_Dropdown ChangshaCapTypeDropdown;
    private TMP_Dropdown ChangshaCapPresetDropdown;
    private TMP_InputField ChangshaCapValueInput;
    private int[] changshaCapPresets = new int[0];
    private bool refreshingChangshaCaps;

    /// <summary>运行时补齐旧Prefab的长沙选项；必须先创建控件，再应用规则默认值。</summary>
    private void EnsureChangshaKamoAndCapControls() {
        if (ChangshaOpenKongDropdown != null) {
            int previous = ChangshaOpenKongDropdown.options.Count == 4 ? ChangshaOpenKongDropdown.value + 1 : GetChangshaOpenKongCount();
            ChangshaOpenKongDropdown.ClearOptions();
            ChangshaOpenKongDropdown.AddOptions(new List<string> { "2张", "4张" });
            SetChangshaOpenKongCount(previous);
        }
        ChangshaKamoToggle = EnsureChangshaRuntimeToggle(ChangshaDealerBirdToggle != null ? ChangshaDealerBirdToggle : TacticalCallToggle, ChangshaKamoToggle);
        if (ChangshaKamoToggle != null) {
            // 克隆只复用样式，不继承模板互斥组及其他玩法的回调。
            ChangshaKamoToggle.group = null;
            ChangshaKamoToggle.onValueChanged = new Toggle.ToggleEvent();
        }
        if (ChangshaCapRow == null) {
            ChangshaCapRow = new GameObject("ChangshaCapRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            ChangshaCapRow.transform.SetParent(ChangshaScoreRow != null ? ChangshaScoreRow.transform.parent : transform, false);
            HorizontalLayoutGroup layout = ChangshaCapRow.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ChangshaCapRow.GetComponent<LayoutElement>().minHeight = 44f;
            if (ChangshaScoreRow != null) ChangshaCapRow.transform.SetSiblingIndex(ChangshaScoreRow.transform.GetSiblingIndex() + 1);
        }
        ChangshaCapTypePanel = EnsureChangshaRuntimeDropdown(ChangshaCapTypePanel, "ChangshaCapType", "封顶方式");
        ChangshaCapPresetPanel = EnsureChangshaRuntimeDropdown(ChangshaCapPresetPanel, "ChangshaCapPreset", "常用封顶");
        ChangshaCapValuePanel = EnsureChangshaRuntimeInput(ChangshaCapValuePanel);
        ChangshaCapValueInput = ChangshaCapValuePanel != null ? ChangshaCapValuePanel.GetComponentInChildren<TMP_InputField>(true) : null;
        foreach (GameObject panel in new[] { ChangshaCapTypePanel, ChangshaCapPresetPanel, ChangshaCapValuePanel }) {
            if (panel == null) continue;
            panel.transform.SetParent(ChangshaCapRow.transform, false);
            LayoutElement element = panel.GetComponent<LayoutElement>();
            if (element == null) element = panel.AddComponent<LayoutElement>();
            element.minWidth = 100f;
            element.preferredWidth = 180f;
            element.flexibleWidth = 1f;
        }
        ChangshaCapTypeDropdown = ChangshaCapTypePanel != null ? ChangshaCapTypePanel.GetComponentInChildren<TMP_Dropdown>(true) : null;
        ChangshaCapPresetDropdown = ChangshaCapPresetPanel != null ? ChangshaCapPresetPanel.GetComponentInChildren<TMP_Dropdown>(true) : null;
        if (ChangshaCapTypeDropdown != null) {
            ChangshaCapTypeDropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
            ChangshaCapTypeDropdown.ClearOptions();
            ChangshaCapTypeDropdown.AddOptions(new List<string> { "不封顶", "大胡数量(鸟另计)", "最终分值(含鸟)" });
            ChangshaCapTypeDropdown.onValueChanged.AddListener(OnChangshaCapTypeChanged);
        }
        if (ChangshaCapPresetDropdown != null) {
            ChangshaCapPresetDropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
            ChangshaCapPresetDropdown.onValueChanged.AddListener(OnChangshaCapPresetChanged);
        }
        if (ChangshaCapValueInput != null) {
            ChangshaCapValueInput.onValueChanged = new TMP_InputField.OnChangeEvent();
            ChangshaCapValueInput.onEndEdit = new TMP_InputField.SubmitEvent();
            ChangshaCapValueInput.characterLimit = 7;
        }
        if (ChangshaBaseScoreNoDealerToggle != null) {
            ChangshaBaseScoreNoDealerToggle.onValueChanged.RemoveListener(OnChangshaCapScoreModeChanged);
            ChangshaBaseScoreNoDealerToggle.onValueChanged.AddListener(OnChangshaCapScoreModeChanged);
        }
        if (ChangshaBirdCountDropdown != null) {
            ChangshaBirdCountDropdown.onValueChanged.RemoveListener(OnChangshaCapBirdCountChanged);
            ChangshaBirdCountDropdown.onValueChanged.AddListener(OnChangshaCapBirdCountChanged);
        }
        if (ChangshaBigHuScoreInput != null) {
            ChangshaBigHuScoreInput.onValueChanged.RemoveListener(OnChangshaCapBigScoreChanged);
            ChangshaBigHuScoreInput.onValueChanged.AddListener(OnChangshaCapBigScoreChanged);
        }
        SetChangshaScoreCapType("none");
    }

    private Toggle EnsureChangshaRuntimeToggle(Toggle template, Toggle existing) {
        if (existing != null || template == null) return existing;
        Toggle result = Instantiate(template, template.transform.parent);
        result.name = "ChangshaKamo";
        result.SetIsOnWithoutNotify(false);
        TMP_Text label = result.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = "卡摸(小胡仅自摸)";
        return result;
    }

    private GameObject EnsureChangshaRuntimeDropdown(GameObject existing, string name, string labelText) {
        if (existing != null || HepaiWayPanel == null) return existing;
        GameObject result = Instantiate(HepaiWayPanel, HepaiWayPanel.transform.parent);
        result.name = name;
        foreach (TMP_Text label in result.GetComponentsInChildren<TMP_Text>(true)) {
            if (label.GetComponentInParent<TMP_Dropdown>(true) != null) continue;
            label.text = labelText;
            break;
        }
        result.SetActive(false);
        return result;
    }

    private GameObject EnsureChangshaRuntimeInput(GameObject existing) {
        if (existing != null || InputHepaiLimitPlane == null) return existing;
        GameObject result = Instantiate(InputHepaiLimitPlane, InputHepaiLimitPlane.transform.parent);
        result.name = "ChangshaCapValue";
        foreach (TMP_Text label in result.GetComponentsInChildren<TMP_Text>(true)) {
            if (label.GetComponentInParent<TMP_InputField>(true) != null) continue;
            label.text = "封顶数值";
            break;
        }
        TMP_InputField input = result.GetComponentInChildren<TMP_InputField>(true);
        if (input != null) input.contentType = TMP_InputField.ContentType.IntegerNumber;
        return result;
    }

    private string GetChangshaScoreCapType() {
        int value = ChangshaCapTypeDropdown != null ? ChangshaCapTypeDropdown.value : 0;
        return value == 1 ? "big_hu" : value == 2 ? "score" : "none";
    }

    private int GetChangshaScoreCapValue() {
        if (GetChangshaScoreCapType() == "none") return 0;
        return ChangshaCapValueInput != null && int.TryParse(ChangshaCapValueInput.text, out int value) ? value : 0;
    }

    private void SetChangshaScoreCapType(string type) {
        if (ChangshaCapTypeDropdown == null) return;
        ChangshaCapTypeDropdown.SetValueWithoutNotify(type == "big_hu" ? 1 : type == "score" ? 2 : 0);
        OnChangshaCapTypeChanged(ChangshaCapTypeDropdown.value);
    }

    private void OnChangshaCapTypeChanged(int value) {
        // 切换类型选中间常用档，数量默认3大；分值档位随当前底分和鸟数生成。
        RefreshChangshaCapPresets();
        if (ChangshaCapPresetDropdown != null && ChangshaCapPresetDropdown.options.Count > 1) {
            ChangshaCapPresetDropdown.SetValueWithoutNotify(1);
            OnChangshaCapPresetChanged(1);
        }
    }

    private void OnChangshaCapPresetChanged(int value) {
        if (refreshingChangshaCaps || ChangshaCapValueInput == null) return;
        bool custom = value >= changshaCapPresets.Length;
        ChangshaCapValueInput.interactable = custom;
        if (!custom && value >= 0) ChangshaCapValueInput.SetTextWithoutNotify(changshaCapPresets[value].ToString());
    }

    private void OnChangshaCapScoreModeChanged(bool value) {
        RefreshChangshaScoreInputVisibility();
        RefreshChangshaCapPresets();
    }

    private void OnChangshaCapBirdCountChanged(int value) { RefreshChangshaCapPresets(); }
    private void OnChangshaCapBigScoreChanged(string value) { RefreshChangshaCapPresets(); }

    /// <summary>更新可达到的常用分值，保留自定义文本，并阻止刷新事件重入和重复改值。</summary>
    private void RefreshChangshaCapPresets() {
        if (refreshingChangshaCaps || ChangshaCapPresetDropdown == null) return;
        bool enabled = _ruleState == "changsha" && GetChangshaScoreCapType() != "none";
        ChangshaCapPresetPanel.SetActive(enabled);
        if (ChangshaCapValuePanel != null) ChangshaCapValuePanel.SetActive(enabled);
        if (!enabled) return;
        refreshingChangshaCaps = true;
        int selected = ChangshaCapPresetDropdown.value;
        changshaCapPresets = Changsha_Create_RoomConfig.ScoreCapPresets(GetChangshaScoreCapType(),
            ChangshaBaseScoreNoDealerToggle != null && ChangshaBaseScoreNoDealerToggle.isOn,
            ReadChangshaScore(ChangshaBigHuScoreInput, 8), GetChangshaBirdCount());
        string unit = GetChangshaScoreCapType() == "big_hu" ? "大" : "分";
        var labels = new List<string>();
        foreach (int preset in changshaCapPresets) labels.Add(preset + unit);
        labels.Add("自定义");
        ChangshaCapPresetDropdown.ClearOptions();
        ChangshaCapPresetDropdown.AddOptions(labels);
        selected = Mathf.Clamp(selected, 0, labels.Count - 1);
        ChangshaCapPresetDropdown.SetValueWithoutNotify(selected);
        refreshingChangshaCaps = false;
        OnChangshaCapPresetChanged(selected);
    }
}
