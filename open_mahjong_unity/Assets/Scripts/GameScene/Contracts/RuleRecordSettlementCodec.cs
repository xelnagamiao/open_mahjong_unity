using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>
/// 牌谱计分板模拟器的单家手牌/副露状态。
/// </summary>
public sealed class RecordSimPlayer {
    public List<int> tileList = new List<int>();
    public List<string> combinationTiles = new List<string>();
    public List<int[]> combinationMasks = new List<int[]>();
}

/// <summary>
/// 牌谱计分板提取器在单局 tick 模拟期间的共享上下文，供各规则的 RuleRecordSettlementCodec 读写。
/// </summary>
public sealed class RecordSettlementSimContext {
    public Round Round;
    public string Rule;
    public string SubRule;
    public RuleManifest Manifest;
    public Dictionary<string, object> GameTitle;
    public int PlayerCount;
    public int RoundNumber;
    public int FirstRoundRow;
    public RecordSimPlayer[] Players;
    public List<ScoreHistoryRecordSettlementExtractor.RecordScoreRow> Output;
    public ScoreHistoryRecordSettlementExtractor.RecordScoreRow LastRow;
    public int LastDiscardPlayerIndex = -1;
    public int LastDiscardTileId = -1;
    public int LastWinnableTileId = -1;
    public int CurrentPlayerIndex;
    public int[] ImmediateScores = new int[4];

    public static void AccumulateSeatScores(int[] accumBySeat, int[] delta) {
        if (accumBySeat == null || delta == null) return;
        for (int i = 0; i < accumBySeat.Length && i < delta.Length; i++) {
            accumBySeat[i] += delta[i];
        }
    }

    public static void RemoveOneTile(List<int> tileList, int tileId) {
        if (tileList == null) return;
        int idx = tileList.IndexOf(tileId);
        if (idx >= 0) tileList.RemoveAt(idx);
    }

    public static int ParseInt(List<string> tick, int index) {
        if (tick == null || index >= tick.Count || string.IsNullOrEmpty(tick[index])) return 0;
        int.TryParse(tick[index], out int val);
        return val;
    }

    public static int[] ParseScoreChanges(List<string> tick, int index) =>
        RuleRecordCodecRegistry.ParseScoreChangesArray(tick, index);

    public static string[] ParseFanList(List<string> tick, int index) =>
        ScoreHistoryRecordSettlementExtractor.ParseFanList(tick, index);

    public static int[] BuildScoreboardHuHand(IReadOnlyList<int> closedHand, string huClass, int hepaiTile, int lastWinnableTileId) =>
        RecordHuHandBuilder.BuildDisplayHand(closedHand, huClass, hepaiTile, lastWinnableTileId, hasRonWinningTile: false);

    public static string ResolveUsernameForSeat(int seatIndex, Round round, Dictionary<string, object> gameTitle) =>
        ScoreHistoryRecordSettlementExtractor.ResolveUsernameForSeat(seatIndex, round, gameTitle);

    public static int[][] CloneMasks(List<int[]> masks) =>
        ScoreHistoryRecordSettlementExtractor.CloneMasks(masks);

    public static void ApplyStartScoreDiffToLastRow(RecordSettlementSimContext sim, List<string> tick, int[] startScores) {
        if (sim.LastRow == null || startScores == null || startScores.Length != 4 || tick.Count <= 3) return;
        int[] scores = JArray.Parse(tick[3]).ToObject<int[]>();
        var changes = new int[4];
        for (int i = 0; i < 4; i++) changes[i] = scores[i] - startScores[i];
        sim.LastRow.scoreChangesByOriginal = GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(changes, sim.Round.seats);
    }
}

/// <summary>
/// 规则专属牌谱计分板提取编解码器。核心 ScoreHistoryRecordSettlementExtractor 只运行通用骨架，
/// 所有规则差异均通过本基类的虚方法分发。
/// </summary>
public abstract class RuleRecordSettlementCodec {
    /// <summary>整局接管提取（如长沙换六张、四川血流/血战双打）；返回 true 表示本局已完全产出。</summary>
    public virtual bool TryExtractWholeRound(
        Round round,
        string subRule,
        Dictionary<string, object> gameTitle,
        List<ScoreHistoryRecordSettlementExtractor.RecordScoreRow> output) => false;

    /// <summary>在每条标准 tick 执行前的轻量钩子（如四川杠牌内联分收集）。</summary>
    public virtual void OnBeforeStandardTick(RecordSettlementSimContext sim, List<string> tick) { }

    /// <summary>拦截并处理规则专属 tick；返回 true 表示该 tick 已消费，跳过通用 switch。</summary>
    public virtual bool TryHandleCustomTick(RecordSettlementSimContext sim, List<string> tick) => false;

    /// <summary>拦截加杠 tick（如温州白板代财神、虹雀加杠扩展）；返回 true 表示已处理。</summary>
    public virtual bool TryHandleAddedKong(RecordSettlementSimContext sim, List<string> tick, int actingPlayerIndex) => false;

    /// <summary>拦截 hu_* tick（如四川血战延迟和牌与简化计分板行）；返回 true 表示已处理。</summary>
    public virtual bool TryHandleHuTick(RecordSettlementSimContext sim, List<string> tick, string action, int actingPlayerIndex) => false;

    /// <summary>拦截 liuju / blood / jiuzhongjiupai tick；返回 true 表示已处理。</summary>
    public virtual bool TryHandleLiujuTick(RecordSettlementSimContext sim, List<string> tick, string action) => false;

    /// <summary>通用 BuildHuSnapshot 构造完成后的规则微调（如长沙开杠自摸手牌替换）。</summary>
    public virtual void OnAfterHuSnapshotBuilt(RecordSettlementSimContext sim, List<string> tick, RoundSettlementSnapshot snapshot) { }

    /// <summary>写入计分板行前的分变调整（如日麻合并已支付立直供托）。</summary>
    public virtual int[] AdjustRowScoreChanges(RecordSettlementSimContext sim, string action, int[] seatChanges) => seatChanges;

    /// <summary>单局所有 tick 模拟完且 immediateScores 合并到 LastRow 之后的收尾钩子。</summary>
    public virtual void OnRoundFinished(RecordSettlementSimContext sim) { }

    /// <summary>整场牌谱所有局提取完成后的收尾钩子（如日麻终局供托补差）。</summary>
    public virtual void OnRecordFinished(
        Dictionary<string, object> gameTitle,
        string rule,
        List<ScoreHistoryRecordSettlementExtractor.RecordScoreRow> rows) { }
}
