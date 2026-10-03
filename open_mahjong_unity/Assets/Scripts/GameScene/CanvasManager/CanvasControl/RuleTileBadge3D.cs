using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>A corner marker on the visible physical face. Hidden/logical-only tile IDs never receive it.</summary>
public sealed class RuleTileBadge3D : MonoBehaviour {
    private TextMeshPro label;
    private string previousCaption;
    public static void Apply(Renderer faceRenderer, int visibleTileId, bool faceUp) {
        if (faceRenderer == null) return;
        var badge = faceRenderer.GetComponent<RuleTileBadge3D>();
        string caption = faceUp ? RuleRegistry.Current?.TileBadgeText?.Invoke(visibleTileId) : null;
        if (string.IsNullOrEmpty(caption)) {
            if (badge?.label != null) badge.label.gameObject.SetActive(false);
            return;
        }
        if (badge == null) badge = faceRenderer.gameObject.AddComponent<RuleTileBadge3D>();
        if (badge.label == null) {
            var marker = new GameObject("RuleTileBadge3D", typeof(TextMeshPro));
            marker.transform.SetParent(faceRenderer.transform, false);
            marker.layer = faceRenderer.gameObject.layer;
            badge.label = marker.GetComponent<TextMeshPro>();
            badge.label.font = GameCanvas.Instance?.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.font != null)?.font ?? TMP_Settings.defaultFontAsset;
            badge.label.fontSize = 10;
            badge.label.alignment = TextAlignmentOptions.Center;
            badge.label.color = new Color(.8f, .19f, .08f);
            badge.label.raycastTarget = false;
            badge.label.textWrappingMode = TextWrappingModes.NoWrap;
            badge.label.rectTransform.sizeDelta = new Vector2(2, 2);
        }
        badge.label.gameObject.SetActive(true);
        if (badge.previousCaption == caption) return;
        badge.previousCaption = caption;
        badge.label.text = caption;
        badge.label.ForceMeshUpdate(true, true);
        // ThreeDTiles.shader defines the front cap as local -Z. Using the
        // renderer's local bounds preserves the authored model and all skins.
        Bounds bounds = faceRenderer.localBounds;
        float height = Mathf.Min(bounds.size.x, bounds.size.y) * .25f;
        float scale = height / Mathf.Max(.001f, badge.label.textBounds.size.y);
        var markerTransform = badge.label.transform;
        markerTransform.localScale = Vector3.one * scale;
        markerTransform.localRotation = Quaternion.identity;
        markerTransform.localPosition = new Vector3(bounds.max.x - height * .72f, bounds.max.y - height * .72f, bounds.min.z - Mathf.Max(.0001f, bounds.size.z * .004f));
        markerTransform.localPosition -= badge.label.textBounds.center * scale;
    }
}
