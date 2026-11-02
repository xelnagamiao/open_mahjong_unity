using TMPro;
using UnityEngine;

/// <summary>High precision, native-weight fonts scoped to the table controls.</summary>
public static class TableHudTypography {
    private static TMP_FontAsset body, emphasis;
    public static TMP_FontAsset Body => body ? body : body = Resources.Load<TMP_FontAsset>("image/UI/TableHud/Fonts/ControlsMedium");
    public static TMP_FontAsset Emphasis => emphasis ? emphasis : emphasis = Resources.Load<TMP_FontAsset>("image/UI/TableHud/Fonts/ControlsBold");
    public static void Apply(TMP_Text text, bool strong = false) {
        if (!text) return;
        var font = strong ? Emphasis : Body;
        if (!font) return;
        if (text.font != font) { text.font = font; text.fontSharedMaterial = font.material; }
        bool nativeControlWeight = false;
        for (var parent = text.transform; parent; parent = parent.parent)
            if (parent.name == "ControlPanel") { nativeControlWeight = true; break; }
        var binding = text.GetComponent<PlatformFontBinding>();
        if (binding) {
            binding.authoredFont = font;
            binding.authoredMaterial = font.material;
            binding.authoredStyle = FontStyles.Normal;
            binding.authoredWeight = FontWeight.Regular;
            binding.styleCaptured = true;
            binding.preserveAuthoredTypeface = true;
            binding.preserveAuthoredChineseFace = nativeControlWeight;
            binding.Apply();
        }
        // The selected font file supplies the weight. Do not dilate a small SDF
        // to synthesize bold, which fills the counters in Chinese characters.
        if (!binding) { text.font = nativeControlWeight ? font : PlatformTypography.ResolvePreserved(font, text.text, text.richText); text.fontSharedMaterial = PlatformTypography.ResolveMaterial(font.material, text.font); }
        text.fontStyle = FontStyles.Normal;
        text.fontWeight = FontWeight.Regular;
    }
}
