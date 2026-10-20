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
        RefreshLedger();
    }

    protected override void OnRoundStarted(GameInfo info) {
        HongKong16LedgerPanel.Close();
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
        // The completion snapshot confirms the same physical hand. Preserve
        // its cards and order so opening replacement does not sort twice.
        GameCanvas.Instance.ChangeHandCards("SyncHandCardsPreservingOrder",0,Mirror.SelfHandTiles.ToArray(),null);
    }
    protected override void OnAskClaim(Response response) {
        Accept(response.game_info);
        Clock.PendingAskFromJiagang=Info?.phase=="waiting_action_qianggang";
        base.OnAskClaim(response);
    }
    protected override void OnDoAction(Response response) { Accept(response.game_info); base.OnDoAction(response); }

    protected override bool TryApplyFamilyWord(TableAction action,string word) {
        if (word=="deal_buhua_tile" && action.Seat=="self") {
            int[] tiles=action.ResolveDealTiles();
            // 十三张闲家及十六张起手补花不增加行牌张。真正摸花后的
            // 十四/十七张手牌仍走公共独立摸牌区，保留其摸切语义。
            if (tiles.Length>0 && (Mirror.SelfHandTiles.Count+tiles.Length)%3!=2) {
                Mirror.RemainTiles-=tiles.Length;
                Mirror.LastDealTileId=0;
                foreach (int tile in tiles) {
                    Mirror.SelfHandTiles.Add(tile);
                    GameCanvas.Instance.ChangeHandCards("GetCardIntoMainHand",tile,null,null);
                    Game3DManager.Instance.Change3DTile("GetCard",tile,0,"self",false,null);
                }
                return true;
            }
        }
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
        HongKong16LedgerPanel.Close();
        RefreshLedger();
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
        RefreshLedger();
        base.OnReadyStatus(response);
    }

    public void CutPull() {
        if (Session.SubRule != New16 || !CanCutPull || Session.IsRealtimeSpectator || Info?.phase != "waiting_ready") return;
        CanCutPull=false;
        RefreshLedger();
        GameStateNetworkManager.Instance.SendAction("pull_cut",0);
    }

    private void RefreshLedger() {
        var names = new string[4];
        foreach (var player in Mirror.PlayerToInfo.Values)
            if (player.original_player_index >= 0 && player.original_player_index < names.Length)
                names[player.original_player_index] = player.username;
        HongKong16LedgerPanel.Show(Session.SubRule, Info, names, Mirror.Self.original_player_index,
            CanCutPull && !Session.IsRealtimeSpectator ? CutPull : (Action)null);
    }

    private static Newtonsoft.Json.Linq.JObject LocalRequest => Active?.waitHints.Values.SelectMany(rows=>rows??Array.Empty<HongKongWait>()).FirstOrDefault(row=>row?.local_request!=null)?.local_request;
    public static HashSet<int> Waiting(TingpaiQuery query) {
        var request=query.RecordPlayerIndex.HasValue?null:LocalRequest;
        if(request==null)return HongKongShape.Waiting(query);
        var rows=LocalHintSnapshots.Rows(request,query.Hand,query.OnReady);
        return rows==null?new HashSet<int>():new HashSet<int>(rows.Properties().Select(p=>int.Parse(p.Name)));
    }
    public static WaitTileHint Describe(WaitHintQuery query) {
        var hand=new List<int>(query.HandWithWin);
        hand.Remove(query.HepaiTile);
        string key=string.Join(",",hand.OrderBy(t=>t));
        var request=query.Record==null?LocalRequest:null;
        var local=request==null?null:LocalHintSnapshots.Rows(request,hand)?.Properties().Select(p=>p.Value.ToObject<HongKongWait>()).ToArray();
        HongKongWait[] waits=local;
        if (local!=null || query.Record==null && Active?.waitHints.TryGetValue(key,out waits)==true) {
            var hint=waits.FirstOrDefault(h=>h.tile==query.HepaiTile);
            if (hint!=null) return DescribeWait(hint,query.SubRule);
        }
        return WaitTileHint.None("牌形听牌");
    }

    internal static WaitTileHint DescribeWait(HongKongWait hint,string subRule) {
        if (hint.ron) return hint.self_draw ? WaitTileHint.Ron($"{hint.ron_fan}{HongKongFanText.Unit(subRule)}起") : WaitTileHint.RonOnly();
        if (hint.self_draw) return WaitTileHint.TsumoOnly($"自摸{hint.self_draw_fan}{HongKongFanText.Unit(subRule)}起");
        return WaitTileHint.None("未起糊");
    }

    public override void OnSessionReset() {
        Info=null; CanCutPull=false; openingSynced=false; waitHints.Clear(); HongKong16LedgerPanel.Hide();
    }
}
