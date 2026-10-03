using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>Only projects authoritative Wenzhou state; physical tiles never change to joker substitutions.</summary>
public sealed class WenzhouGameState : TurnBasedGameState {
    public const string RuleId = "wenzhou";
    public const string SubRule = "wenzhou/mil2024";
    public const string RuleVersion = "mil-wenzhou-2024-om1";
    public static WenzhouGameState Active => RuleRegistry.ActiveGameState as WenzhouGameState;
    public WenzhouInfo Info { get; private set; }
    private Dictionary<string, WenzhouWait[]> waits = new Dictionary<string, WenzhouWait[]>();
    private Dictionary<int, int[]> endHands;
    private int roundGeneration;
    private bool settlementAccepted, pendingSettlement;
    private Response pendingReadyStatus, pendingGameEnd;

    private void Accept(GameInfo info) {
        if (info == null) return;
        Info = info.wenzhou_info ?? Info;
        if (info.wenzhou_waits != null) waits = info.wenzhou_waits;
        WenzhouPanel.Show(Info);
    }
    protected override void OnRoundStarted(GameInfo info) {
        ResetSettlement();
        Info = null; waits.Clear(); endHands = null; Accept(info);
        if (Info?.self_has_draw_slot == true && Mirror.SelfHandTiles.Count > 0) {
            var tiles = Mirror.SelfHandTiles.ToArray();
            GameCanvas.Instance.ChangeHandCards("SyncHandCards", 0, tiles.Take(tiles.Length - 1).ToArray(), null);
            GameCanvas.Instance.ChangeHandCards("GetCardNoAnimation", tiles[tiles.Length - 1], null, null);
        }
    }
    protected override void OnAskHandAction(Response response) { Accept(response.game_info); base.OnAskHandAction(response); }
    protected override void OnAskClaim(Response response) {
        Accept(response.game_info);
        Clock.PendingAskFromJiagang = Info?.phase == "waiting_action_qianggang";
        base.OnAskClaim(response);
    }
    protected override void OnDoAction(Response response) {
        Accept(response.game_info);
        Info = response.do_action_info?.wenzhou_info ?? Info;
        if (response.do_action_info?.action_list?.Contains("wenzhou_kong_claim_source") == true) {
            var action = TableAction.From(response.do_action_info, Mirror);
            action.Words = new[] { "cut" }; action.CutClass = action.IsMoGang; action.Silent = true;
            PlayAction(action);
        } else base.OnDoAction(response);
        WenzhouPanel.Show(Info);
    }
    protected override void OnShowResult(Response response) {
        if (response.show_result_info == null || settlementAccepted) return;
        settlementAccepted = true;
        Accept(response.game_info);
        AcceptFinalMelds(response.game_info);
        Info = response.show_result_info?.wenzhou_info ?? Info;
        endHands = response.show_result_info?.wenzhou_end_hands;
        WenzhouPanel.Show(Info);
        var table = Game3DManager.Instance;
        if (table != null && table.HasPendingRecordTableAnimations) {
            pendingSettlement = true;
            Presenter.CloseTableForSettlement();
            table.StartCoroutine(PresentSettlementWhenIdle(response, roundGeneration));
        } else CompleteSettlement(response, roundGeneration);
    }
    private IEnumerator PresentSettlementWhenIdle(Response response, int generation) {
        var table = Game3DManager.Instance;
        while (Active == this && generation == roundGeneration && table != null && table.HasPendingRecordTableAnimations)
            yield return null;
        if (Active == this && generation == roundGeneration) CompleteSettlement(response, generation);
    }
    private void CompleteSettlement(Response response, int generation) {
        if (Active != this || generation != roundGeneration) return;
        pendingSettlement = false;
        var ready = pendingReadyStatus;
        var gameEnd = pendingGameEnd;
        pendingReadyStatus = pendingGameEnd = null;
        // Let the normal path open its lifecycle and mark match-end before
        // replaying packets that arrived while earlier table work was finishing.
        base.OnShowResult(response);
        if (ready != null) base.OnReadyStatus(ready);
        if (gameEnd != null) base.OnGameEnd(gameEnd);
    }
    private void ResetSettlement() {
        roundGeneration++;
        settlementAccepted = pendingSettlement = false;
        pendingReadyStatus = pendingGameEnd = null;
        RoundEndPresentation.Instance?.StopActiveSequence();
    }
    protected override void OnGameEnd(Response response) {
        if (pendingSettlement) {
            // Saving cannot wait for presentation: exit/reconnect may cancel the gate.
            // The store is keyed by game_id, so the later normal path is idempotent.
            if (!Session.IsRealtimeSpectator && !Session.IsDuplicate)
                LocalRecordStore.SavePushedDetail(response.game_end_info?.record_detail);
            pendingGameEnd = response;
        }
        else base.OnGameEnd(response);
    }
    private static void AcceptFinalMelds(GameInfo info) {
        if (info?.players_info == null) return;
        foreach (var server in info.players_info) {
            var player = Mirror.Info(Mirror.SeatOf(server.player_index));
            if (player == null) continue;
            player.combination_tiles = new List<string>(server.combination_tiles ?? Array.Empty<string>());
            player.combination_masks = (server.combination_mask ?? Array.Empty<int[]>())
                .Select(mask => mask == null ? Array.Empty<int>() : (int[])mask.Clone()).ToList();
        }
    }
    private void RevealFinalTable(Dictionary<int, int[]> hands, int generation) {
        Game3DManager.Instance?.StartCoroutine(RevealFinalTableWhenIdle(hands, generation));
    }
    private IEnumerator RevealFinalTableWhenIdle(Dictionary<int, int[]> hands, int generation) {
        var table = Game3DManager.Instance;
        // This property includes live ankan/draw queues, despite its replay-oriented name.
        // An already queued hidden meld must finish before its physical faces are restored.
        while (Active == this && generation == roundGeneration && table != null && table.HasPendingRecordTableAnimations)
            yield return null;
        if (Active != this || generation != roundGeneration || table == null) yield break;
        // An earlier cut/kong must not remove cards from a newly revealed hand.
        RoundEndPresentation.Instance?.HideSelfGameplayControl(false);
        table.RevealSichuanLiujuAllHands(hands);
        foreach (var seat in Mirror.IndexToPosition.Values) {
            var masks = Mirror.Info(seat)?.combination_masks;
            var groups = table.GetPosPanel(seat)?.combination3DObjects;
            if (masks == null || groups == null) continue;
            for (int meld = 0; meld < masks.Count && meld < groups.Length; meld++) {
                int[] mask = masks[meld];
                var group = groups[meld];
                if (mask.Length != 8 || mask.Where((_, i) => i % 2 == 0).Any(flag => flag != 2)) continue;
                if (group == null || group.childCount != 4) {
                    UnityEngine.Debug.LogError($"Wenzhou final concealed meld is missing: seat={seat}, meld={meld}");
                    continue;
                }
                for (int tileIndex = 0; tileIndex < 4; tileIndex++) {
                    int physical = mask[(3 - tileIndex) * 2 + 1];
                    var old = group.GetChild(tileIndex);
                    var oldTile = old.GetComponent<Tile3D>();
                    if (physical < 10 || oldTile == null || oldTile.GetTileId() == physical) continue;
                    // Replace only this face at its existing pose. Rebuilding every meld
                    // would disturb the separate horizontal tile of an added kong.
                    var replacement = MahjongObjectPool.Instance.Spawn(physical, old.position, old.rotation);
                    if (replacement == null) continue;
                    replacement.transform.SetParent(group, worldPositionStays: true);
                    replacement.transform.SetSiblingIndex(tileIndex);
                    replacement.GetComponent<Tile3D>()?.ApplyCombinationPeekState(physical, 2);
                    Card3DHoverManager.Instance?.RegisterCard(replacement, physical);
                    MahjongObjectPool.Instance.Return(-1, old.gameObject);
                }
            }
        }
    }
    protected override SettlementEnvelope BuildEnvelope(ShowResultInfo info) {
        var envelope = base.BuildEnvelope(info);
        if (!envelope.IsLiuju) {
            var remaining = endHands?.Where(p => p.Key != envelope.WinnerIndex).ToDictionary(p => p.Key, p => p.Value);
            int generation = roundGeneration;
            envelope.AfterHandReveal = () => {
                if (Active != this || generation != roundGeneration) return;
                RevealFinalTable(remaining, generation);
            };
        }
        return envelope;
    }
    protected override void OnTableClosedForSettlement(SettlementEnvelope envelope) {
        if (envelope.IsLiuju) {
            RevealFinalTable(endHands, roundGeneration);
        }
    }
    protected override void OnSettlementPresented(SettlementEnvelope envelope, ShowResultInfo info) {
        if (envelope.IsLiuju) Presenter.ApplyScores(envelope.ScoresAfter);
    }
    protected override void OnReadyStatus(Response response) {
        if (response.ready_status_info == null) return;
        Info = response.ready_status_info.wenzhou_info ?? Info;
        WenzhouPanel.Show(Info);
        // Every Wenzhou ready packet contains all four seats, so the newest
        // snapshot subsumes earlier packets without starting the UI lifecycle twice.
        if (pendingSettlement) { pendingReadyStatus = response; return; }
        base.OnReadyStatus(response);
    }
    public static List<(int targetTile, List<int> displayTiles)> CollectJiagangOptions(List<int> hand, List<string> combinations) {
        var result = new List<(int, List<int>)>();
        var info = Active?.Info;
        var self = NormalGameStateManager.Instance.player_to_info["self"];
        foreach (int tile in hand.Distinct()) {
            if (info == null || tile == info.caishen || !TurnClock.Current.IsKongCandidate("jiagang", tile)) continue;
            int logical = tile == 46 && info.caishen != 46 ? info.white_natural : tile;
            int index = combinations.IndexOf($"k{logical}");
            if (index < 0) continue;
            var display = self.combination_masks != null && index < self.combination_masks.Count
                ? self.combination_masks[index].Where((value, i) => i % 2 == 1).ToList() : new List<int> { tile, tile, tile };
            display.Add(tile); result.Add((tile, display));
        }
        return result;
    }
    public static string HandKey(IEnumerable<int> hand) => string.Join(",", hand.OrderBy(t => t));
    private static WenzhouWait[] ReadWaits(IEnumerable<int> hand, int? player = null) {
        var record = GameRecordManager.Instance;
        var snapshot = record != null && record.gameObject.activeSelf ? record.WenzhouReplayWaitsFor(player) : Active?.waits;
        return LookupWaits(snapshot, hand);
    }
    internal static WenzhouWait[] LookupWaits(Dictionary<string, WenzhouWait[]> snapshot, IEnumerable<int> hand) {
        string key = HandKey(hand);
        return snapshot != null && snapshot.TryGetValue(key, out var value) ? value : Array.Empty<WenzhouWait>();
    }
    public static HashSet<int> Waiting(TingpaiQuery query) => new HashSet<int>(ReadWaits(query.Hand, query.PlayerIndex).Select(w => w.tile));
    public static WaitTileHint Describe(WaitHintQuery query) {
        var hand = new List<int>(query.HandWithWin); hand.Remove(query.HepaiTile);
        var hint = ReadWaits(hand, query.Record?.SelfPlayerIndex).FirstOrDefault(w => w.tile == query.HepaiTile);
        if (hint?.ron == true) return WaitTileHint.Ron($"{hint.ron_multiplier}倍");
        if (hint?.self_draw == true) return WaitTileHint.TsumoOnly($"自摸{hint.self_draw_multiplier}倍");
        return WaitTileHint.None("过水或未成和");
    }
    public override void OnSessionReset() { ResetSettlement(); Info = null; waits.Clear(); endHands = null; WenzhouPanel.Hide(); }
}
