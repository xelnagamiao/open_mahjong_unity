using TMPro;
using UnityEngine;

/// <summary>Physical face labels inherit the tile mesh transform and never expose concealed faces.</summary>
public sealed class RuleTileBadge3D : MonoBehaviour {
    private TextMeshPro label, auxiliary;

    public static void Apply(Renderer faceRenderer, int visibleTileId, bool faceUp, bool halfTurn = false) {
        if (!faceRenderer) return;
        var badge = faceRenderer.GetComponent<RuleTileBadge3D>();
        string caption = faceUp ? RuleRegistry.Current?.TileBadgeText?.Invoke(visibleTileId) : null;
        string tileName = faceUp && TileCornerLabel.Enabled ? TileCornerLabel.Caption(visibleTileId) : null;
        if (!badge && string.IsNullOrEmpty(caption) && string.IsNullOrEmpty(tileName)) return;
        if (!badge) badge = faceRenderer.gameObject.AddComponent<RuleTileBadge3D>();
        badge.SetLabel(ref badge.label, caption, faceRenderer, false, string.IsNullOrEmpty(tileName), RuleTileBadge.Ink, halfTurn);
        badge.SetLabel(ref badge.auxiliary, tileName, faceRenderer, true, true, TileCornerLabel.Ink(visibleTileId), halfTurn);
    }

    private void SetLabel(ref TextMeshPro target, string caption, Renderer face, bool helper, bool right, Color ink, bool halfTurn) {
        if (string.IsNullOrEmpty(caption)) { if (target) target.gameObject.SetActive(false); return; }
        if (!target) {
            var marker = new GameObject(helper ? "TileCornerLabel3D" : "RuleTileBadge3D", typeof(TextMeshPro));
            marker.transform.SetParent(face.transform, false); marker.layer = face.gameObject.layer;
            target = marker.GetComponent<TextMeshPro>();
            target.font = (helper ? PlatformTypography.LatinBold : PlatformTypography.ChineseRegular) ?? TMP_Settings.defaultFontAsset;
            target.fontSize = 10; target.fontStyle = FontStyles.Normal;
            target.alignment = TextAlignmentOptions.Center;
            target.raycastTarget = false; target.textWrappingMode = TextWrappingModes.NoWrap;
            target.rectTransform.sizeDelta = new Vector2(2, 2);
        }
        target.gameObject.SetActive(true);
        target.color = ink;
        if (target.text != caption) { target.text = caption; target.ForceMeshUpdate(true, true); }
        Bounds bounds = face.localBounds;
        float width = Mathf.Min(bounds.size.x, bounds.size.y);
        float height = width * (helper ? .22f : .265f);
        // The line box is too tall for Latin indices, while Mesh.bounds may
        // still be zero on inactive pooled TMP objects in a Player build.
        // Glyph metrics are available from TMP's generated layout in both cases.
        Bounds text = InkBounds(target);
        float scale = Mathf.Min(height / Mathf.Max(.001f, text.size.y), width * .36f / Mathf.Max(.001f, text.size.x));
        var markerTransform = target.transform;
        markerTransform.localScale = Vector3.one * scale;
        markerTransform.localRotation = TileFaceFrame.Rotation(halfTurn);
        float x = (right ? 1 : -1) * (bounds.extents.x - width * .075f - text.size.x * scale * .5f);
        float y = bounds.extents.y - width * .075f - text.size.y * scale * .5f;
        markerTransform.localPosition = TileFaceFrame.Point(bounds, new Vector2(x, y), halfTurn)
            - markerTransform.localRotation * text.center * scale;
    }

    private static Bounds InkBounds(TMP_Text text) {
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < text.textInfo.characterCount; i++) {
            var character = text.textInfo.characterInfo[i];
            if (!character.isVisible || character.textElement?.glyph == null) continue;
            var metrics = character.textElement.glyph.metrics;
            Vector2 topLeft = new Vector2(character.origin + metrics.horizontalBearingX * character.scale,
                character.baseLine + metrics.horizontalBearingY * character.scale);
            min = Vector2.Min(min, topLeft - Vector2.up * metrics.height * character.scale);
            max = Vector2.Max(max, topLeft + Vector2.right * metrics.width * character.scale);
        }
        if (max.x > min.x && max.y > min.y) return new Bounds((min + max) * .5f, max - min);
        // A missing font must never expand an index into a table-sized quad.
        Bounds fallback = text.textBounds;
        return fallback.size.x > .001f && fallback.size.y > .001f
            ? fallback : new Bounds(Vector3.zero, Vector3.one);
    }
}
