using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    private static readonly string[] TierNames = { "初级场", "中级场", "高级场", "最高场" };

    public void ApplyGuobiaoTierPreset(int tier) {
        if (_ruleState != "guobiao" || tier < 0 || tier >= TierNames.Length) return;
        var scrollPosition = RoomContent.anchoredPosition;
        SubRuleDropdown.SetValueWithoutNotify(0);
        DuplicateWallToggle.isOn = false;
        GuobiaoFlowersToggle.SetIsOnWithoutNotify(true);
        if (TianDiRenHeToggle) TianDiRenHeToggle.SetIsOnWithoutNotify(false);
        InputHepaiLimitToggle.SetIsOnWithoutNotify(false); HepaiLimitInput.text = "8";
        tipsToggle.SetIsOnWithoutNotify(tier == 0);
        countTipsToggle.SetIsOnWithoutNotify(false);
        pointerTipsToggle.SetIsOnWithoutNotify(tier != 3);
        CuoHeheToggle.SetIsOnWithoutNotify(tier != 0);
        CuoheTypeDropdown.SetValueWithoutNotify(0);
        TacticalCallToggle.SetIsOnWithoutNotify(true);
        roundTimer.SetValueWithoutNotify(3); stepTimer.SetValueWithoutNotify(tier == 1 ? 2 : 1);
        RefreshVisibility(); RefreshSubRuleDescription(); RefreshRoomPresentation(true);
        Canvas.ForceUpdateCanvases();
        RoomContent.GetComponentInParent<ScrollRect>().StopMovement();
        RoomContent.anchoredPosition = scrollPosition;
    }

    private void BindTierPresets() {
        var root = transform.Find("Create_Panel/TierPresets");
        if (!root) return;
        for (int i = 0; i < TierNames.Length; i++) {
            int tier = i;
            root.GetChild(i).GetComponent<Button>().onClick.AddListener(() => {
                if (_ruleState == "riichi") ApplyRiichiPreset(tier); else ApplyGuobiaoTierPreset(tier);
            });
        }
    }

    private void LayoutTierPresets(float width, float margin, float rightMargin, ScrollRect scroll) {
        var root = transform.Find("Create_Panel/TierPresets") as RectTransform;
        if (!root) return;
        bool visible = (_ruleState == "guobiao" && GetSelectedSubRule() == "guobiao/standard")
            || (_ruleState == "riichi" && GetSelectedRiichiSubRule() == "riichi/standard");
        root.gameObject.SetActive(visible);
        if (!visible) return;
        bool sidebar = margin >= 210;
        RoomBox(root, sidebar ? 32 : margin, 16, sidebar ? margin - 64 : width - margin - rightMargin, sidebar ? 304 : 64);
        float buttonWidth = sidebar ? root.rect.width : (root.rect.width - 24) / 4;
        for (int i = 0; i < root.childCount; i++) {
            var button = root.GetChild(i).GetComponent<Button>();
            button.GetComponentInChildren<TMP_Text>().text = _ruleState == "riichi" ? RiichiRoomRules.Presets[i].Label : TierNames[i];
            RoomBox((RectTransform)button.transform, sidebar ? 0 : i * (buttonWidth + 8), sidebar ? i * 80 : 0, buttonWidth, 64);
        }
        if (!sidebar) ((RectTransform)scroll.transform).offsetMax = new Vector2(-rightMargin, -96);
    }

    private void RefreshTierPresets() {
        RefreshDetailedConfigButtonLabel();
        var root = transform.Find("Create_Panel/TierPresets");
        if (!root) return;
        for (int i = 0; i < root.childCount; i++) {
            bool selected = _ruleState == "guobiao" && SubRuleDropdown.value == 0
                && !DuplicateWallToggle.isOn && GuobiaoFlowersToggle.isOn && !InputHepaiLimitToggle.isOn
                && (!TianDiRenHeToggle || !TianDiRenHeToggle.isOn)
                && tipsToggle.isOn == (i == 0) && !countTipsToggle.isOn && pointerTipsToggle.isOn == (i != 3)
                && CuoHeheToggle.isOn == (i != 0) && (i == 0 || CuoheTypeDropdown.value == 0)
                && TacticalCallToggle.isOn && GetSelectedRoundTimer() == 20 && GetSelectedStepTimer() == (i == 1 ? 8 : 5);
            var button = root.GetChild(i).GetComponent<Button>();
            if (_ruleState == "riichi") selected = i == selectedRiichiPresetIndex;
            button.GetComponent<Image>().color = selected ? RoomNavy : Color.white;
            button.GetComponentInChildren<TMP_Text>().color = selected ? Color.white : Color.black;
        }
    }

}
