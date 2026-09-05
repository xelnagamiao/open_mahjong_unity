using System;

/// <summary>
/// 规则清单：一种玩法在客户端的全部"声明式"差异。
///
/// 目标是让"新增一种规则"只需要新增一份清单（加上可能的少量 Feature），
/// 核心代码不再出现 <c>roomRule == "xxx"</c>。
/// 清单里的字段只从现有规则的实际差异中提炼，不为想象中的规则预留。
///
/// 后续阶段会补充：Features（可组合机制）、HandLayout、Calc 门面、文案表等。
/// </summary>
public sealed class RuleManifest {
    /// <summary>服务端 room_rule，例如 "hongque"。</summary>
    public string RuleId;

    /// <summary>缺省子规则，例如 "hongque/v1.6"；牌谱/计分板缺 sub_rule 时回退用。</summary>
    public string DefaultSubRule;

    /// <summary>显示名（日志/调试用，UI 文案仍走 RuleNameDictionary）。</summary>
    public string DisplayName;

    /// <summary>
    /// 驱动器工厂。为 null 表示使用回合制默认驱动（当前即 NormalGameStateManager 的既有流程）。
    /// 每个规则最多创建一次，见 RuleRegistry.GetDriver。
    /// </summary>
    public Func<IGameDriver> DriverFactory;

    /// <summary>副露统一竖排（认走张不横置）。</summary>
    public bool VerticalMelds;

    /// <summary>听牌提示由驱动器自行计算并推送，通用 TingpaiCheck 链不得覆盖。</summary>
    public bool TipsProvidedByDriver;

    public bool MatchesSubRule(string subRule) {
        if (string.IsNullOrEmpty(subRule) || string.IsNullOrEmpty(RuleId)) return false;
        return subRule.StartsWith(RuleId + "/", StringComparison.Ordinal) || subRule == RuleId;
    }
}
