using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>场景设置页共用的绑定、颜色和提示。控件引用由场景拖好，这里只改运行时状态。</summary>
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

    public static readonly Color SelectedOrange = new Color(1f, 0.55f, 0f, 1f);
    public static readonly Color UnselectedBlueGray = new Color(0.2f, 0.24f, 0.32f, 1f);
    public static readonly Color TabOn = new Color(0.28f, 0.48f, 0.92f, 1f);
    public static readonly Color TabOff = new Color(0.17f, 0.21f, 0.30f, 1f);

    public static void BindClick(Button button, UnityAction action)
    {
        if (button != null) button.onClick.AddListener(action);
    }

    public static void BindToggleOn(Toggle toggle, UnityAction action)
    {
        if (toggle == null) return;
        toggle.onValueChanged.AddListener(on => { if (on) action(); });
    }

    public static void BindSwatches(Button[] buttons, Action<Color> apply)
    {
        int n = buttons != null ? Mathf.Min(buttons.Length, PresetColors.Length) : 0;
        for (int i = 0; i < n; i++)
        {
            if (buttons[i] == null) continue;
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
        if (button == null) return;
        if (button.transition != Selectable.Transition.None)
        {
            button.transition = Selectable.Transition.None;
        }
        Image image = button.GetComponent<Image>();
        if (image != null) image.color = selected ? TabOn : TabOff;
    }

    /// <summary>
    /// 场景里部分 Toggle 把同一张 Image 既当底又当 checkmark。
    /// Fade 会把未选中透明度打到 0；这里拆开并保证底图可见。
    /// </summary>
    public static void PrepareModeToggle(Toggle toggle)
    {
        if (toggle == null) return;
        toggle.transition = Selectable.Transition.None;
        toggle.toggleTransition = Toggle.ToggleTransition.None;
        if (toggle.graphic != null && toggle.graphic == toggle.targetGraphic)
        {
            toggle.graphic = null;
        }
        Image bg = toggle.targetGraphic as Image;
        if (bg == null) bg = toggle.GetComponent<Image>();
        if (bg == null) return;
        if (bg.sprite == null)
        {
            bg.sprite = SolidWhiteSprite();
            bg.type = Image.Type.Simple;
        }
        bg.canvasRenderer.SetAlpha(1f);
    }

    public static void SetToggleSelected(Toggle toggle, bool selected)
    {
        if (toggle == null) return;
        PrepareModeToggle(toggle);
        Image bg = toggle.targetGraphic as Image;
        if (bg == null) bg = toggle.GetComponent<Image>();
        if (bg != null)
        {
            bg.color = selected ? SelectedOrange : UnselectedBlueGray;
            bg.canvasRenderer.SetAlpha(1f);
        }
        else if (toggle.targetGraphic != null)
        {
            toggle.targetGraphic.color = selected ? SelectedOrange : UnselectedBlueGray;
            toggle.targetGraphic.canvasRenderer.SetAlpha(1f);
        }
        Image check = toggle.graphic as Image;
        if (check != null && check != bg)
        {
            check.color = selected ? Color.white : new Color(1f, 1f, 1f, 0.35f);
        }
    }

    private static Sprite _solidWhite;

    public static Sprite SolidWhiteSprite()
    {
        if (_solidWhite != null) return _solidWhite;
        Texture2D tex = Texture2D.whiteTexture;
        _solidWhite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        return _solidWhite;
    }
}
