using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>港麻客户端只做状态投影；合法动作、补花、番数与拉踢由服务端状态机决定。</summary>
public sealed class HongKongGameState : TurnBasedGameState {
    public const string RuleId="hongkong";
    public const string Qingzhang="hongkong/qingzhang";
    public const string New13="hongkong/new13";
    public const string New16="hongkong/new16";
    public const string New13Gametower="hongkong/new13_gametower";
    public const string New13Lianhuise="hongkong/new13_lianhuise";
    public const string QingzhangRemix="hongkong/qingzhang_lianhuise";
    public static HongKongGameState Active => RuleRegistry.ActiveGameState as HongKongGameState;
    public HongKongInfo Info { get; private set; }
    public bool CanCutPull { get; private set; }
    private Dictionary<string,HongKongWait[]> waitHints = new Dictionary<string,HongKongWait[]>();
    private bool openingSynced;
    public override bool IsSelfLocked => SeatHasTag("self",tag=>tag=="declared_ready");

    private void Accept(GameInfo info) {
        if (info==null) return;
        if (info.hongkong_info!=null) Info=info.hongkong_info;
        if (info.hongkong_waits!=null) waitHints=info.hongkong_waits;
        if (info.players_info!=null && IsActive)
            RefreshTags(info.players_info.ToDictionary(p=>p.player_index,p=>p.tag_list??Array.Empty<string>()));
        if (Info?.phase!="waiting_ready") CanCutPull=false;
        HongKongLedgerPanel.Show(Info,CanCutPull && !Session.IsRealtimeSpectator);
    }

    protected override void OnRoundStarted(GameInfo info) {
        CanCutPull=false; openingSynced=info?.hongkong_info?.opening_complete==true; Accept(info);
    }
    protected override void OnAskHandAction(Response response) {
        Accept(response.game_info);
        SyncOpeningHand(response.game_info);
        base.OnAskHandAction(response);
    }
    private void SyncOpeningHand(GameInfo info) {
        if (openingSynced || Info?.opening_complete!=true || info?.self_hand_tiles==null) return;
        openingSynced=true;
        Mirror.SelfHandTiles.Clear();
        Mirror.SelfHandTiles.AddRange(info.self_hand_tiles);
        Mirror.Self.hand_tiles_count=Mirror.SelfHandTiles.Count;
        Mirror.LastDealTileId=Mirror.SelfHandTiles.Count%3==2 ? Mirror.SelfHandTiles.Last() : 0;
        GameCanvas.Instance.ChangeHandCards("SyncHandCards",0,Mirror.SelfHandTiles.ToArray(),null);
    }
    protected override void OnAskClaim(Response response) {
        Accept(response.game_info);
        Clock.PendingAskFromJiagang=Info?.phase=="waiting_action_qianggang";
        base.OnAskClaim(response);
    }
    protected override void OnDoAction(Response response) { Accept(response.game_info); base.OnDoAction(response); }

    protected override bool TryApplyFamilyWord(TableAction action,string word) {
        if (word=="riichi") {
            // 声明不放立直棒、不扣1000分；后续横置弃牌使用通用展示。
            var player=Mirror.Info(action.Seat);
            if (player!=null) player.tag_list=(player.tag_list??Array.Empty<string>()).Concat(new[] {"declared_ready"}).Distinct().ToArray();
            return true;
        }
        if (word=="hongkong_score") {
            if (GameCanvas.HasNonZeroGangScoreChanges(action.GangScoreChanges)) {
                Presenter.ApplyScoreDeltas(action.GangScoreChanges);
                GameCanvas.Instance.ShowGangScoreFloats(action.GangScoreChanges);
            }
            return true;
        }
        return false;
    }

    protected override void OnShowResult(Response response) {
        if (response.show_result_info?.hongkong_info!=null) Info=response.show_result_info.hongkong_info;
        CanCutPull=false;
        HongKongLedgerPanel.Show(Info,false);
        base.OnShowResult(response);
    }

    protected override SettlementEnvelope BuildEnvelope(ShowResultInfo info) {
        var env=base.BuildEnvelope(info);
        if (!string.IsNullOrEmpty(info.hongkong_flower_win)) { env.SkipHandReveal=true; env.WinTile=0; }
        return env;
    }

    protected override void OnReadyStatus(Response response) {
        if (response.ready_status_info==null) return;
        Clock.LastAskActionTick=response.ready_status_info.action_tick;
        Info=response.ready_status_info.hongkong_info??Info;
        CanCutPull=response.ready_status_info.can_cut_pull;
        HongKongLedgerPanel.Show(Info,CanCutPull && !Session.IsRealtimeSpectator);
        base.OnReadyStatus(response);
    }

    public void CutPull() {
        if (!CanCutPull || Session.IsRealtimeSpectator) return;
        CanCutPull=false;
        HongKongLedgerPanel.Show(Info,false);
        GameStateNetworkManager.Instance.SendAction("pull_cut",0);
    }

    public static WaitTileHint Describe(WaitHintQuery query) {
        var hand=new List<int>(query.HandWithWin);
        hand.Remove(query.HepaiTile);
        string key=string.Join(",",hand.OrderBy(t=>t));
        if (query.Record==null && Active?.waitHints.TryGetValue(key,out var waits)==true) {
            var hint=waits.FirstOrDefault(h=>h.tile==query.HepaiTile);
            if (hint!=null) {
                if (hint.ron) return WaitTileHint.Ron($"{hint.ron_fan}{HongKongFanText.Unit(query.SubRule)}起");
                if (hint.self_draw) return WaitTileHint.TsumoOnly($"自摸{hint.self_draw_fan}{HongKongFanText.Unit(query.SubRule)}起");
                return WaitTileHint.None("未起糊");
            }
        }
        return WaitTileHint.None("牌形听牌");
    }

    public override void OnSessionReset() {
        Info=null; CanCutPull=false; openingSynced=false; waitHints.Clear(); HongKongLedgerPanel.Hide();
    }
}
