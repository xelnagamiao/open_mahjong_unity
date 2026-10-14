using System.Collections.Generic;
using UnityEngine;

internal static class MalaysiaRuleBootstrap {
    public const string Description = "传统84张马来西亚三人麻将：筒子、字牌、16花与4飞。东南西三席，5番起和，可吃碰杠，飞可作百搭或当花。输家花分、飞牌分与即时杠花分分别结算；默认满番不叠加。规则依据 Cheah / Sloper 定型文，细节见平台规则书。";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        foreach (string id in new[] { MalaysiaGameState.RuleId, MalaysiaGameState.SubRule }) {
            RuleNameDictionary.WholeName[id] = "马来西亚三人麻将";
            RuleNameDictionary.ShortName[id] = "马来三麻";
        }
        RuleRegistry.Register(new RuleManifest {
            RuleId=MalaysiaGameState.RuleId, DefaultSubRule=MalaysiaGameState.SubRule,
            DisplayName="马来西亚三人麻将", LobbyName="马来西亚三人麻将", LobbyOrder=20,
            LobbySubRules=new[] { new RuleLobbySubRule(MalaysiaGameState.SubRule,"传统84张",Description) },
            MinPlayersToStart=3, GameStateFactory=()=>new MalaysiaGameState(), OutboundChannel="malaysia",
            ReplayFactory=record=>record.CreateMalaysiaReplayModule(), BuildRoom=MalaysiaRoomBuilder.Build,
            CreateRoomDefaults=new Dictionary<string,object> {
                {CreateRoomKeys.GameRound,4}, {CreateRoomKeys.RoundTimer,3}, {CreateRoomKeys.StepTimer,1},
                {CreateRoomKeys.Tips,true}, {CreateRoomKeys.CountTips,false}, {CreateRoomKeys.PointerTips,true},
                {CreateRoomKeys.Password,false}, {CreateRoomKeys.RandomSeed,false},
                {CreateRoomKeys.TouristLimit,false}, {CreateRoomKeys.AllowSpectator,true},
            },
            HandJokerTiles=_=>new[]{81}, RecordHandJokerTiles=_=>new[]{81},
            OpeningHandHasDrawSlot=info=>info.malaysia_info?.self_has_draw_slot == true,
            TipsProvidedByGameState=true,
            Tingpai=q=>MalaysiaGameState.Active?.Hints.Waiting(q) ?? new HashSet<int>(),
            DescribeWaitingTile=q=>MalaysiaGameState.Active?.Hints.Describe(q),
            FanNameText=(rule,label)=>MalaysiaFanText.Name(label), FanValueText=(rule,label)=>MalaysiaFanText.Value(label),
            SettlementTotal=q=>new SettlementTotalDisplay {
                FanText=$"基本和分 {q.HuScore}分",
                ScoreText=q.WinnerPointDelta.HasValue ? $"{q.WinnerPointDelta.Value:+#;-#;0}分" : "",
            },
            ScoreboardFanText=q=>$"{q.HuScore}分",
            RoundName=round=>$"第{round}局", MaxRoundText=rounds=>$"{rounds*3}局",
            RoundSupplementText=info=>info.detailed_config?.TryGetValue("multiple_max",out object multiple)==true && multiple is bool enabled && enabled ? "叠满:开" : "叠满:关",
            DefaultHepaiLimit=5, HasFlowerReplacement=true, ReplacementFromTailEnd=true,
            SupportsRobbedAddedKongSource=true, RulebookKey="malaysia",
        });
        ActionWords.Register(new ActionWordSpec { Word="malaysia_peng:", Kind=ActionWordKind.Peng, Label=w=>w.EndsWith(":0")?"碰":$"碰（{w.Split(':')[1]}飞）" });
        ActionWords.Register(new ActionWordSpec { Word="malaysia_chi:", Kind=ActionWordKind.Chi, Label=w=>ChowLabel(w) });
        ActionWords.Register(new ActionWordSpec { Word="malaysia_redeem:", Kind=ActionWordKind.Other, Label=_=>"赎飞", BlocksAutoCut=true });
        ActionWords.Register(new ActionWordSpec { Word="malaysia_fly", Kind=ActionWordKind.Other, Label=_=>"飞当花", BlocksAutoCut=true });
    }
    private static string ChowLabel(string word) {
        var p=word.Split(':'); int middle=int.Parse(p[1])%10;
        return $"吃 {middle-1}{middle}{middle+1}"+(p[2]=="0"?"":"（飞）");
    }
}
internal static class MalaysiaFanText {
    public static string Name(string label) { var p=label?.Split('|'); return p?.Length>=4 && p[0]=="MY" ? p[3] : label; }
    public static string Value(string label) { var p=label?.Split('|'); return p?.Length>=4 && p[0]=="MY" ? p[2]+(p[2]=="满番"?"":"番") : ""; }
}
