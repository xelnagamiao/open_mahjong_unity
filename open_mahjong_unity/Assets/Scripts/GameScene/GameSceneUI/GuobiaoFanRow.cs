using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>动态番种条目使用场景中保存的模板和现有手牌展示组件。</summary>
public sealed class GuobiaoFanRow : MonoBehaviour {
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text valueText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text contextText;
    [SerializeField] private TMP_Text noteText;
    [SerializeField] private PlayerInfoHandView handView;
    [SerializeField] private Image headerBackground;

    public void Bind(GuobiaoFanCatalog.Entry entry, Color headerColor, string ruleId = "guobiao") {
        headerBackground.color = headerColor;
        TableText.Set(nameText, entry.name, ruleId);
        TableText.Set(valueText, string.IsNullOrEmpty(entry.valueText) ? entry.fan + " 番" : entry.valueText, ruleId);
        TableText.Set(descriptionText, entry.description, ruleId);
        contextText.gameObject.SetActive(false);
        TableText.Set(noteText, entry.note, ruleId);
        noteText.gameObject.SetActive(!string.IsNullOrEmpty(entry.note));
        var groups = entry.ExampleGroups();
        handView.gameObject.SetActive(groups.Length > 0);
        handView.SetExample(groups, entry.faceDownTiles);
    }
}
