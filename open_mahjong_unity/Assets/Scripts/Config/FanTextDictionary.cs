/// <summary>
/// 番文本入口（核心）。各族的番表在 Rules/&lt;Family&gt;/&lt;Family&gt;FanText.cs，
/// 通过 RuleManifest.FanNameText / FanValueText / FuNameText / FuValueText / PlaysGongHuSound 接入；
/// 这里只按 rule 字串（room_rule 或 sub_rule 均可）解析清单再转发，没有任何规则名。
/// </summary>
public static class FanTextDictionary {
    private static RuleManifest ManifestOf(string rule) => RuleRegistry.Resolve(rule, rule);

    /// <summary>番种展示名；清单未声明时原样返回。</summary>
    public static string GetFanNameDisplayText(string rule, string fanName) {
        if (string.IsNullOrEmpty(fanName)) return fanName;
        var hook = ManifestOf(rule)?.FanNameText;
        return hook != null ? hook(rule, fanName) : fanName;
    }

    /// <summary>番种番值文本（"88番" / "满贯" / "3台"）；清单未声明时 "0番"。</summary>
    public static string GetFanDisplayText(string rule, string fanName) {
        var hook = ManifestOf(rule)?.FanValueText;
        return hook != null ? hook(rule, fanName) : "0番";
    }

    /// <summary>副种副值文本（古典）；清单未声明时 "0副"。</summary>
    public static string GetFuDisplayText(string rule, string fuName) {
        var hook = ManifestOf(rule)?.FuValueText;
        return hook != null ? hook(fuName) : "0副";
    }

    /// <summary>副种展示名（古典）；清单未声明时原样返回。</summary>
    public static string GetFuNameDisplayText(string rule, string fuName) {
        var hook = ManifestOf(rule)?.FuNameText;
        return hook != null ? hook(fuName) : fuName;
    }

    /// <summary>是否播放 Gong_hu 音效（大番 / 役满 / 高分，由族定义）。</summary>
    public static bool ShouldPlayGongHuSound(string rule, string[] huFan, int huScore = 0) {
        var hook = ManifestOf(rule)?.PlaysGongHuSound;
        return hook != null && hook(rule, huFan, huScore);
    }

    /// <summary>"88番" / "88Fan" → 88。</summary>
    public static bool TryParseSingleFanValue(string display, out int fan) {
        fan = 0;
        if (string.IsNullOrEmpty(display)) return false;
        if (display.EndsWith("番") && int.TryParse(display.Substring(0, display.Length - 1), out fan)) return true;
        if (display.EndsWith("Fan") && int.TryParse(display.Substring(0, display.Length - 3), out fan)) return true;
        return false;
    }
}
