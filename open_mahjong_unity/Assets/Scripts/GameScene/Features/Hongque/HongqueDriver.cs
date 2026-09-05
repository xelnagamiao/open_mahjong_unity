/// <summary>
/// 虹雀的协议驱动器：开局/重连发全量桌面，对局中只发增量事件与当前可选项。
/// 与回合制麻将的 ask/do 询问式协议不同，因此不复用回合制驱动，而是把权威状态投影到同一张牌桌上。
///
/// 本类只做协议层面的接线；牌桌投影逻辑仍在 HongqueTableAdapter。
/// </summary>
public sealed class HongqueDriver : GameDriverBase {
    public const string RuleId = "hongque";

    /// <summary>虹雀的"过"必须走自己的协议通道，发标准 pass 会走 GB 通道导致服务端收不到回应。</summary>
    private const string PassAction = "hongque_pass";

    public override string DriverId => "hongque.state_sync";

    public override bool IsActive => HongqueTableAdapter.IsActive;

    public override bool TryHandleMessage(string suffix, Response response) {
        switch (suffix) {
            case "game_start":
            case "reconnect":
            case "update":
                HongqueTableAdapter.EnsureInstance().ApplyState(response.gamestate_id, response.hongque_state);
                if (response.hongque_state != null && response.hongque_state.sync_mode == "reconnect") {
                    AutoReconnect.OnGameRestored();
                }
                return true;
            default:
                // ready_status 等公共后缀交回核心
                return false;
        }
    }

    public override bool TryChooseAction(string actionType) {
        return HongqueTableAdapter.IsActive && HongqueTableAdapter.Instance.TryChooseAction(actionType);
    }

    public override bool TryCutTile(int tileId) {
        if (!HongqueTableAdapter.IsActive || !HongqueTileVisual.IsHongqueId(tileId)) return false;
        HongqueTableAdapter.Instance.SendDiscard(tileId);
        return true;
    }

    public override string PassActionName => PassAction;

    public override bool TryConfirmRoundResult() {
        if (!HongqueTableAdapter.IsActive || !HongqueTableAdapter.Instance.IsRoundEnd) return false;
        HongqueTableAdapter.Instance.ConfirmRoundResult();
        return true;
    }
}
