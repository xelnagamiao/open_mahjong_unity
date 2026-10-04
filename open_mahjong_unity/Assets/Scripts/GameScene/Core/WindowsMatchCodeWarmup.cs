using UnityEngine;

/// <summary>把 Windows Mono 首次动作的代码编译移到进入场景之前；不执行游戏动作。</summary>
internal static class WindowsMatchCodeWarmup {
#if UNITY_STANDALONE_WIN && ENABLE_MONO && !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void PrepareMatchCode() {
        var types = new System.Collections.Generic.List<System.Type> {
            typeof(Game3DManager), typeof(GameCanvas), typeof(SoundManager),
            typeof(WindowsManager), typeof(NormalGameStateManager), typeof(GameSession),
            typeof(TableMirror), typeof(TurnBasedGameState), typeof(GuobiaoGameState),
            typeof(SettlementPresenter), typeof(RoundEndPresentation), typeof(EndResultPanel),
            typeof(EndLiujuPanel), typeof(ScoreHistorySettlementHelper), typeof(ScoreHistoryPanel),
            typeof(GameSceneUIManager), typeof(BoardCanvas), typeof(GamePlayerPanel),
            typeof(GameRecordManager), typeof(TipsBlock), typeof(TipsContainer),
            typeof(TileCard), typeof(Tile3D), typeof(GuobiaoFanText), typeof(FanTextDictionary),
            typeof(HepaiRevealDirector), typeof(ScoreHistoryMainFanCell), typeof(ScoreHistoryFanTooltip)
        };
        const System.Reflection.BindingFlags nested =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        const System.Reflection.BindingFlags methods = nested |
            System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Instance;

        // 编译迭代器及初始化方法，避免首次动作、开局和结算时才在主线程编译。
        for (int i = 0; i < types.Count; i++) types.AddRange(types[i].GetNestedTypes(nested));
        foreach (System.Type type in types) {
            if (!type.ContainsGenericParameters) {
                foreach (System.Reflection.ConstructorInfo constructor in type.GetConstructors(methods)) {
                    constructor.MethodHandle.GetFunctionPointer();
                }
            }
            foreach (System.Reflection.MethodInfo method in type.GetMethods(methods)) {
                if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null) continue;
                // Unity Mono 的 RuntimeHelpers.PrepareMethod 是空实现，获取入口才会触发编译。
                method.MethodHandle.GetFunctionPointer();
            }
        }
    }
#endif
}
