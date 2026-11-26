using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>四川（血战/血流/双打）及共用血战终局协议的牌谱计分板编解码器。</summary>
public class SichuanRecordSettlementCodec : RuleRecordSettlementCodec {
    private readonly bool isSichuanFamily;
    private int huCount;
    private bool hadChajiao;
    private int[] accumBySeat;

    public SichuanRecordSettlementCodec(bool isSichuanFamily = true) {
        this.isSichuanFamily = isSichuanFamily;
    }

    public static void RegisterRecordCodecs() {
        RuleRecordCodecRegistry.RegisterRoundProperty("dingque_suits", (round, token) =>
            round.dingqueSuits = (token as JObject)?.ToObject<Dictionary<int, int>>());
        RuleRecordCodecRegistry.RegisterRoundProperty("sbr_passed_fans", (round, token) =>
            round.sbrPassedFans = (token as JObject)?.ToObject<Dictionary<int, SortedDictionary<int, int>>>());
        Func<Round, List<string>, bool> bloodHandler = (round, tick) => {
            if (tick.Count < 2) return false;
            string step = tick[1];
            int[] sc = null;
            if (step == "settle_hu") sc = RuleRecordCodecRegistry.ParseScoreChangesArray(tick, 6);
            else if (step == "chajiao") sc = RuleRecordCodecRegistry.ParseScoreChangesArray(tick, 5);
            else if (step == "cha_refund" || step == "settle_gang") sc = RuleRecordCodecRegistry.ParseScoreChangesArray(tick, 2);
            else return false;
            RuleRecordCodecRegistry.AddSeatScoreChanges(round, sc);
            return true;
        };
        RuleRecordCodecRegistry.RegisterTickScoreHandler("liuju", bloodHandler);
        RuleRecordCodecRegistry.RegisterTickScoreHandler("blood", bloodHandler);
    }

    public static bool TryGetBoardSummary(Round round, out SichuanBoardSettlementInfo board) {
        board = null;
        if (round?.actionTicks == null) return false;
        for (int i = round.actionTicks.Count - 1; i >= 0; i--) {
            var tick = round.actionTicks[i];
            if (tick == null || tick.Count < 3 || tick[0] != "liuju" || tick[1] != "board_summary") continue;
            try {
                board = JsonConvert.DeserializeObject<SichuanBoardSettlementInfo>(tick[2]);
                if (board != null) return true;
            } catch (JsonException) { }
        }
        return false;
    }

    private bool IsBloodBattle(RecordSettlementSimContext sim) {
        if (sim.SubRule == GuobiaoGameState.BloodBattleSubRule || sim.SubRule == GuangdongMilRules.Doubles) return true;
        if (!isSichuanFamily) return false;
        if (sim.GameTitle != null && sim.GameTitle.TryGetValue("blood_battle", out object val) && val != null) {
            return val.ToString() != "false";
        }
        return true;
    }

    public override bool TryExtractWholeRound(
        Round round,
        string subRule,
        Dictionary<string, object> gameTitle,
        List<ScoreHistoryRecordSettlementExtractor.RecordScoreRow> output) {
        if (!isSichuanFamily) return false;
        bool hasSummary = subRule.StartsWith("sichuan/", StringComparison.Ordinal) && TryGetBoardSummary(round, out _);
        if (!SichuanLobby.IsXueliu(subRule) && !SichuanLobby.IsDoubles(subRule) && !hasSummary) return false;

        int roundNumber = round.currentRound > 0 ? round.currentRound : round.roundIndex;
        var totals = new Round { seats = round.seats };
        string label = null;
        var winners = new HashSet<int>();
        foreach (List<string> tick in round.actionTicks) {
            GameRecordJsonDecoder.AccumulateScoreChangesFromTick(totals, tick);
            if (tick != null && tick.Count >= 2 && tick[0] == "liuju" && tick[1] == "chajiao")
                label = SichuanRoundLabel.Chajiao;
            if (tick != null && tick.Count >= 3 && (tick[0] == "liuju" || tick[0] == "blood")
                && tick[1] == "settle_hu" && int.TryParse(tick[2], out int winner) && winner >= 0 && winner < 4)
                winners.Add(winner);
        }
        if (!SichuanLobby.IsXueliu(subRule)) {
            TryGetBoardSummary(round, out var board);
            label = ScoreHistorySettlementHelper.ResolveSichuanBoardRoundLabel(subRule, board, winners.Count, label != null);
        }
        output.Add(new ScoreHistoryRecordSettlementExtractor.RecordScoreRow {
            snapshot = ScoreHistorySettlementHelper.CreateSichuanScoreboardSnapshot(subRule, label),
            scoreChangesByOriginal = totals.scoreChanges?.ToArray() ?? new int[4],
            roundNumber = roundNumber,
        });
        return true;
    }

    public override void OnBeforeStandardTick(RecordSettlementSimContext sim, List<string> tick) {
        if (!isSichuanFamily) return;
        string action = tick[0];
        if (action == "g" || action == "ag" || action == "jg" || action == "gr") {
            RecordSettlementSimContext.AccumulateSeatScores(sim.ImmediateScores, GameRecordJsonDecoder.ParseInlineGangScoreChanges(tick));
        }
    }

    public override bool TryHandleHuTick(RecordSettlementSimContext sim, List<string> tick, string action, int actingPlayerIndex) {
        if (IsBloodBattle(sim) && IsDeferredHuTick(tick)) return true;
        if (!isSichuanFamily) return false;
        int[] scoreChanges = RecordSettlementSimContext.ParseScoreChanges(tick, 4);
        sim.LastRow = new ScoreHistoryRecordSettlementExtractor.RecordScoreRow {
            snapshot = ScoreHistorySettlementHelper.CreateSichuanScoreboardSnapshot(sim.SubRule, null),
            scoreChangesByOriginal = GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(scoreChanges, sim.Round.seats),
            roundNumber = sim.RoundNumber,
        };
        sim.Output.Add(sim.LastRow);
        return true;
    }

    public override bool TryHandleLiujuTick(RecordSettlementSimContext sim, List<string> tick, string action) {
        bool blood = IsBloodBattle(sim);
        if ((action == "liuju" || action == "blood") && tick.Count >= 2 && (isSichuanFamily || blood)) {
            string step = tick[1];
            if (step == "reveal_hu") {
                huCount = 0;
                hadChajiao = false;
                accumBySeat = new int[4];
                return true;
            }
            if (step == "settle_hu") {
                accumBySeat ??= new int[4];
                huCount++;
                RecordSettlementSimContext.AccumulateSeatScores(accumBySeat, RecordSettlementSimContext.ParseScoreChanges(tick, 6));
                if (tick.Count > 7 && RecordSettlementSimContext.ParseInt(tick, 7) != 0 && !hadChajiao) {
                    sim.LastRow = FlushEndgameRow(sim);
                }
                return true;
            }
            if (step == "chajiao") {
                accumBySeat ??= new int[4];
                hadChajiao = true;
                RecordSettlementSimContext.AccumulateSeatScores(accumBySeat, RecordSettlementSimContext.ParseScoreChanges(tick, 5));
                if (tick.Count > 6 && RecordSettlementSimContext.ParseInt(tick, 6) != 0) {
                    sim.LastRow = FlushEndgameRow(sim);
                }
                return true;
            }
            if (step == "settle_gang") {
                accumBySeat ??= new int[4];
                RecordSettlementSimContext.AccumulateSeatScores(accumBySeat, RecordSettlementSimContext.ParseScoreChanges(tick, 2));
                return true;
            }
            if (step == "cha_refund") {
                RecordSettlementSimContext.AccumulateSeatScores(sim.ImmediateScores, RecordSettlementSimContext.ParseScoreChanges(tick, 2));
                return true;
            }
            if (step == "final") return true;
        }
        if (isSichuanFamily) {
            sim.LastRow = new ScoreHistoryRecordSettlementExtractor.RecordScoreRow {
                snapshot = ScoreHistorySettlementHelper.CreateSichuanScoreboardSnapshot(sim.SubRule, SichuanRoundLabel.Liuju),
                scoreChangesByOriginal = new int[4],
                roundNumber = sim.RoundNumber,
            };
            sim.Output.Add(sim.LastRow);
            return true;
        }
        return false;
    }

    private ScoreHistoryRecordSettlementExtractor.RecordScoreRow FlushEndgameRow(RecordSettlementSimContext sim) {
        string label = ScoreHistorySettlementHelper.ResolveSichuanEndgameRoundLabel(huCount, hadChajiao, sim.SubRule);
        var row = new ScoreHistoryRecordSettlementExtractor.RecordScoreRow {
            snapshot = ScoreHistorySettlementHelper.CreateSichuanScoreboardSnapshot(sim.SubRule, label),
            scoreChangesByOriginal = GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(
                accumBySeat ?? new int[4], sim.Round.seats),
            roundNumber = sim.RoundNumber,
        };
        sim.Output.Add(row);
        return row;
    }

    private static bool IsDeferredHuTick(List<string> tick) {
        int[] scoreChanges = RecordSettlementSimContext.ParseScoreChanges(tick, 4);
        if (scoreChanges == null) return true;
        foreach (int change in scoreChanges) {
            if (change != 0) return false;
        }
        return true;
    }
}
