using System.Collections;
using UnityEngine;

/// <summary>牌谱/观战回放和牌 3D 演出入口。</summary>
public static partial class HepaiRevealDirector {
    public static bool IsRecordShowCardsExpanded(string winnerPosition) {
        return RecordSetting.Instance != null && RecordSetting.Instance.IsShowCardsMode;
    }

    public static HepaiPresentationRequest BuildRecordRequest(
        string winnerPosition,
        string huClass,
        int[] hand,
        string[] huFan,
        string recordRule,
        bool showCardsExpanded,
        int hepaiTile,
        string discardPlayerPosition) {
        HepaiPresentationRequest request = BuildRequestCore(
            winnerPosition, huClass, hand, huFan, recordRule, discardPlayerPosition);
        request.IsRecordShowCardsExpanded = showCardsExpanded;
        request.RecordRule = recordRule;
        return request;
    }

    public static IEnumerator PlayRecord(HepaiPresentationRequest request) {
        if (request == null || Game3DManager.Instance == null) {
            yield break;
        }
        yield return Game3DManager.Instance.PlayRecordHepaiReveal(request);
    }
}
