using TMPro;
using UnityEngine;

[DefaultExecutionOrder(21000), DisallowMultipleComponent, RequireComponent(typeof(TMP_Text))]
public sealed class PlatformFontBinding : MonoBehaviour
{
    public TMP_FontAsset authoredFont;
    public Material authoredMaterial;
    public FontStyles authoredStyle;
    public FontWeight authoredWeight = FontWeight.Regular;
    public bool styleCaptured;
    [Tooltip("Keep an explicitly designed title typeface for every script in this label.")]
    public bool preserveAuthoredTypeface;
    [Tooltip("Use the explicitly authored CJK face on this label, without synthetic bold.")]
    public bool preserveAuthoredChineseFace;
    TMP_Text label;
    Salasasa.Home.HeaderButtonPresentation header;
    Salasasa.Home.HeaderReturnGame returnHeader;
    TMP_FontAsset appliedFont;
    Material appliedMaterial;
    FontStyles appliedStyle;
    FontWeight appliedWeight;
    string observedText;

    void Awake() => Cache();
    void Cache()
    {
        if (!label) label = GetComponent<TMP_Text>();
        if (!header) header = GetComponentInParent<Salasasa.Home.HeaderButtonPresentation>(true);
        if (!returnHeader) returnHeader = GetComponentInParent<Salasasa.Home.HeaderReturnGame>(true);
        if (!styleCaptured) { authoredStyle = label.fontStyle; authoredWeight = label.fontWeight; styleCaptured = true; }
    }
    void OnEnable()
    {
        PlatformTypography.Changed += Apply;
        ConfigManager.OnLanguageChanged += Apply;
        Apply();
    }
    void OnDisable()
    {
        PlatformTypography.Changed -= Apply;
        ConfigManager.OnLanguageChanged -= Apply;
    }
    void LateUpdate()
    {
        if (label.font != appliedFont || label.fontSharedMaterial != appliedMaterial || label.fontStyle != appliedStyle
            || label.fontWeight != appliedWeight || label.text != observedText) Apply();
    }
    public void Apply()
    {
        Cache();
        if (!authoredFont) return;
        var source = authoredFont;
        var material = authoredMaterial ? authoredMaterial : source.material;
        var profile = header ? header.profile : returnHeader ? returnHeader.profile : null;
        if (profile && profile.font)
        {
            source = profile.font;
            bool outline = header ? label == header.outline : label == returnHeader.outline;
            bool shadow = header ? label == header.shadow : label == returnHeader.captionShadow;
            material = outline ? profile.outlineMaterial : shadow ? profile.shadowMaterial : profile.faceMaterial;
            if (!material) material = source.material;
        }
        bool chinese = PlatformTypography.HasChinese(label.text);
        appliedFont = preserveAuthoredChineseFace && chinese && !profile ? source
            : preserveAuthoredTypeface && !profile ? PlatformTypography.ResolvePreserved(source, label.text, label.richText)
            : PlatformTypography.Resolve(source, label.text, label.richText);
        appliedMaterial = PlatformTypography.ResolveMaterial(material, appliedFont);
        appliedStyle = profile ? FontStyles.Normal : chinese ? authoredStyle & ~FontStyles.Bold : authoredStyle;
        appliedWeight = profile || chinese ? FontWeight.Regular : authoredWeight;
        if (label.font != appliedFont) label.font = appliedFont;
        if (appliedMaterial && label.fontSharedMaterial != appliedMaterial) label.fontSharedMaterial = appliedMaterial;
        if (label.fontStyle != appliedStyle) label.fontStyle = appliedStyle;
        if (label.fontWeight != appliedWeight) label.fontWeight = appliedWeight;
        if (chinese && label.richText)
        {
            var normalized = PlatformTypography.NormalizeChineseMarkup(label.text);
            if (normalized != label.text) label.text = normalized;
        }
        observedText = label.text;
    }
}
