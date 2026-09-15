/// <summary>
/// 台湾麻将（16 张）。对应服务端 game_taiwan/TaiwanGameState。
/// 与标准骨架的差别：报听锁手（declared_ready）、花胡时花牌从他家移到和牌者、听牌资格随询问/动作下发、错和局中续打。
/// </summary>
public class TaiwanGameState : TurnBasedGameState {
    public const string RuleId = "taiwan";

    public static TaiwanGameState Active => RuleRegistry.ActiveGameState as TaiwanGameState;

    private readonly CuoheContinuation cuohe = new CuoheContinuation();

    /// <summary>自身当前听牌资格；仅用于客户端和牌提示计算（TipsContainer）。</summary>
    public string SelfReadyQualification { get; private set; }

    protected override void OnRoundStarted(GameInfo gameInfo) {
        cuohe.Clear();
        SelfReadyQualification = null;
    }

    protected override void OnBeforeAsk() {
        cuohe.TryResume();
    }

    protected override void OnHandAskReceived(AskHandActionGBInfo info) {
        UpdateSelfReadyQualification(info.player_index, info.ready_qualification);
    }

    protected override void OnBeforeActionPlayed(TableAction action) {
        UpdateSelfReadyQualification(action.PlayerIndex, action.ReadyQualification);
    }

    private void UpdateSelfReadyQualification(int playerIndex, string readyQualification) {
        if (playerIndex != Session.SelfIndex || string.IsNullOrEmpty(readyQualification)) return;
        SelfReadyQualification = readyQualification == "none" ? null : readyQualification;
    }

    /// <summary>花胡：把补花张从原持有者移到和牌者补花区（buhua_recipient 缺省为动作者本人）。</summary>
    protected override bool TryApplyFamilyWord(TableAction action, string word) {
        if (word != "hu_flower") return false;
        ApplyFlowerWinTransfer(action.BuhuaRecipient, action.BuhuaTile, action.Seat);
        return true;
    }

    private static void ApplyFlowerWinTransfer(int? recipientIndex, int? transferTileId, string fallbackRecipientSeat) {
        if (!transferTileId.HasValue) return;

        string recipientSeat = fallbackRecipientSeat;
        if (recipientIndex.HasValue
            && Mirror.IndexToPosition.TryGetValue(recipientIndex.Value, out string resolvedRecipient)) {
            recipientSeat = resolvedRecipient;
        }

        int transferTile = transferTileId.Value;
        string fromSeat = null;
        foreach (var entry in Mirror.PlayerToInfo) {
            if (entry.Key == recipientSeat
                || entry.Value?.huapai_list == null
                || !entry.Value.huapai_list.Contains(transferTile)) {
                continue;
            }
            fromSeat = entry.Key;
            break;
        }
        if (fromSeat == null) return;

        Mirror.PlayerToInfo[fromSeat].huapai_list.Remove(transferTile);
        Mirror.PlayerToInfo[recipientSeat].huapai_list.Add(transferTile);
        Game3DManager.Instance.TransferFlowerWinTile(transferTile, fromSeat, recipientSeat);
    }

    protected override void OnBeforeHuPresented(SettlementEnvelope env) {
        cuohe.MarkIfCuohe(env.WinnerIndex, env.FanLabels);
    }

    public static bool IsReadyLockTag(string tag) => tag == "declared_ready";

    /// <summary>报听后锁手：自动摸切只能打摸入牌，手牌区按此置灰。</summary>
    public override bool IsSelfLocked => SeatHasTag("self", IsReadyLockTag);

    public override void OnSessionReset() {
        cuohe.Clear();
        SelfReadyQualification = null;
    }
}
