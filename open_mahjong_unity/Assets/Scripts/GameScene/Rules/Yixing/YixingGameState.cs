using System;

/// <summary>宜兴的客户端状态投影；合法性、补花顺序和支付均由服务端裁定。</summary>
public sealed class YixingGameState : TurnBasedGameState {
    public const string RuleId = "yixing";
    public const string SubRule = "yixing/standard";
    public const string RuleVersion = "yixing-user-20261002-om2";
    public static YixingGameState Active => RuleRegistry.ActiveGameState as YixingGameState;
    public YixingInfo Info { get; private set; }
    private void Accept(GameInfo info) { if (info?.yixing_info != null) Info = info.yixing_info; }
    protected override void OnRoundStarted(GameInfo info) { Info = null; Accept(info); }
    protected override void OnAskHandAction(Response response) { Accept(response.game_info); base.OnAskHandAction(response); }
    protected override void OnAskClaim(Response response) {
        Accept(response.game_info);
        Clock.PendingAskFromJiagang = Info?.phase == "waiting_action_qianggang";
        base.OnAskClaim(response);
    }
    protected override void OnDoAction(Response response) { Accept(response.game_info); base.OnDoAction(response); }
    protected override bool TryApplyFamilyWord(TableAction action, string word) => word == "yixing_last_choice";
    protected override void OnShowResult(Response response) {
        Accept(response.game_info);
        Info = response.show_result_info?.yixing_info ?? Info;
        base.OnShowResult(response);
    }
    protected override void OnReadyStatus(Response response) {
        if (response.ready_status_info == null) return;
        Clock.LastAskActionTick = response.ready_status_info.action_tick;
        Info = response.ready_status_info.yixing_info ?? Info;
        base.OnReadyStatus(response);
    }
    public override void OnSessionReset() { Info = null; }
}
