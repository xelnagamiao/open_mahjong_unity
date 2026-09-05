using UnityEngine;

/// <summary>
/// 把虹雀注册进 RuleRegistry。核心代码不引用本模块；本模块通过 Unity 的
/// SubsystemRegistration 回调自注册，IL2CPP 会保留该方法，关闭域重载时也会在每次 Play 重新执行。
/// </summary>
internal static class HongqueRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleRegistry.Register(new RuleManifest {
            RuleId = HongqueDriver.RuleId,
            DefaultSubRule = "hongque/v1.6",
            DisplayName = "虹雀",
            DriverFactory = () => new HongqueDriver(),
            VerticalMelds = true,
            TipsProvidedByDriver = true,
        });
    }
}
