using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public partial class GameRecordManager {
    private readonly HashSet<int> exchangeSixRecordReady = new HashSet<int>();
    private readonly HashSet<int> exchangeSixRecordOpen = new HashSet<int>();
    private readonly HashSet<int> exchangeSixRecordDisqualified = new HashSet<int>();
    private readonly Dictionary<int, Dictionary<int, string[]>> exchangeSixRecordLocked = new Dictionary<int, Dictionary<int, string[]>>();
    private readonly Dictionary<int, int> exchangeSixRecordHidden = new Dictionary<int, int>();
    private readonly Dictionary<int, int> exchangeSixRecordInstant = new Dictionary<int, int>();
    private ChangshaExchangeSixSettlement[] exchangeSixRecordWins;
    private int[] exchangeSixRecordPublicTiles = Array.Empty<int>();
    private int[] exchangeSixRecordDice = Array.Empty<int>();
    private int[] exchangeSixRecordDealDice = Array.Empty<int>();
    private int exchangeSixRecordRound, exchangeSixRecordCompletedRounds, exchangeSixRecordSourceOffset;
    private int[] exchangeSixRecordExchangeDice = Array.Empty<int>();
    private bool exchangeSixRecordExchanging;
    private bool exchangeSixRecordRolling;
    private int? exchangeSixRecordSmallHuForbiddenSeat;
    private readonly Dictionary<int, List<int>> exchangeSixRecordFourJoy = new Dictionary<int, List<int>>();

    private bool IsExchangeSixRecord() => ReadGameTitleString(gameRecord?.gameTitle, "sub_rule", "") == ChangshaGameState.ExchangeSixSubRule;

    private void ResetExchangeSixRecord() {
        exchangeSixRecordReady.Clear(); exchangeSixRecordOpen.Clear(); exchangeSixRecordDisqualified.Clear();
        exchangeSixRecordLocked.Clear(); exchangeSixRecordHidden.Clear(); exchangeSixRecordInstant.Clear();
        exchangeSixRecordWins = null;
        exchangeSixRecordPublicTiles = exchangeSixRecordDice = Array.Empty<int>();
        exchangeSixRecordFourJoy.Clear();
        exchangeSixRecordDealDice = Array.Empty<int>();
        exchangeSixRecordExchangeDice = Array.Empty<int>();
        exchangeSixRecordRound = exchangeSixRecordCompletedRounds = exchangeSixRecordSourceOffset = 0;
        exchangeSixRecordExchanging = false;
        exchangeSixRecordRolling = false;
        exchangeSixRecordSmallHuForbiddenSeat = null;
    }

    internal ChangshaExchangeSixWait[] ExchangeSixRecordWaits(int seat, List<int> hand, List<string> melds) {
        if (!IsExchangeSixRecord() || exchangeSixRecordDisqualified.Contains(seat) || hand == null
            || hand.Count != 13 - 3 * (melds?.Count ?? 0)) return Array.Empty<ChangshaExchangeSixWait>();
        var waits = new List<ChangshaExchangeSixWait>();
        foreach (int tile in ChangshaExchangeSixShape.Tiles) {
            var candidate = new List<int>(hand) {tile};
            var fans = ChangshaExchangeSixShape.Fans(candidate, melds, exchangeSixRecordReady.Contains(seat));
            if (fans.Count == 0) continue;
            if (exchangeSixRecordLocked.TryGetValue(seat, out var locked)) {
                if (!locked.TryGetValue(tile, out string[] frozen)) continue;
                fans = new List<string>(frozen);
            }
            if (exchangeSixRecordReady.Contains(seat)) fans.Add("报听");
            if (exchangeSixRecordOpen.Contains(seat)) fans.Add("明牌");
            if (fans.Count > 1) fans.Remove("小胡");
            int score = ChangshaExchangeSixShape.Score(fans);
            waits.Add(new ChangshaExchangeSixWait {tile = tile, fans = fans.ToArray(), score = score,
                blocked_small_hu = seat == exchangeSixRecordSmallHuForbiddenSeat && score == 2});
        }
        return waits.ToArray();
    }

    // 四喜已即时入账；终局普通 hu tick 含整局差额，播放时只补剩余差额。
    private List<string> NormalizeExchangeSixTerminalTick(List<string> tick) {
        if (!IsExchangeSixRecord() || tick == null || tick.Count < 5 || !tick[0].StartsWith("hu_")) return tick;
        int[] changes = ParseTickScoreChanges(tick, 4);
        if (changes == null) return tick;
        var copy = new List<string>(tick);
        foreach (var item in exchangeSixRecordInstant) changes[item.Key] -= item.Value;
        copy[4] = JsonConvert.SerializeObject(changes);
        return copy;
    }

    private void ConsumeExchangeSixWall(int position) {
        int original = currentOriginalIndices[position];
        currentTilesList.RemoveAt(position); currentOriginalIndices.RemoveAt(position);
        // 骰子可从墙中抽墩，必须按原位置标灰，不能当作连续从头摸走。
        consumedBackIndices.Add(original);
    }

    private bool ApplyExchangeSixRecordTick(List<string> tick) {
        if (!IsExchangeSixRecord() || tick == null || tick.Count == 0) return false;
        if (tick[0] == "d" || tick[0] == "gd") exchangeSixRecordFourJoy.Remove(currentPlayerIndex);
        if (!tick[0].StartsWith("changsha_")) return false;
        string action = tick[0];
        if (action == "changsha_deal_dice") {
            exchangeSixRecordDealDice = JsonConvert.DeserializeObject<int[]>(tick[1]);
            exchangeSixRecordSmallHuForbiddenSeat = ParseTickInt(tick, 2);
        } else if (action == "changsha_exchange_start") {
            exchangeSixRecordRound = ParseTickInt(tick, 1);
            exchangeSixRecordExchangeDice = JsonConvert.DeserializeObject<int[]>(tick[2]);
            exchangeSixRecordSourceOffset = ParseTickInt(tick, 3);
            exchangeSixRecordExchanging = true;
            exchangeSixRecordRolling = false;
        } else if (action == "changsha_exchange_six") {
            exchangeSixRecordRound = tick.Count > 5 ? ParseTickInt(tick, 5) : exchangeSixRecordCompletedRounds + 1;
            exchangeSixRecordCompletedRounds = exchangeSixRecordRound;
            exchangeSixRecordExchangeDice = JsonConvert.DeserializeObject<int[]>(tick[1]);
            exchangeSixRecordExchanging = false;
            exchangeSixRecordRolling = false;
            int offset = ParseTickInt(tick, 2);
            exchangeSixRecordSourceOffset = offset;
            int[][] outgoing = JsonConvert.DeserializeObject<int[][]>(tick[3]);
            var hands = new List<int>[4];
            for (int seat = 0; seat < 4; seat++) {
                hands[seat] = new List<int>(recordPlayer_to_info[indexToPosition[seat]].tileList);
                foreach (int tile in outgoing[seat]) hands[seat].Remove(tile);
                hands[seat].AddRange(outgoing[(seat + offset) % 4]);
            }
            for (int seat = 0; seat < 4; seat++) recordPlayer_to_info[indexToPosition[seat]].tileList = hands[seat];
            exchangeSixRecordDisqualified.UnionWith(JsonConvert.DeserializeObject<int[]>(tick[4]));
        } else if (action == "changsha_exchange_roll") {
            exchangeSixRecordRolling = true;
            exchangeSixRecordRound = ParseTickInt(tick, 1);
            exchangeSixRecordExchangeDice = JsonConvert.DeserializeObject<int[]>(tick[2]);
            exchangeSixRecordSourceOffset = ParseTickInt(tick, 3);
        } else if (action == "changsha_four_joy") {
            int seat = ParseTickInt(tick, 1), tile = ParseTickInt(tick, 2);
            if (!exchangeSixRecordFourJoy.ContainsKey(seat)) exchangeSixRecordFourJoy[seat] = new List<int>();
            exchangeSixRecordFourJoy[seat].AddRange(Enumerable.Repeat(tile, 4));
            var changes = JsonConvert.DeserializeObject<Dictionary<int, int>>(tick[4]);
            foreach (var player in recordPlayerList) {
                int delta = changes[player.originalPlayerIndex];
                player.score += delta;
                exchangeSixRecordInstant.TryGetValue(player.playerIndex, out int previous);
                exchangeSixRecordInstant[player.playerIndex] = previous + delta;
            }
        } else if (action == "changsha_declaration") {
            int seat = ParseTickInt(tick, 1);
            if (tick[2] == "changsha_report_ready") exchangeSixRecordReady.Add(seat);
            if (tick[2] == "changsha_open_hand") exchangeSixRecordOpen.Add(seat);
        } else if (action == "changsha_lock_patterns") {
            exchangeSixRecordLocked[ParseTickInt(tick, 1)] = JsonConvert.DeserializeObject<Dictionary<int, string[]>>(tick[2]);
        } else if (action == "changsha_hidden_draw") {
            int seat = ParseTickInt(tick, 1);
            exchangeSixRecordFourJoy.Remove(seat);
            exchangeSixRecordHidden[seat] = ParseTickInt(tick, 3);
            int position = tick[2] == "normal" ? 0 : backwardTilesType == "double" && currentTilesList.Count > 1 ? currentTilesList.Count - 2 : currentTilesList.Count - 1;
            ConsumeExchangeSixWall(position);
            if (tick[2] != "normal") backwardTilesType = backwardTilesType == "double" ? "single" : "double";
            currentPlayerIndex = seat;
        } else if (action == "changsha_hidden_reveal") {
            int seat = ParseTickInt(tick, 1);
            var player = recordPlayer_to_info[indexToPosition[seat]];
            player.tileList.Add(exchangeSixRecordHidden[seat]);
            player.showHandDrawSlotActive = true;
            exchangeSixRecordHidden.Remove(seat);
        } else if (action == "changsha_open_kong") {
            int[] positions = JsonConvert.DeserializeObject<int[]>(tick[3]);
            if (backwardTilesType == "double" && positions.Contains(currentTilesList.Count - 2)) backwardTilesType = "single";
            foreach (int position in positions.OrderByDescending(value => value)) ConsumeExchangeSixWall(position);
            currentPlayerIndex = ParseTickInt(tick, 1);
            exchangeSixRecordDice = JsonConvert.DeserializeObject<int[]>(tick[2]);
            exchangeSixRecordPublicTiles = JsonConvert.DeserializeObject<int[]>(tick[4]);
        } else if (action == "changsha_batch_discard") {
            int seat = ParseTickInt(tick, 1);
            var player = recordPlayer_to_info[indexToPosition[seat]];
            foreach (int tile in JsonConvert.DeserializeObject<int[]>(tick[2])) {
                player.discardTiles.Add(tile); player.discardIsMoqie.Add(true); player.discardRiichiFlags.Add(false);
                lastDiscardTileId = lastWinnableTileId = tile;
            }
            lastDiscardPlayerIndex = seat;
            waitingForDrawAfterCut = true;
            currentPlayerIndex = (seat + 1) % 4;
            exchangeSixRecordPublicTiles = Array.Empty<int>();
        } else if (action == "changsha_kong_continue") {
            int seat = ParseTickInt(tick, 1), tile = ParseTickInt(tick, 3);
            var player = recordPlayer_to_info[indexToPosition[seat]];
            int riverIndex = player.discardTiles.LastIndexOf(tile);
            player.discardTiles.RemoveAt(riverIndex); player.discardIsMoqie.RemoveAt(riverIndex); player.discardRiichiFlags.RemoveAt(riverIndex);
            player.tileList.Add(tile); player.showHandDrawSlotActive = true;
            currentPlayerIndex = seat;
            lastWinnableTileId = -1;
        } else if (action == "changsha_sea_draw") {
            int seat = ParseTickInt(tick, 1);
            exchangeSixRecordFourJoy.Remove(seat);
            var player = recordPlayer_to_info[indexToPosition[seat]];
            player.tileList.Add(ParseTickInt(tick, 2)); player.showHandDrawSlotActive = true;
            ConsumeExchangeSixWall(0); currentPlayerIndex = seat;
        } else if (action == "changsha_sea_release") {
            var player = recordPlayer_to_info[indexToPosition[ParseTickInt(tick, 1)]];
            player.tileList.RemoveAt(player.tileList.Count - 1); player.showHandDrawSlotActive = false;
        } else if (action == "changsha_exchange_six_win") {
            exchangeSixRecordWins = JsonConvert.DeserializeObject<ChangshaExchangeSixSettlement[]>(tick[1]);
            foreach (var winner in exchangeSixRecordWins) {
                var player = recordPlayer_to_info[indexToPosition[winner.winner]];
                player.tileList = new List<int>(winner.groups[0].hand);
                player.showHandDrawSlotActive = false;
                player.isHu = true;
            }
        }
        return true;
    }

    private void RefreshExchangeSixRecordPresentation() {
        if (!IsExchangeSixRecord()) return;
        var open = recordPlayerList.Where(player => exchangeSixRecordOpen.Contains(player.playerIndex))
            .ToDictionary(player => player.playerIndex, player => player.tileList.ToArray());
        if (exchangeSixRecordWins != null) foreach (var winner in exchangeSixRecordWins)
            open[winner.winner] = winner.groups[0].hand;
        var fourJoy = exchangeSixRecordFourJoy.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray());
        var counts = recordPlayerList.ToDictionary(player => player.playerIndex, player => player.tileList.Count);
        Game3DManager.Instance?.SyncChangshaExchangeRecordHands(open, fourJoy, indexToPosition, counts);
        GameCanvas.Instance?.ShowExchangeSixState(new ChangshaExchangeSixState {
            phase = exchangeSixRecordExchanging ? exchangeSixRecordRolling ? "exchange_dice" : "exchange" : exchangeSixRecordPublicTiles.Length > 0 ? "open_kong_public" : "playing",
            public_state = new ChangshaExchangeSixPublic {kong_tiles = exchangeSixRecordPublicTiles, kong_dice = exchangeSixRecordDice,
                exchange_round = exchangeSixRecordRound, completed_exchange_rounds = exchangeSixRecordCompletedRounds,
                dice = exchangeSixRecordExchangeDice, source_offset = exchangeSixRecordSourceOffset,
                deal_dice = exchangeSixRecordDealDice, small_hu_forbidden_seat = exchangeSixRecordSmallHuForbiddenSeat,
                reported_ready_seats = exchangeSixRecordReady.ToArray(), open_hand_seats = exchangeSixRecordOpen.ToArray(),
                disqualified_seats = exchangeSixRecordDisqualified.ToArray()},
            private_state = new ChangshaExchangeSixPrivate {action_list = Array.Empty<string>()},
        }, false);
    }

    private IEnumerator AnnounceExchangeSixRecordWinners(string huClass) {
        // 同批多人共享一次分变；倒牌逐家沿牌谱座位映射，不能借用实战桌面的座位。
        foreach (var winner in exchangeSixRecordWins) {
            var group = winner.groups[0];
            yield return CoAnnounceRecordHuAndReveal(huClass, group.fans,
                indexToPosition[winner.winner], group.hand);
        }
    }

    // 该模式包含不入原手的公开四张批次，顺序播放也沿用跳转推演，防止两套扣牌逻辑漂移。
    private bool AdvanceExchangeSixRecord(List<string> tick) {
        if (!IsExchangeSixRecord() || tick[0].StartsWith("hu_") || tick[0] == "liuju" || tick[0] == "end") return false;
        GotoAction(currentNode + 1);
        return true;
    }

    internal static bool AccumulateExchangeSixDrawScore(Round round, List<string> tick) {
        if (tick == null || tick.Count < 2 || tick[0] != "changsha_exchange_six_draw_scores") return false;
        int[] changes = JsonConvert.DeserializeObject<int[]>(tick[1]);
        round.scoreChanges = new List<int>(GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(changes, round.seats));
        return true;
    }
}
