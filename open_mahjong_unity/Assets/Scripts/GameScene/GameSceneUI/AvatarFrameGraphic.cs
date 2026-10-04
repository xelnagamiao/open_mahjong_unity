using UnityEngine;
using UnityEngine.UI;

/// <summary>A narrow, softly lit cosmetic band around the portrait.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class AvatarFrameGraphic : MaskableGraphic {
    public const int DefaultItemId = 2201;
    public const int B1ItemId = 2202;
    private const float Thickness = 3f;
    [SerializeField] private int appearanceItemId = DefaultItemId;
    private UnityEngine.UI.Image portraitOwner;

    public static Color BuiltinColor(int itemId) {
        if (itemId == 0 || itemId == DefaultItemId) return new Color32(220, 199, 160, 255);
        if (itemId == B1ItemId) return new Color32(122, 205, 228, 255);
        return Color.clear;
    }

    public static void Apply(UnityEngine.UI.Image portrait, int itemId) {
        if (portrait == null) return;
        Color tint = GameSettings.Current.GetAvatarFrameColor(itemId);
        var corners = portrait.GetComponent<AvatarPortraitCorners>();
        if (tint.a > 0) {
            if (corners == null) corners = portrait.gameObject.AddComponent<AvatarPortraitCorners>();
            corners.SetRadius(OuterRadius(itemId) - Thickness);
            corners.enabled = true;
        } else if (corners != null) corners.enabled = false;
        var child = portrait.transform.Find("InventoryAvatarFrame");
        if (child == null && tint.a <= 0) return;
        AvatarFrameGraphic frame;
        if (child == null) {
            var go = new GameObject("InventoryAvatarFrame", typeof(RectTransform), typeof(CanvasRenderer), typeof(AvatarFrameGraphic));
            go.layer = portrait.gameObject.layer;
            go.transform.SetParent(portrait.transform, false);
            frame = go.GetComponent<AvatarFrameGraphic>();
            frame.raycastTarget = false;
        } else frame = child.GetComponent<AvatarFrameGraphic>();
        frame.rectTransform.anchorMin = Vector2.zero; frame.rectTransform.anchorMax = Vector2.one;
        frame.rectTransform.offsetMin = new Vector2(-Thickness, -Thickness);
        frame.rectTransform.offsetMax = new Vector2(Thickness, Thickness);
        frame.appearanceItemId = itemId == 0 ? DefaultItemId : itemId;
        frame.portraitOwner = portrait;
        frame.color = tint;
        frame.SetVerticesDirty();
        frame.gameObject.SetActive(tint.a > 0);
    }

    protected override void OnPopulateMesh(VertexHelper vh) {
        vh.Clear();
        var r = rectTransform.rect;
        // Inventory icons use a wider rectangle with preserveAspect enabled.
        // Match Image's aspect fit and pivot before expanding its visible bounds.
        if (portraitOwner != null) {
            var portraitRect = portraitOwner.GetPixelAdjustedRect();
            var sprite = portraitOwner.overrideSprite;
            if (portraitOwner.preserveAspect && sprite != null && portraitRect.width > 0 && portraitRect.height > 0) {
                var spriteSize = sprite.rect.size;
                if (spriteSize.x > 0 && spriteSize.y > 0) {
                    float fit = Mathf.Min(portraitRect.width / spriteSize.x, portraitRect.height / spriteSize.y);
                    var fittedSize = spriteSize * fit;
                    portraitRect.position += Vector2.Scale(portraitRect.size - fittedSize, portraitOwner.rectTransform.pivot);
                    portraitRect.size = fittedSize;
                }
            }
            var min = rectTransform.InverseTransformPoint(portraitOwner.rectTransform.TransformPoint(portraitRect.min));
            var max = rectTransform.InverseTransformPoint(portraitOwner.rectTransform.TransformPoint(portraitRect.max));
            r = Rect.MinMaxRect(min.x - Thickness, min.y - Thickness, max.x + Thickness, max.y + Thickness);
        }
        float radius = Mathf.Min(OuterRadius(appearanceItemId), Mathf.Min(r.width, r.height) * .5f);
        if (r.width <= Thickness * 2 || r.height <= Thickness * 2) return;
        const int segments = 12;
        const int count = 4 * (segments + 1);
        // Adjacent strips fill the entire band; a subpixel bevel carries the gloss.
        // The outer transparent fringe softens the silhouette without a dark backing.
        for (int strip = 0; strip < 5; strip++) {
            float inset = strip == 0 ? -.45f : strip == 1 ? 0 : strip == 2 ? .7f : strip == 3 ? 2.25f : Thickness;
            var ring = new Rect(r.xMin + inset, r.yMin + inset, r.width - inset * 2, r.height - inset * 2);
            for (int i = 0; i < count; i++) {
                float angle = (-90 + i / (segments + 1) * 90 + (i % (segments + 1)) * 90f / segments) * Mathf.Deg2Rad;
                float light = Mathf.Clamp01((-.6f * Mathf.Cos(angle) + .8f * Mathf.Sin(angle) + 1) * .5f);
                var shade = strip <= 1 ? Tint(.78f + .14f * light)
                    : strip == 2 ? Color.Lerp(color, new Color(1, 1, 1, color.a), .12f + .46f * light)
                    : strip == 3 ? Tint(.86f + .14f * light)
                    : Tint(.80f - .20f * light);
                if (strip == 0) shade.a = 0;
                vh.AddVert(RoundedPoint(ring, Mathf.Max(0, radius - inset), i, segments), shade, Vector2.zero);
            }
        }
        for (int strip = 0; strip < 4; strip++) {
            for (int i = 0; i < count; i++) {
                int outer = strip * count + i;
                int next = strip * count + (i + 1) % count;
                vh.AddTriangle(outer, next, outer + count);
                vh.AddTriangle(next, next + count, outer + count);
            }
        }
    }

    private Color Tint(float brightness) => new Color(color.r * brightness, color.g * brightness, color.b * brightness, color.a);

    private static float OuterRadius(int itemId) => itemId == B1ItemId ? 8f : 4f;

    internal static Vector3 RoundedPoint(Rect r, float radius, int index, int segments) {
        int corner = index / (segments + 1);
        float angle = (-90 + corner * 90 + (index % (segments + 1)) * 90f / segments) * Mathf.Deg2Rad;
        float x = corner < 2 ? r.xMax - radius : r.xMin + radius;
        float y = corner == 0 || corner == 3 ? r.yMin + radius : r.yMax - radius;
        return new Vector3(x + Mathf.Cos(angle) * radius, y + Mathf.Sin(angle) * radius);
    }
}
