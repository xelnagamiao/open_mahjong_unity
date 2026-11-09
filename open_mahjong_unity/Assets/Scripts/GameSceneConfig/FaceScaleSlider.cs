using UnityEngine;

/// <summary>缩放条：左端 25%，中点 100%，右端 200%；存档仍保存真实缩放。</summary>
public static class FaceScaleSlider
{
    public const float MinScale = .25f, MaxScale = 2f;

    public static float ToScale(float position)
    {
        position = Mathf.Clamp01(position);
        return position <= .5f
            ? Mathf.Lerp(MinScale, 1f, position * 2f)
            : Mathf.Lerp(1f, MaxScale, (position - .5f) * 2f);
    }

    public static float ToPosition(float scale)
    {
        return scale <= 1f
            ? .5f * Mathf.InverseLerp(MinScale, 1f, scale)
            : .5f + .5f * Mathf.InverseLerp(1f, MaxScale, scale);
    }

    public static void Configure(UnityEngine.UI.Slider slider)
    {
        slider.wholeNumbers = false;
        slider.minValue = 0f;
        slider.maxValue = 1f;
    }
}
