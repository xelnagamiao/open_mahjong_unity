using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 2D 牌面：宽度固定，高度按原图像素比自适应，避免拉伸。
/// 分层时花纹与牌面背景同尺寸直接叠加；预装包花纹位置写在 PNG 里。
/// </summary>
public static class TileFaceFit {
    public const float DefaultSlotWidth = 97.3438f;

    public static bool ApplyHandLayers(RectTransform root, Image faceImage, Image backgroundImage, int tileId) {
        Sprite face = TileFaceResolver.LoadSprite(tileId);
        bool layered = TileFaceResolver.ShouldLayerHandFace(tileId);
        Sprite background = layered ? TileFaceResolver.LoadHandBackground() : null;
        bool showBackground = layered && background != null && backgroundImage != null;
        if (backgroundImage != null) {
            backgroundImage.enabled = showBackground;
            backgroundImage.raycastTarget = false;
            if (showBackground) {
                backgroundImage.sprite = background;
                backgroundImage.preserveAspect = true;
            }
        }
        if (faceImage != null && face != null) {
            faceImage.sprite = face;
            faceImage.preserveAspect = true;
        }
        Sprite fit = showBackground ? background : face;
        ApplyFixedWidth(root, faceImage, fit);
        return face != null;
    }

    public static void ApplyFixedWidth(RectTransform root, Image image, Sprite sprite) {
        if (root == null || sprite == null || sprite.rect.width <= 0.01f) {
            return;
        }

        float width = root.sizeDelta.x;
        if (width <= 0.01f) {
            width = DefaultSlotWidth;
        }
        float height = width * sprite.rect.height / sprite.rect.width;
        Vector2 size = new Vector2(width, height);
        Vector2 previous = root.sizeDelta;
        root.sizeDelta = size;

        if (image != null) {
            image.preserveAspect = true;
            RectTransform imageRect = image.rectTransform;
            if (imageRect != null && imageRect != root) {
                MatchSizedChild(imageRect, previous, size);
            }
        }

        for (int i = 0; i < root.childCount; i++) {
            Transform child = root.GetChild(i);
            if (child == null) {
                continue;
            }
            string childName = child.name;
            if (childName != "Button" && childName != "fill" && childName != "SlotHitArea"
                && childName != "Image" && childName != "FaceBackground") {
                continue;
            }
            MatchSizedChild(child as RectTransform, previous, size);
        }

        LayoutElement layout = root.GetComponent<LayoutElement>();
        if (layout != null) {
            layout.preferredWidth = size.x;
            layout.preferredHeight = size.y;
            layout.minWidth = size.x;
            layout.minHeight = size.y;
        }
    }

    private static void MatchSizedChild(RectTransform child, Vector2 previousRoot, Vector2 newSize) {
        if (child == null) {
            return;
        }
        bool stretched = child.anchorMin != child.anchorMax;
        if (stretched) {
            return;
        }
        Vector2 childSize = child.sizeDelta;
        bool matchesRoot = Mathf.Abs(childSize.x - previousRoot.x) < 1f
            && Mathf.Abs(childSize.y - previousRoot.y) < 1f;
        if (matchesRoot || childSize.x > 1f) {
            child.sizeDelta = newSize;
        }
    }
}
