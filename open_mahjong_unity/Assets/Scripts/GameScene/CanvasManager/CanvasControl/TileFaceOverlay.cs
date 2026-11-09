using UnityEngine;

/// <summary>One coordinate system for every overlay on a displayed tile body.</summary>
[DisallowMultipleComponent]
public sealed class TileFaceOverlay : MonoBehaviour {
    public const float ReferenceWidth = 100f;
    public static readonly Vector2 CornerInset = new Vector2(7f, 10f);
    private UnityEngine.UI.Image body;
    private RectTransform overlay;
    private static readonly string[] RuleBadgeNames = { "RuleTileBadge", "WenzhouJokerBadge", "GuangdongGhostBadge" };

    public static RectTransform Get(Transform card) {
        var binding = card.GetComponent<TileFaceOverlay>();
        if (!binding) binding = card.gameObject.AddComponent<TileFaceOverlay>();
        binding.body = card.GetComponent<TileCard>()?.BadgeBody ?? card.GetComponent<StaticCard>()?.BadgeBody;
        if (!binding.overlay) {
            var go = new GameObject("TileFaceOverlay", typeof(RectTransform));
            go.layer = card.gameObject.layer;
            binding.overlay = (RectTransform)go.transform;
            binding.overlay.SetParent(card, false);
            binding.overlay.anchorMin = binding.overlay.anchorMax = binding.overlay.pivot = Vector2.one * .5f;
        }
        binding.overlay.SetAsLastSibling();
        binding.Sync();
        return binding.overlay;
    }

    private void OnEnable() => Canvas.willRenderCanvases += Sync;
    private void OnDisable() => Canvas.willRenderCanvases -= Sync;

    public static void SetRuleCorner(RectTransform rect, bool helperVisible) {
        float inset = Mathf.Abs(rect.anchoredPosition.x);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(helperVisible ? 0 : 1, 1);
        rect.anchoredPosition = new Vector2(helperVisible ? inset : -inset, rect.anchoredPosition.y);
        var text = rect.GetComponent<TMPro.TMP_Text>();
        if (text) text.alignment = helperVisible ? TMPro.TextAlignmentOptions.TopLeft : TMPro.TextAlignmentOptions.TopRight;
    }

    public static void RefreshRuleCorners(Transform card, bool helperVisible) {
        var binding = card.GetComponent<TileFaceOverlay>();
        if (!binding || !binding.overlay) return;
        foreach (string name in RuleBadgeNames) {
            var badge = binding.overlay.Find(name) as RectTransform;
            if (badge) SetRuleCorner(badge, helperVisible);
        }
    }

    // Canvas layouts can resize RoundPanel slots after SetTile. Read the actual
    // displayed sprite rectangle just before rendering, including hover/drag,
    // pivot, aspect fitting, skin changes and nonuniform parent transforms.
    public void Sync() {
        if (!overlay) return;
        RectTransform source = body ? body.rectTransform : transform as RectTransform;
        if (!source) return;
        Rect rect = body ? body.GetPixelAdjustedRect() : source.rect;
        if (body && body.preserveAspect && body.sprite && body.sprite.rect.width > 0 && body.sprite.rect.height > 0) {
            Vector2 pixels = body.sprite.rect.size;
            Vector2 fit = pixels * Mathf.Min(rect.width / pixels.x, rect.height / pixels.y);
            rect = new Rect(rect.position + Vector2.Scale(rect.size - fit, source.pivot), fit);
        }
        float width = rect.width * transform.InverseTransformVector(source.TransformVector(Vector3.right)).magnitude;
        float height = rect.height * transform.InverseTransformVector(source.TransformVector(Vector3.up)).magnitude;
        if (width <= 0 || height <= 0) return;
        overlay.localPosition = transform.InverseTransformPoint(source.TransformPoint(rect.center));
        overlay.localRotation = Quaternion.Inverse(transform.rotation) * source.rotation;
        overlay.sizeDelta = new Vector2(ReferenceWidth, height / width * ReferenceWidth);
        overlay.localScale = Vector3.one * (width / ReferenceWidth);
    }
}
