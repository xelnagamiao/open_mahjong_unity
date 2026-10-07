using System.Collections;
using UnityEngine;

public partial class Game3DManager {
    private IEnumerator MoveRiichiTenbouAfterDiscardCoroutine(GameObject tenbou, string seat,
        Vector3 start, Transform end, Quaternion rotation) {
        while (tenbou != null && HasPendingRecordStepAnimations(seat)) yield return null;
        // 结算/重连会清掉尚未起飞的点棒，不能在清桌后补播。
        if (tenbou == null || end == null) yield break;
        tenbou.SetActive(true);
        yield return MoveTenbouCoroutine(tenbou, start, end, rotation, 0.6f);
    }

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
