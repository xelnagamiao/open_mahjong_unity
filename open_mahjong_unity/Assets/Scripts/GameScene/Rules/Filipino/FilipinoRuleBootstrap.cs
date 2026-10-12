using UnityEngine;
internal static class FilipinoRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        var defaults=TaiwanLobby.Defaults(); defaults[CreateRoomKeys.SubRule]=0;
        defaults.Remove(CreateRoomKeys.Cuohe); defaults.Remove(CreateRoomKeys.CuoheType);
        RuleRegistry.Register(new RuleManifest {
            RuleId=FilipinoGameState.RuleId, DefaultSubRule=FilipinoGameState.Classic16,
            DisplayName="菲律宾麻将", LobbyName="菲律宾麻将", LobbyOrder=34,
            LobbySubRules=new[]{new RuleLobbySubRule(FilipinoGameState.Classic16,"经典十六张","Ging 2010：五组面子一对；字牌作花。和牌者掷骰定下家庄，默认自摸双付。")},
            CreateRoomDefaults=defaults, LobbyHasDetailedConfig=true,
            BuildRoom=FilipinoRoomBuilder.Build, GameStateFactory=()=>new FilipinoGameState(), OutboundChannel="filipino",
            ReplayFactory=record=>record.CreateFilipinoReplayModule(),
            Tingpai=FilipinoShape.Waiting, DescribeWaitingTile=q=>WaitTileHint.None("牌形听牌"),
            OpeningHandHasDrawSlot=info=>info.filipino_info?.self_has_draw_slot==true,
            HasFlowerReplacement=true, ReplacementFromTailEnd=true, PublicReadyStateReplay=true,
            RoundName=n=>$"第{n}局", RoundStatusText=info=>$"第{info.current_round}局",
            MaxRoundText=n=>$"{n*4}局", RoundSupplementText=info=>"16张",
            FanNameText=(rule,label)=>label, FanValueText=(rule,label)=>"",
            SettlementTotal=q=>new SettlementTotalDisplay { FanText=$"{q.HuScore}点", ScoreText=q.WinnerPointDelta.HasValue?$"{q.WinnerPointDelta.Value:+#;-#;0}点":"" },
            ScoreboardFanText=q=>$"{q.HuScore}点", DefaultHepaiLimit=0,
        });
    }
}
