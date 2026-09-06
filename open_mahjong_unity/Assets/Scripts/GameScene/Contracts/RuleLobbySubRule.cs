/// <summary>大厅建房子规则一行：下拉显示名 + 介绍文案。</summary>
public sealed class RuleLobbySubRule {
    public string Key;
    public string DisplayName;
    public string Description;

    public RuleLobbySubRule() { }

    public RuleLobbySubRule(string key, string displayName, string description) {
        Key = key;
        DisplayName = displayName;
        Description = description;
    }
}
