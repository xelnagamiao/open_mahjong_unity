using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

/// <summary>规则详细配置及其可选值。</summary>
internal sealed class DetailedConfigOption {
    public string Section { get; }
    public string Key { get; }
    public string Label { get; }
    public IReadOnlyList<string> Choices { get; }
    public IReadOnlyList<object> Values { get; }
    public object DefaultValue { get; }
    public int DefaultIndex { get; }
    public int ValueCount => Values.Count;

    public DetailedConfigOption(
        string section,
        string key,
        string label,
        string[] choices,
        object[] values,
        object defaultValue) {
        if (string.IsNullOrWhiteSpace(section)) {
            throw new ArgumentException("详细配置分组不能为空", nameof(section));
        }
        if (string.IsNullOrWhiteSpace(key)) {
            throw new ArgumentException("详细配置键不能为空", nameof(key));
        }
        if (string.IsNullOrWhiteSpace(label)) {
            throw new ArgumentException("详细配置名称不能为空", nameof(label));
        }
        if (choices == null || values == null || choices.Length != values.Length || values.Length == 0) {
            throw new ArgumentException("详细配置的显示值与实际值必须一一对应");
        }

        var choiceCopy = new string[choices.Length];
        var valueCopy = new object[values.Length];
        Array.Copy(choices, choiceCopy, choices.Length);
        Array.Copy(values, valueCopy, values.Length);
        for (int i = 0; i < choiceCopy.Length; i++) {
            if (string.IsNullOrWhiteSpace(choiceCopy[i])) {
                throw new ArgumentException($"详细配置 {key} 的显示值不能为空", nameof(choices));
            }
            for (int j = 0; j < i; j++) {
                if (string.Equals(choiceCopy[j], choiceCopy[i], StringComparison.Ordinal)) {
                    throw new ArgumentException($"详细配置 {key} 存在重复显示值：{choiceCopy[i]}", nameof(choices));
                }
                if (ValuesEqual(valueCopy[j], valueCopy[i])) {
                    throw new ArgumentException($"详细配置 {key} 存在重复实际值", nameof(values));
                }
            }
        }

        Section = section;
        Key = key;
        Label = label;
        Choices = Array.AsReadOnly(choiceCopy);
        Values = Array.AsReadOnly(valueCopy);
        DefaultIndex = FindValueIndex(defaultValue);
        if (DefaultIndex < 0) {
            throw new ArgumentException($"详细配置 {key} 的默认值不在可选值中", nameof(defaultValue));
        }
        DefaultValue = Values[DefaultIndex];
    }

    public int FindValueIndex(object value) {
        for (int i = 0; i < Values.Count; i++) {
            if (ValuesEqual(Values[i], value)) return i;
        }
        return -1;
    }

    public object GetValue(int index) {
        if (index < 0 || index >= Values.Count) {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        return Values[index];
    }

    public string FormatValue(object value) {
        for (int i = 0; i < Values.Count; i++) {
            if (ValuesEqual(Values[i], value)) return Choices[i];
        }
        return value?.ToString() ?? string.Empty;
    }

    internal static bool ValuesEqual(object expected, object actual) {
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
    public string Name { get; }
    public string Description { get; }
    /// <summary>相对各选项默认值的差异；未列出的选项使用自身默认值。</summary>
    public IReadOnlyDictionary<string, object> Overrides { get; }

    public DetailedConfigPreset(
        string name,
        string description,
        IDictionary<string, object> overrides) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("详细配置预设名称不能为空", nameof(name));
        }
        if (string.IsNullOrWhiteSpace(description)) {
            throw new ArgumentException("详细配置预设说明不能为空", nameof(description));
        }
        if (overrides == null) {
            throw new ArgumentNullException(nameof(overrides));
        }
        Name = name;
        Description = description;
        Overrides = new ReadOnlyDictionary<string, object>(
            new Dictionary<string, object>(overrides));
    }
}

/// <summary>可自定义台值的稳定台种；Id 用于序列化，显示文案与之分离。</summary>
internal sealed class DetailedConfigFanValue {
    public string Id { get; }
    public string Section { get; }
    public string Label { get; }
    public string Unit { get; }
    private readonly Func<IReadOnlyDictionary<string, object>, bool> _isEnabled;

    public DetailedConfigFanValue(
        string id,
        string section,
        string label,
        string unit,
        Func<IReadOnlyDictionary<string, object>, bool> isEnabled = null) {
        Id = RequireText(id, nameof(id));
        Section = RequireText(section, nameof(section));
        Label = RequireText(label, nameof(label));
        Unit = unit ?? string.Empty;
        _isEnabled = isEnabled;
    }

    public bool IsEnabled(IReadOnlyDictionary<string, object> values) {
        return _isEnabled == null || _isEnabled(values);
    }

    private static string RequireText(string value, string parameterName) {
        if (string.IsNullOrWhiteSpace(value)) {
            throw new ArgumentException("台种定义不能为空", parameterName);
        }
        return value;
    }
}

/// <summary>
/// 以基础台表加稀疏差异保存的台种设置。基础值由同一配置中的 presetKey 决定。
/// </summary>
internal sealed class DetailedConfigFanTable {
    public string Key { get; }
    public string PresetKey { get; }
    public string Section { get; }
    public string Label { get; }
    public IReadOnlyList<DetailedConfigFanValue> Fans { get; }
    public int MinimumTai { get; }
    public int MaximumTai { get; }
    private readonly Func<string, string, int> _presetTaiResolver;

    public DetailedConfigFanTable(
        string key,
        string presetKey,
        string section,
        string label,
        IEnumerable<DetailedConfigFanValue> fans,
        Func<string, string, int> presetTaiResolver,
        int minimumTai = 1,
        int maximumTai = 64) {
        if (string.IsNullOrWhiteSpace(key)) {
            throw new ArgumentException("自定义台表键不能为空", nameof(key));
        }
        if (string.IsNullOrWhiteSpace(presetKey)) {
            throw new ArgumentException("基础台表预设键不能为空", nameof(presetKey));
        }
        if (string.IsNullOrWhiteSpace(section)) {
            throw new ArgumentException("自定义台表分组不能为空", nameof(section));
        }
        if (string.IsNullOrWhiteSpace(label)) {
            throw new ArgumentException("自定义台表名称不能为空", nameof(label));
        }
        if (minimumTai < 1 || maximumTai < minimumTai) {
            throw new ArgumentOutOfRangeException(nameof(minimumTai));
        }
        if (fans == null) throw new ArgumentNullException(nameof(fans));
        _presetTaiResolver = presetTaiResolver
            ?? throw new ArgumentNullException(nameof(presetTaiResolver));
        var fanList = new List<DetailedConfigFanValue>(fans);
        if (fanList.Count == 0) {
            throw new ArgumentException("自定义台表至少需要一个台种", nameof(fans));
        }
        var fanIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (DetailedConfigFanValue fan in fanList) {
            if (fan == null || !fanIds.Add(fan.Id)) {
                throw new ArgumentException("自定义台表存在空值或重复 fan_id", nameof(fans));
            }
        }
        Key = key;
        PresetKey = presetKey;
        Section = section;
        Label = label;
        Fans = fanList.AsReadOnly();
        MinimumTai = minimumTai;
        MaximumTai = maximumTai;
    }

    public int GetPresetTai(string scoringPreset, string fanId) {
        int value = _presetTaiResolver(scoringPreset, fanId);
        if (value < MinimumTai || value > MaximumTai) {
            throw new InvalidOperationException(
                $"基础台表 {scoringPreset} 的 {fanId} 台值超出允许范围");
        }
        return value;
    }
}

/// <summary>详细配置界面的显示文案；与馆规数据定义分离。</summary>
internal sealed class DetailedConfigPresentation {
    public string DialogTitle { get; }
    public string PresetLabel { get; }
    public string CustomPresetName { get; }
    public string CustomDescription { get; }
    public string EmptyDisplayLabel { get; }
    public string EmptyDisplayValue { get; }

    public DetailedConfigPresentation(
        string dialogTitle,
        string presetLabel,
        string customPresetName,
        string customDescription,
        string emptyDisplayLabel,
        string emptyDisplayValue) {
        DialogTitle = RequireText(dialogTitle, nameof(dialogTitle));
        PresetLabel = RequireText(presetLabel, nameof(presetLabel));
        CustomPresetName = RequireText(customPresetName, nameof(customPresetName));
        CustomDescription = RequireText(customDescription, nameof(customDescription));
        EmptyDisplayLabel = RequireText(emptyDisplayLabel, nameof(emptyDisplayLabel));
        EmptyDisplayValue = RequireText(emptyDisplayValue, nameof(emptyDisplayValue));
    }

    private static string RequireText(string value, string parameterName) {
        if (string.IsNullOrWhiteSpace(value)) {
            throw new ArgumentException("详细配置界面文案不能为空", parameterName);
        }
        return value;
    }
}

/// <summary>
/// 某条规则的详细配置定义，由创建页和房间详情共同使用。
/// 所有会被保存的馆规都必须声明为可见选项；本模型刻意不支持隐藏配置状态。
/// </summary>
internal sealed class DetailedConfigDefinition {
    private readonly IReadOnlyDictionary<string, DetailedConfigOption> _optionsByKey;

    public string RuleKey { get; }
    public DetailedConfigPresentation Presentation { get; }
    public IReadOnlyList<DetailedConfigOption> Options { get; }
    public IReadOnlyList<DetailedConfigPreset> Presets { get; }
    public DetailedConfigFanTable FanTable { get; }
    public int DefaultPresetIndex { get; }
    public DetailedConfigPreset DefaultPreset => Presets[DefaultPresetIndex];

    public DetailedConfigDefinition(
        string ruleKey,
        DetailedConfigPresentation presentation,
        IEnumerable<DetailedConfigOption> options,
        IEnumerable<DetailedConfigPreset> presets,
        int defaultPresetIndex = 0,
        DetailedConfigFanTable fanTable = null) {
        if (string.IsNullOrWhiteSpace(ruleKey)) {
            throw new ArgumentException("详细配置规则键不能为空", nameof(ruleKey));
        }
        RuleKey = ruleKey;
        Presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));

        if (options == null) throw new ArgumentNullException(nameof(options));
        var optionList = new List<DetailedConfigOption>(options);
        if (optionList.Count == 0) {
            throw new ArgumentException("详细配置至少需要一个可见选项", nameof(options));
        }
        var optionsByKey = new Dictionary<string, DetailedConfigOption>();
        foreach (DetailedConfigOption option in optionList) {
            if (option == null) throw new ArgumentException("详细配置选项不能为 null", nameof(options));
            if (optionsByKey.ContainsKey(option.Key)) {
                throw new ArgumentException($"详细配置存在重复键：{option.Key}", nameof(options));
            }
            optionsByKey.Add(option.Key, option);
        }
        Options = optionList.AsReadOnly();
        _optionsByKey = new ReadOnlyDictionary<string, DetailedConfigOption>(optionsByKey);
        FanTable = fanTable;
        if (FanTable != null) {
            if (optionsByKey.ContainsKey(FanTable.Key)) {
                throw new ArgumentException($"自定义台表键与普通选项重复：{FanTable.Key}", nameof(fanTable));
            }
            if (!optionsByKey.ContainsKey(FanTable.PresetKey)) {
                throw new ArgumentException($"基础台表选项不存在：{FanTable.PresetKey}", nameof(fanTable));
            }
            DetailedConfigOption presetOption = optionsByKey[FanTable.PresetKey];
            foreach (object presetValue in presetOption.Values) {
                string presetId = presetValue?.ToString() ?? string.Empty;
                foreach (DetailedConfigFanValue fan in FanTable.Fans) {
                    FanTable.GetPresetTai(presetId, fan.Id);
                }
            }
        }

        if (presets == null) throw new ArgumentNullException(nameof(presets));
        var presetList = new List<DetailedConfigPreset>(presets);
        if (presetList.Count == 0) {
            throw new ArgumentException("详细配置至少需要一个快速预设", nameof(presets));
        }
        if (defaultPresetIndex < 0 || defaultPresetIndex >= presetList.Count) {
            throw new ArgumentOutOfRangeException(nameof(defaultPresetIndex));
        }
        var presetNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (DetailedConfigPreset preset in presetList) {
            if (preset == null) throw new ArgumentException("详细配置预设不能为 null", nameof(presets));
            if (string.Equals(
                    preset.Name,
                    Presentation.CustomPresetName,
                    StringComparison.Ordinal)) {
                throw new ArgumentException(
                    $"详细配置预设不能与自定义项同名：{preset.Name}",
                    nameof(presets));
            }
            if (!presetNames.Add(preset.Name)) {
                throw new ArgumentException($"详细配置存在重复预设：{preset.Name}", nameof(presets));
            }
            ValidatePreset(preset);
        }
        Presets = presetList.AsReadOnly();
        DefaultPresetIndex = defaultPresetIndex;
    }

    public bool TryGetOption(string key, out DetailedConfigOption option) {
        if (string.IsNullOrEmpty(key)) {
            option = null;
            return false;
        }
        return _optionsByKey.TryGetValue(key, out option);
    }

    public object GetPresetValue(DetailedConfigPreset preset, DetailedConfigOption option) {
        if (preset == null) throw new ArgumentNullException(nameof(preset));
        if (option == null) throw new ArgumentNullException(nameof(option));
        object value = preset.Overrides.TryGetValue(option.Key, out object presetValue)
            ? presetValue
            : option.DefaultValue;
        return option.GetValue(option.FindValueIndex(value));
    }

    private void ValidatePreset(DetailedConfigPreset preset) {
        foreach (KeyValuePair<string, object> entry in preset.Overrides) {
            if (!_optionsByKey.TryGetValue(entry.Key, out DetailedConfigOption option)) {
                throw new ArgumentException(
                    $"详细配置预设 {preset.Name} 包含未知键：{entry.Key}",
                    nameof(preset));
            }
            if (option.FindValueIndex(entry.Value) < 0) {
                throw new ArgumentException(
                    $"详细配置预设 {preset.Name} 为 {entry.Key} 指定了非法值",
                    nameof(preset));
            }
        }
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
