using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public partial class TipsContainer {
    private Coroutine shangraoPointerExit;
    private int shangraoTipOpeningVersion;

    public void HideTipsAfterPointerExit(PointerEventData pointer) {
        if (!DeferShangraoPointerExit(pointer, false)) HideTips();
    }

    public void EndCutPreviewTipsAfterPointerExit(PointerEventData pointer) {
        if (!DeferShangraoPointerExit(pointer, true)) EndCutPreviewTips();
    }

    private bool DeferShangraoPointerExit(PointerEventData pointer, bool cutPreview) {
        if (pointer == null || !gameObject.activeInHierarchy || visibleHintSubRule != ShangraoGameState.SubRule
            || guangdongHintPager == null || !guangdongHintPager.activeSelf) return false;
        if (shangraoPointerExit != null) StopCoroutine(shangraoPointerExit);
        shangraoPointerExit = StartCoroutine(FinishShangraoPointerExit(pointer, cutPreview, shangraoTipOpeningVersion));
        return true;
    }

    private IEnumerator FinishShangraoPointerExit(PointerEventData pointer, bool cutPreview, int openingVersion) {
        yield return new WaitForSecondsRealtime(.15f);
        var canvas = GetComponentInParent<Canvas>().rootCanvas;
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        while (openingVersion == shangraoTipOpeningVersion && gameObject.activeInHierarchy) {
            bool inside = RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, pointer.position, camera);
            if (!inside) {
                shangraoPointerExit = null;
                if (cutPreview) EndCutPreviewTips();
                else HideTips();
                yield break;
            }
            yield return null;
        }
        shangraoPointerExit = null;
    }
}
