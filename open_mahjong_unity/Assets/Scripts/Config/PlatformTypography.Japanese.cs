using TMPro;
using UnityEngine;

public sealed partial class PlatformTypography
{
    static TMP_FontAsset japaneseRegular;
    public static TMP_FontAsset JapaneseRegular => japaneseRegular ? japaneseRegular
        : japaneseRegular = Resources.Load<TMP_FontAsset>("Localization/JapaneseRegular");

    public static bool HasKana(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (char c in text)
            if ((c >= '\u3040' && c <= '\u30FF') || (c >= '\u31F0' && c <= '\u31FF')
                || (c >= '\uFF66' && c <= '\uFF9F')) return true;
        return false;
    }

    public static bool HasCjk(string text) => HasChinese(text) || HasKana(text);
    public static bool UseJapaneseFont(string text) => HasKana(text)
        || ConfigManager.CurrentLanguage == AppLanguage.Japanese && HasChinese(text);
}
