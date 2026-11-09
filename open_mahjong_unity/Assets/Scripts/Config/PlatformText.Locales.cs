using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>Additional presentation catalogs; source keys remain unchanged in game state and protocols.</summary>
public static partial class PlatformText
{
    [Serializable] sealed class LocaleEntry { public string key, text; }
    [Serializable] sealed class LocaleContext { public string name; public LocaleEntry[] entries; }
    [Serializable] sealed class LocaleUnit { public string key, one, few, many, other; public bool ordinal; }
    [Serializable] sealed class LocaleCatalog { public LocaleEntry[] entries; public LocaleContext[] contexts; public LocaleUnit[] units; }
    sealed class LocaleTemplate { public Regex expression; public string text; public string[] tokens; public bool[] localizeValues; }
    sealed class LocaleData
    {
        public readonly Dictionary<string,string> exact = new Dictionary<string,string>(StringComparer.Ordinal);
        public readonly Dictionary<string,string> normalized = new Dictionary<string,string>(StringComparer.Ordinal);
        public readonly Dictionary<string,Dictionary<string,string>> contexts = new Dictionary<string,Dictionary<string,string>>(StringComparer.Ordinal);
        public readonly Dictionary<string,List<LocaleTemplate>> contextTemplates = new Dictionary<string,List<LocaleTemplate>>(StringComparer.Ordinal);
        public readonly Dictionary<string,LocaleUnit> units = new Dictionary<string,LocaleUnit>(StringComparer.Ordinal);
        public readonly List<LocaleTemplate> templates = new List<LocaleTemplate>();
        public readonly Dictionary<string,string> cache = new Dictionary<string,string>(StringComparer.Ordinal);
    }
    static readonly Dictionary<AppLanguage,LocaleData> localeCatalogs = new Dictionary<AppLanguage,LocaleData>();
    static readonly Regex LocaleNumberUnit = new Regex(@"^([+−\-]?[\d,.]+)\s*(分钟|本场|人|名|场|局|副|番|翻|符|点|分|张|枚|件|秒|级|段|页|条|底|台|倍|巡|圈|组|种|项|次)$", RegexOptions.CultureInvariant);
    static readonly Regex EmbeddedQuantity = new Regex(@"([+−\-]?[\d,.]+)\s*(分钟|本场|人|名|场|局|副|番|符|点|分|张|枚|件|秒|级|段|页|条|底|台|倍|巡)", RegexOptions.CultureInvariant);

    public static bool IsAdditionalLanguage(AppLanguage language) => language == AppLanguage.French || language == AppLanguage.German || language == AppLanguage.Russian;
    // Japanese shares the catalog loader but retains the compact CJK UI layout.
    public static bool HasLocaleCatalog(AppLanguage language) => IsAdditionalLanguage(language) || language == AppLanguage.Japanese;
    public static string LocaleCode(AppLanguage language) => language == AppLanguage.Japanese ? "ja" : language == AppLanguage.French ? "fr" : language == AppLanguage.German ? "de" : language == AppLanguage.Russian ? "ru" : language == AppLanguage.English ? "en" : language == AppLanguage.TraditionalChinese ? "zh-TW" : "zh-CN";

    static LocaleData LoadLocale(AppLanguage language)
    {
        if (localeCatalogs.TryGetValue(language, out var data)) return data;
        data = new LocaleData();
        var asset = Resources.Load<TextAsset>("Localization/PlatformUI." + LocaleCode(language));
        var catalog = asset ? JsonUtility.FromJson<LocaleCatalog>(asset.text) : null;
        foreach (var entry in catalog?.entries ?? Array.Empty<LocaleEntry>())
        {
            if (string.IsNullOrEmpty(entry.key) || string.IsNullOrEmpty(entry.text)) continue;
            data.exact[entry.key] = entry.text;
            data.normalized[Normalize(entry.key)] = entry.text;
        }
        foreach (var context in catalog?.contexts ?? Array.Empty<LocaleContext>())
        {
            var values = new Dictionary<string,string>(StringComparer.Ordinal);
            foreach (var entry in context.entries ?? Array.Empty<LocaleEntry>()) values[entry.key] = entry.text;
            data.contexts[context.name] = values;
            data.contextTemplates[context.name] = BuildLocaleTemplates(values);
        }
        foreach (var unit in catalog?.units ?? Array.Empty<LocaleUnit>()) data.units[unit.key] = unit;
        data.templates.AddRange(BuildLocaleTemplates(data.exact));
        localeCatalogs[language] = data;
        return data;
    }

    static List<LocaleTemplate> BuildLocaleTemplates(Dictionary<string,string> entries)
    {
        var templates = new List<LocaleTemplate>();
        foreach (var entry in entries.OrderByDescending(e => Placeholders.Replace(e.Key, "").Length).ThenByDescending(e => e.Key.Length))
        {
            var matches = Placeholders.Matches(entry.Key);
            if (matches.Count == 0 || !HasChinese(entry.Key)) continue;
            var pattern = new StringBuilder("^");
            var tokens = new string[matches.Count];
            var localizeValues = new bool[matches.Count];
            int offset = 0;
            for (int i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                pattern.Append(Regex.Escape(entry.Key.Substring(offset, match.Index - offset)));
                bool numericSuffix = matches.Count == 1 && Regex.IsMatch(entry.Key, @"^\{[^}]+\}\s*(分钟|本场|位|名|级|段|人|场|局|副|番|符|点|分|张|枚|件|秒|页|条|圈|巡|底|台|倍)$");
                pattern.Append("(?<v").Append(i).Append(numericSuffix ? ">[+−\\-]?[\\d,.]+)" : entry.Key.Contains("\n") ? ">.+?)" : ">[^\\r\\n]+?)");
                tokens[i] = match.Value;
                localizeValues[i] = match.Value == "{tile}" || match.Value == "{wind}" || match.Value == "{state}" || entry.Key == "财神 {0}" || entry.Key == "白板当{0}";
                offset = match.Index + match.Length;
            }
            pattern.Append(Regex.Escape(entry.Key.Substring(offset))).Append('$');
            templates.Add(new LocaleTemplate { expression = new Regex(pattern.ToString(), RegexOptions.CultureInvariant | RegexOptions.Singleline), text = entry.Value, tokens = tokens, localizeValues = localizeValues });
        }
        return templates;
    }

    static string GetLocale(string source, AppLanguage language)
    {
        var data = LoadLocale(language);
        if (data.exact.TryGetValue(source, out var direct)) return direct;
        if (data.cache.TryGetValue(source, out var cached)) return cached;
        string translated = TranslateLocale(source, language, data, true);
        if (data.cache.Count > 4096) data.cache.Clear();
        data.cache[source] = translated;
        return translated;
    }

    static string TranslateLocale(string source, AppLanguage language, LocaleData data, bool split, string context = null)
    {
        string trimmed = source.Trim();
        if (!string.IsNullOrEmpty(context) && data.contexts.TryGetValue(context, out var values))
        {
            if (values.TryGetValue(source, out var contextual)) return contextual;
            if (values.TryGetValue(trimmed, out contextual))
            {
                int start = source.IndexOf(trimmed, StringComparison.Ordinal);
                return source.Substring(0, start) + contextual + source.Substring(start + trimmed.Length);
            }
            var contextualNumber = LocaleNumberUnit.Match(trimmed);
            if (contextualNumber.Success && TryFormatUnit(contextualNumber.Groups[1].Value, contextualNumber.Groups[2].Value, language, data, context, out var contextualQuantity)) return contextualQuantity;
            if (data.contextTemplates.TryGetValue(context, out var contextTemplates)
                && TryTranslateLocaleTemplate(source, language, data, contextTemplates, context, out contextual)) return contextual;
        }
        if (data.exact.TryGetValue(source, out var direct)) return direct;
        if (data.normalized.TryGetValue(Normalize(source), out direct))
        {
            int start = source.IndexOf(trimmed, StringComparison.Ordinal);
            return source.Substring(0, start) + direct + source.Substring(start + trimmed.Length);
        }
        if (!HasChinese(source)) return source;
        if (trimmed.Length == 2 && trimmed[0] >= '1' && trimmed[0] <= '9' && "万筒饼条".IndexOf(trimmed[1]) >= 0)
        {
            string tileKey = "一二三四五六七八九"[trimmed[0] - '1'].ToString() + (trimmed[1] == '筒' ? '饼' : trimmed[1]);
            if (data.exact.TryGetValue(tileKey, out var tileName)) return tileName;
        }
        var number = LocaleNumberUnit.Match(trimmed);
        if (number.Success && TryFormatUnit(number.Groups[1].Value, number.Groups[2].Value, language, data, context, out var quantity)) return quantity;
        if (TryTranslateLocaleTemplate(source, language, data, data.templates, context, out var templated)) return templated;
        var repeated = Multiplicity.Match(trimmed);
        if (repeated.Success)
        {
            string name = repeated.Groups["name"].Value;
            string translated = TranslateLocale(name, language, data, true, context);
            if (translated != name) return translated + repeated.Groups["mark"].Value + repeated.Groups["count"].Value;
        }
        var round = Regex.Match(trimmed, @"^(?<round>(?:[东南西北]风[东南西北]|[东南西北][一二三四0-9]+局))\s+(?<honba>\d+)本场$");
        if (round.Success) return TranslateLocale(round.Groups["round"].Value, language, data, true, context) + (language == AppLanguage.Japanese ? " " : " · ") + TranslateLocale(round.Groups["honba"].Value + "本场", language, data, true, context);
        var rank = Regex.Match(trimmed, @"^第\s*(\d+)\s*名$");
        if (rank.Success) return language == AppLanguage.Japanese ? "第" + rank.Groups[1].Value + "位" : "#" + rank.Groups[1].Value;
        if (split && source.IndexOf('<') >= 0)
        {
            var parts = Tags.Split(source);
            if (parts.Length > 1)
            {
                for (int i = 0; i < parts.Length; i += 2) parts[i] = TranslateLocale(parts[i], language, data, true, context);
                return string.Concat(parts);
            }
        }
        if (split && source.IndexOf('\n') >= 0)
        {
            var lines = Regex.Split(source, @"(\r?\n)");
            for (int i = 0; i < lines.Length; i += 2) lines[i] = TranslateLocale(lines[i], language, data, true, context);
            return string.Concat(lines);
        }
        if (split)
        {
            var parts = Parts.Split(source);
            if (parts.Length > 1)
            {
                for (int i = 0; i < parts.Length; i++) parts[i] = language == AppLanguage.Japanese ? TranslateLocale(parts[i], language, data, false, context)
                    : parts[i] == "：" ? ": " : parts[i] == "；" ? "; " : parts[i] == "（" ? " (" : parts[i] == "）" ? ")" : TranslateLocale(parts[i], language, data, false, context);
                return string.Concat(parts);
            }
        }
        return source;
    }

    static bool TryTranslateLocaleTemplate(string source, AppLanguage language, LocaleData data, List<LocaleTemplate> templates, string context, out string translated)
    {
        foreach (var template in templates)
        {
            var match = template.expression.Match(source);
            if (!match.Success) continue;
            translated = Placeholders.Replace(template.text, token => {
                int i = Array.IndexOf(template.tokens, token.Value);
                if (i < 0) return token.Value;
                string captured = match.Groups["v" + i].Value;
                // Names and other literal captures remain untouched; only the existing tile/state whitelist is localized.
                return template.localizeValues[i] ? TranslateLocale(captured, language, data, true, context) : captured;
            });
            return true;
        }
        translated = null;
        return false;
    }

    public static string GetContextual(string source, string context) => GetContextual(source, context, ConfigManager.CurrentLanguage);
    public static string GetContextual(string source, string context, AppLanguage language)
    {
        if (string.IsNullOrEmpty(source) || !HasLocaleCatalog(language)) return Get(source, language);
        context = (context ?? "").Split('/')[0];
        var data = LoadLocale(language);
        if (!string.IsNullOrEmpty(context) && data.contexts.TryGetValue(context, out var values))
        {
            if (values.TryGetValue(source, out var direct)) return direct;
            var repeated = Multiplicity.Match(source);
            if (repeated.Success && values.TryGetValue(repeated.Groups["name"].Value, out direct))
                return direct + repeated.Groups["mark"].Value + repeated.Groups["count"].Value;
        }
        var number = LocaleNumberUnit.Match(source);
        if (number.Success && TryFormatUnit(number.Groups[1].Value, number.Groups[2].Value, language, data, context, out var quantity)) return quantity;
        string result = TranslateLocale(source, language, data, true, context);
        foreach (Match match in EmbeddedQuantity.Matches(source))
        {
            string amount = match.Groups[1].Value, unit = match.Groups[2].Value;
            if (data.units.ContainsKey(context + ":" + unit)
                && TryFormatUnit(amount, unit, language, data, null, out var general)
                && TryFormatUnit(amount, unit, language, data, context, out var specific))
                result = result.Replace(general, specific);
        }
        return result;
    }

    static bool TryFormatUnit(string number, string key, AppLanguage language, LocaleData data, string context, out string result)
    {
        result = null;
        LocaleUnit unit = null;
        if (!string.IsNullOrEmpty(context)) data.units.TryGetValue(context + ":" + key, out unit);
        if (unit == null && !data.units.TryGetValue(key, out unit)) return false;
        string form = unit.other;
        if (decimal.TryParse(number.Replace('−', '-'), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            amount = Math.Abs(amount);
            if (unit.ordinal) form = language == AppLanguage.French && amount == 1 ? unit.one : unit.other;
            else if (language == AppLanguage.Russian && amount == decimal.Truncate(amount))
            {
                decimal lastTwo = amount % 100, last = amount % 10;
                form = last == 1 && lastTwo != 11 ? unit.one : last >= 2 && last <= 4 && (lastTwo < 12 || lastTwo > 14) ? unit.few : unit.many;
            }
            else form = amount == 1 || language == AppLanguage.French && amount >= 0 && amount < 2 ? unit.one : unit.other;
        }
        form = string.IsNullOrEmpty(form) ? unit.other : form;
        result = form.Contains("{n}") ? form.Replace("{n}", number) : number + (language == AppLanguage.Japanese ? "" : " ") + form;
        return true;
    }
}
