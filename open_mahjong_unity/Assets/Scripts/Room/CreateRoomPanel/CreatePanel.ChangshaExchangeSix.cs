using System.Linq;
using TMPro;
using UnityEngine;

public partial class CreatePanel {
    private TMP_Dropdown exchangeSixSelectionTime;
    private GameObject exchangeSixSelectionTimePanel;

    private void RefreshExchangeSixSelectionTime() {
        if (!exchangeSixSelectionTimePanel && IsChangshaExchangeSix) {
            // 选六张时间沿用计时控件的样式，但单独计时，不占普通出牌的局时或步时。
            exchangeSixSelectionTimePanel = Instantiate(roundTimer.transform.parent.gameObject, roundTimer.transform.parent.parent);
            exchangeSixSelectionTimePanel.name = "ChangshaExchangeSelectionTime";
            exchangeSixSelectionTimePanel.transform.SetSiblingIndex(roundTimer.transform.parent.GetSiblingIndex() + 1);
            exchangeSixSelectionTime = exchangeSixSelectionTimePanel.GetComponentInChildren<TMP_Dropdown>(true);
            exchangeSixSelectionTime.onValueChanged = new TMP_Dropdown.DropdownEvent();
            exchangeSixSelectionTime.ClearOptions();
            exchangeSixSelectionTime.AddOptions(Enumerable.Range(10, 51).Select(seconds => seconds + "秒").ToList());
            exchangeSixSelectionTime.SetValueWithoutNotify(20);
            exchangeSixSelectionTime.RefreshShownValue();
            exchangeSixSelectionTime.gameObject.SetActive(true);
            foreach (var help in exchangeSixSelectionTimePanel.GetComponentsInChildren<CreateRoomHelpTrigger>(true)) Destroy(help);
            var field = (RectTransform)exchangeSixSelectionTime.transform;
            field.anchorMin = new Vector2(.5f, field.anchorMin.y);
            field.offsetMin = new Vector2(0, field.offsetMin.y);
            foreach (var label in exchangeSixSelectionTimePanel.GetComponentsInChildren<TMP_Text>(true)) {
                if (label.GetComponentInParent<TMP_Dropdown>(true)) continue;
                label.text = "选六张时间";
                label.rectTransform.anchorMin = new Vector2(0, label.rectTransform.anchorMin.y);
                label.rectTransform.anchorMax = new Vector2(.5f, label.rectTransform.anchorMax.y);
                label.rectTransform.offsetMin = new Vector2(0, label.rectTransform.offsetMin.y);
                label.rectTransform.offsetMax = new Vector2(-8, label.rectTransform.offsetMax.y);
                label.enableAutoSizing = true; label.fontSizeMin = 18;
            }
        }
        if (exchangeSixSelectionTimePanel) exchangeSixSelectionTimePanel.SetActive(IsChangshaExchangeSix);
    }

    // 模式由大厅子规则统一选择，建房、显隐与说明始终读取同一个选项。
    private bool IsChangshaExchangeSix => _ruleState == "changsha"
        && GetSelectedSubRule() == ChangshaGameState.ExchangeSixSubRule;
}
