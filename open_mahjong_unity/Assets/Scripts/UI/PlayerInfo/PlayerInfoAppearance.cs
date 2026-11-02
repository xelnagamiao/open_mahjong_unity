using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PlayerInfoAppearance : MonoBehaviour {
    public enum Role {
        Surface, Header, HeaderInk, Section, Ink, Muted, Accent, OnAccent,
        Control, ControlInk, Detail, Divider, Close, CloseInk, ProgressTrack, ChartGrid, ChartPoint
    }
    [Serializable] public sealed class Palette {
        public Color[] colors;
        public Color this[Role role] => colors[(int)role];
    }
    [Serializable] public sealed class Binding { public Graphic graphic; public Role role; }
    [Serializable] public sealed class ControlBinding {
        public Selectable control;
        public TMP_Text label;
        public Role fill;
        public Role ink;
    }

    [SerializeField] private Palette palette = CreatePalette();
    [SerializeField] private Binding[] bindings = Array.Empty<Binding>();
    [SerializeField] private ControlBinding[] controls = Array.Empty<ControlBinding>();
    [SerializeField] private PlayerInfoPanel panel;
    [SerializeField] private PlayerInfoStatistics statistics;
    [SerializeField] private PlayerInfoTrendChart trend;
    public Palette Current => palette;

    private void OnEnable() => ApplyCurrent();

#if UNITY_EDITOR
    public void ConfigureBaked(PlayerInfoPanel target, PlayerInfoStatistics data, PlayerInfoTrendChart chart, Binding[] colors, ControlBinding[] buttons) {
        panel = target; statistics = data; trend = chart; bindings = colors; controls = buttons;
        palette = CreatePalette();
        ApplyCurrent();
    }
#endif

    public void ApplyCurrent() {
        var palette = Current;
        foreach (var binding in bindings)
            if (binding.graphic) binding.graphic.color = palette[binding.role];
        foreach (var binding in controls) {
            PaintControl(binding.control, palette[binding.fill]);
            if (binding.label) binding.label.color = palette[binding.ink];
        }
        if (statistics) statistics.ApplyAppearance(palette);
        if (trend) trend.SetPalette(palette[Role.Accent], palette[Role.ChartPoint], palette[Role.ChartGrid]);
        if (panel) panel.RefreshDesign();
    }

    public void PaintTab(Selectable control, TMP_Text label, bool selected) {
        var palette = Current;
        PaintControl(control, palette[selected ? Role.Accent : Role.Control]);
        if (label) label.color = palette[selected ? Role.OnAccent : Role.ControlInk];
    }

    public void PaintCategory(Button control, TMP_Text label, Image indicator, bool selected) {
        var palette = Current;
        PaintControl(control, Color.clear);
        var state = control.colors;
        state.highlightedColor = new Color(1, 1, 1, .035f);
        state.pressedColor = new Color(1, 1, 1, .02f);
        control.colors = state;
        if (label) label.color = palette[selected ? Role.Accent : Role.Muted];
        if (indicator) { indicator.color = palette[Role.Accent]; indicator.gameObject.SetActive(selected); }
    }

    public static void PaintControl(Selectable control, Color fill) {
        if (!control) return;
        if (control.targetGraphic) control.targetGraphic.color = Color.white;
        control.transition = Selectable.Transition.ColorTint;
        var state = control.colors;
        state.normalColor = state.selectedColor = fill;
        state.highlightedColor = Color.Lerp(fill, Color.white, .11f);
        state.pressedColor = Color.Lerp(fill, Color.black, .16f);
        state.disabledColor = Color.Lerp(fill, Color.gray, .45f);
        state.colorMultiplier = 1;
        state.fadeDuration = .08f;
        control.colors = state;
    }

    public static Palette CreatePalette() => Make("3A3A3A", "202020", "FFFFFF", "565656", "FFFFFF", "D0D0D0", "ECA15B", "161616", "414141", "FFFFFF", "262626", "747474", "AE463B", "FFFFFF", "525252", "858585", "FFE0B0");

    private static Palette Make(params string[] hex) {
        var colors = new Color[hex.Length];
        for (int i = 0; i < hex.Length; i++) ColorUtility.TryParseHtmlString("#" + hex[i], out colors[i]);
        return new Palette { colors = colors };
    }
}
