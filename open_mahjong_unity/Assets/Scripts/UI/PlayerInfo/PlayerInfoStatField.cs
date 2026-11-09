using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>原有统计的一组名称和值，分别对齐，外观由预制体设置。</summary>
public sealed class PlayerInfoStatField : MonoBehaviour {
    [SerializeField] private TMP_Text label;
    [SerializeField] private TMP_Text value;
    [SerializeField] private Image separator;
    public string Label => label.text;
    public string Value => value.text;

    public void ApplyAppearance(PlayerInfoAppearance.Palette palette) {
        label.color = palette[PlayerInfoAppearance.Role.Muted];
        value.color = palette[PlayerInfoAppearance.Role.Ink];
        if (separator) separator.color = palette[PlayerInfoAppearance.Role.Divider];
    }

    public void Bind(string name, string text, string ruleContext = null) {
        // 文字与明细行底色之间留白，名称和值分别向内收。
        var labelMargin = label.margin;
        labelMargin.x = 10;
        label.margin = labelMargin;
        var valueMargin = value.margin;
        valueMargin.z = 10;
        value.margin = valueMargin;
        TableText.Set(label, name, ruleContext ?? string.Empty);
        TableText.Set(value, text, ruleContext ?? string.Empty);
        value.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }
}
