using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Styles only the replay transport, its selectors and table utility buttons.</summary>
[ExecuteAlways, AddComponentMenu("Mahjong/UI/Table HUD Theme")]
public sealed class TableHudTheme : MonoBehaviour {
    // Preserve the first five serialized values used by existing scenes.
    public enum Style { IndigoGold, Jade, PaperCinnabar, GraphiteIce, OriginalBlue, MistBlue, Violet, WarmGray }
    public enum SurfaceRole { Panel, Header, MatchStrip, Button, PrimaryButton, Divider }
    public enum TurnBackground { Glass, Ice, Porcelain, Outline }
    [Serializable] public struct Artwork { public Image image; public Sprite sprite; public bool whiteLabel; }
    [SerializeField] private Style style = Style.OriginalBlue;
    [Header("巡目按钮背景（固定连续布局）")]
    [SerializeField] private TurnBackground turnBackground;
    [SerializeField] private Transform[] roots;
    [SerializeField] private TMP_Text[] primaryTexts;
    [SerializeField] private TMP_Text[] mutedTexts;
    [SerializeField] private TMP_Text[] accentTexts;
    private bool pending;
    private readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();
    private Sprite transportSprite;
    public Style CurrentStyle => style;
    public TurnBackground CurrentTurnBackground => turnBackground;
    public static readonly string[] TurnBackgroundNames = { "玻璃", "冰蓝", "瓷白", "细描边" };
    public static readonly string[] Names = { "夜蓝", "青瓷", "铜棕", "石墨", "晶蓝", "雾蓝", "紫晶", "琥珀" };
    public static readonly int[] DisplayOrder = { 4, 1, 6, 3, 7, 0, 5, 2 };

    public readonly struct Palette {
        public readonly Color panel, header, strip, button, accent, text, muted, edge, shadow, glow, valueTint, valueText;
        public readonly float corner;
        public readonly bool chamfer;
        public Palette(string background, string highlight) {
            shadow = C(background); glow = C(highlight);
            panel = Color.Lerp(shadow, glow, .12f); text = C("F3F6FC"); accent = Color.Lerp(glow, Color.white, .5f);
            header = strip = panel;
            button = Color.Lerp(panel, glow, .18f);
            muted = Color.Lerp(text, panel, .22f);
            edge = Color.Lerp(panel, glow, .6f);
            valueTint = Color.Lerp(Color.white, glow, .025f); valueText = C("24272E");
            corner = 4; chamfer = false;
        }
    }
    public static Palette GetPalette(Style value) {
        return value switch {
            Style.Jade => new Palette("092C27", "399C85"),
            Style.PaperCinnabar => new Palette("3A2E21", "C18C56"),
            Style.GraphiteIce => new Palette("1B2533", "75889F"),
            Style.OriginalBlue => new Palette("051A46", "3472DF"),
            Style.Violet => new Palette("261943", "976CC8"),
            Style.WarmGray => new Palette("372E27", "B28D66"),
            Style.IndigoGold => new Palette("0D1B38", "486FAD"),
            _ => new Palette("112A46", "6C9BC9")
        };
    }
    private static Color C(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var color); return color; }
    private void OnEnable() => Apply();
    private void OnValidate() => pending = true;
    private void Update() { if (pending) { pending = false; Apply(); } }
    public void SetStyle(Style value) { style = value; Apply(); }
    public void SetPreset(int index) => SetStyle((Style)Mathf.Clamp(index, 0, Names.Length - 1));
    public void SetTurnBackground(int index) { turnBackground = (TurnBackground)Mathf.Clamp(index, 0, 3); Apply(); }

    // This guard also protects scenes authored by the old broad theme baker.
    public static bool IsDesignTarget(Transform target) {
        bool allowed = false;
        for (var t = target; t; t = t.parent) {
            if (t.name == "RoundPanel" || t.name == "MouseControllerTips" || t.name == "Delay") return false;
            if (t.name == "ControlPanel" || t.name == "GamePageControl" || t.name == "#ExitButtonManager") allowed = true;
        }
        return allowed;
    }

    [ContextMenu("Apply HUD Style")]
    public void Apply() {
        var p = GetPalette(style);
        if (roots != null) foreach (var root in roots) {
            if (!root) continue;
            foreach (var surface in root.GetComponentsInChildren<TableHudSurface>(true)) {
                if (!IsDesignTarget(surface.transform)) continue;
                Color fill = surface.role switch {
                    SurfaceRole.Button => p.button,
                    SurfaceRole.PrimaryButton => Color.Lerp(p.panel, p.accent, .16f),
                    SurfaceRole.Divider => p.edge,
                    _ => p.panel
                };
                surface.color = surface.bottomColor = fill;
                surface.edgeColor = p.edge; surface.edgeWidth = surface.role == SurfaceRole.Divider ? 0 : 1;
                surface.corner = surface.role == SurfaceRole.Divider ? 0 : 6;
                surface.chamfer = false; surface.SetVerticesDirty();
            }
            foreach (var button in root.GetComponentsInChildren<Button>(true)) {
                if (!IsDesignTarget(button.transform)) continue;
                var colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(.93f, .96f, 1f, 1f);
                colors.pressedColor = new Color(.77f, .85f, .92f, 1f);
                colors.selectedColor = Color.white;
                colors.disabledColor = new Color(.65f, .65f, .65f, .55f);
                colors.colorMultiplier = 1; colors.fadeDuration = .08f; button.colors = colors;
            }
        }
        Paint(primaryTexts, p.text); Paint(mutedTexts, p.muted); Paint(accentTexts, p.text);
        if (roots != null) foreach (var root in roots) {
            if (!root) continue;
            foreach (var source in root.GetComponentsInChildren<TableHudSourceGraphic>(true))
                if (IsDesignTarget(source.transform)) source.Apply(style, source.utilityIcon ? ReferenceMaterial : null);
            foreach (var playback in root.GetComponentsInChildren<TableHudPlaybackArtwork>(true))
                if (IsDesignTarget(playback.transform)) playback.RefreshPalette();
            foreach (var selector in root.GetComponentsInChildren<TableHudSelectorPopup>(true)) selector.RefreshPalette();
        }
        var transport = transform.Find("#GameRecordUI/ControlPanel") as RectTransform;
        if (transport) TableHudReplayLayout.Reflow(transport);
        GetComponent<TableHudUtilityLayout>()?.RefreshLayout();
    }

    public Material ReferenceMaterial => GetArtworkMaterial(GetPalette(style));
    public Sprite TransportSprite {
        get {
            if (transportSprite) return transportSprite;
            var original = Resources.Load<Sprite>("image/UI/TableHud/Source/ReplayControlBar");
            if (!original) return null;
            // Remove export padding, retaining equal corner borders on both axes.
            // The layout derives PPU from the painted source height so the entire
            // vertical gradient and all four corners share one uniform scale.
            var r = original.rect; float inset = 24f * r.width / 2164f;
            float border = 32f * r.width / 2164f;
            transportSprite = Sprite.Create(original.texture, new Rect(r.x + inset, r.y + inset, r.width - 2 * inset, r.height - 2 * inset),
                new Vector2(.5f,.5f), original.pixelsPerUnit, 0, SpriteMeshType.FullRect, new Vector4(border,border,border,border));
            transportSprite.name = "Continuous transport / supplied artwork";
            transportSprite.hideFlags = HideFlags.HideAndDontSave;
            return transportSprite;
        }
    }
    private Material GetArtworkMaterial(Palette p) {
        if (style == Style.OriginalBlue) return null;
        int key = (int)style;
        if (materials.TryGetValue(key, out var cached) && cached) return cached;
        var shader = Shader.Find("Mahjong/UI/SourceArtworkPalette"); if (!shader) return null;
        var material = new Material(shader) { name = "Supplied HUD artwork / " + style, hideFlags = HideFlags.HideAndDontSave };
        material.SetColor("_Low", p.shadow);
        material.SetColor("_High", p.glow);
        material.SetColor("_Ink", p.text);
        materials[key] = material; return material;
    }
    private void OnDestroy() {
        foreach (var material in materials.Values) if (material) {
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        }
        materials.Clear();
        if (transportSprite) { if (Application.isPlaying) Destroy(transportSprite); else DestroyImmediate(transportSprite); }
    }
    private static void Paint(TMP_Text[] texts, Color color) {
        if (texts == null) return;
        foreach (var text in texts) if (text && IsDesignTarget(text.transform)) {
            bool utility = false;
            for (var parent = text.transform; parent; parent = parent.parent)
                if (parent.name == "GamePageControl" || parent.name == "#ExitButtonManager") { utility = true; break; }
            // Utility captions sit directly on the table, not on the light palette panel.
            text.color = utility ? new Color32(242,246,249,255) : color;
            TableHudTypography.Apply(text);
        }
    }
}
