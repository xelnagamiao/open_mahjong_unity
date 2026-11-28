using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>System default is used until a player explicitly chooses a language.</summary>
public static class SystemAppLanguage
{
    public static bool TryFromTag(string tag, out AppLanguage language)
    {
        var raw = (tag ?? "").Trim().Replace('_', '-').ToLowerInvariant();
        if (string.IsNullOrEmpty(raw))
        {
            language = AppLanguage.English;
            return false;
        }
        var parts = raw.Split('-');
        var primary = parts[0];
        if (primary == "fr") { language = AppLanguage.French; return true; }
        if (primary == "de") { language = AppLanguage.German; return true; }
        if (primary == "ru") { language = AppLanguage.Russian; return true; }
        if (primary == "ja") { language = AppLanguage.Japanese; return true; }
        if (primary == "es") { language = AppLanguage.Spanish; return true; }
        if (primary == "en") { language = AppLanguage.English; return true; }
        if (primary == "zh")
        {
            if (System.Array.IndexOf(parts, "hans") >= 0) { language = AppLanguage.SimplifiedChinese; return true; }
            if (System.Array.IndexOf(parts, "hant") >= 0) { language = AppLanguage.TraditionalChinese; return true; }
            language = System.Array.IndexOf(parts, "tw") >= 0 || System.Array.IndexOf(parts, "hk") >= 0 || System.Array.IndexOf(parts, "mo") >= 0
                ? AppLanguage.TraditionalChinese : AppLanguage.SimplifiedChinese;
            return true;
        }
        language = AppLanguage.English;
        return false;
    }

    public static bool TryFromTags(IEnumerable<string> tags, out AppLanguage language)
    {
        if (tags != null)
        {
            foreach (var tag in tags)
            {
                if (TryFromTag(tag, out language)) return true;
            }
        }
        language = AppLanguage.English;
        return false;
    }

    public static AppLanguage FromTag(string tag)
    {
        return TryFromTag(tag, out var language) ? language : AppLanguage.English;
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    static extern int SalasasaSystemLanguage();
#elif UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    static extern string SalasasaPreferredLanguages();
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    const uint MUI_LANGUAGE_NAME = 0x8;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetUserDefaultPreferredUILanguages(
        uint dwFlags,
        out uint pulNumLanguages,
        char[] pwszLanguagesBuffer,
        ref uint pcchLanguagesBuffer);

    static bool TryDetectWindowsPreferredLanguage(out AppLanguage language)
    {
        language = AppLanguage.English;
        try
        {
            uint bufferLength = 0;
            if (!GetUserDefaultPreferredUILanguages(MUI_LANGUAGE_NAME, out _, null, ref bufferLength)
                || bufferLength == 0 || bufferLength > 4096)
            {
                return false;
            }
            var buffer = new char[bufferLength];
            if (!GetUserDefaultPreferredUILanguages(MUI_LANGUAGE_NAME, out _, buffer, ref bufferLength))
            {
                return false;
            }
            int length = (int)System.Math.Min(bufferLength, (uint)buffer.Length);
            int start = 0;
            for (int i = 0; i < length; i++)
            {
                if (buffer[i] != '\0') continue;
                if (i == start) break;
                var tag = new string(buffer, start, i - start);
                if (TryFromTag(tag, out language)) return true;
                start = i + 1;
            }
        }
        catch (System.Exception) { }
        return false;
    }
#endif

    static AppLanguage FromSystemLanguage(SystemLanguage systemLanguage)
    {
        return systemLanguage switch
        {
            SystemLanguage.French => AppLanguage.French,
            SystemLanguage.German => AppLanguage.German,
            SystemLanguage.Russian => AppLanguage.Russian,
            SystemLanguage.Japanese => AppLanguage.Japanese,
            SystemLanguage.Spanish => AppLanguage.Spanish,
            SystemLanguage.ChineseTraditional => AppLanguage.TraditionalChinese,
            SystemLanguage.ChineseSimplified or SystemLanguage.Chinese => AppLanguage.SimplifiedChinese,
            _ => AppLanguage.English
        };
    }

    public static AppLanguage Detect()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return (AppLanguage)Mathf.Clamp(SalasasaSystemLanguage(), 0, (int)AppLanguage.Spanish);
#elif UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var localeListClass = new AndroidJavaClass("android.os.LocaleList");
            using var localeList = localeListClass.CallStatic<AndroidJavaObject>("getDefault");
            if (localeList != null)
            {
                int count = localeList.Call<int>("size");
                for (int i = 0; i < count; i++)
                {
                    using var item = localeList.Call<AndroidJavaObject>("get", i);
                    if (item != null && TryFromTag(item.Call<string>("toLanguageTag"), out var matched))
                        return matched;
                }
            }
        }
        catch (System.Exception) { }
        try
        {
            using var localeClass = new AndroidJavaClass("java.util.Locale");
            using var locale = localeClass.CallStatic<AndroidJavaObject>("getDefault");
            if (locale != null && TryFromTag(locale.Call<string>("toLanguageTag"), out var matched))
                return matched;
        }
        catch (System.Exception) { }
#elif UNITY_IOS && !UNITY_EDITOR
        try
        {
            var raw = SalasasaPreferredLanguages();
            if (!string.IsNullOrEmpty(raw) && TryFromTags(raw.Split(','), out var matched))
                return matched;
        }
        catch (System.Exception) { }
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (TryDetectWindowsPreferredLanguage(out var winMatched))
            return winMatched;
        if (TryFromTag(CultureInfo.CurrentUICulture?.Name, out var cultureMatched))
            return cultureMatched;
#else
        if (TryFromTag(CultureInfo.CurrentUICulture?.Name, out var cultureMatched))
            return cultureMatched;
#endif
        return FromSystemLanguage(Application.systemLanguage);
    }
}
