using UnityEngine;

namespace Salasasa.Home
{
    [DefaultExecutionOrder(24000), DisallowMultipleComponent]
    public sealed class LobbyFanTablePlacement : MonoBehaviour
    {
        public CompleteLobbyController owner;
        public RectTransform[] docks;
        public RectTransform dialog;
        readonly Vector3[] corners = new Vector3[4];
        void OnEnable() { Apply(); }
        void LateUpdate() { Apply(); }
        public void Apply()
        {
            if (!owner || !owner.headerNavigation || docks == null || docks.Length == 0) return;
            var rect = (RectTransform)transform;
            var parent = (RectTransform)rect.parent;
            var dock = docks[0];
            float scale = CanvasReferenceLayout.EffectiveScale(owner.artwork.canvas) / CanvasReferenceLayout.PixelsPerUnit(parent);
            Vector4 inset = owner.headerNavigation.Insets * scale;
            dock.GetWorldCorners(corners);
            float bottom = Mathf.Max(inset.w, parent.InverseTransformPoint(corners[1]).y - parent.rect.yMin);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset.x, bottom + 16);
            rect.offsetMax = new Vector2(-inset.z, -inset.y - 16);
            // Retain the original 1920-reference width; wider windows add surrounding space.
            // On narrow viewports only the available width constrains the existing content layout.
            float width = Mathf.Min(1420.8f, Mathf.Max(0, rect.rect.width - 32));
            dialog.anchorMin = new Vector2(.5f, 0); dialog.anchorMax = new Vector2(.5f, 1);
            dialog.offsetMin = new Vector2(-width * .5f, 0);
            dialog.offsetMax = new Vector2(width * .5f, 0);
        }
    }
}
