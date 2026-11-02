using UnityEngine;

/// <summary>System default is used until a player explicitly chooses a language.</summary>
public static class SystemAppLanguage
{
    public static AppLanguage FromTag(string tag)
    {
        var parts = (tag ?? "").Trim().Replace('_', '-').ToLowerInvariant().Split('-');
        if (parts[0] != "zh") return AppLanguage.English;
        if (System.Array.IndexOf(parts, "hans") >= 0) return AppLanguage.SimplifiedChinese;
        if (System.Array.IndexOf(parts, "hant") >= 0) return AppLanguage.TraditionalChinese;
        return System.Array.IndexOf(parts, "tw") >= 0 || System.Array.IndexOf(parts, "hk") >= 0 || System.Array.IndexOf(parts, "mo") >= 0
            ? AppLanguage.TraditionalChinese : AppLanguage.SimplifiedChinese;
    }
#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    static extern int SalasasaSystemLanguage();
#endif
    public static AppLanguage Detect()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return (AppLanguage)Mathf.Clamp(SalasasaSystemLanguage(), 0, 2);
#elif UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var localeClass = new AndroidJavaClass("java.util.Locale");
            using var locale = localeClass.CallStatic<AndroidJavaObject>("getDefault");
            return FromTag(locale.Call<string>("toLanguageTag"));
        }
        catch (System.Exception) { }
#endif
        return Application.systemLanguage switch
        {
            SystemLanguage.ChineseTraditional => AppLanguage.TraditionalChinese,
            SystemLanguage.ChineseSimplified or SystemLanguage.Chinese => AppLanguage.SimplifiedChinese,
            _ => AppLanguage.English
        };
    }
}
