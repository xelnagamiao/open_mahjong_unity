using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 规则注册表：room_rule / sub_rule → RuleManifest → 每局一个 IGameState。
///
/// 各族在 <c>[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]</c> 中调用 Register，
/// 核心不 import 任何族。未注册的规则回退到 <see cref="DefaultGameStateFactory"/>（回合制标准族）。
/// </summary>
public static class RuleRegistry {
    private static readonly Dictionary<string, RuleManifest> ManifestsByRule =
        new Dictionary<string, RuleManifest>(StringComparer.Ordinal);

    /// <summary>当前对局/牌谱的规则清单；无对局或规则未注册时为 null。</summary>
    public static RuleManifest Current { get; private set; }

    /// <summary>当前对局的族 GameState；无对局时为 null。每局 SetCurrent 时新建。</summary>
    public static IGameState ActiveGameState { get; private set; }

    /// <summary>
    /// 未注册规则 / 清单未声明 GameStateFactory 时使用的族工厂。由回合制族模块在自注册时设置
    /// （TurnBasedGameState）；为 null 时 ActiveGameState 为 null，核心按无族处理。
    /// </summary>
    public static Func<IGameState> DefaultGameStateFactory;

    /// <summary>所有已注册清单（大厅/目录用）。</summary>
    public static IEnumerable<RuleManifest> All => ManifestsByRule.Values;

    /// <summary>按 LobbyOrder 排序的清单，供建房下拉。</summary>
    public static List<RuleManifest> Ordered {
        get {
            var list = new List<RuleManifest>(ManifestsByRule.Values);
            list.Sort((a, b) => {
                int c = a.LobbyOrder.CompareTo(b.LobbyOrder);
                return c != 0 ? c : string.CompareOrdinal(a.RuleId, b.RuleId);
            });
            return list;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSessionStatics() {
        // 不清 ManifestsByRule / DefaultGameStateFactory：各模块的 Register 与本方法同属 SubsystemRegistration，
        // 执行顺序无保证。Register 采用覆盖写入，因此重复注册是幂等的。
        Current = null;
        ActiveGameState = null;
    }

    public static void Register(RuleManifest manifest) {
        if (manifest == null || string.IsNullOrEmpty(manifest.RuleId)) {
            Debug.LogError("RuleRegistry.Register: 清单缺少 RuleId");
            return;
        }
        ManifestsByRule[manifest.RuleId] = manifest;
    }

    /// <summary>按 room_rule 精确解析；room_rule 为空或未注册时尝试用 sub_rule 前缀解析。</summary>
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

    /// <summary>按 room_rule / sub_rule 解析清单；未注册返回 null。表现层读清单开关时用。</summary>
    public static RuleManifest Resolve(string roomRule, string subRule = null) {
        TryResolve(roomRule, subRule, out RuleManifest manifest);
        return manifest;
    }
    /// <summary>为该清单创建一个新的族 GameState；清单未声明工厂时用默认族。</summary>
    public static IGameState CreateGameState(RuleManifest manifest) {
        Func<IGameState> factory = manifest?.GameStateFactory ?? DefaultGameStateFactory;
        return factory?.Invoke();
    }

    /// <summary>
    /// 确定当前规则并保证有一个对应族的 GameState 实例：
    /// 清单与当前一致则沿用现有实例（同一场对局内每局 game_start、以及消息先到再初始化的情形都不重建，
    /// 族的跨局挂起状态得以保留）；清单变化或尚无实例时新建。
    /// 对局开始（InitializeGame）与 gamestate 消息路由都走这里；退出走 ClearCurrent。
    /// </summary>
    public static IGameState SetCurrent(string roomRule, string subRule = null) {
        TryResolve(roomRule, subRule, out RuleManifest manifest);
        if (ActiveGameState != null && Current == manifest) return ActiveGameState;
        ActiveGameState?.OnSessionReset();
        Current = manifest;
        ActiveGameState = CreateGameState(manifest);
        return ActiveGameState;
    }

    /// <summary>对局/牌谱退出时由核心调用。</summary>
    public static void ClearCurrent() {
        ActiveGameState?.OnSessionReset();
        Current = null;
        ActiveGameState = null;
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
