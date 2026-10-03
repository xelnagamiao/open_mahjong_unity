using System.Collections.Generic;
using System.Linq;

/// <summary>杭州 MIL 2025 的权威消息投影；动作合法性、财神和承包计分均由服务端负责。</summary>
public sealed class HangzhouGameState : TurnBasedGameState {
    public const string RuleId = "hangzhou";
    public const string SubRule = "hangzhou/mil2025";
    public const string RuleVersion = "mil-hangzhou-2025-om1";
    public const int Joker = 46; // TileIdOrder.Haku: 47 is 發.
    public static HangzhouGameState Active => RuleRegistry.ActiveGameState as HangzhouGameState;
    public HangzhouInfo Info { get; private set; }
    public readonly HangzhouHintsCache Hints = new HangzhouHintsCache();
    public override bool IsSelfLocked => Info?.forced_draw_discard != null && Session.SelfIndex >= 0 && Session.SelfIndex < Info.forced_draw_discard.Length && Info.forced_draw_discard[Session.SelfIndex];
    private void Accept(HangzhouInfo info) {
        if (info == null) return;
        Info = info; Hints.Accept(info); HangzhouStatePanel.Show(info);
    }
    protected override void OnRoundStarted(GameInfo info) {
        Info = null; Hints.Reset(); Accept(info?.hangzhou_info);
        if (Info?.self_has_draw_slot == true && Mirror.SelfHandTiles.Count > 0) {
            var hand = Mirror.SelfHandTiles.ToArray();
            GameCanvas.Instance.ChangeHandCards("SyncHandCards", 0, hand.Take(hand.Length - 1).ToArray(), null);
            GameCanvas.Instance.ChangeHandCards("GetCardNoAnimation", hand[hand.Length - 1], null, null);
        }
    }
    protected override void OnAskHandAction(Response response) { Accept(response.game_info?.hangzhou_info); Accept(response.ask_hand_action_info?.hangzhou_info); base.OnAskHandAction(response); }
    protected override void OnAskClaim(Response response) { Accept(response.game_info?.hangzhou_info); base.OnAskClaim(response); }
    protected override void OnDoAction(Response response) { Accept(response.game_info?.hangzhou_info); Accept(response.do_action_info?.hangzhou_info); base.OnDoAction(response); }
    public static bool IsPiaoDiscard(TableAction action) => action != null && !action.Silent && !action.ConcealedDiscard && action.HasWord("cut") && action.ResolveCutTiles().Contains(Joker);
    protected override void OnBeforeActionPlayed(TableAction action) {
        if (!IsPiaoDiscard(action)) return;
        SoundManager.Instance?.PlayActionSound(action.Seat,"hangzhou_piao");
        GameCanvas.Instance?.ShowActionDisplay(action.Seat,"hangzhou_piao",Session.RoomRule);
    }
    protected override bool TryApplyFamilyWord(TableAction action, string word) => word == "hangzhou_tail_burn";
    protected override void OnShowResult(Response response) { Accept(response.game_info?.hangzhou_info); Accept(response.show_result_info?.hangzhou_info); base.OnShowResult(response); }
    protected override SettlementEnvelope BuildEnvelope(ShowResultInfo info) {
        var envelope = base.BuildEnvelope(info);
        if (info.hangzhou_win_source == "ten_winds") {
            envelope.SkipHandReveal = true;
            envelope.WinTile = 0;
            envelope.AfterHandReveal = () => {
                if (Active != this) return;
                RoundEndPresentation.Instance?.HideSelfGameplayControl(false);
                Game3DManager.Instance?.RevealSichuanLiujuAllHands(new Dictionary<int,int[]> { { info.hepai_player_index, info.hepai_player_hand } });
            };
        }
        return envelope;
    }
    protected override void OnReadyStatus(Response response) {
        if (response.ready_status_info == null) return;
        Clock.LastAskActionTick = response.ready_status_info.action_tick;
        Accept(response.ready_status_info.hangzhou_info); base.OnReadyStatus(response);
    }
    public override void OnSessionReset() { Info = null; Hints.Reset(); HangzhouStatePanel.Hide(); }
}
