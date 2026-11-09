using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>共用的运行时选色页：饱和度/明度、色相和 HEX，修改即时保存。</summary>
public sealed class RuntimeColorPicker : MonoBehaviour
{
    public TMP_Text title;
    public RuntimeColorPickerSurface saturationValue, hueStrip;
    public RectTransform saturationCursor, hueCursor;
    public Image colorPreview;
    public TMP_InputField hex;
    public Button close, backdrop;
    Action<Color> changed;
    float hue, saturation, value;

    void Awake()
    {
        close.onClick.AddListener(Close);
        backdrop.onClick.AddListener(Close);
        hex.onEndEdit.AddListener(ApplyHex);
    }

    public void Show(string caption, Color color, Action<Color> onChanged)
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        title.text = caption;
        changed = onChanged;
        Color.RGBToHSV(color, out hue, out saturation, out value);
        Refresh();
    }

    public void Close()
    {
        // Commit a still-focused HEX field before closing by mouse.
        if (hex.isFocused) ApplyHex(hex.text);
        gameObject.SetActive(false);
    }

    void OnDisable() => changed = null;

    public void SetHue(float position)
    {
        hue = Mathf.Clamp01(position);
        Publish();
    }

    public void SetSaturationValue(float s, float v)
    {
        saturation = Mathf.Clamp01(s);
        value = Mathf.Clamp01(v);
        Publish();
    }

    void ApplyHex(string text)
    {
        if (changed == null) return;
        string digits = (text ?? "").Trim().TrimStart('#');
        if (digits.Length == 6 && ColorUtility.TryParseHtmlString("#" + digits, out var color)) {
            Color.RGBToHSV(color, out hue, out saturation, out value);
            Publish();
        } else Refresh();
    }

    void Publish()
    {
        Refresh();
        changed?.Invoke(Color.HSVToRGB(hue, saturation, value));
    }

    void Refresh()
    {
        var color = Color.HSVToRGB(hue, saturation, value);
        saturationValue.SetHue(hue);
        colorPreview.color = color;
        hex.SetTextWithoutNotify("#" + ColorUtility.ToHtmlStringRGB(color));
        saturationCursor.anchorMin = saturationCursor.anchorMax = new Vector2(saturation, value);
        saturationCursor.anchoredPosition = Vector2.zero;
        hueCursor.anchorMin = hueCursor.anchorMax = new Vector2(hue, .5f);
        hueCursor.anchoredPosition = Vector2.zero;
    }
}
