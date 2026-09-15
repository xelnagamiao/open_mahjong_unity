using UnityEngine;

public partial class ConfigManager
{
    const string FrameColorPrefix = "TableFrameColor_";
    const string FrameBrightnessPrefix = "TableFrameBrightness_";
    const string FrameShadowKey = "TableFrameShadowIntensity";
    const string FrameHighlightKey = "TableFrameHighlightIntensity";

    public Color GetTableFrameColor(string style)
    {
        if (ColorUtility.TryParseHtmlString(PlayerPrefs.GetString(FrameColorPrefix + style, ""), out var color))
            return new Color(color.r, color.g, color.b, 1);
        return TableFrameStyles.SolidColor(style);
    }
    public float GetTableFrameBrightness(string style) => Mathf.Clamp(PlayerPrefs.GetFloat(FrameBrightnessPrefix + style, 0), -1, 1);
    public float GetTableFrameShadowIntensity() => Mathf.Clamp01(PlayerPrefs.GetFloat(FrameShadowKey, 1));
    public float GetTableFrameHighlightIntensity() => Mathf.Clamp01(PlayerPrefs.GetFloat(FrameHighlightKey, 1));
    public Color GetTableFrameDisplayColor(string style)
    {
        return ApplyColorBrightness(GetTableFrameColor(style), GetTableFrameBrightness(style));
    }
    public void SetTableFrameColor(string style, Color color)
    {
        if (!TableFrameStyles.IsSolid(style)) return;
        PlayerPrefs.SetString(FrameColorPrefix + style, "#" + ColorUtility.ToHtmlStringRGB(color));
    }
    public void SetTableFrameBrightness(string style, float value)
    {
        if (TableFrameStyles.IsSolid(style)) PlayerPrefs.SetFloat(FrameBrightnessPrefix + style, Mathf.Clamp(value, -1, 1));
    }
    public void SetTableFrameShadowIntensity(float value) => PlayerPrefs.SetFloat(FrameShadowKey, Mathf.Clamp01(value));
    public void SetTableFrameHighlightIntensity(float value) => PlayerPrefs.SetFloat(FrameHighlightKey, Mathf.Clamp01(value));

    private void ResetTableFrameParameters()
    {
        foreach (string style in TableFrameStyles.OrderedNames)
        {
            PlayerPrefs.DeleteKey(FrameColorPrefix + style);
            PlayerPrefs.DeleteKey(FrameBrightnessPrefix + style);
        }
        PlayerPrefs.DeleteKey(FrameShadowKey);
        PlayerPrefs.DeleteKey(FrameHighlightKey);
    }
}
