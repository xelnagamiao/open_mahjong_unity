#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;

// Editor-only authoring; runtime controls bind the serialized scene objects.
public partial class CardFacePreviewSlot
{
    public void BakePreviewLayout() {
        if (tableBackgroundLayer != null) return;
        var layer = new GameObject("TableBackgroundLayer", typeof(RectTransform), typeof(Image));
        layer.layer = image.gameObject.layer;
        layer.transform.SetParent(image.transform, false);
        layer.transform.SetAsFirstSibling();
        tableBackgroundLayer = layer.GetComponent<Image>();
        tableBackgroundLayer.raycastTarget = false;
        StretchToParent(tableBackgroundLayer.rectTransform);
        layer.SetActive(false);
    }
}
#endif
