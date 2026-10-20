#if UNITY_EDITOR
using System;
using System.Linq;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
public partial class CreatePanel {
    public void BakeShangraoDetailsPanel() {
        if (transform.Find("DetailedConfigPanel_shangrao")) { return; }
        var source = transform.Find("DetailedConfigPanel_taiwan");
        if (!source) throw new InvalidOperationException("Taiwan rule panel template missing");
        var copy = Instantiate(source.gameObject, transform);
        copy.name = "DetailedConfigPanel_shangrao";
        try {
            copy.SetActive(false);
            var fanPanel = copy.transform.Find("FanTablePanel");
            if (fanPanel) DestroyImmediate(fanPanel.gameObject);
            var header = copy.transform.Find("Dialog/HeaderPanel");
            header.GetComponentInChildren<TMP_Text>(true).text = "上饶麻将 · 高级设置";
            var scroll = copy.transform.Find("Dialog/Create_Panel/ScrollArea").GetComponent<ScrollRect>();
            var content = scroll.content;
            foreach (Transform child in content.Cast<Transform>().ToArray()) DestroyImmediate(child.gameObject);
            DetailedConfigRegistry.TryGet("shangrao", out var definition);
            void AddRow(string name, string title, IEnumerable<string> labels) {
                var row = RoomRect(name, content);
                RoomLabel("Label", row, title, 28);
                var field = Instantiate(SubRuleDropdown, row);
                field.name = "Dropdown"; field.onValueChanged = new TMP_Dropdown.DropdownEvent();
                field.ClearOptions(); field.AddOptions(labels.ToList()); field.SetValueWithoutNotify(0);
            }
            AddRow("DetailedConfigPreset", "规则预设", definition.Presets.Select(p => p.Name).Concat(new[] { "自定义规则" }));
            string section = null;
            foreach (var option in definition.Options) {
                if (section != option.Section) { section = option.Section; RoomLabel("Section_" + section, content, section, 28); }
                AddRow("DetailedConfig_" + option.Key, option.Label, option.Choices);
            }
            RoomStyleDetailedDialog(copy.transform);
            var nav = header.Find("SectionJump").GetComponent<TMP_Dropdown>();
            nav.onValueChanged = new TMP_Dropdown.DropdownEvent();
            var anchors = new[] { content }.Concat(content.Cast<RectTransform>().Where(r => r.name.StartsWith("Section_"))).ToArray();
            nav.ClearOptions(); nav.AddOptions(anchors.Select((r,i) => i==0 ? "全部配置" : r.GetComponent<TMP_Text>().text).ToList());
            RoomGet<CreateRoomSectionNavigator>(nav.gameObject).Configure(nav, scroll, anchors);
            RoomFill(header.GetComponentInChildren<TMP_Text>(true).rectTransform, 28, 0, 390, 0);
            var footer = copy.transform.Find("Dialog/Create_Panel/Footer");
            foreach (var button in footer.GetComponentsInChildren<Button>(true)) {
                button.onClick = new Button.ButtonClickedEvent();
                button.GetComponentInChildren<TMP_Text>(true).text = button.name == "Confirm" ? "应用规则" : button.name == "Cancel" ? "取消" : "恢复默认设置";
            }
            RoomGet<CreateRoomScrollbarSize>(scroll.gameObject);
            ConfigureSettingsDialog(copy);
        } catch { DestroyImmediate(copy); throw; }
    }

}
#endif
