/// <summary>山西公开报听锁手；合法杠、强制和牌由服务端决定。</summary>
public sealed class ShanxiGameState : TurnBasedGameState {
    public override bool IsSelfLocked => SeatHasTag("self", tag => tag == "declared_ready");
    protected override void OnRoundStarted(GameInfo gameInfo) {
        var known = Manager.player_to_info["self"].known_concealed_discards;
        known.Clear();
        foreach (var player in gameInfo.players_info)
            if (player.known_concealed_discards != null) known.AddRange(player.known_concealed_discards);
    }
    protected override void OnActionPlayed(TableAction action) {
        if (action.ConcealedDiscard && action.HasWord("cut")) {
            if (action.Seat == "self" && action.CutTile.HasValue)
                Manager.player_to_info["self"].known_concealed_discards.Add(action.CutTile.Value);
            if (!action.Silent) {
                GameCanvas.Instance.ShowActionDisplay(action.Seat, "riichi", "shanxi");
                SoundManager.Instance.PlayActionSound(action.Seat, "riichi");
            }
        }
    }
}
