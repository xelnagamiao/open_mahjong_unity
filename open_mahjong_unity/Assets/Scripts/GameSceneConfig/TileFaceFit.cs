using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 2D 牌面：宽度固定，高度按原图像素比自适应，避免拉伸。
/// 分层花纹以牌体实际显示范围为基准，再应用当前背景独立保存的位置和等比缩放。
/// </summary>
public static class TileFaceFit {
    public const float DefaultSlotWidth = 97.3438f;

    public static Vector2 HandSizeFor(int tileId, float width) {
        Sprite body = TileFaceResolver.ShouldLayerHandFace(tileId) ? TileFaceResolver.LoadHandBackground() : null;
        if (body == null) body = TileFaceResolver.LoadSprite(tileId);
        return new Vector2(width, body != null && body.rect.width > 0f
            ? width * body.rect.height / body.rect.width : width / TileTextureLayout.TableAspect);
    }

    /// <summary>内容型牌行随牌高伸缩；固定范围的结算牌行仍由 SettlementTileRowLayout 等比适配。</summary>
    public static void FitRowHeight(Transform row) {
        if (row == null || row.GetComponent<HorizontalLayoutGroup>() == null) return;
        var fitters = row.GetComponents<ContentSizeFitter>();
        if (fitters.Length == 0) fitters = new[] { row.gameObject.AddComponent<ContentSizeFitter>() };
        foreach (var fitter in fitters) fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        LayoutRebuilder.MarkLayoutForRebuild(row as RectTransform);
    }

    /// <summary>与 3D 预览共用牌体比例、底色、背景贴图与花纹缩放。</summary>
    public static bool ApplyTableLayers(RectTransform root, Image face, ref Image body, ref Image texture, int tileId) {
        Sprite sprite = TileFaceResolver.LoadTableSprite(tileId);
        if (root == null || face == null || sprite == null) return false;
        if (body == null) body = CreateHandBackground(face);
        if (texture == null) {
            texture = CreateHandBackground(face);
            texture.name = "TableBackground";
        }
        body.enabled = true;
        body.sprite = TileFaceResolver.FlatTableBackground;
        body.preserveAspect = true;
        body.raycastTarget = false;
        CenterArtwork(body.rectTransform, root.rect.size, Vector2.zero);
        body.rectTransform.localScale = Vector3.one;
        var config = ConfigManager.Instance;
        texture.sprite = config != null && config.UseTableFaceBackground && !config.TableFaceUseSolidColor
            ? TileFaceResolver.LoadTableBackground() : null;
        texture.enabled = texture.sprite != null;
        texture.preserveAspect = (config != null ? config.FrontEdgeMode : CardBackManager.FrontEdgeMode)
            != CardEdgePanel.FrontEdgeMode.FollowTableBg;
        face.sprite = sprite;
        face.preserveAspect = true;
        face.type = Image.Type.Simple;
        face.useSpriteMesh = false;
        FitTableArtwork(body.rectTransform, face, texture, TileFaceResolver.TableImageScaleFor(tileId));
        return true;
    }

    public static void FitTableArtwork(RectTransform body, Image face, Image texture, float imageScale) {
        Vector2 size = TileTextureLayout.FitTableCanvas(body.rect.size);
        CenterArtwork(face.rectTransform, size * imageScale, Vector2.zero);
        face.rectTransform.localScale = Vector3.one;
        if (texture != null) {
            CenterArtwork(texture.rectTransform, size, Vector2.zero);
            texture.rectTransform.localScale = Vector3.one;
        }
    }

    // 香港麻将万子略收小花纹，保留牌体尺寸和原图的透明留白。
    public static float HandArtworkScale(int tileId) {
        return ConfigManager.Instance != null
            && ConfigManager.Instance.StandardTilePackId == TilePackIds.PackHkMahjong
            && tileId >= 11 && tileId <= 19 ? .92f : 1f;
    }

    public static bool ApplyHandLayers(RectTransform root, Image faceImage, ref Image backgroundImage, int tileId) {
        Sprite face = TileFaceResolver.LoadSprite(tileId);
        bool layered = TileFaceResolver.ShouldLayerHandFace(tileId);
        Sprite background = layered ? TileFaceResolver.LoadHandBackground() : null;
        if (layered && background != null && backgroundImage == null && faceImage != null) {
            // 兼容主场景中旧的 StaticCard 实例：它们还没有绑定牌体底图。
            backgroundImage = CreateHandBackground(faceImage);
        }
        bool showBackground = layered && background != null && backgroundImage != null;
        if (backgroundImage != null) {
            backgroundImage.enabled = showBackground;
            backgroundImage.raycastTarget = false;
            if (showBackground) {
                backgroundImage.sprite = background;
                backgroundImage.preserveAspect = true;
                backgroundImage.useSpriteMesh = false;
            }
        }
        if (faceImage != null && face != null) {
            faceImage.sprite = face;
            faceImage.preserveAspect = true;
            faceImage.useSpriteMesh = false;
        }
        Sprite fit = showBackground ? background : face;
        ApplyFixedWidth(root, faceImage, fit);
        if (faceImage != null && faceImage.rectTransform != root) {
            if (showBackground) ApplyHandArtwork(faceImage, backgroundImage, tileId, HandSurfaceLibrary.CurrentFaceLayout);
            else {
                // 对象池从正面复用为牌背/虹雀时不能残留上一次的位移和缩放。
                CenterArtwork(faceImage.rectTransform, root.rect.size, Vector2.zero);
                faceImage.rectTransform.localScale = Vector3.one * HandArtworkScale(tileId);
            }
        }
        return face != null;
    }

    public static void ApplyHandArtwork(UnityEngine.UI.Image artwork, UnityEngine.UI.Image background, int tileId,
        HandSurfaceLibrary.FaceLayout layout) {
        if (artwork == null || background == null || background.sprite == null || artwork == background) return;
        var body = background.rectTransform;
        Vector2 pixels = background.sprite.rect.size;
        if (pixels.x <= 0 || pixels.y <= 0) return;
        Rect bounds = background.GetPixelAdjustedRect();
        float fit = Mathf.Min(bounds.width / pixels.x, bounds.height / pixels.y);
        Vector2 size = pixels * fit;
        Vector2 center = bounds.position + Vector2.Scale(bounds.size - size, body.pivot) + size * .5f;
        var parent = artwork.rectTransform.parent as RectTransform;
        if (parent == null) return;
        // 先在牌体局部坐标中计算，再变换到花纹父节点，兼容顶左 pivot、旋转牌与预览槽。
        Vector3 localCenter = parent.InverseTransformPoint(body.TransformPoint(center));
        Vector3 axisX = parent.InverseTransformVector(body.TransformVector(Vector3.right));
        Vector3 axisY = parent.InverseTransformVector(body.TransformVector(Vector3.up));
        layout = layout.Normalized;
        Vector3 offset = axisX * (size.x * layout.x) + axisY * (size.y * layout.y);
        Vector2 parentSize = new Vector2(size.x * axisX.magnitude, size.y * axisY.magnitude);
        CenterArtwork(artwork.rectTransform, parentSize, (Vector2)(localCenter + offset) - parent.rect.center);
        artwork.rectTransform.localRotation = Quaternion.Inverse(parent.rotation) * body.rotation;
        artwork.rectTransform.localScale = Vector3.one * (layout.scale * HandArtworkScale(tileId));
        artwork.preserveAspect = true;
    }

    private static void CenterArtwork(RectTransform artwork, Vector2 size, Vector2 offset) {
        artwork.anchorMin = artwork.anchorMax = artwork.pivot = new Vector2(.5f, .5f);
        artwork.sizeDelta = size; artwork.anchoredPosition = offset;
        artwork.localRotation = Quaternion.identity;
    }

    private static Image CreateHandBackground(Image faceImage) {
        RectTransform face = faceImage.rectTransform;
        var go = new GameObject("FaceBackground", typeof(RectTransform), typeof(Image));
        go.layer = faceImage.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(face.parent, false);
        rect.anchorMin = face.anchorMin;
        rect.anchorMax = face.anchorMax;
        rect.pivot = face.pivot;
        rect.anchoredPosition3D = face.anchoredPosition3D;
        rect.sizeDelta = face.sizeDelta;
        rect.localRotation = face.localRotation;
        rect.localScale = face.localScale;
        rect.SetSiblingIndex(face.GetSiblingIndex());
        var image = go.GetComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    public static void ApplyFixedWidth(RectTransform root, Image image, Sprite sprite) {
        if (root == null || sprite == null || sprite.rect.width <= 0.01f) {
            return;
        }

        float width = root.rect.width;
        if (width <= 0.01f) {
            width = DefaultSlotWidth;
        }
        float height = width * sprite.rect.height / sprite.rect.width;
        Vector2 size = new Vector2(width, height);
        Vector2 previous = root.rect.size;
        root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
        root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);

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
        // RectTransform 的尺寸变化不会自动使所有父布局失效（尤其是自定义结算牌行）。
        if (previous != size) LayoutRebuilder.MarkLayoutForRebuild(root);
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
