public partial class GuobiaoGameState {
    protected override void OnHandAskReceived(AskHandActionGBInfo info) {
        AcceptHintHandAsk(info);
        if (UsesAsyncLocalTips(Session.SubRule)) FourJokersTips.Accept(info.four_jokers_hints);
    }
}
