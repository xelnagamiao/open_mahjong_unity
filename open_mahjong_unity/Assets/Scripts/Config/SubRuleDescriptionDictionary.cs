using System.Collections.Generic;

/// <summary>创建房间界面中的子规则名称与介绍。</summary>
public sealed class CreateRoomSubRuleTextConfig {
    public string Key { get; }
    public string DisplayName { get; }
    public string Description { get; }

    public CreateRoomSubRuleTextConfig(string key, string displayName, string description) {
        Key = key;
        DisplayName = displayName;
        Description = description;
    }
}

/// <summary>创建房间界面中的主规则名称及其子规则文案。</summary>
public sealed class CreateRoomRuleTextConfig {
    public string Rule { get; }
    public string DisplayName { get; }
    public IReadOnlyList<CreateRoomSubRuleTextConfig> SubRules { get; }

    public CreateRoomRuleTextConfig(
        string rule,
        string displayName,
        params CreateRoomSubRuleTextConfig[] subRules) {
        Rule = rule;
        DisplayName = displayName;
        SubRules = subRules ?? System.Array.Empty<CreateRoomSubRuleTextConfig>();
    }

    public CreateRoomSubRuleTextConfig GetSubRule(int index) {
        if (SubRules.Count == 0) return null;
        return SubRules[index >= 0 && index < SubRules.Count ? index : 0];
    }
}

/// <summary>
/// 创建房间规则文案：目录来自 <see cref="RuleRegistry"/>（各族 LobbySubRules）。
/// 下拉顺序为 Manifest.LobbyOrder。
/// </summary>
public static class CreateRoomRuleTextConfigCatalog {
    public static IReadOnlyList<CreateRoomRuleTextConfig> Rules {
        get {
            var result = new List<CreateRoomRuleTextConfig>();
            foreach (RuleManifest manifest in RuleRegistry.Ordered) {
                result.Add(FromManifest(manifest));
            }
            return result;
        }
    }

    public static CreateRoomRuleTextConfig GetRule(string rule) {
        RuleManifest manifest = RuleRegistry.Resolve(rule, rule);
        if (manifest != null) return FromManifest(manifest);
        IReadOnlyList<CreateRoomRuleTextConfig> all = Rules;
        return all.Count > 0 ? all[0] : new CreateRoomRuleTextConfig(rule ?? "", rule ?? "");
    }

    public static CreateRoomRuleTextConfig GetRule(int index) {
        IReadOnlyList<CreateRoomRuleTextConfig> all = Rules;
        if (all.Count == 0) return new CreateRoomRuleTextConfig("", "");
        return all[index >= 0 && index < all.Count ? index : 0];
    }

    public static CreateRoomRuleTextConfig FromManifest(RuleManifest manifest) {
        string name = !string.IsNullOrEmpty(manifest.LobbyName) ? manifest.LobbyName : manifest.DisplayName;
        RuleLobbySubRule[] src = manifest.LobbySubRules;
        if (src == null || src.Length == 0) {
            return new CreateRoomRuleTextConfig(manifest.RuleId, name);
        }
        var subs = new CreateRoomSubRuleTextConfig[src.Length];
        for (int i = 0; i < src.Length; i++) {
            subs[i] = new CreateRoomSubRuleTextConfig(src[i].Key, src[i].DisplayName, src[i].Description);
        }
        return new CreateRoomRuleTextConfig(manifest.RuleId, name, subs);
    }
}

/// <summary>各子规则说明文案。实时查清单，不在静态构造时缓存（注册发生在 SubsystemRegistration）。</summary>
public static class SubRuleDescriptionDictionary {
    public static string GetDescription(string subRule) {
        if (string.IsNullOrEmpty(subRule)) return "";
        foreach (RuleManifest manifest in RuleRegistry.All) {
            if (manifest.LobbySubRules == null) continue;
            foreach (RuleLobbySubRule row in manifest.LobbySubRules) {
                if (row.Key == subRule) return row.Description ?? "";
            }
        }
        return "";
    }
}
