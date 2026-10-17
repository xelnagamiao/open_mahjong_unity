public partial class GuobiaoGameState {
    protected override void OnAskHandAction(Response response) {
        if (GuobiaoGameState.UsesAsyncLocalTips(Session.SubRule)) FourJokersTips.Accept(response.ask_hand_action_info?.four_jokers_hints);
        base.OnAskHandAction(response);
    }
}
