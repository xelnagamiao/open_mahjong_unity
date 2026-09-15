using TMPro;
using UnityEngine;

/// <summary>原有统计的一组名称和值，分别对齐，外观由预制体设置。</summary>
public sealed class PlayerInfoStatField : MonoBehaviour {
    [SerializeField] private TMP_Text label;
    [SerializeField] private TMP_Text value;
    public string Label => label.text;
    public string Value => value.text;

    public void Bind(string name, string text) {
        // 文字与明细行底色之间留白，名称和值分别向内收。
        var labelMargin = label.margin;
        labelMargin.x = 10;
        label.margin = labelMargin;
        var valueMargin = value.margin;
        valueMargin.z = 10;
        value.margin = valueMargin;
        label.text = name;
        value.text = text;
        value.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }
}
