using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 规则注册表：room_rule / sub_rule → RuleManifest → IGameDriver。
///
/// 各规则模块在 <c>[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]</c> 中调用 Register，
/// 核心不 import 任何规则模块。未注册的规则 Resolve 失败，核心按"回合制默认流程"处理。
/// </summary>
public static class RuleRegistry {
    private static readonly Dictionary<string, RuleManifest> ManifestsByRule =
        new Dictionary<string, RuleManifest>(StringComparer.Ordinal);
    private static readonly Dictionary<string, IGameDriver> DriversByRule =
        new Dictionary<string, IGameDriver>(StringComparer.Ordinal);

    /// <summary>当前对局/牌谱的规则清单；无对局或规则未注册时为 null。</summary>
    public static RuleManifest Current { get; private set; }

    /// <summary>当前对局的驱动器；回合制默认流程或无对局时为 null。</summary>
    public static IGameDriver ActiveDriver { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSessionStatics() {
        // 不清 ManifestsByRule：各模块的 Register 与本方法同属 SubsystemRegistration，执行顺序无保证。
        // Register 采用覆盖写入，因此重复注册是幂等的。
        Current = null;
        ActiveDriver = null;
        DriversByRule.Clear();
    }

    public static void Register(RuleManifest manifest) {
        if (manifest == null || string.IsNullOrEmpty(manifest.RuleId)) {
            Debug.LogError("RuleRegistry.Register: 清单缺少 RuleId");
            return;
        }
        ManifestsByRule[manifest.RuleId] = manifest;
        DriversByRule.Remove(manifest.RuleId);
    }

    /// <summary>按 room_rule 精确解析；room_rule 为空时尝试用 sub_rule 前缀解析。</summary>
    public static bool TryResolve(string roomRule, string subRule, out RuleManifest manifest) {
        if (!string.IsNullOrEmpty(roomRule) && ManifestsByRule.TryGetValue(roomRule, out manifest)) {
            return true;
        }
        if (!string.IsNullOrEmpty(subRule)) {
            foreach (RuleManifest candidate in ManifestsByRule.Values) {
                if (candidate.MatchesSubRule(subRule)) {
                    manifest = candidate;
                    return true;
                }
            }
        }
        manifest = null;
        return false;
    }

    public static bool TryResolve(string roomRule, out RuleManifest manifest) {
        return TryResolve(roomRule, null, out manifest);
    }

    /// <summary>取（或懒创建）该清单的驱动器；清单未声明 DriverFactory 时返回 null。</summary>
    public static IGameDriver GetDriver(RuleManifest manifest) {
        if (manifest == null || manifest.DriverFactory == null) return null;
        if (DriversByRule.TryGetValue(manifest.RuleId, out IGameDriver existing) && existing != null) {
            return existing;
        }
        IGameDriver created = manifest.DriverFactory();
        DriversByRule[manifest.RuleId] = created;
        return created;
    }

    /// <summary>对局/牌谱开始时由核心调用，确定当前规则与驱动器。</summary>
    public static void SetCurrent(string roomRule, string subRule) {
        if (TryResolve(roomRule, subRule, out RuleManifest manifest)) {
            Current = manifest;
            ActiveDriver = GetDriver(manifest);
        } else {
            Current = null;
            ActiveDriver = null;
        }
    }

    /// <summary>对局/牌谱退出时由核心调用。</summary>
    public static void ClearCurrent() {
        ActiveDriver?.OnSessionReset();
        Current = null;
        ActiveDriver = null;
    }

    /// <summary>
    /// 解析 <c>gamestate/{rule}/{suffix}</c>。仅当恰有三段（rule 与 suffix 都非空）时返回 true；
    /// <c>gamestate/get_spectator_list</c>、<c>gamestate/vote_update</c> 这类两段式公共消息返回 false。
    /// </summary>
    public static bool TryParseGameStateType(string type, out string rule, out string suffix) {
        rule = null;
        suffix = null;
        const string prefix = "gamestate/";
        if (string.IsNullOrEmpty(type) || !type.StartsWith(prefix, StringComparison.Ordinal)) return false;
        int ruleStart = prefix.Length;
        int slash = type.IndexOf('/', ruleStart);
        if (slash <= ruleStart || slash >= type.Length - 1) return false;
        rule = type.Substring(ruleStart, slash - ruleStart);
        suffix = type.Substring(slash + 1);
        return true;
    }
}
