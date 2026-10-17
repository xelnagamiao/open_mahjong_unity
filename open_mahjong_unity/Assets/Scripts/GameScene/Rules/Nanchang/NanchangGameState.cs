using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>Only projects authoritative Nanchang state; physical tiles never change to joker substitutions.</summary>
public sealed class NanchangGameState : TurnBasedGameState {
    public const string RuleId = "nanchang";
    public const string SubRule = "nanchang/mil2026";
    public const string RuleVersion = "mil-nanchang-2026";
    public static NanchangGameState Active => RuleRegistry.ActiveGameState as NanchangGameState;
    public NanchangInfo Info { get; private set; }
    private Dictionary<string, NanchangWait[]> waits = new Dictionary<string, NanchangWait[]>();
    private Dictionary<int, int[]> endHands;
    private int roundGeneration;
    private bool settlementAccepted, pendingSettlement;
    private Response pendingReadyStatus, pendingGameEnd;
    private readonly NanchangAskClock askClock = new NanchangAskClock();
    private int clockHandNumber;
    private string clockGamestateId;

    private void Accept(GameInfo info) {
        if (info == null) return;
        Info = info.nanchang_info ?? Info;
        if (info.nanchang_hints != null) waits = info.nanchang_hints;
        NanchangTableIndicators.Refresh(Info);
    }
    protected override void OnRoundStarted(GameInfo info) {
        int nextHand = info?.nanchang_info?.hand_number ?? 0;
        if (nextHand != clockHandNumber || Session.GamestateId != clockGamestateId) {
            askClock.Reset(); clockHandNumber = nextHand; clockGamestateId = Session.GamestateId;
        }
        ResetSettlement();
        Info = null; waits.Clear(); endHands = null; Accept(info);
    }
    protected override void OnAskHandAction(Response response) {
        Accept(response.game_info);
        var info = response.ask_hand_action_info;
        if (info == null || askClock.IsClosed(info.action_tick)) return;
        base.OnAskHandAction(response);
        PresentAskClock(response,info.action_tick,info.remaining_time,info.step_remaining ?? Session.RoomStepTime);
    }
    public override void OnPlayerTagsRefreshed() {
        base.OnPlayerTagsRefreshed();
        NanchangTableIndicators.Refresh(Info);
        if (Session.Tips) TipsContainer.Instance?.RefreshTenpaiTipsIfCached();
    }
    protected override void OnAskClaim(Response response) {
        Accept(response.game_info);
        var info = response.ask_other_action_info;
        if (info == null || askClock.IsClosed(info.action_tick)) return;
        Clock.PendingAskFromJiagang = false;
        base.OnAskClaim(response);
        PresentAskClock(response,info.action_tick,info.remaining_time,info.step_remaining ?? Session.RoomStepTime);
    }
    private void PresentAskClock(Response response, int tick, int bank, int step) {
        if (!Clock.IsSelfActionRequired) return;
        if (Info?.clock_active == false || Clock.AllowActionList.Count == 0) {
            Clock.Clear("nanchangAlreadyAnswered");
            return;
        }
        double now = NanchangAskClock.Now;
        askClock.Project(tick,Info?.clock_remaining_ms ?? (bank+(double)step)*1000,
            Info?.clock_step_remaining_ms ?? step*1000d,response.received_monotonic ?? now,now,
            out double bankSeconds,out double stepSeconds);
        GameCanvas.Instance.LoadingRemianTime(bankSeconds,stepSeconds);
    }
    public override void OnAskWindowClosed(AskCloseReason reason) {
        if (reason == AskCloseReason.Acted || reason == AskCloseReason.TimedOut) askClock.Close();
        base.OnAskWindowClosed(reason);
    }
    protected override void OnDoAction(Response response) {
        Accept(response.game_info);
        Info = response.do_action_info?.nanchang_info ?? Info;
        base.OnDoAction(response);
        NanchangTableIndicators.Refresh(Info);
    }
    protected override void OnShowResult(Response response) {
        if (response.show_result_info == null || settlementAccepted) return;
        settlementAccepted = true;
        Accept(response.game_info);
        AcceptFinalMelds(response.game_info);
        Info = response.show_result_info?.nanchang_info ?? Info;
        endHands = response.show_result_info?.nanchang_end_hands;
        NanchangTableIndicators.Refresh(Info);
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
                    UnityEngine.Debug.LogError($"Nanchang final concealed meld is missing: seat={seat}, meld={meld}");
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
        Clock.LastAskActionTick = response.ready_status_info.action_tick;
        Info = response.ready_status_info.nanchang_info ?? Info;
        NanchangTableIndicators.Refresh(Info);
        // Every Nanchang ready packet contains all four seats, so the newest
        // snapshot subsumes earlier packets without starting the UI lifecycle twice.
        if (pendingSettlement) { pendingReadyStatus = response; return; }
        base.OnReadyStatus(response);
    }
    public static string HandKey(IEnumerable<int> hand) => string.Join(",", hand.OrderBy(t => t));
    private static NanchangWait[] ReadWaits(IEnumerable<int> hand, int? player = null, Action ready = null) {
        var record = GameRecordManager.Instance;
        var snapshot = record != null && record.gameObject.activeSelf ? record.NanchangReplayWaitsFor(player) : Active?.waits;
        return LookupWaits(snapshot, hand, ready);
    }
    internal static NanchangWait[] LookupWaits(Dictionary<string, NanchangWait[]> snapshot, IEnumerable<int> hand, Action ready = null) {
        var request=snapshot?.Values.SelectMany(rows=>rows??Array.Empty<NanchangWait>()).FirstOrDefault(row=>row?.local_request!=null)?.local_request;
        if(request!=null)return LocalHintSnapshots.Rows(request,hand,ready)?.Properties().Select(p=>p.Value.ToObject<NanchangWait>()).ToArray()??Array.Empty<NanchangWait>();
        string key = HandKey(hand);
        return snapshot != null && snapshot.TryGetValue(key, out var value) ? value : Array.Empty<NanchangWait>();
    }
    private static bool SameMelds(NanchangWait row, List<string> melds) => (row.melds ?? Array.Empty<string>()).OrderBy(m=>m).SequenceEqual((melds ?? new List<string>()).OrderBy(m=>m));
    public static HashSet<int> Waiting(TingpaiQuery query) => new HashSet<int>(ReadWaits(query.Hand, query.RecordPlayerIndex, query.OnReady).Where(w=>SameMelds(w,query.Melds)).Select(w => w.tile));
    public static WaitTileHint Describe(WaitHintQuery query) {
        if(query?.HandWithWin==null) return WaitTileHint.None("未满足");
        var hand = new List<int>(query.HandWithWin); hand.Remove(query.HepaiTile);
        var hint = ReadWaits(hand, query.Record?.SelfPlayerIndex).FirstOrDefault(w => w.tile == query.HepaiTile && SameMelds(w,query.Melds));
        return DescribeRow(hint);
    }
    internal static WaitTileHint DescribeRow(NanchangWait hint) {
        if(hint==null || hint.status=="unmet") return WaitTileHint.None("未满足");
        if(hint.status=="below_minimum") return WaitTileHint.None("未起和");
        if(hint.status=="self_draw_only") return WaitTileHint.TsumoOnly("仅自摸");
        if(hint.status!="allowed" || hint.points<=0) return WaitTileHint.None("未满足");
        var values=hint.ron_points?.Values.Where(p=>p>0).Distinct().OrderBy(p=>p).ToArray();
        int low=hint.points_min>0?hint.points_min:values?.FirstOrDefault()??hint.points;
        int high=hint.points_max>0?hint.points_max:values?.LastOrDefault()??hint.points;
        string label=high>low?$"{low}～{high}分":$"{hint.points}分";
        return WaitTileHint.Ron(label);
    }
    public override bool IsSelfLocked => Info?.declared_jingdiao?.Length==4 && Mirror.IndexToPosition.Any(p=>p.Value=="self" && p.Key>=0 && p.Key<4 && Info.declared_jingdiao[p.Key]);
    protected override bool TryApplyFamilyWord(TableAction action,string word) => word=="nc_jingdiao" || word=="nc_draw";
    public override void OnSessionReset() { askClock.Reset(); clockHandNumber=0; clockGamestateId=null; ResetSettlement(); Info = null; waits.Clear(); endHands = null; NanchangTableIndicators.Clear(); }
}
