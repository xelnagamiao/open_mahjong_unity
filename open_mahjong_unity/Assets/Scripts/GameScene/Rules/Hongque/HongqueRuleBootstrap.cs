using UnityEngine;

/// <summary>
/// 把虹雀注册进核心：规则清单 + 族 GameState（HongqueGameState）+ 动作词表（HongqueActionWords）。
/// 核心代码不引用本模块；本模块通过 Unity 的 SubsystemRegistration 回调自注册，
/// IL2CPP 会保留该方法，关闭域重载时也会在每次 Play 重新执行（注册均为幂等覆盖）。
/// </summary>
internal static class HongqueRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleRegistry.Register(new RuleManifest {
            RuleId = HongqueGameState.RuleId,
            DefaultSubRule = "hongque/v1.6",
            DisplayName = "虹雀",
            LobbyOrder = 8,
            LobbySubRules = HongqueLobby.SubRules,
            CreateRoomDefaults = HongqueLobby.Defaults(),
            HepaiWayOptions = new[] { "允许多家和牌", "头跳" },
            JiagangExtendsLastMeld = true,
            GameStateFactory = () => new HongqueGameState(),
            VerticalMelds = true,
            TipsProvidedByGameState = true,
            FanNameText = HongqueFanText.FanName,
            FanValueText = HongqueFanText.FanValue,
            RoundName = HongqueFanText.RoundName,
            MaxRoundText = HongqueFanText.MaxRoundText,
            MaxRoundIsHandCount = true,
            ScoreboardFanText = HongqueFanText.ScoreboardFanText,
            SettlementTotal = HongqueFanText.SettlementTotal,
            PlaysGongHuSound = HongqueFanText.PlaysGongHu,
            // 虹雀自摸也叫"和"，报声同国标
            ActionCaption = word => word == "hu_self" ? "和" : null,
            ActionVoice = word => word == "hu_self" ? "hu"
                // bd 是牌谱中的补牌摸牌节点；虹雀没有此前的补花节点来报声。
                : word == HongqueActionWords.Supplement || word == "bd" ? "bupai" : null,
        });
        HongqueActionWords.RegisterAll();
    }
}
