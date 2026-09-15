using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>场景设置共用的 RGB / 明暗数值显示，不在刷新时触发颜色修改事件。</summary>
public static class SceneConfigColorUi
{
    // Unity's editor invokes Slider.onValueChanged during Prelayout too. A redraw
    // is not user input and must not persist colors or rebuild preview children.
    public static bool IsLayoutRefresh => CanvasUpdateRegistry.IsRebuildingLayout();

    public static void SyncChannel(Slider slider, TMP_Text label, float channel)
    {
        int value = Mathf.RoundToInt(Mathf.Clamp01(channel) * 255f);
        if (slider != null) slider.SetValueWithoutNotify(value);
        if (label != null) label.text = value.ToString();
    }

    public static void SyncBrightness(Slider slider, TMP_Text label, float brightness)
    {
        int value = Mathf.RoundToInt(Mathf.Clamp(brightness, -1f, 1f) * 100f);
        if (slider != null)
        {
            // Also normalize old scenes/prefabs serialized as a 0..255 RGB slider.
            // Callers guard their change handlers while synchronizing the UI.
            slider.minValue = -100f;
            slider.maxValue = 100f;
            slider.wholeNumbers = true;
            slider.SetValueWithoutNotify(value);
        }
        if (label != null) label.text = value > 0 ? "+" + value : value.ToString();
    }
}
