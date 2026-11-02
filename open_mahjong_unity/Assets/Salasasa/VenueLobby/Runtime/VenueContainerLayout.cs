using UnityEngine;

namespace Salasasa.Venues
{
    [DisallowMultipleComponent]
    public sealed class VenueContainerLayout : MonoBehaviour
    {
        public static readonly string[] VariantNames = { "暗底浮框" };
        public const int VariantCount = 1;

        public RectTransform availableArea, stage, baseTab, eventTab;
        public RectTransform[] pages;
        public VenueShapeGraphic shadow, outerFrame, innerFrame;
        [SerializeField, HideInInspector] int variant;

        public int Variant => variant;
        public Vector2 ReferenceSize => DesignSize(variant);
        Vector2 previousSize = new Vector2(-1, -1);
        int previousVariant = -1;

        void OnEnable() { Apply(true); }
        void LateUpdate() { Apply(false); }

        public void SetVariant(int value)
        {
            variant = Mathf.Clamp(value, 0, VariantCount - 1);
            Apply(true);
        }

        public void Apply(bool force = false)
        {
            if (!availableArea || !stage || !baseTab || !eventTab || !shadow || !outerFrame || !innerFrame || pages == null) return;
            var available = availableArea.rect.size;
            int selected = Mathf.Clamp(variant, 0, VariantCount - 1);
            if (!force && available == previousSize && selected == previousVariant) return;
            previousSize = available;
            previousVariant = selected;
            variant = selected;

            Vector2 size = DesignSize(selected);
            Vector2 pageOrigin = PageOrigin(selected);
            float scale = FitScale(available, size);
            stage.anchorMin = stage.anchorMax = stage.pivot = Vector2.one * .5f;
            stage.anchoredPosition = Vector2.zero;
            stage.sizeDelta = size;
            stage.localScale = new Vector3(scale, scale, 1);

            float tabTop = 20;
            Place(baseTab, size.x * .5f - 208, tabTop, 200, 48);
            Place(eventTab, size.x * .5f + 8, tabTop, 200, 48);
            foreach (var page in pages)
                if (page) Place(page, pageOrigin.x, pageOrigin.y, 1480, 710);

            Place(shadow.rectTransform, 6, 10, size.x, size.y);
            Place(outerFrame.rectTransform, 0, 0, size.x, size.y);
            float innerPadding = 8;
            Place(innerFrame.rectTransform, pageOrigin.x - innerPadding, pageOrigin.y - innerPadding,
                1480 + innerPadding * 2, 710 + innerPadding * 2);

            var paper = new Color32(228, 231, 235, 255);
            shadow.gameObject.SetActive(true);
            shadow.corners = outerFrame.corners = VenueShapeGraphic.Corners.All;
            shadow.Paint(new Color(0, 0, 0, .22f), Color.clear, 0, VenueShapeGraphic.Shape.Cut, 12);
            outerFrame.Paint(new Color32(13, 17, 23, 214), new Color32(218, 225, 235, 136), 2, VenueShapeGraphic.Shape.Cut, 12);
            innerFrame.Paint(paper, new Color32(255, 255, 255, 150), 1, VenueShapeGraphic.Shape.Square, 0);
        }

        public static Vector2 DesignSize(int index)
        {
            return new Vector2(1544, 826);
        }

        public static Vector2 PageOrigin(int index)
        {
            return new Vector2(32, 88);
        }

        public static float FitScale(Vector2 available, Vector2 design)
        {
            if (available.x <= 0 || available.y <= 0) return 1;
            return Mathf.Max(.01f, Mathf.Min(1, (available.x - 48) / design.x, (available.y - 48) / design.y));
        }

        static void Place(RectTransform rect, float left, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one;
        }
    }
}
