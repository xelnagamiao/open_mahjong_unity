public partial class GuobiaoGameState {
    protected override void OnAskHandAction(Response response) {
        if (GuobiaoGameState.UsesAuthoritativeTips(Session.SubRule)) FourJokersTips.Accept(response.ask_hand_action_info?.four_jokers_hints);
        base.OnAskHandAction(response);
    }
}
