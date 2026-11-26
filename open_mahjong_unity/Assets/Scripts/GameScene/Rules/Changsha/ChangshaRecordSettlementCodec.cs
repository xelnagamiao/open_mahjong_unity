using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>长沙麻将（含换六张、开杠明牌自摸手牌）牌谱计分板编解码器。</summary>
public sealed class ChangshaRecordSettlementCodec : RuleRecordSettlementCodec {
    public static void RegisterRecordCodecs() {
        RuleRecordCodecRegistry.RegisterTickScoreHandler("changsha_exchange_six_draw_scores",
            GameRecordManager.AccumulateExchangeSixDrawScore);
    }

    public override bool TryExtractWholeRound(
        Round round,
        string subRule,
        Dictionary<string, object> gameTitle,
        List<ScoreHistoryRecordSettlementExtractor.RecordScoreRow> output) {
        if (subRule != ChangshaGameState.ExchangeSixSubRule) return false;
        ScoreHistoryRecordSettlementExtractor.ExtractExchangeSixRoundRows(round, gameTitle, output);
        return true;
    }

    public override void OnAfterHuSnapshotBuilt(RecordSettlementSimContext sim, List<string> tick, RoundSettlementSnapshot snapshot) {
        int changshaHandIndex = ChangshaRecordCodec.OpenKongSelfDrawHandIndex(tick);
        if (changshaHandIndex >= 0) {
            snapshot.hepaiPlayerHand = JArray.Parse(tick[changshaHandIndex]).ToObject<int[]>();
        }
    }
}
