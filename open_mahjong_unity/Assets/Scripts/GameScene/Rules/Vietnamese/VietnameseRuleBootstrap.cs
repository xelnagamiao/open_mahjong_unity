using System;
using System.Linq;
using System.Collections.Generic;
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
            RulebookPath=(sub,config)=>"https://drive.google.com/file/d/1ETjCEPo-fVDKkcQwZ_mK3qCsop5fhagD/view",
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
        // Keep the platform's separate meld buttons, colors and auto-pass filters.
        // Each physical choice still submits the server's opaque vn_claim ID.
        ActionWords.Register(new ActionWordSpec{Word="vn_chi",Kind=ActionWordKind.Chi,BlocksAutoCut=true,Label=w=>"吃",Expand=VietnameseGameState.ClaimChoices});
        ActionWords.Register(new ActionWordSpec{Word="vn_peng",Kind=ActionWordKind.Peng,BlocksAutoCut=true,Label=w=>"碰",Expand=VietnameseGameState.ClaimChoices});
        ActionWords.Register(new ActionWordSpec{Word="vn_gang",Kind=ActionWordKind.MingGang,BlocksAutoCut=true,Label=w=>"杠",Expand=VietnameseGameState.ClaimChoices});
        ActionWords.Register(new ActionWordSpec{Word="vn_replace",Kind=ActionWordKind.Other,DisplayPriority=40,BlocksAutoCut=true,Label=w=>"补鬼",Expand=VietnameseGameState.ReplacementChoices});
        ActionWords.Register(new ActionWordSpec{Word="vn_bounce",Kind=ActionWordKind.Other,DisplayPriority=30,BlocksAutoCut=true,Label=w=>"弹鬼",Expand=VietnameseGameState.BounceChoices});
        ActionWords.Register(new ActionWordSpec{Word="hu_special",Kind=ActionWordKind.Tsumo,DisplayPriority=50,Label=w=>"无花无叶和"});
    }
}
internal static class VietnameseScoreText {
    // The existing Chinese result fonts cover these units. One mun is six phan;
    // use 番/满 here instead of displaying unsupported accented Latin glyphs.
    public static string Amount(int phan)=>phan<6?$"{phan}番":$"{phan/6}满"+(phan%6==0?"":$" + {phan%6}番");
    private static readonly Dictionary<string,string> Names=new Dictionary<string,string> {
        {"fully_closed","门前清"},
        {"single_wait","单种待牌"},
        {"all_runs","全顺"},
        {"seat_wind","门风刻"},
        {"round_wind","圈风刻"},
        {"dragon_sets","三元刻"},
        {"quads","杠"},
        {"all_called","全求人"},
        {"half_flush","混一色"},
        {"full_flush","清一色"},
        {"all_sets","对对和"},
        {"little_three_dragons","小三元"},
        {"big_three_dragons","大三元"},
        {"terminals_honors","混幺九"},
        {"all_terminals","清幺九"},
        {"all_honors","字一色"},
        {"four_closed_sets","四暗刻"},
        {"little_four_winds","小四喜"},
        {"big_four_winds","大四喜"},
        {"four_quads","四杠"},
        {"four_closed_quads","四暗杠"},
        {"nine_gates","九莲宝灯"},
        {"thirteen_orphans","十三幺"},
        {"seven_pairs","七对"},
        {"true_no_flowers_leaves","真无花无叶"},
        {"discarding_flowers_jokers","弃花弃鬼"},
        {"discarding_blue_jokers","弃蓝鬼"},
        {"discarding_flowers","弃花"},
        {"seat_flowers","正花"},
        {"eight_flowers","多花奖励"},
        {"blue_joker_set","蓝组鬼牌"},
        {"red_joker_set","红组鬼牌"},
        {"matching_any_number","总合同时置换"},
        {"replacement_win","补牌和"},
        {"last_tile","海底和"},
        {"rob_kong","抢杠和"},
        {"blessing_of_heaven","天和"},
        {"blessing_of_earth","地和"},
        {"blessing_of_man","人和"},
        {"six_consecutive_wins","六连和"},
        {"no_jokers_bonus","无鬼加满"},
        {"small_no_jokers_bonus","无鬼加番"},
        {"wait_closed_bonus","门清单听加满"},
        {"wait_runs_closed","门清平和单听"},
        {"no_jokers_mosquito","无鬼无番和"},
        {"bonus_discard_liability","花鬼放铳加满"},
        {"safe_discard_declaration","无花无叶宣告"},
    };
    public static string Name(string label) {
        var p=label?.Split('|');if(p?.Length<3 || p==null || p[0]!="VN")return label;
        string key=p[1];if(Names.TryGetValue(key,out string name))return name;
        if(key.StartsWith("replaced_") && int.TryParse(key.Substring(9),out int tile))return "置换"+VietnameseTileVisual.Name(tile);
        if(key.StartsWith("number_bouquet_"))return "同号四花";
        if(key.StartsWith("bouquet_"))return "同组四花";
        return key.Replace('_',' ');
    }
    public static string Value(string label){var p=label?.Split('|');return p?.Length>=3 && p[1]!="safe_discard_declaration" && int.TryParse(p[2],out int n)?Amount(n):"";}
}
