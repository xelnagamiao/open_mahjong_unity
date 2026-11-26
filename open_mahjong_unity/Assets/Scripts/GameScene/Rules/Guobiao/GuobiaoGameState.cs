/// <summary>
/// 国标麻将（含错和局中续打、局终亮暗杠）。对应服务端 game_guobiao/GuobiaoGameState。
/// 与标准骨架的差别：结算 extras 带 revealed_angang_masks；错和后本盘续打。
/// </summary>
public partial class GuobiaoGameState : TurnBasedGameState {
    public const string RuleId = "guobiao";
    public const string SixteenSubRule = "guobiao/sixteen";
    public static bool UsesAsyncLocalTips(string sub) => IsJokerSubRule(sub) || sub == SixteenSubRule;
    public const string HuahunSubRule = "guobiao/huahun";
    public static bool IsJokerSubRule(string sub) => sub == FourJokersSubRule || sub == HuahunSubRule;
    public const string FourJokersSubRule = "guobiao/four_jokers";
    public const string SanmaSubRule = "guobiao/sanma";

    public static GuobiaoGameState Active => RuleRegistry.ActiveGameState as GuobiaoGameState;

    public override bool UsesClaimCandidates(string actionType) =>
        actionType == "peng" && IsTransparentSubRule(Session.SubRule);

    public override int GetCurrentHintDrawTile(int? pendingCutTileId) =>
        pendingCutTileId.HasValue ? 0 : BuildHintTiming()?.DrawTile ?? 0;

    public override void PopulateLiveWaitHintQuery(WaitHintQuery query, int? pendingCutTileId) {
        if (!pendingCutTileId.HasValue) query.GuobiaoTiming = BuildHintTiming();
    }

    public override bool TryCollectKongOptions(string actionType, System.Collections.Generic.List<int> handTiles,
        System.Collections.Generic.List<string> combinations,
        out System.Collections.Generic.List<(int targetTile, System.Collections.Generic.List<int> displayTiles)> options) {
        if (actionType == "angang" && Session.SubRule == HuahunSubRule) {
            options = new System.Collections.Generic.List<(int, System.Collections.Generic.List<int>)>();
            if (HuahunRules.CanFlowerKong(handTiles))
                options.Add((51, new System.Collections.Generic.List<int> { 51, 52, 53, 54 }));
            options.AddRange(ActionButton.CollectStandardAngangOptions(handTiles));
            return true;
        }
        options = null;
        return false;
    }

    private readonly CuoheContinuation cuohe = new CuoheContinuation();

    /// <summary>国标局终亮杠快照，供和牌/流局结算面板读取（EndResultPanel / EndLiujuPanel）。</summary>
    public GuobiaoEndResultExtras LastEndExtras { get; private set; }

    protected override void OnRoundStarted(GameInfo gameInfo) {
        ResetHintTiming();
        ResetOpeningFlowers();
        PlayerInfo self = OpeningSelfPlayer(gameInfo);
        Mirror.LastDealTileId = self?.has_draw_slot == true && self.hand_tiles?.Length > 0
            ? self.hand_tiles[self.hand_tiles.Length - 1] : 0;
        cuohe.Clear();
        FourJokersTips.Accept(gameInfo.four_jokers_hints);
        RestoreBloodBattle(gameInfo);
        RestoreTransparentHands(gameInfo);
    }

    protected override void OnBeforeAsk() {
        cuohe.TryResume();
        ResumeBloodBattle();
    }

    protected override SettlementEnvelope BuildEnvelope(ShowResultInfo info) {
        SettlementEnvelope env = base.BuildEnvelope(info);
        LastEndExtras = !IsTransparentSubRule(Session.SubRule)
            && info.revealed_angang_masks != null && info.revealed_angang_masks.Count > 0
            ? new GuobiaoEndResultExtras { RevealedAngangMasks = info.revealed_angang_masks }
            : null;
        env.Extras = LastEndExtras;
        return env;
    }

    protected override void OnBeforeHuPresented(SettlementEnvelope env) {
        cuohe.MarkIfCuohe(env.WinnerIndex, env.FanLabels);
    }

    public override void OnSessionReset() {
        ResetHintTiming();
        ResetOpeningFlowers();
        cuohe.Clear();
        LastEndExtras = null;
        bloodPendingContinue = false;
        bloodHistoryWritten = false;
        transparentPublicHands = null;
    }
}
