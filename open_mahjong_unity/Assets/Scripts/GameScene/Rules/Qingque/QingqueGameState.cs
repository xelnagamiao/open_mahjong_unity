/// <summary>
/// 青雀（国标改良）。对应服务端 game_mmcr/QingqueGameState。流程与标准回合制骨架一致，差异全在服务端算番。
/// </summary>
public class QingqueGameState : TurnBasedGameState {
    public const string RuleId = "qingque";
}
