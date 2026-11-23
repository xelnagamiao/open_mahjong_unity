using UnityEngine;
using UnityEngine.UI;

// ScrollRect layout notifications propagate through stretched children. Moving a card
// on the non-pixel-perfect content canvas does not change its image's local mesh.
public sealed class RecordCardImage : Image {
    private Rect lastRect;
    private bool hasRect;

    protected override void OnEnable() {
        hasRect = false;
        base.OnEnable();
    }

    protected override void OnRectTransformDimensionsChange() {
        Rect current = rectTransform.rect;
        if (hasRect && current == lastRect && canvas && !canvas.pixelPerfect) return;
        lastRect = current;
        hasRect = true;
        base.OnRectTransformDimensionsChange();
    }
}
