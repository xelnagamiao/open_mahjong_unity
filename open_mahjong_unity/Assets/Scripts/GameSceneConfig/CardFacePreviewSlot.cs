using UnityEngine;
using UnityEngine.UI;

/// <summary>场景里画好的单张牌面预览槽。背景在根 Image 上，花纹叠在 FaceOverlay 上。</summary>
public class CardFacePreviewSlot : MonoBehaviour {
    public int tileId;
    public Image image;
    [SerializeField] private Image overlay;
    private Image tableBackgroundLayer;
    private bool showingTable;

    public void ApplyTable(Sprite sprite, Sprite background, Color baseColor, bool dimMissingCustom) {
        showingTable = true;
        image.sprite = TileFaceResolver.FlatTableBackground;
        image.preserveAspect = true;
        baseColor.a = dimMissingCustom ? .45f : 1f;
        image.color = baseColor;
        Color tint = dimMissingCustom ? new Color(1f, 1f, 1f, .45f) : Color.white;
        if (background != null && tableBackgroundLayer == null) {
            var layer = new GameObject("TableBackgroundLayer", typeof(RectTransform), typeof(Image));
            layer.transform.SetParent(image.transform, false);
            layer.transform.SetAsFirstSibling();
            tableBackgroundLayer = layer.GetComponent<Image>();
            tableBackgroundLayer.raycastTarget = false;
        }
        if (tableBackgroundLayer != null) {
            tableBackgroundLayer.gameObject.SetActive(background != null);
            tableBackgroundLayer.sprite = background;
            tableBackgroundLayer.color = tint;
            bool stretch = (ConfigManager.Instance != null ? ConfigManager.Instance.FrontEdgeMode : CardBackManager.FrontEdgeMode)
                == CardEdgePanel.FrontEdgeMode.FollowTableBg;
            tableBackgroundLayer.preserveAspect = !stretch;
        }
        overlay.sprite = sprite;
        overlay.color = tint;
        // 内置与上传图片都完整等比居中；透明留白也是原图的一部分。
        overlay.preserveAspect = true;
        overlay.type = Image.Type.Simple;
        overlay.useSpriteMesh = false;
        overlay.enabled = sprite != null;
        overlay.gameObject.SetActive(sprite != null);
        FitTableLayers();
    }

    public void Apply(Sprite sprite, bool dimMissingCustom) {
        Apply(sprite, null, dimMissingCustom);
    }

    public void Apply(Sprite sprite, Sprite background, bool dimMissingCustom) {
        showingTable = false;
        if (tableBackgroundLayer != null) tableBackgroundLayer.gameObject.SetActive(false);
        // 切回手牌时恢复原槽位内的完整等比预览，2D 牌体比例保持原资源设计。
        StretchToParent(overlay.rectTransform);
        image.preserveAspect = true;
        Color tint = dimMissingCustom ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
        bool layered = background != null && sprite != null;
        if (layered) {
            image.sprite = background;
            image.color = tint;
            overlay.sprite = sprite;
            overlay.color = tint;
            overlay.preserveAspect = true;
            overlay.type = Image.Type.Simple;
            overlay.useSpriteMesh = false;
            overlay.enabled = true;
            overlay.gameObject.SetActive(true);
            return;
        }
        overlay.enabled = false;
        overlay.gameObject.SetActive(false);
        if (sprite != null) {
            image.sprite = sprite;
            image.color = tint;
        } else {
            image.sprite = null;
            image.color = new Color(0.3f, 0.3f, 0.3f, 1f);
        }
    }

    private void OnRectTransformDimensionsChange() {
        if (showingTable && image != null && overlay != null) FitTableLayers();
    }

    private void FitTableLayers() {
        Vector2 size = TileTextureLayout.FitTableCanvas(image.rectTransform.rect.size);
        FitLayer(overlay.rectTransform, size * TileTextureLayout.TableImageScale);
        if (tableBackgroundLayer != null) FitLayer(tableBackgroundLayer.rectTransform, size);
    }

    private static void FitLayer(RectTransform rect, Vector2 size) {
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
    }

    private static void StretchToParent(RectTransform rect) {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
