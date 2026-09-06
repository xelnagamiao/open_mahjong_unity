/// <summary>
/// 简单麻将。对应服务端 game_jiandan。流程与标准回合制骨架一致；出站走自己的 "jiandan" 通道（见 Bootstrap 清单）。
/// </summary>
public class JiandanGameState : TurnBasedGameState {
    public const string RuleId = "jiandan";
}
