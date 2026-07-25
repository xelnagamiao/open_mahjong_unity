using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>规则详细配置及其可选值。</summary>
internal sealed class DetailedConfigOption {
    public readonly string Section;
    public readonly string Key;
    public readonly string Label;
    public readonly string[] Choices;
    public readonly object[] Values;
    public readonly object DefaultValue;

    public DetailedConfigOption(
        string section,
        string key,
        string label,
        string[] choices,
        object[] values,
        object defaultValue) {
        if (choices == null || values == null || choices.Length != values.Length || values.Length == 0) {
            throw new ArgumentException("详细配置的显示值与实际值必须一一对应");
        }
        Section = section;
        Key = key;
        Label = label;
        Choices = choices;
        Values = values;
        DefaultValue = defaultValue;
    }

    public string FormatValue(object value) {
        for (int i = 0; i < Values.Length; i++) {
            if (ValuesEqual(Values[i], value)) return Choices[i];
        }
        return value?.ToString() ?? string.Empty;
    }

    private static bool ValuesEqual(object expected, object actual) {
        if (expected == null || actual == null) return expected == null && actual == null;
        if (expected is bool expectedBool) {
            return bool.TryParse(actual.ToString(), out bool actualBool) && expectedBool == actualBool;
        }
        if (expected is int expectedInt) {
            return int.TryParse(
                Convert.ToString(actual, CultureInfo.InvariantCulture),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int actualInt)
                && expectedInt == actualInt;
        }
        return string.Equals(expected.ToString(), actual.ToString(), StringComparison.Ordinal);
    }
}

internal sealed class DetailedConfigPreset {
    public readonly string Name;
    public readonly string Description;
    public readonly Dictionary<string, object> Values;

    public DetailedConfigPreset(string name, string description, Dictionary<string, object> values) {
        Name = name;
        Description = description;
        Values = values;
    }
}

/// <summary>某条规则的详细配置定义，由创建页和房间详情共同使用。</summary>
internal sealed class DetailedConfigDefinition {
    public readonly string RuleKey;
    public readonly string DialogTitle;
    public readonly string PresetLabel;
    public readonly string CustomPresetName;
    public readonly string CustomDescription;
    public readonly List<DetailedConfigOption> Options;
    public readonly List<DetailedConfigPreset> Presets;
    public readonly Dictionary<string, object> AdditionalDefaults;
    public readonly string EmptyDisplayLabel;
    public readonly string EmptyDisplayValue;
    public readonly int DefaultPresetIndex;

    public DetailedConfigDefinition(
        string ruleKey,
        string dialogTitle,
        string presetLabel,
        string customPresetName,
        string customDescription,
        List<DetailedConfigOption> options,
        List<DetailedConfigPreset> presets,
        Dictionary<string, object> additionalDefaults,
        string emptyDisplayLabel,
        string emptyDisplayValue,
        int defaultPresetIndex = 0) {
        RuleKey = ruleKey;
        DialogTitle = dialogTitle;
        PresetLabel = presetLabel;
        CustomPresetName = customPresetName;
        CustomDescription = customDescription;
        Options = options;
        Presets = presets;
        AdditionalDefaults = additionalDefaults;
        EmptyDisplayLabel = emptyDisplayLabel;
        EmptyDisplayValue = emptyDisplayValue;
        DefaultPresetIndex = defaultPresetIndex;
    }
}

internal static class DetailedConfigPresetBuilder {
    public static Dictionary<string, object> CompletePreset(
        IEnumerable<DetailedConfigOption> options,
        IDictionary<string, object> additionalDefaults,
        IDictionary<string, object> overrides) {
        var values = new Dictionary<string, object>();
        foreach (DetailedConfigOption option in options) values[option.Key] = option.DefaultValue;
        foreach (KeyValuePair<string, object> entry in additionalDefaults) values[entry.Key] = entry.Value;
        foreach (KeyValuePair<string, object> entry in overrides) values[entry.Key] = entry.Value;
        return values;
    }
}

internal static class DetailedConfigRegistry {
    private static readonly Dictionary<string, DetailedConfigDefinition> Definitions = CreateDefinitions();

    public static bool TryGet(string ruleKey, out DetailedConfigDefinition definition) {
        if (string.IsNullOrEmpty(ruleKey)) {
            definition = null;
            return false;
        }
        return Definitions.TryGetValue(ruleKey, out definition);
    }

    private static Dictionary<string, DetailedConfigDefinition> CreateDefinitions() {
        DetailedConfigDefinition taiwan = Taiwan_Create_RoomConfig.CreateDetailedConfigDefinition();
        return new Dictionary<string, DetailedConfigDefinition> {
            { taiwan.RuleKey, taiwan },
        };
    }
}
