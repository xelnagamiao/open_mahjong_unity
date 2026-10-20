public partial class GuobiaoGameState {
    protected override void OnHandAskReceived(AskHandActionGBInfo info) {
        if (UsesAsyncLocalTips(Session.SubRule)) FourJokersTips.Accept(info.four_jokers_hints);
    }
}
