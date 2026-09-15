using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>场景设置页共用绑定与选中色。引用由场景拖好，这里只改运行时状态。</summary>
public static class SceneConfigUi
{
    public static readonly Color[] PresetColors =
    {
        new Color(0.218f, 0.372f, 0.66f, 1f),
        new Color(0.72f, 0.10f, 0.14f, 1f),
        new Color(0.95f, 0.55f, 0.10f, 1f),
        new Color(0.93f, 0.80f, 0.20f, 1f),
        new Color(0.12f, 0.55f, 0.25f, 1f),
        new Color(0.10f, 0.65f, 0.65f, 1f),
        new Color(0.45f, 0.25f, 0.70f, 1f),
        new Color(0.80f, 0.30f, 0.55f, 1f),
        new Color(0.15f, 0.15f, 0.18f, 1f),
        new Color(0.92f, 0.92f, 0.92f, 1f),
    };

    public static readonly Color UnselectedBlueGray = new Color(0.2f, 0.24f, 0.32f, 1f);
    /// <summary>与牌张设置勾选块同一橙色（Image 1, 0.5, 0）。</summary>
    public static readonly Color SelectedOrange = new Color(1f, 0.5f, 0f, 1f);
    public static readonly Color TabOn = new Color(0.28f, 0.48f, 0.92f, 1f);
    public static readonly Color TabOff = new Color(0.17f, 0.21f, 0.30f, 1f);
    public const float ToggleColorFade = 0.1f;
    public const float SurfaceHeaderHeight = 80f;
    public static readonly Color SurfaceHeaderBackground = new Color32(237, 242, 248, 255);

    public static TMP_Text CreateSurfaceHeaderTitle(Transform parent, string caption, TMP_FontAsset font)
    {
        var go = new GameObject("SurfaceTitle", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = font;
        text.text = caption;
        text.color = new Color32(40, 58, 78, 255);
        text.fontSize = 28;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        var rect = text.rectTransform;
        rect.anchorMin = new Vector2(0, 0); rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, .5f);
        rect.offsetMin = new Vector2(20, 8); rect.offsetMax = new Vector2(108, -8);
        return text;
    }

    public static void BindClick(Button button, UnityAction action)
    {
        button.onClick.AddListener(action);
    }

    public static void BindToggleOn(Toggle toggle, UnityAction action)
    {
        toggle.onValueChanged.AddListener(on => { if (on) action(); });
    }

    public static void BindSwatches(Button[] buttons, Action<Color> apply)
    {
        int n = Mathf.Min(buttons.Length, PresetColors.Length);
        for (int i = 0; i < n; i++)
        {
            Color color = PresetColors[i];
            buttons[i].onClick.AddListener(() => apply(color));
        }
    }

    public static bool TryParseHex(string hex, out Color color)
    {
        color = default;
        if (hex == null) hex = "";
        hex = hex.Trim();
        if (hex.StartsWith("#")) hex = hex.Substring(1);
        if (hex.Length == 6) hex += "FF";
        return hex.Length == 8 && ColorUtility.TryParseHtmlString("#" + hex, out color);
    }

    public static void ApplyHex(TMP_InputField input, Action<Color> apply, string successTip,
        string invalidTip = "HEX 格式不正确")
    {
        if (!TryParseHex(input.text, out Color color))
        {
            ShowTip(invalidTip);
            return;
        }
        apply(color);
        ShowTip(successTip);
    }

    public static void ShowTip(string message)
    {
        if (NotificationManager.Instance != null)
        {
            NotificationManager.Instance.ShowTip("设置", true, message);
        }
        else
        {
            Debug.Log("[SceneConfig] " + message);
        }
    }

    public static void SetButtonSelected(Button button, bool selected)
    {
        button.transition = Selectable.Transition.None;
        button.GetComponent<Image>().color = selected ? TabOn : TabOff;
    }

    /// <summary>
    /// 关掉 ColorTint / Toggle Fade。graphic 与底图是同一张时，
    /// Toggle.OnEnable 仍会 PlayEffect 把 alpha 打成 0/1，必须清空 graphic。
    /// </summary>
    public static void ConfigureToggle(Toggle toggle)
    {
        toggle.transition = Selectable.Transition.None;
        toggle.toggleTransition = Toggle.ToggleTransition.None;
        toggle.graphic = null;
    }

    /// <summary>
    /// 未选中 defaultColor，选中 selectedColor。
    /// Image.color 保持白（顶点色），显示色只走 CanvasRenderer，避免两者相乘变黑。
    /// </summary>
    public static void SetToggleSelected(
        Toggle toggle,
        bool selected,
        Color defaultColor,
        Color selectedColor,
        bool instant = false,
        float fade = ToggleColorFade)
    {
        ConfigureToggle(toggle);
        Image bg = (Image)toggle.targetGraphic;
        bg.color = Color.white;
        Color target = selected ? selectedColor : defaultColor;
        bg.CrossFadeColor(target, instant || !bg.isActiveAndEnabled ? 0f : fade, true, true);
    }
}
