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
        if (!JinluoWeatherDropdown) return;
        // 外层由内容区布局管理；控件止于半行，左边缘与普通下拉框一致。
        var field = (RectTransform)JinluoWeatherDropdown.transform;
        field.anchorMin = Vector2.zero;
        field.anchorMax = new Vector2(.5f, 1);
        field.offsetMin = new Vector2(128, 10);
        field.offsetMax = new Vector2(0, -10);
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
        // 保留独立行，仅在左半行显示天气控件。
        JinluoWeatherPanel.transform.SetParent(RoomContent, false);
        JinluoWeatherPanel.transform.SetSiblingIndex(JinluoModeToggle.transform.parent.GetSiblingIndex() + 1);
        RoomHeight((RectTransform)JinluoWeatherPanel.transform, 76);
        RoomBox(title.rectTransform, 0, 10, 120, 56);
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
