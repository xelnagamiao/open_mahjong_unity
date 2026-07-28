using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

public partial class GameRecordManager {
    /// <summary>牌谱听牌提示与对局当时 tips 房间设置无关；game_title.tips 仅作信息面板元数据。</summary>
    public bool ShouldShowRecordTips() {
        return gameObject.activeSelf && gameRecord?.gameTitle != null;
    }

    public void HideRecordTips() {
        TipsBlock.Instance.HideTipsBlock();
    }

    public void RefreshRecordTips() {
        if (!ShouldShowRecordTips()) {
            HideRecordTips();
            return;
        }

        if (!recordPlayer_to_info.TryGetValue("self", out RecordPlayer selfPlayer) || selfPlayer == null) {
            HideRecordTips();
            return;
        }

        if (selfPlayer.isHu) {
            HideRecordTips();
            return;
        }

        List<int> handForCheck = RecordChongHintCalculator.NormalizeHandForTingpai(selfPlayer.tileList);
        if (handForCheck == null) {
            HideRecordTips();
            return;
        }

        TryGetActiveRecordRuleContext(out string roomRule, out _);
        Dictionary<string, object> detailedConfig = GetDetailedConfigSnapshot();
        HashSet<int> waiting = RecordChongHintCalculator.ComputeWaitingTilesForPlayer(selfPlayer, roomRule, detailedConfig);
        if (waiting.Count == 0) {
            HideRecordTips();
            return;
        }

        RecordTipsContext ctx = BuildRecordTipsContext(selfPlayer);
        TipsBlock.Instance.ShowRecordTips(ctx, handForCheck, waiting.ToList());
    }

    private RecordTipsContext BuildRecordTipsContext(RecordPlayer selfPlayer) {
        TryGetActiveRecordRuleContext(out string roomRule, out string subRule);

        int hepaiLimit = gameRecord.gameTitle.ContainsKey("hepai_limit")
            ? ReadGameTitleInt(gameRecord.gameTitle, "hepai_limit", 0)
            : roomRule == "riichi" || roomRule == "changsha" ? 1 : 8;

        int displayRound = currentRoundIndex;
        if (gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData) && roundData.currentRound > 0) {
            displayRound = roundData.currentRound;
        }

        var ctx = new RecordTipsContext {
            RoomRule = roomRule,
            SubRule = subRule,
            HepaiLimit = hepaiLimit,
            CurrentRound = displayRound,
            SelfPlayerIndex = selectedPlayerIndex,
            RemainTiles = GetRecordRemainTiles(),
            SelfHuapaiList = selfPlayer.huapaiList ?? new List<int>(),
            SelfCombinationMasks = selfPlayer.combinationMasks ?? new List<int[]>(),
            SelfIsRiichi = selfPlayer.isRiichi,
            ReadyQualification = selfPlayer.readyQualification,
            DoraIndicators = new List<int>(recordRiichiDoraIndicators),
            SelfDingqueSuit = selfPlayer.dingqueSuit,
            DetailedConfig = GetDetailedConfigSnapshot(),
            PlayersByPosition = new Dictionary<string, RecordTipsPlayerVisible>(),
        };

        foreach (var kv in recordPlayer_to_info) {
            if (kv.Value == null) continue;
            ctx.PlayersByPosition[kv.Key] = new RecordTipsPlayerVisible {
                DiscardTiles = kv.Value.discardTiles ?? new List<int>(),
                CombinationTiles = kv.Value.combinationTiles ?? new List<string>(),
            };
        }

        return ctx;
    }

    public Dictionary<string, object> GetDetailedConfigSnapshot() {
        if (!gameObject.activeSelf || gameRecord?.gameTitle == null || !gameRecord.gameTitle.TryGetValue("detailed_config", out object raw) || raw == null) return null;
        if (raw is JObject objectValue) return objectValue.ToObject<Dictionary<string, object>>();
        if (raw is IDictionary<string, object> dictionary) return new Dictionary<string, object>(dictionary);
        return null;
    }
}
