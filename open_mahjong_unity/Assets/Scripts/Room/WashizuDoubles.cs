using System.Collections.Generic;
using System.Linq;

/// <summary>Client presentation and fixed room settings for the captain format.</summary>
public static class WashizuDoubles {
    public static readonly string[] RoleLabels = { "A队主将", "A队助手", "B队主将", "B队助手" };
    public const string Description = "鹫巢麻将规则变体，四位玩家分为A、B两队，助手坐上家，主将坐下家，副将无击飞，最终两队只比对主将的分数判断胜负，鹫巢双打没有连庄。";
    public static readonly Dictionary<string, object> FixedOptions = new Dictionary<string, object> {
        { "pao", false }, { "pao_suukantsu", false }, { "rank_points", "none" },
        { "return_score", 0 }, { "end_deposits", "discard" }, { "chombo_penalty", "fixed_9000" },
        { "tenpai_renchan", true }, { "agari_yame", false }, { "tenpai_yame", false },
        { "target_score", 0 }, { "extension_rounds", 0 }, { "tie_break", "shared" },
    };
    public static bool IsEnabled(string subRule) => subRule == WashizuTile.DoublesSubRule;
    public static bool ValidRoles(int[] roles) => roles != null && roles.Length == 4
        && roles.OrderBy(r => r).SequenceEqual(new[] { 0, 1, 2, 3 });
    public static string RoleLabel(int team, string role) => (team == 0 || team == 1)
        && (role == "captain" || role == "assistant") ? RoleLabels[team * 2 + (role == "assistant" ? 1 : 0)] : "";
    public static string ResultReason(string reason) => reason == "captain_bust" ? "主将击飞，按击飞时点数判定"
        : reason == "draw" ? "主将同分，和局" : "按主将最终点数判定";
    public static void Apply(Riichi_Create_RoomConfig config) {
        if (!IsEnabled(config.SubRule)) return;
        config.RedDora = false; config.OpenXiru = false; config.OpenTobi = true; config.HepaiWay = "head_bump";
        config.DetailedConfig = config.DetailedConfig == null ? new Dictionary<string, object>() : new Dictionary<string, object>(config.DetailedConfig);
        foreach (var pair in FixedOptions) config.DetailedConfig[pair.Key] = pair.Value;
    }
}
