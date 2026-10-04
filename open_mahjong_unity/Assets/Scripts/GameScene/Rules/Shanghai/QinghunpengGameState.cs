/// <summary>清混碰独立状态；允许过和与换牌，使用通用回合制动作和补花表现。</summary>
public sealed class QinghunpengGameState : TurnBasedGameState {
    public override bool IsSelfLocked => false;
}
