using System;
using System.Collections.Generic;
using UnityEngine;

internal static class HongKongRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        var defaults = TaiwanLobby.Defaults();
        defaults[CreateRoomKeys.SubRule] = 0;
        defaults.Remove(CreateRoomKeys.Cuohe);
        defaults.Remove(CreateRoomKeys.CuoheType);
        RuleRegistry.Register(new RuleManifest {
            RuleId=HongKongGameState.RuleId, DefaultSubRule=HongKongGameState.Qingzhang,
            DisplayName="香港麻将", LobbyName="香港麻将", LobbyOrder=6,
            LobbySubRules = new[] {
                new RuleLobbySubRule(HongKongGameState.Qingzhang,"清章十三","香港麻雀协会《香港麻雀总例》：三番起糊、十番封顶、每局轮庄，默认无花，可选择有花。"),
                new RuleLobbySubRule(HongKongGameState.New13Gametower,"新章十三（Wiki）","Mahjong Wiki 的 IGS 分栏。"),
                new RuleLobbySubRule(HongKongGameState.New13Lianhuise,"新章十三（恋绘色）","恋绘色《香港新章规则书》。"),
                new RuleLobbySubRule(HongKongGameState.QingzhangRemix,"新章十三（恋绘色魔改）",HongKong_Create_RoomConfig.RemixDescription),
                new RuleLobbySubRule(HongKongGameState.New16,"新章十六","香港麻雀协会《港式十六张新章麻雀总例》2025-07-28详述版：144张，五组面子一对，叮牌、连庄、拉踢。"),
            },
            CreateRoomDefaults=defaults, LobbyHasDetailedConfig=true,
            GameStateFactory=()=>new HongKongGameState(), OutboundChannel="hongkong",
            Tingpai=HongKongShape.Waiting, DescribeWaitingTile=HongKongGameState.Describe,
            FanNameText=HongKongFanText.Name, FanValueText=HongKongFanText.Value,
            SettlementTotal=HongKongFanText.Total, ScoreboardFanText=q=>$"{q.HuScore}{HongKongFanText.Unit(q.Rule)}",
            RoundName=RoundTextDictionary.WindNumberRoundName, DefaultHepaiLimit=0,
            RoundStatusText=info=>RoundTextDictionary.WindNumberRoundName(info.current_round),
            // This is a compact settings slot beside the seed indicator.
            RoundSupplementText=info=>info.sub_rule==HongKongGameState.Qingzhang ? "3番起糊"
                : info.sub_rule==HongKongGameState.QingzhangRemix ? "30分起和"
                : info.sub_rule==HongKongGameState.New16
                    ? info.hongkong_info?.dealer_streak>0 ? $"连庄:{info.hongkong_info.dealer_streak}" : "拉踢制"
                    : HongKong_Create_RoomConfig.IsLianhuise(info.sub_rule,info.detailed_config) ? "恋绘色" : "线性计分",
            SupportsRobbedAddedKongSource=true, ReplacementFromTailEnd=true,
            PublicReadyStateReplay=true, RulebookKey="hongkong", RulebookPath=HongKong_Create_RoomConfig.RulebookPath,
            RecordFlowerWin=HongKongFanText.IsFlowerTick,
            SettlementHasNoWinTile=HongKongFanText.IsFlowerWin,
            HuPresentationAction=(word,fans,config)=>HongKongFanText.IsFlowerWin(fans) ? "hu_flower" : word,
            ReplayConcealedKongMask=(rule,tiles)=> {
                var mask=new int[8];
                var record=GameRecordManager.Instance;
                var config=record!=null && record.gameObject.activeSelf ? record.GetDetailedConfigSnapshot() : GameSession.Current.DetailedConfig;
                bool visible=rule==HongKongGameState.Qingzhang || HongKong_Create_RoomConfig.IsLianhuise(rule,config);
                if (rule==HongKongGameState.QingzhangRemix) return new[]{2,tiles[0],0,tiles[1],0,tiles[2],2,tiles[3]};
                for (int i=0;i<4;i++) { mask[i*2]=visible ? 0 : 2; mask[i*2+1]=tiles[i]; }
                return mask;
            },
            ActionCaption=word=>word=="riichi" ? (IsDingRuleContext() ? "叮" : "报听") : null,
            ActionVoice=word=>word=="riichi" && IsDingRuleContext() ? "ding" : null,
        });
        foreach (string word in new[] {"riichi","riichi_cancel","ding_initial","pull_cut"}) {
            ActionWords.Register(new ActionWordSpec { Word=word, Kind=ActionWordKind.Other,
                DisplayPriority=word=="riichi_cancel" ? (int?)60 : null,
                // Shared action words must leave other rule families' captions intact.
                Label=w=>GameSession.Current.RoomRule!=HongKongGameState.RuleId ? null
                    : w=="ding_initial" ? "天叮" : w=="pull_cut" ? "斩拉" : w=="riichi_cancel" ? "取消声明"
                    : GameSession.Current.SubRule==HongKongGameState.New16 ? "叮牌" : "报听" });
        }
    }

    private static bool IsDingRuleContext() {
        // 牌谱/观战使用自身子规则，不能沿用此前牌桌的规则。
        GameRecordManager.ResolveActionRuleContext(null, null, out _, out string subRule);
        return subRule == HongKongGameState.New16;
    }
}

internal static class HongKongFanText {
    public static bool IsFlowerTick(IReadOnlyList<string> tick) {
        if (tick==null || tick.Count<4 || !tick[0].StartsWith("hu_")) return false;
        string[] fans=Newtonsoft.Json.JsonConvert.DeserializeObject<string[]>(tick[3]);
        return IsFlowerWin(fans);
    }
    public static bool IsFlowerWin(IList<string> fans) {
        if (fans==null) return false;
        foreach (string fan in fans)
            if (fan.StartsWith("HK|small_flower_win|") || fan.StartsWith("HK|big_flower_win|") || fan.StartsWith("HK|flower_win|")
                || fan.StartsWith("HK|flower_heavenly|") || fan.StartsWith("HK|flower_humanly|")) return true;
        return false;
    }
    public static string Unit(string rule) => rule==HongKongGameState.New16 || rule==HongKongGameState.QingzhangRemix ? "分" : "番";
    public static string Name(string rule,string label) {
        string[] parts=label?.Split('|');
        return parts?.Length>=4 && parts[0]=="HK" ? parts[3] : label;
    }
    public static string Value(string rule,string label) {
        string[] parts=label?.Split('|');
        return parts?.Length>=4 && parts[0]=="HK" ? parts[2]+(parts.Length>=5 ? parts[4] : Unit(rule)) : "";
    }
    public static SettlementTotalDisplay Total(SettlementTotalQuery query) => new SettlementTotalDisplay {
        FanText=$"{query.HuScore}{Unit(query.Rule)}",
        ScoreText=query.Rule==HongKongGameState.New16 && query.WinnerPointDelta==0 && query.HuScore>0 ? "记账"
            : query.WinnerPointDelta.HasValue ? $"{query.WinnerPointDelta.Value:+#;-#;0}点" : "",
    };
}
