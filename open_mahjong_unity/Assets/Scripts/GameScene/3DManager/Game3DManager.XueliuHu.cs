using System.Collections;
using UnityEngine;

public partial class Game3DManager {
    /// <summary>血流只移动本次和牌张；暗手保持立起，花区保留每次和牌记录。</summary>
    public IEnumerator PlayXueliuWinTile(string winner, int tile, bool zimo, bool multi,
        string source, bool recycle, bool qianggang, bool syncLiveState = true) {
        PosPanel3D panel = GetPosPanel(winner);
        if (panel?.buhuaPosition == null || tile < 10) yield break;
        GameObject card;
        Vector3? start = null;
        if (zimo) {
            Transform hand = IsRecordShowCardsModeActive() && winner != "self"
                ? panel.ShowCardsPosition : panel.cardsPosition;
            GameObject drawn = TryTakeLastHandTileObject(hand);
            start = drawn != null ? drawn.transform.position : (panel.outputPos != null ? panel.outputPos : panel.cardsPosition).position;
            if (drawn != null) MahjongObjectPool.Instance.Return(-1, drawn);
            card = MahjongObjectPool.Instance.SpawnPresentationTile(tile, start.Value, GetMeldVerticalWorldRotation(winner));
        } else if (multi) {
            var request = new HepaiPresentationRequest {
                DiscardPlayerPosition = source, HepaiTile = tile, IsQianggang = qianggang
            };
            if (TryGetWinTileSpawnPose(request, out Vector3 sourcePos, out _)) start = sourcePos;
            card = MahjongObjectPool.Instance.SpawnPresentationTile(tile, start ?? panel.buhuaPosition.position,
                GetMeldVerticalWorldRotation(winner));
        } else {
            card = DetachRonSourceObject(source, tile, preferAddedKong: qianggang);
            if (syncLiveState) {
                if (qianggang) SyncGuobiaoBloodRobbedKong(source, tile);
                else TableMirror.Current.SyncRonDiscardRemoved(source, tile);
            }
        }
        if (card == null) {
            Debug.LogWarning($"血流和牌张未找到：winner={winner}, tile={tile}, source={source}");
            yield break;
        }
        yield return CoAnimateTileToBuhua(card, panel, winner, tile, faceDown: false, dimmed: multi, start);
        card.name = $"XueliuHu_{panel.buhuaPosition.childCount}";
        if (multi && recycle) {
            GameObject original = DetachRonSourceObject(source, tile, preferAddedKong: qianggang);
            if (original != null) MahjongObjectPool.Instance.Return(-1, original);
            if (syncLiveState) {
                if (qianggang) SyncGuobiaoBloodRobbedKong(source, tile);
                else TableMirror.Current.SyncRonDiscardRemoved(source, tile);
            }
        }
    }
}
