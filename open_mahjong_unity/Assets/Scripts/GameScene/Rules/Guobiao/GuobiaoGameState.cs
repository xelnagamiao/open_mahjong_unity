/// <summary>
/// 国标麻将（含错和局中续打、局终亮暗杠）。对应服务端 game_guobiao/GuobiaoGameState。
/// 与标准骨架的差别：结算 extras 带 revealed_angang_masks；错和后本盘续打。
/// </summary>
public class GuobiaoGameState : TurnBasedGameState {
    public const string RuleId = "guobiao";

    public static GuobiaoGameState Active => RuleRegistry.ActiveGameState as GuobiaoGameState;

    private readonly CuoheContinuation cuohe = new CuoheContinuation();

    /// <summary>国标局终亮杠快照，供和牌/流局结算面板读取（EndResultPanel / EndLiujuPanel）。</summary>
    public GuobiaoEndResultExtras LastEndExtras { get; private set; }

    protected override void OnRoundStarted(GameInfo gameInfo) {
        cuohe.Clear();
    }

    protected override void OnBeforeAsk() {
        cuohe.TryResume();
    }

    protected override SettlementEnvelope BuildEnvelope(ShowResultInfo info) {
        SettlementEnvelope env = base.BuildEnvelope(info);
        LastEndExtras = info.revealed_angang_masks != null && info.revealed_angang_masks.Count > 0
            ? new GuobiaoEndResultExtras { RevealedAngangMasks = info.revealed_angang_masks }
            : null;
        env.Extras = LastEndExtras;
        return env;
    }

    protected override void OnBeforeHuPresented(SettlementEnvelope env) {
        cuohe.MarkIfCuohe(env.WinnerIndex, env.FanLabels);
    }

    public override void OnSessionReset() {
        cuohe.Clear();
        LastEndExtras = null;
    }
}
