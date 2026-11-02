using UnityEngine;

namespace Salasasa.Home
{
    [ExecuteAlways, DefaultExecutionOrder(300)]
    public sealed class FullscreenPageLayout : MonoBehaviour
    {
        public FourSideHeader navigation;
        public Canvas matchCanvas;
        public RectTransform matchViewport;
        public RectTransform originalMatchPanel;
        public float originalMatchScale = 0.80833334f;

        Vector2 previousSize;
        Vector4 previousInsets = new Vector4(-1, -1, -1, -1);
        float previousRatio;

        void OnEnable() { Apply(true); }
        void LateUpdate() { Apply(false); }

        public void Apply(bool force)
        {
            if (!navigation || !navigation.owner || !matchCanvas || !matchViewport || !originalMatchPanel) return;
            var root = matchCanvas.transform as RectTransform;
            if (!root || root.rect.width <= 0 || root.rect.height <= 0) return;
            var inset = navigation.Insets;
            bool inPageRoot = matchCanvas.transform.IsChildOf(navigation.owner.pageRoot);
            if(inPageRoot)inset=Vector4.zero;
            float ratio = CanvasReferenceLayout.EffectiveScale(navigation.owner.artwork.canvas) / Mathf.Max(.001f, CanvasReferenceLayout.EffectiveScale(matchCanvas));
            var size = root.rect.size;
            if (!force && size == previousSize && inset == previousInsets && Mathf.Approximately(ratio, previousRatio)) return;
            previousSize = size; previousInsets = inset; previousRatio = ratio;

            matchViewport.anchorMin = Vector2.zero;
            matchViewport.anchorMax = Vector2.one;
            matchViewport.offsetMin = new Vector2(inset.x, inset.w) * ratio;
            matchViewport.offsetMax = new Vector2(-inset.z, -inset.y) * ratio;

            float scale = Mathf.Max(.001f, originalMatchScale);
            originalMatchPanel.anchorMin = originalMatchPanel.anchorMax = originalMatchPanel.pivot = Vector2.up;
            originalMatchPanel.anchoredPosition = Vector2.zero;
            originalMatchPanel.localScale = new Vector3(scale, scale, scale);
            originalMatchPanel.sizeDelta = matchViewport.rect.size / scale;
        }
    }
}
