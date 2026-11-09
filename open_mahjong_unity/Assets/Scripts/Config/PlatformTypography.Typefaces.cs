using System.Collections.Generic;
using TMPro;
using UnityEngine;

public sealed partial class PlatformTypography
{
    public TMP_FontAsset chineseRegular;
    public static TMP_FontAsset ChineseRegular => Profile ? Profile.chineseRegular : TMP_Settings.defaultFontAsset;
    public static TMP_FontAsset LatinBold => Profile ? Profile.bold : TMP_Settings.defaultFontAsset;

    static TMP_FontAsset ResolveChineseFont(TMP_FontAsset font) => HeavyFace(font) ? ChineseRegular : font;

    public static TMP_FontAsset ResolvePreserved(TMP_FontAsset font, string text, bool richText = false)
    {
        if (UseJapaneseFont(text) && JapaneseRegular) return JapaneseRegular;
        var entry = Profile ? Profile.mixedFonts.Find(item => item.font == font) : null;
        var source = entry != null ? entry.chinese : font;
        return HasChinese(text) && HeavyFace(source) ? Resolve(source, text, richText) : font;
    }

    public static void RefreshSceneFonts()
    {
        Refresh();
        foreach (var binding in Resources.FindObjectsOfTypeAll<PlatformFontBinding>())
            if (binding && binding.gameObject.scene.IsValid() && binding.gameObject.scene.isLoaded)
                binding.Apply();
    }

    static void ApplyGlobalFonts()
    {
        if (!Profile || !Profile.chineseRegular) return;
        bool japanese = ConfigManager.CurrentLanguage == AppLanguage.Japanese && JapaneseRegular;
        TMP_Settings.defaultFontAsset = japanese ? JapaneseRegular : Profile.chineseRegular;
        TMP_Settings.fallbackFontAssets = japanese
            ? new List<TMP_FontAsset> { JapaneseRegular, Profile.chineseRegular }
            : new List<TMP_FontAsset> { Profile.chineseRegular };
    }
}
