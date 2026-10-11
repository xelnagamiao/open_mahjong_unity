using System.Collections.Generic;

public partial class GuobiaoGameState {
    public const string TransparentSubRule = "guobiao/transparent";
    private Dictionary<int, int[]> transparentPublicHands;

    public IEnumerable<int> PublicOpponentHandTiles() {
        if (transparentPublicHands == null) yield break;
        foreach (var pair in transparentPublicHands) {
            if (!Mirror.IndexToPosition.TryGetValue(pair.Key, out string seat) || seat == "self") continue;
            foreach (int tile in pair.Value) if (WashizuTile.IsTransparent(tile)) yield return tile;
        }
    }

    private void RestoreTransparentHands(GameInfo info) {
        transparentPublicHands = Session.SubRule == TransparentSubRule ? info.washizu_hands : null;
        Game3DManager.Instance?.SetWashizuPublicHands(transparentPublicHands, Mirror.IndexToPosition);
    }

    protected override void OnDoAction(Response response) {
        bool update = Session.SubRule == TransparentSubRule && response.do_action_info?.is_claim != true
            && response.do_action_info?.washizu_hands != null;
        if (update) transparentPublicHands = response.do_action_info.washizu_hands;
        base.OnDoAction(response);
        if (update) Game3DManager.Instance?.SetWashizuPublicHands(transparentPublicHands, Mirror.IndexToPosition);
    }
}
