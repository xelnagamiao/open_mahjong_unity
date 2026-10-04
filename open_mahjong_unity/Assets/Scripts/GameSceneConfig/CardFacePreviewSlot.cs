using UnityEngine;
using UnityEngine.UI;

/// <summary>场景里画好的单张牌面预览槽。背景在根 Image 上，花纹叠在 FaceOverlay 上。</summary>
public partial class CardFacePreviewSlot : MonoBehaviour {
    public int tileId;
    public Image image;
    [SerializeField] private Image overlay;
    [SerializeField] private Image tableBackgroundLayer;
    private bool showingTable;
    private float tableImageScale = TileTextureLayout.TableImageScale;

    public void ApplyTable(Sprite sprite, Sprite background, Color baseColor, bool dimMissingCustom,
        float imageScale = TileTextureLayout.TableImageScale) {
        showingTable = true;
        overlay.rectTransform.localScale = Vector3.one;
        tableImageScale = imageScale;
        image.sprite = TileFaceResolver.FlatTableBackground;
        image.preserveAspect = true;
        baseColor.a = 1f;
        Color tint = dimMissingCustom ? new Color(1f, 1f, 1f, .45f) : Color.white;
        image.color = baseColor * tint;
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
        overlay.rectTransform.localScale = Vector3.one * TileFaceFit.HandArtworkScale(tileId);
        image.preserveAspect = true;
        Color tint = dimMissingCustom ? new Color(1f, 1f, 1f, .45f) : Color.white;
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
            FitHandLayers();
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
        if (image == null || overlay == null) return;
        if (showingTable) FitTableLayers();
        else if (overlay.enabled) FitHandLayers();
    }

    private void FitHandLayers() {
        TileFaceFit.ApplyHandArtwork(overlay, image, tileId, HandSurfaceLibrary.CurrentFaceLayout);
    }

    private void FitTableLayers() {
        TileFaceFit.FitTableArtwork(image.rectTransform, overlay, tableBackgroundLayer, tableImageScale);
    }

    private static void StretchToParent(RectTransform rect) {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
