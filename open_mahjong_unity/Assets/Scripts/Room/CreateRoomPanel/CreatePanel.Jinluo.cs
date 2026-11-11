using System;
using System.Linq;
using TMPro;
using UnityEngine;

public partial class CreatePanel {
    [SerializeField] private UnityEngine.UI.Toggle JinluoModeToggle;
    [SerializeField] private GameObject JinluoWeatherPanel;
    [SerializeField] private TMP_Dropdown JinluoWeatherDropdown;
    private bool jinluoControlsWereEnabled;

    private bool IsJinluoEnabled => _ruleState == "guobiao" && GetSelectedSubRule() == "guobiao/standard"
        && JinluoModeToggle && JinluoModeToggle.isOn;

    private void BindJinluoControls() {
        if (JinluoModeToggle) JinluoModeToggle.onValueChanged.AddListener(_ => RefreshVisibility());
    }

    private void ReflowJinluoWeather() {
        if (!JinluoWeatherPanel || !RoomContent) return;
        ((RectTransform)JinluoWeatherPanel.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, RoomContent.rect.width);
    }

    private void RefreshJinluoControls() {
        bool supported = _ruleState == "guobiao" && GetSelectedSubRule() == "guobiao/standard";
        if (JinluoModeToggle) {
            JinluoModeToggle.gameObject.SetActive(supported);
            if (!supported) JinluoModeToggle.SetIsOnWithoutNotify(false);
        }
        if (JinluoWeatherPanel) JinluoWeatherPanel.SetActive(IsJinluoEnabled);
        CuoHeheToggle.interactable = !IsJinluoEnabled;
        tipsToggle.interactable = true;
        if (TianDiRenHeToggle) {
            TianDiRenHeToggle.interactable = !IsJinluoEnabled;
            if (IsJinluoEnabled) TianDiRenHeToggle.SetIsOnWithoutNotify(true);
        }
        if (IsJinluoEnabled) {
            CuoHeheToggle.isOn = true;
        } else if (jinluoControlsWereEnabled && CuoHeheToggle.isOn) {
            // 离开金锣赛时恢复普通规则的提示/错和互斥。
            tipsToggle.SetIsOnWithoutNotify(false);
        }
        jinluoControlsWereEnabled = IsJinluoEnabled;
    }

#if UNITY_EDITOR
    /// <summary>沿用现有控件烘焙，保存场景引用；运行时只切换已保存控件的显示状态。</summary>
    public void BakeJinluoControls() {
        JinluoModeToggle = EnsureClonedToggle(GuobiaoFlowersToggle, JinluoModeToggle, "JinluoMode", "金锣赛", false);
        JinluoModeToggle.group = null;
        JinluoModeToggle.onValueChanged = new UnityEngine.UI.Toggle.ToggleEvent();
        JinluoModeToggle.transform.SetSiblingIndex(TianDiRenHeToggle.transform.GetSiblingIndex() + 1);
        var toggleLabel = JinluoModeToggle.GetComponentInChildren<TMP_Text>(true);
        TableText.Set(toggleLabel, "金锣赛", "guobiao");
        RoomGet<CreateRoomHelpTrigger>(JinluoModeToggle.gameObject).Configure(this, JinluoRules.Description);
        JinluoWeatherPanel = EnsureClonedDropdownPanel(HepaiWayPanel, JinluoWeatherPanel, "JinluoWeatherPanel", "七对天气");
        JinluoWeatherDropdown = JinluoWeatherPanel.GetComponentInChildren<TMP_Dropdown>(true);
        JinluoWeatherDropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
        JinluoWeatherDropdown.ClearOptions();
        JinluoWeatherDropdown.AddOptions(JinluoRules.WeatherLabels.ToList());
        JinluoWeatherDropdown.SetValueWithoutNotify(0);
        JinluoWeatherDropdown.RefreshShownValue();
        RoomStyleDropdown(JinluoWeatherDropdown, stepTimer.captionText.fontSizeMax);
        var title = JinluoWeatherPanel.GetComponentsInChildren<TMP_Text>(true).First(label => !label.GetComponentInParent<TMP_Dropdown>(true));
        TableText.Set(title, "七对天气", "guobiao");
        // 天气包含较长的规则说明，使用内容区整行，避免挤进原有多列选择栏。
        JinluoWeatherPanel.transform.SetParent(RoomContent, false);
        JinluoWeatherPanel.transform.SetSiblingIndex(JinluoModeToggle.transform.parent.GetSiblingIndex() + 1);
        RoomHeight((RectTransform)JinluoWeatherPanel.transform, 76);
        RoomFill((RectTransform)JinluoWeatherDropdown.transform, 200, 10, 24, 10);
        RoomBox(title.rectTransform, 24, 10, 160, 56);
        ReflowJinluoWeather();
        foreach (var label in JinluoWeatherPanel.GetComponentsInChildren<TMP_Text>(true).Concat(new[] { toggleLabel })) {
            label.fontStyle &= ~FontStyles.Bold;
            label.fontWeight = FontWeight.Regular;
        }
        RoomGet<CreateRoomHelpTrigger>(JinluoWeatherPanel).Configure(this,
            "七对天气：由房主选择，整场固定。符合28素番或特殊七对的牌可直接和；其余七对须满足所选天气。门风+场风需同时具备两个风的对子，连风时同一对可同时满足；幺九对只计数牌，同对须为不同花色的同序数对子。");
        JinluoWeatherPanel.SetActive(false);
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
