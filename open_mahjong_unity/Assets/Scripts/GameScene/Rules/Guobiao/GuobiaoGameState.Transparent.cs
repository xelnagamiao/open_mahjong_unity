using System.Collections.Generic;

public partial class GuobiaoGameState {
    public const string TransparentSubRule = "guobiao/transparent";
    public const string TransparentRealtimeSubRule = "guobiao/transparent_realtime";
    public static bool IsTransparentSubRule(string subRule) => subRule == TransparentSubRule || subRule == TransparentRealtimeSubRule;
    private Dictionary<int, int[]> transparentPublicHands;

    public IEnumerable<int> PublicOpponentHandTiles() {
        if (transparentPublicHands == null) yield break;
        foreach (var pair in transparentPublicHands) {
            if (!Mirror.IndexToPosition.TryGetValue(pair.Key, out string seat) || seat == "self") continue;
            foreach (int tile in pair.Value) if (WashizuTile.IsTransparent(tile)) yield return tile;
        }
    }

    private void RestoreTransparentHands(GameInfo info) {
        transparentPublicHands = IsTransparentSubRule(Session.SubRule) ? info.washizu_hands : null;
        Game3DManager.Instance?.SetWashizuPublicHands(transparentPublicHands, Mirror.IndexToPosition);
        ApplyRealtimeHandLayouts(info.washizu_layouts);
    }

    private void ApplyRealtimeHandLayouts(Dictionary<int, WashizuHandLayout> layouts, Response acknowledgement = null) {
        if (Session.SubRule != TransparentRealtimeSubRule || layouts == null) return;
        Game3DManager.Instance?.SetWashizuRealtimeLayouts(layouts, Mirror.IndexToPosition);
        if (layouts.TryGetValue(Session.SelfIndex, out WashizuHandLayout self))
            GameCanvas.Instance.ReceiveWashizuSelfLayout(self, acknowledgement);
        foreach (var pair in layouts) {
            if (pair.Key == Session.SelfIndex) continue;
            if (transparentPublicHands == null) transparentPublicHands = new Dictionary<int, int[]>();
            transparentPublicHands[pair.Key] = pair.Value.tiles;
        }
    }

    protected override bool HandleExtraMessage(string suffix, Response response) {
        if (suffix != "hand_order") return base.HandleExtraMessage(suffix, response);
        if (response.gamestate_id == UserDataManager.Instance.GamestateId)
            ApplyRealtimeHandLayouts(response.washizu_layouts, response);
        return true;
    }

    protected override void OnDoAction(Response response) {
        bool update = IsTransparentSubRule(Session.SubRule) && response.do_action_info?.is_claim != true
            && response.do_action_info?.washizu_hands != null;
        if (update) transparentPublicHands = response.do_action_info.washizu_hands;
        if (response.do_action_info?.is_claim != true) ApplyRealtimeHandLayouts(response.do_action_info?.washizu_layouts);
        base.OnDoAction(response);
        if (update) Game3DManager.Instance?.SetWashizuPublicHands(transparentPublicHands, Mirror.IndexToPosition);
    }
}
