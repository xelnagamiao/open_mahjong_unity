using System.Collections.Generic;
using UnityEngine;

internal static class WenzhouRuleBootstrap {
    public const string Description = "MIL《温州麻将（试点）竞赛规则（试行2024版）》：136张，庄家17张、闲家16张。每局翻财，财神可作任意牌，非财神白板固定代财神本牌；可吃碰杠及点和，五面子一将、八对加单张或三财和牌。软和、硬和、双番，连庄倍数与财神另行结算。";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleNameDictionary.WholeName[WenzhouGameState.RuleId] = "温州麻将";
        RuleNameDictionary.WholeName[WenzhouGameState.SubRule] = "温州麻将（MIL 2024）";
        RuleNameDictionary.ShortName[WenzhouGameState.RuleId] = "温州";
        RuleNameDictionary.ShortName[WenzhouGameState.SubRule] = "温州";
        RuleRegistry.Register(new RuleManifest {
            RuleId = WenzhouGameState.RuleId, DefaultSubRule = WenzhouGameState.SubRule,
            DisplayName = "温州麻将", LobbyName = "温州麻将", LobbyOrder = 21,
            LobbySubRules = new[] { new RuleLobbySubRule(WenzhouGameState.SubRule, "MIL 2024", Description) },
            CreateRoomDefaults = new Dictionary<string, object> {
                { CreateRoomKeys.GameRound, 4 }, { CreateRoomKeys.RoundTimer, 3 }, { CreateRoomKeys.StepTimer, 1 },
                { CreateRoomKeys.Tips, true }, { CreateRoomKeys.CountTips, false }, { CreateRoomKeys.PointerTips, true },
                { CreateRoomKeys.Password, false }, { CreateRoomKeys.RandomSeed, false },
                { CreateRoomKeys.TouristLimit, false }, { CreateRoomKeys.AllowSpectator, true },
            },
            GameStateFactory = () => new WenzhouGameState(), OutboundChannel = "wenzhou",
            Tingpai = WenzhouGameState.Waiting, DescribeWaitingTile = WenzhouGameState.Describe,
            FanNameText = (rule, label) => WenzhouText.FanName(label),
            FanValueText = (rule, label) => WenzhouText.FanValue(label),
            SettlementTotal = q => new SettlementTotalDisplay {
                FanText = $"和牌{q.HuScore}倍",
                ScoreText = q.WinnerPointDelta.HasValue ? $"{q.WinnerPointDelta.Value:+#;-#;0}分" : "",
            },
            ScoreboardFanText = q => $"{q.HuScore}倍",
            SettlementFootnote = q => "和牌底分1分，庄家按连庄倍数收付；杠分与财神另行结算。",
            ActionVoice = word => word == "hu_self" ? "hu" : null,
            RoundName = RoundTextDictionary.WindSeatRoundName,
            // The dedicated legend owns dynamic joker/streak metadata in both live play and replay.
            RoundSupplementText = info => "",
            DefaultHepaiLimit = 0, HasFlowerReplacement = false,
            RonWinTileTravelsFromRiver = true, SupportsRobbedAddedKongSource = false,
            AdjustHepaiPresentation = AdjustPendingKongWinPresentation,
            ReplacementFromTailEnd = true, RecordUsesInlineKongScores = true,
            ReplayConcealedKongMask = (rule, tiles) => new[] { 2, tiles[0], 2, tiles[1], 2, tiles[2], 2, tiles[3] },
            RulebookKey = "wenzhou",
        });
    }

    private static void AdjustPendingKongWinPresentation(HepaiPresentationRequest request, bool isRobbedKong) {
        if (!isRobbedKong) return;
        // Wenzhou does not commit an added kong until its claim window closes.
        // Its winning tile is supplied in the authoritative winner hand, while the
        // original pung stays intact. No river or fourth meld object exists to take.
        request.WinTileMode = HepaiWinTilePresentMode.RonInstantThenPause;
        request.RestoreRecordHandFromSnapshot = true;
        // In the legacy presenter this flag recycles an already displayed fourth
        // kong tile. Clearing it only changes that source operation; the server
        // result, settlement envelope, and replay win_source retain the rob fact.
        request.IsQianggang = false;
    }
}

public static class WenzhouText {
    public static string FanName(string label) {
        string[] parts = label?.Split('|');
        return parts?.Length >= 4 && parts[0] == "WZ" ? parts[3] : label;
    }
    public static string FanValue(string label) {
        string[] parts = label?.Split('|');
        return parts?.Length >= 4 && parts[0] == "WZ" ? parts[2].Replace("x", "×") : "";
    }
    public static string Tile(int tile) {
        if (tile >= 11 && tile <= 19) return $"{tile % 10}万";
        if (tile >= 21 && tile <= 29) return $"{tile % 10}饼";
        if (tile >= 31 && tile <= 39) return $"{tile % 10}条";
        switch (tile) { case 41: return "东"; case 42: return "南"; case 43: return "西"; case 44: return "北"; case 45: return "中"; case 46: return "白"; case 47: return "发"; default: return "未翻财"; }
    }
}
