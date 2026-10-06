using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class CreatePanel {
    // Upgrade existing saved room panels when the shared catalogue adds options.
    private void EnsureRiichiCatalogRows(RectTransform content, DetailedConfigDefinition definition, GameObject panel) {
        bool added = false;
        var rowTemplate = content.Cast<Transform>().FirstOrDefault(row => row.name.StartsWith("DetailedConfig_"));
        var sectionTemplate = content.Cast<Transform>().FirstOrDefault(row => row.name.StartsWith("Section_"));
        if (!rowTemplate || !sectionTemplate) {
            Debug.LogError("Riichi detailed settings row templates are missing.", this);
            return;
        }
        foreach (var option in definition.Options) {
            if (content.Find("DetailedConfig_" + option.Key)) continue;
            if (!content.Find("Section_" + option.Section)) {
                var section = Instantiate(sectionTemplate, content);
                section.name = "Section_" + option.Section;
                section.GetComponent<TMP_Text>().text = option.Section;
            }
            // Clone the baked presentation so builds use the same fonts and layout.
            var row = Instantiate(rowTemplate, content);
            row.name = "DetailedConfig_" + option.Key;
            row.Find("Label").GetComponent<TMP_Text>().text = option.Label;
            var dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
            dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
            dropdown.ClearOptions(); dropdown.AddOptions(option.Choices.ToList());
            added = true;
        }
        if (added) {
            var scroll = panel.transform.Find("Dialog/Create_Panel/ScrollArea")?.GetComponent<ScrollRect>();
            var nav = panel.transform.Find("Dialog/HeaderPanel/SectionJump")?.GetComponent<TMP_Dropdown>();
            if (nav && scroll) {
                var anchors = new[] { content }.Concat(content.Cast<RectTransform>().Where(row => row.name.StartsWith("Section_"))).ToArray();
                nav.ClearOptions(); nav.AddOptions(anchors.Select((row, i) => i == 0 ? "全部配置" : row.GetComponent<TMP_Text>().text).ToList());
                var navigator = nav.GetComponent<CreateRoomSectionNavigator>();
                if (!navigator) navigator = nav.gameObject.AddComponent<CreateRoomSectionNavigator>();
                navigator.Configure(nav, scroll, anchors);
            }
        }
    }
}
