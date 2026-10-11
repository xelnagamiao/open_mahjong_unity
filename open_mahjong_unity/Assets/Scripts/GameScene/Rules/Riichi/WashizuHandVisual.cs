using UnityEngine;
using UnityEngine.UI;

/// <summary>Neutral, rounded glass body behind the existing hand artwork.</summary>
public sealed class WashizuHandVisual : MonoBehaviour {
    private static Material glassMaterial;
    private static Material inkMaterial;
    private Image inkFace;
    private Material originalFaceMaterial;
    private Image glass;
    private RectTransform source;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSharedMaterial() {
        if (glassMaterial != null) Destroy(glassMaterial);
        glassMaterial = null;
        if (inkMaterial != null) Destroy(inkMaterial);
        inkMaterial = null;
    }

    public void Apply(bool transparent, Image body, Image face) {
        if (glass != null) glass.gameObject.SetActive(transparent);
        bool hasBody = body != null && body.enabled;
        if (!transparent || hasBody || inkFace != face) RestoreFaceMaterial();
        if (!transparent) return;
        if (glass == null) {
            if (glassMaterial == null) glassMaterial = new Material(Resources.Load<Shader>("Washizu/HandGlass")) {
                name = "Washizu hand glass", hideFlags = HideFlags.HideAndDontSave
            };
            var go = new GameObject("WashizuGlassBody", typeof(RectTransform), typeof(Image));
            go.layer = gameObject.layer;
            glass = go.GetComponent<Image>();
            glass.raycastTarget = false;
            glass.material = glassMaterial;
        }
        Image reference = hasBody ? body : face;
        if (reference == null) return;
        source = reference.rectTransform;
        glass.transform.SetParent(source.parent, false);
        int insert = source.GetSiblingIndex();
        if (glass.transform.GetSiblingIndex() < insert) insert--;
        glass.transform.SetSiblingIndex(insert);
        MatchRect();
        Color tint = reference.color;
        glass.color = tint;
        // Callers restore their base tint before each application (hover, selection,
        // disabled state, or pool reuse). Keep their Image/layout/sprite intact.
        if (hasBody) body.color = new Color(tint.r, tint.g, tint.b, 0);
        if (!hasBody && face != null && inkFace == null) {
            if (inkMaterial == null) inkMaterial = new Material(Resources.Load<Shader>("Washizu/HandInk")) {
                name="Washizu separated hand artwork", hideFlags=HideFlags.HideAndDontSave
            };
            inkFace=face; originalFaceMaterial=face.material; face.material=inkMaterial;
        }
        if (face != null) face.color *= new Color(.97f,.98f,1f,.975f);
    }

    private void RestoreFaceMaterial() {
        if (inkFace != null) inkFace.material=originalFaceMaterial;
        inkFace=null; originalFaceMaterial=null;
    }
    private void OnDestroy() { RestoreFaceMaterial(); }

    private void OnRectTransformDimensionsChange() { MatchRect(); }
    /// <summary>Position changes do not raise the dimensions callback.</summary>
    public void SyncLayout() { MatchRect(); }
    private void MatchRect() {
        if (glass == null || source == null) return;
        RectTransform r = glass.rectTransform;
        r.anchorMin=source.anchorMin; r.anchorMax=source.anchorMax; r.pivot=source.pivot;
        r.sizeDelta=source.sizeDelta; r.anchoredPosition3D=source.anchoredPosition3D;
        r.localRotation=source.localRotation; r.localScale=source.localScale;
    }
}
