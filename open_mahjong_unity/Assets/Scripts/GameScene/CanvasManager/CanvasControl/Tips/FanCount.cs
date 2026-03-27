using UnityEngine;
using TMPro;

public class FanCount : MonoBehaviour {
    [SerializeField] private TextMeshProUGUI FanName;
    [SerializeField] private TextMeshProUGUI FanValue;

    [Header("副数颜色")]
    [SerializeField] private Color fuColor = new Color(0.55f, 0.82f, 0.49f);
    [Header("番数颜色")]
    [SerializeField] private Color fanColor = new Color(0.55f, 0.70f, 1f);

    public void SetFanCount(string name, string valueDisplay) {
        FanName.text = name;
        FanValue.text = valueDisplay;
    }

    public void ApplyFuColor() {
        FanName.color = fuColor;
        FanValue.color = fuColor;
    }

    public void ApplyFanColor() {
        FanName.color = fanColor;
        FanValue.color = fanColor;
    }
}
