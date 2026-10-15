using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public static partial class ScoreHistoryRecordSettlementExtractor {
    // 计分板使用服务器记录的胡牌手牌和付款明细，不从换牌前的起手牌重新算分。
    private static void ExtractExchangeSixRoundRows(Round round, Dictionary<string, object> title, List<RecordScoreRow> output) {
        ChangshaExchangeSixSettlement[] wins = null;
        int[] drawChanges = null;
        foreach (var tick in round.actionTicks) {
            if (tick.Count == 0) continue;
            if (tick[0] == "changsha_exchange_six_win") wins = JsonConvert.DeserializeObject<ChangshaExchangeSixSettlement[]>(tick[1]);
            if (tick[0] == "changsha_exchange_six_draw_scores") drawChanges = JsonConvert.DeserializeObject<int[]>(tick[1]);
            bool hu = tick[0] == "hu_self" || tick[0] == "hu_first" || tick[0] == "hu_second" || tick[0] == "hu_third";
            if (!hu && tick[0] != "liuju") continue;
            int[] changes = hu ? JsonConvert.DeserializeObject<int[]>(tick[4]) : drawChanges;
            var snapshot = new RoundSettlementSnapshot {
                subRule = ChangshaGameState.ExchangeSixSubRule, isLiuju = !hu, hasWin = hu, huClass = tick[0],
            };
            if (hu) {
                var first = wins[0];
                snapshot.hepaiPlayerIndex = first.winner;
                snapshot.winnerUsername = string.Join(" / ", wins.Select(win => ResolveUsernameForSeat(win.winner, round, title)));
                snapshot.huScore = wins.Sum(win => win.payment);
                snapshot.winnerScoreDelta = changes[first.winner];
                snapshot.huFan = JsonConvert.DeserializeObject<string[]>(tick[3]);
                snapshot.hepaiPlayerHand = first.groups[0].hand;
                snapshot.combinationMask = first.combination_masks;
            }
            // hu / draw_scores 都含四喜在内的整局差额，计分板每批次只记一次。
            output.Add(new RecordScoreRow {
                snapshot = snapshot,
                scoreChangesByOriginal = GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(changes, round.seats),
                roundNumber = round.currentRound > 0 ? round.currentRound : round.roundIndex,
            });
        }
    }
}
