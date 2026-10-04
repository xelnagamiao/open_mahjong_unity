using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public partial class GameRecordManager {
    private GuizhouInfo recordGuizhouInfo;
    private string recordGuizhouWinSource;
    private readonly GuizhouReplayKongVisibility recordGuizhouKongs = new GuizhouReplayKongVisibility();
    private bool IsGuizhouRecord() => RecordRuleManifest?.RuleId == GuizhouGameState.RuleId;
    private bool IsGuizhouRecordRobWin => IsGuizhouRecord() && recordGuizhouWinSource == "rob_kong";

    public static int[] GuizhouReplayKongMask(IReadOnlyList<int> tiles) {
        bool revealed = Instance?.recordGuizhouKongs.Hidden != true;
        return new[] { 2, tiles[0], revealed ? 0 : 2, tiles[1], revealed ? 0 : 2, tiles[2], 2, tiles[3] };
    }
    private void ResetGuizhouRecordState() {
        recordGuizhouInfo = null; recordGuizhouWinSource = null;
        recordGuizhouKongs.Reset(null);
        if (IsGuizhouRecord() && gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round round)) {
            recordGuizhouInfo = JsonConvert.DeserializeObject<GuizhouInfo>(JsonConvert.SerializeObject(round.guizhou));
            recordGuizhouKongs.Reset(new IReadOnlyList<int>[] { round.p0Tiles, round.p1Tiles, round.p2Tiles, round.p3Tiles });
        }
        GuizhouLedgerPanel.Show(recordGuizhouInfo);
    }
    private void PlayGuizhouRecordAnnouncement(IReadOnlyList<string> tick) {
        if (!IsGuizhouRecord() || tick.Count < 4 || tick[0] != "guizhou" || tick[1] != "ready") return;
        if (int.TryParse(tick[2], out int seat) && indexToPosition.TryGetValue(seat, out string position)
            && recordPlayer_to_info.TryGetValue(position, out RecordPlayer player))
            SoundManager.Instance?.PlayActionSound(position, "riichi", player.voice_used);
    }
    private bool ApplyGuizhouRecordAction(IReadOnlyList<string> tick) {
        if (!IsGuizhouRecord() || tick == null || tick.Count == 0) return false;
        if (tick[0] == "c") recordGuizhouKongs.Discard(currentPlayerIndex);
        else if ((tick[0] == "p" || tick[0] == "g") && tick.Count >= 3)
            recordGuizhouKongs.Claim(int.Parse(tick[2]));
        else if (tick[0] == "ag" && tick.Count >= 2)
            recordGuizhouKongs.ConcealedKong(currentPlayerIndex, int.Parse(tick[1]));
        if (tick.Count < 3 || tick[0] != "guizhou") return false;
        switch (tick[1]) {
            case "ready": {
                var player = recordPlayerList.First(p => p.playerIndex == int.Parse(tick[2]));
                player.readyQualification = tick[3];
                if (!player.tagList.Contains("declared_ready")) player.tagList.Add("declared_ready");
                break;
            }
            case "reveal_kongs": {
                var masks = JsonConvert.DeserializeObject<int[][][]>(tick[2]);
                foreach (var player in recordPlayerList) player.combinationMasks = new List<int[]>(masks[player.playerIndex]);
                if (recordGuizhouInfo != null) recordGuizhouInfo.opening_revealed = true;
                break;
            }
            case "ledger": recordGuizhouInfo = JsonConvert.DeserializeObject<GuizhouInfo>(tick[2]); break;
            case "draw_score": {
                var changes = JsonConvert.DeserializeObject<int[]>(tick[2]);
                var deltas = new Dictionary<int, int>();
                MapTickScoreChangesToDeltas(changes, deltas);
                ApplyScoreDeltas(deltas, out _, out _);
                recordGuizhouInfo = JsonConvert.DeserializeObject<GuizhouInfo>(tick[3]);
                break;
            }
            case "win_source": {
                int actor = int.Parse(tick[2]);
                int payer = int.TryParse(tick[3], out int parsed) ? parsed : -1;
                recordGuizhouWinSource = tick[4];
                int tile = int.Parse(tick[5]);
                recordPlayerList.First(p => p.playerIndex == actor).isHu = true;
                lastWinnableTileId = tile;
                lastJiagangPlayerIndex = recordGuizhouWinSource == "rob_kong" ? payer : -1;
                if (payer >= 0) lastDiscardPlayerIndex = payer;
                if (payer >= 0 && bool.Parse(tick[6])) {
                    var source = recordPlayerList.First(p => p.playerIndex == payer);
                    if (recordGuizhouWinSource == "rob_kong") {
                        source.tileList.Remove(tile); source.showHandDrawSlotActive = false;
                    } else if (recordGuizhouWinSource == "discard" && source.discardTiles.Count > 0) {
                        int last = source.discardTiles.Count - 1;
                        source.discardTiles.RemoveAt(last);
                        if (source.discardIsMoqie.Count > last) source.discardIsMoqie.RemoveAt(last);
                        if (source.discardRiichiFlags.Count > last) source.discardRiichiFlags.RemoveAt(last);
                    }
                }
                recordGuizhouInfo = JsonConvert.DeserializeObject<GuizhouInfo>(tick[7]);
                break;
            }
            case "state": {
                recordGuizhouInfo = JsonConvert.DeserializeObject<GuizhouInfo>(tick[2]);
                int[] scores = JsonConvert.DeserializeObject<int[]>(tick[3]);
                foreach (var player in recordPlayerList) {
                    player.score = scores[player.playerIndex]; userIdToScore[player.userId] = player.score;
                }
                break;
            }
        }
        GuizhouLedgerPanel.Show(recordGuizhouInfo);
        return true;
    }
    private void RefreshGuizhouRecordMelds() {
        if (Game3DManager.Instance == null || GameCanvas.Instance == null) return;
        Game3DManager.Instance.StopAllRunningAnimations();
        Game3DManager.Instance.Clear3DTile();
        GameCanvas.Instance.ChangeHandCards("InitHandCardsFromRecord", 0, recordPlayer_to_info["self"].tileList.ToArray(), null);
        Game3DManager.Instance.Change3DTile("InitHandCardsFromRecord", 0, 0, null, false, null);
        RebuildRecord3DTableWithoutAnimation();
    }
}

// Tracks first discards rather than current river sizes: a claim can remove a
// first discard, but never cancels that player's completion of the opening turn.
internal sealed class GuizhouReplayKongVisibility {
    private readonly bool[] discarded = new bool[4];
    private readonly HashSet<int>[] initialQuads = new HashSet<int>[4];
    public bool Hidden { get; private set; }

    public void Reset(IReadOnlyList<int>[] hands) {
        Hidden = false;
        for (int i = 0; i < 4; i++) {
            discarded[i] = false;
            initialQuads[i] = new HashSet<int>((hands?[i] ?? Array.Empty<int>())
                .GroupBy(t => t).Where(g => g.Count() == 4).Select(g => g.Key));
        }
    }
    public void Discard(int seat) { discarded[seat] = true; initialQuads[seat].Clear(); }
    public void Claim(int seat) { initialQuads[seat].Clear(); }
    public void ConcealedKong(int seat, int tile) {
        Hidden = !discarded[seat] && initialQuads[seat].Contains(tile)
            && Enumerable.Range(0, 4).Any(i => i != seat && !discarded[i]);
        initialQuads[seat].Remove(tile);
    }
}
