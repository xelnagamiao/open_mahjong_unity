using System;
using System.Collections.Generic;

/// <summary>
/// 建房表单草稿值集合：供 RoomOptionField.VisibleWhen 与表单渲染器读写。
/// </summary>
public sealed class RoomOptionDraft {
    public string RuleId;
    public string SubRule;
    public readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.Ordinal);

    public bool GetBool(string key, bool fallback = false) {
        if (Values.TryGetValue(key, out object val) && val is bool b) return b;
        return fallback;
    }

    public int GetInt(string key, int fallback = 0) {
        if (!Values.TryGetValue(key, out object val) || val == null) return fallback;
        if (val is int i) return i;
        try { return Convert.ToInt32(val); } catch { return fallback; }
    }

    public string GetString(string key, string fallback = "") {
        if (Values.TryGetValue(key, out object val) && val != null) return val.ToString();
        return fallback;
    }
}

/// <summary>
/// 规则专属建房选项的声明式 Schema 基类。
/// </summary>
public abstract class RoomOptionField {
    public string Key;
    public string Label;
    public string HelpKey;
    public object DefaultValue;
    public Func<RoomOptionDraft, bool> VisibleWhen;
}

public sealed class ToggleOptionField : RoomOptionField { }

public sealed class DropdownOptionField : RoomOptionField {
    public string[] OptionLabels;
    public object[] OptionValues;
}

public sealed class IntRangeOptionField : RoomOptionField {
    public int MinValue;
    public int MaxValue;
}
