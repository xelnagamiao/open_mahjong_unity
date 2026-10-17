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

    public void Bind(GuobiaoFanCatalog.Entry entry, Color headerColor) {
        headerBackground.color = headerColor;
        nameText.text = entry.name;
        valueText.text = string.IsNullOrEmpty(entry.valueText) ? entry.fan + " 番" : entry.valueText;
        descriptionText.text = entry.description;
        contextText.gameObject.SetActive(false);
        noteText.text = entry.note;
        noteText.gameObject.SetActive(!string.IsNullOrEmpty(entry.note));
        var groups = entry.ExampleGroups();
        handView.gameObject.SetActive(groups.Length > 0);
        handView.SetExample(groups, entry.faceDownTiles);
    }
}
