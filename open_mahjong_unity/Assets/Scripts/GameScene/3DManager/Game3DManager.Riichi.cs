using System.Collections;

public partial class Game3DManager {
    public void RemoveRobbedKanTile(string seat, int tileId, bool fromDraw) {
        if (!IsHandAnimPlayer(seat)) return;
        if (IsRecordShowCardsModeActive() && seat != "self") {
            StartCoroutine(TrackRecordHandAnimation(RemoveRobbedRecordKanTile(seat, tileId, fromDraw), seat));
        } else EnqueueJiagangHandWork(seat, tileId, fromDraw);
    }

    private IEnumerator RemoveRobbedRecordKanTile(string seat, int tileId, bool fromDraw) {
        var panel = GetPosPanel(seat);
        if (panel == null) yield break;
        yield return RemoveRecordShowHandCardCoroutine(panel.ShowCardsPosition, tileId, fromDraw, seat);
        if (fromDraw) ClearRecordPlayerDrawSlotState(seat);
        yield return RearrangeRecordShowMergeAllWithAnimation(panel.ShowCardsPosition, seat);
    }
}
