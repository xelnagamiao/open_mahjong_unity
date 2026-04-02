using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class FanCount : MonoBehaviour {
    [SerializeField] private TextMeshProUGUI FanName;
    [SerializeField] private TextMeshProUGUI FanValue;
    [SerializeField] private Image BackgroundImage;

    [Header("副数颜色")]
    [SerializeField] private Color fuColor = new Color(0.55f, 0.82f, 0.49f);
    [Header("番数颜色")]
    [SerializeField] private Color fanColor = new Color(0.55f, 0.70f, 1f);

    public void SetFanCount(string name, string valueDisplay) {
        FanName.text = name;
        FanValue.text = valueDisplay;
    }

    public void ApplyFuColor() {
        BackgroundImage.color = fuColor;
    }

    public void ApplyFanColor() {
        BackgroundImage.color = fanColor;
    }
}
