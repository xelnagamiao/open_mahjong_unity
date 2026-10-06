using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

internal static class RiichiRoomRules {
    internal sealed class Preset {
        public string Id, Label, Source, SubRule;
        public Dictionary<string, object> Room, Values;
    }

    private static JObject catalog;
    private static JObject Catalog => catalog ?? (catalog = JObject.Parse(
        Resources.Load<TextAsset>("RiichiRuleOptions")?.text
        ?? throw new InvalidOperationException("Missing RiichiRuleOptions catalog")));
    private static Preset[] presets;
    public static Preset[] Presets => presets ?? (presets = Catalog["presets"].Select(p => new Preset {
        Id = (string)p["id"], Label = (string)p["label"], Source = (string)p["source"],
        SubRule = (string)p["sub_rule"] ?? "riichi/standard",
        Room = Values(p["room"]), Values = Values(p["values"]),
    }).ToArray());

    private static object Value(JToken token) => token.Type == JTokenType.Integer ? (object)(int)token
        : token.Type == JTokenType.Boolean ? (object)(bool)token : (string)token;
    private static Dictionary<string, object> Values(JToken token) => ((JObject)token).Properties()
        .ToDictionary(p => p.Name, p => Value(p.Value));

    public static DetailedConfigDefinition CreateDefinition() {
        var options = Catalog["options"].Select(o => new DetailedConfigOption(
            (string)o["section"], (string)o["key"], (string)o["label"],
            o["choices"].Select(t => (string)t).ToArray(), o["values"].Select(Value).ToArray(), Value(o["default"])));
        return new DetailedConfigDefinition("riichi", new DetailedConfigPresentation(
            "立直麻将 · 规则设置", "规则预设", "自定义规则", "自定义规则", "规则配置", "默认"),
            options, Presets.Select(p => new DetailedConfigPreset(p.Label, p.Label, p.Values)));
    }

    public static string Help(string key) => Catalog["options"].FirstOrDefault(o => (string)o["key"] == key)?["help"]?.ToString() ?? "";
}
