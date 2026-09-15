using UnityEngine;

/// <summary>
/// 把回合制标准族登记为 RuleRegistry 的默认族：任何未单独注册的 room_rule 都由 TurnBasedGameState 承载。
/// 各回合制族（国标/日麻/川麻……）在自己的 Bootstrap 里注册清单并指定自己的 GameStateFactory。
/// </summary>
internal static class TurnBasedRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleRegistry.DefaultGameStateFactory = () => new TurnBasedGameState();
    }
}
