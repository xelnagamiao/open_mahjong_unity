/// <summary>大厅建房子规则一行：下拉显示名 + 介绍文案。</summary>
public sealed class RuleLobbySubRule {
    public string Key;
    public string DisplayName;
    /// <summary>修改规则时不得修改建房描述和 Unity 描述。</summary>
    public string Description;

    public RuleLobbySubRule() { }

    public RuleLobbySubRule(string key, string displayName, string description) {
        Key = key;
        DisplayName = displayName;
        Description = description;
    }
}
