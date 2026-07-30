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

    private void Awake() {
        ConfigureText(FanName, 18f);
        ConfigureText(FanValue, 16f);
    }

    public void SetFanCount(string name, string valueDisplay) {
        ConfigureText(FanName, 18f);
        ConfigureText(FanValue, 16f);
        SplitLongChangshaDetail(ref name, ref valueDisplay);
        FanName.text = name;
        FanValue.text = valueDisplay;

        EndResultPanel endResultPanel = GetComponentInParent<EndResultPanel>(true);
        Transform boundary = endResultPanel != null ? endResultPanel.transform : null;
        if (boundary == null) {
            ShuheweiPlayerPanel shuheweiPanel =
                GetComponentInParent<ShuheweiPlayerPanel>(true);
            boundary = shuheweiPanel != null ? shuheweiPanel.transform : null;
        }
        LayoutHierarchyRebuilder.RebuildUpwards(transform, boundary);
    }

    private static void SplitLongChangshaDetail(ref string name, ref string valueDisplay) {
        SplitPrefixDetail(ref name, ref valueDisplay, "鸟牌:");
        SplitPrefixDetail(ref name, ref valueDisplay, "中鸟:");
        SplitPrefixDetail(ref name, ref valueDisplay, "扎鸟倍数:");
    }

    private static void SplitPrefixDetail(ref string name, ref string valueDisplay, string prefix) {
        if (string.IsNullOrEmpty(name) || !name.StartsWith(prefix)) return;
        string detail = name.Substring(prefix.Length);
        name = prefix.TrimEnd(':');
        valueDisplay = detail;
    }

    private static void ConfigureText(TMP_Text text, float minSize) {
        if (text == null) return;
        text.enableAutoSizing = true;
        text.fontSizeMax = Mathf.Max(text.fontSize, 40f);
        text.fontSizeMin = minSize;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Truncate;
        text.alignment = TextAlignmentOptions.Center;
    }

    public void ApplyFuColor() {
        BackgroundImage.color = fuColor;
    }

    public void ApplyFanColor() {
        BackgroundImage.color = fanColor;
    }
}
