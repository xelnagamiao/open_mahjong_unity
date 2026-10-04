/// <summary>敲牌标签驱动锁手；出牌、补花与结算沿用回合制表现。</summary>
public sealed class ShanghaiGameState : TurnBasedGameState {
    public override bool IsSelfLocked => SeatHasTag("self", tag => tag == "declared_ready");
}
