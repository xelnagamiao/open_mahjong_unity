using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    private static readonly Color RoomOrange = new Color32(242, 173, 69, 255);


    private static void RoomPrimaryButton(Button button) {
        if (!button) return;
        if (button.targetGraphic is UnityEngine.UI.Image image) image.color = RoomOrange;
        var label = button.GetComponentInChildren<TMP_Text>(true);
        if (label) { label.color = new Color32(42, 35, 24, 255); label.fontStyle = FontStyles.Normal; }
    }

    private void ConfirmSettingsReset(string content, Action reset) {
        if (!NotificationManager.Instance) return;
        var message = NotificationManager.Instance.ShowConfirmation("恢复默认设置", content, reset, "确定", "取消");
        if (!message) return;
        foreach (var button in message.GetComponentsInChildren<Button>(true)) {
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label && label.text == "确定") RoomPrimaryButton(button);
        }
    }

    private static void ShowSettingsDialog(GameObject overlay) {
        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        var motion = overlay.GetComponent<CreateRoomPopupMotion>();
        if (motion) motion.Show((RectTransform)overlay.transform.Find("Dialog"));
    }

    private static void HideSettingsDialog(GameObject overlay) {
        if (!overlay) return;
        var motion = overlay.GetComponent<CreateRoomPopupMotion>();
        if (motion) motion.Hide();
        else overlay.SetActive(false);
    }


    private void RoomDropdownTemplateHeight(TMP_Dropdown dropdown) {
        if (!dropdown.template) return;
        float height = Mathf.Min(540, dropdown.options.Count * 52 + 8);
        if ((dropdown == chooseRule || dropdown == SubRuleDropdown) && dropdown.itemText) {
            var label = dropdown.itemText;
            float savedSize = label.fontSize;
            bool savedAutoSize = label.enableAutoSizing;
            label.enableAutoSizing = false;
            label.fontSize = RoomRuleItemFontSize;
            float width = ((RectTransform)dropdown.transform).rect.width;
            foreach (var option in dropdown.options)
                width = Mathf.Max(width, label.GetPreferredValues(PlatformText.Get(option.text), float.PositiveInfinity, float.PositiveInfinity).x + 40);
            label.fontSize = savedSize;
            label.enableAutoSizing = savedAutoSize;
            width = Mathf.Max(((RectTransform)dropdown.transform).rect.width, Mathf.Min(560, width));
            dropdown.template.anchorMin = dropdown.template.anchorMax = Vector2.zero;
            dropdown.template.pivot = new Vector2(0, 1);
            dropdown.template.anchoredPosition = new Vector2(0, -5);
            dropdown.template.sizeDelta = new Vector2(width, height);
        } else {
            dropdown.template.sizeDelta = new Vector2(0, height);
        }
    }
}
