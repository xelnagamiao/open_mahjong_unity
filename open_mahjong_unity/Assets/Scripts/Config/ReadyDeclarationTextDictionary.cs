using System;
using System.Collections.Generic;

/// <summary>把共用报听动作映射为各规则采用的界面用语。</summary>
public static class ReadyDeclarationTextDictionary {
    private static readonly Dictionary<string, string> ReadyDeclarationText =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "taiwan", "报听" },
        };

    public static string GetReadyDeclarationText(string roomRule) {
        string baseRule = GetBaseRule(roomRule);
        return ReadyDeclarationText.TryGetValue(baseRule, out string text) ? text : "立直";
    }

    public static string GetCancelReadyDeclarationText(string roomRule) {
        return $"取消{GetReadyDeclarationText(roomRule)}";
    }

    public static bool HasCustomReadyDeclarationText(string roomRule) {
        return ReadyDeclarationText.ContainsKey(GetBaseRule(roomRule));
    }

    private static string GetBaseRule(string roomRule) {
        if (string.IsNullOrEmpty(roomRule)) return string.Empty;
        int separator = roomRule.IndexOf('/');
        return separator >= 0 ? roomRule.Substring(0, separator) : roomRule;
    }
}
