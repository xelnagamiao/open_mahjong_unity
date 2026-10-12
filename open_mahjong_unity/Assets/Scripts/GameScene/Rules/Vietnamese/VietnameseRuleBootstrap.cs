using System;
using System.Linq;
using UnityEngine;
internal static class VietnameseRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        var defaults=TaiwanLobby.Defaults();defaults[CreateRoomKeys.SubRule]=0;
        defaults.Remove(CreateRoomKeys.Cuohe);defaults.Remove(CreateRoomKeys.CuoheType);
        RuleRegistry.Register(new RuleManifest {
            RuleId="vietnamese",DefaultSubRule=VietnameseGameState.Classic160,
            DisplayName="越南麻将",LobbyName="越南麻将",LobbyOrder=33,
            LobbySubRules=new[]{new RuleLobbySubRule(VietnameseGameState.Classic160,"Classic 160张","How to Play Vietnamese Mahjong · Bridget Young 2026 修订扩展版。四人经典规则，含鬼牌、弹鬼、包付与累进计分。")},
            CreateRoomDefaults=defaults,LobbyHasDetailedConfig=true,BuildRoom=VietnameseRoomBuilder.Build,
            GameStateFactory=()=>new VietnameseGameState(),OutboundChannel="vietnamese",
            ReplayFactory=record=>record.CreateVietnameseReplayModule(),Tingpai=VietnameseShape.Waiting,
            DescribeWaitingTile=q=>WaitTileHint.None("牌形听牌"),OpeningHandHasDrawSlot=info=>info.vietnamese_info?.self_has_draw_slot==true,
            HasFlowerReplacement=true,ReplacementFromTailEnd=true,PublicReadyStateReplay=true,SupportsRobbedAddedKongSource=true,
            TileBadgeText=t=>VietnameseTileVisual.IsExtra(t)?VietnameseTileVisual.Name(t):null,
            RoundName=RoundTextDictionary.WindNumberRoundName,RoundStatusText=i=>RoundTextDictionary.WindNumberRoundName(i.current_round),
            RoundSupplementText=i=>"160张",DefaultHepaiLimit=0,
            FanNameText=(rule,label)=>VietnameseScoreText.Name(label),FanValueText=(rule,label)=>VietnameseScoreText.Value(label),
            SettlementTotal=q=>new SettlementTotalDisplay{FanText=VietnameseScoreText.Amount(q.HuScore),ScoreText=q.WinnerPointDelta.HasValue?$"{q.WinnerPointDelta.Value:+#;-#;0}点":""},
            ScoreboardFanText=q=>VietnameseScoreText.Amount(q.HuScore),
            SettlementHasNoWinTile=labels=>labels!=null && labels.Contains("VN|safe_discard_declaration|0"),
        });
        ActionWords.Register(new ActionWordSpec{Word="vn_claim",Kind=ActionWordKind.Other,DisplayPriority=20,BlocksAutoCut=true,Label=w=>"吃碰杠",Expand=VietnameseGameState.ClaimChoices});
        ActionWords.Register(new ActionWordSpec{Word="vn_replace",Kind=ActionWordKind.Other,DisplayPriority=40,BlocksAutoCut=true,Label=w=>"补鬼",Expand=VietnameseGameState.ReplacementChoices});
        ActionWords.Register(new ActionWordSpec{Word="vn_bounce",Kind=ActionWordKind.Other,DisplayPriority=30,BlocksAutoCut=true,Label=w=>"弹鬼",Expand=VietnameseGameState.BounceChoices});
        ActionWords.Register(new ActionWordSpec{Word="hu_special",Kind=ActionWordKind.Tsumo,DisplayPriority=50,Label=w=>"无花无叶和"});
    }
}
internal static class VietnameseScoreText {
    public static string Amount(int phan)=>phan<6?$"{phan} phán":$"{phan/6} mủn"+(phan%6==0?"":$" + {phan%6} phán");
    public static string Name(string label){var p=label?.Split('|');return p?.Length>=3&&p[0]=="VN"?p[1].Replace('_',' '):label;}
    public static string Value(string label){var p=label?.Split('|');return p?.Length>=3&&int.TryParse(p[2],out int n)?Amount(n):"";}
}
