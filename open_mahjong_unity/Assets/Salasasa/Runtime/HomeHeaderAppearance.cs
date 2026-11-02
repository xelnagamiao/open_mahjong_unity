using UnityEngine;
using UnityEngine.EventSystems;

namespace Salasasa.Home
{
    [DisallowMultipleComponent]
    public sealed class HomeHeaderAppearance : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public const string Preference = "SalasasaInterface.HomeIndicator.V2";
        public const int OriginalVariant = 0;
        public const int WhiteCornerVariant = 4;
        public const int VariantCount = 5;
        public static readonly string[] VariantNames = { "青色四角", "金色图标", "雾白方块", "墨蓝方块", "白色四角" };
        public static readonly int[] DisplayVariantOrder = { 4, 0, 1, 2, 3 };
        public static readonly string[] DisplayVariantNames = { "白色四角", "青色四角", "金色图标", "雾白方块", "墨蓝方块" };
        public FourSideHeaderButton target;
        public HomeHeaderPalette palette;
        public UnityEngine.UI.Image icon;
        public TMPro.TMP_Text caption;
        public UnityEngine.UI.Image originalIndicator;
        public GameObject[] plates;
        public HomeHeaderPlateGraphic[] surfaces;
        public UnityEngine.UI.Image[] choiceSurfaces;
        public Rect iconContentBounds = new Rect(.2638889f, .375f, .4722222f, .53125f);
        public Vector2 blockPadding = new Vector2(8, 7);
        [SerializeField] int initialVariant;
        public int Variant { get; private set; }
        public int DefaultVariant => initialVariant;
        bool previousSelected, initialized, hovered, pressed;
        bool UsesBlock => Variant == 2 || Variant == 3;

        void Awake() => RestoreAndApply();
        void OnEnable() { initialized = false; }
        void OnDisable() { hovered = pressed = false; initialized = false; }
        void LateUpdate()
        {
            if (target && (!initialized || previousSelected != target.IsSelected)) Apply();
            if (target && UsesBlock) UpdatePlateLayout();
            if (originalIndicator) originalIndicator.enabled = false;
        }
        public void SelectVariant(int value)
        {
            Variant = Mathf.Clamp(value, 0, VariantCount - 1);
            if (Application.isPlaying) { PlayerPrefs.SetInt(Preference, Variant); PlayerPrefs.Save(); }
            Apply();
        }
        public void BakeDefault()
        {
            initialVariant = Variant = WhiteCornerVariant; hovered = pressed = false; Apply();
        }
        public void SelectDisplayVariant(int index) => SelectVariant(DisplayVariantOrder[Mathf.Clamp(index, 0, VariantCount - 1)]);
        public void RestoreAndApply()
        {
            Variant = Mathf.Clamp(PlayerPrefs.GetInt(Preference, initialVariant), 0, VariantCount - 1);
            Apply();
        }
        public Color DefaultColor(int slot)
        {
            if (slot == 2) return Variant == 0 || Variant == 3 ? new Color32(79, 210, 231, 255) : Variant == 1 || Variant == WhiteCornerVariant ? Color.white : new Color32(215, 224, 229, 255);
            if (Variant == 2) return new Color32(29, 42, 53, 255);
            if (Variant == 1 && slot == 0) return new Color32(255, 211, 112, 255);
            return Color.white;
        }
        Color SelectedColor(int slot) => Variant == WhiteCornerVariant && slot == 2 ? Color.white : palette ? palette.ResolveColor(slot, DefaultColor(slot)) : DefaultColor(slot);
        public void Apply()
        {
            if (!target) return;
            initialized = true; previousSelected = target.IsSelected;
            if (plates != null)
                for (int i = 0; i < plates.Length; i++)
                    if (plates[i])
                    {
                        plates[i].SetActive(previousSelected && UsesBlock && i == Variant);
                    }
            UpdatePlateLayout();
            PaintSurfaces();
            if (target.cornerSelection)
            {
                target.cornerSelection.enabled = !UsesBlock;
                target.cornerSelection.washOpacity = 0;
                target.cornerSelection.color = SelectedColor(2);
                target.cornerSelection.artwork = System.Array.Empty<UnityEngine.UI.Graphic>();
                target.SetSelected(previousSelected);
            }
            if (icon) icon.color = previousSelected ? SelectedColor(0) : Color.white;
            if (caption) caption.color = previousSelected ? SelectedColor(1) : Color.white;
            if (originalIndicator) originalIndicator.enabled = false;
            if (choiceSurfaces != null)
                for (int i = 0; i < choiceSurfaces.Length; i++)
                    if (choiceSurfaces[i]) choiceSurfaces[i].color = i == Variant ? new Color32(51, 115, 143, 255) : new Color32(43, 58, 73, 255);
        }

        void UpdatePlateLayout()
        {
            if (plates == null || !icon || !caption) return;
            for (int i = 2; i < plates.Length; i++)
            {
                if (!plates[i]) continue;
                var plate = (RectTransform)plates[i].transform;
                var parent = plate.parent as RectTransform;
                if (!parent) continue;
                var imageRect = icon.rectTransform.rect;
                var ink = new Rect(imageRect.x + imageRect.width * iconContentBounds.x,
                    imageRect.y + imageRect.height * iconContentBounds.y,
                    imageRect.width * iconContentBounds.width, imageRect.height * iconContentBounds.height);
                var lower = (Vector2)parent.InverseTransformPoint(icon.transform.TransformPoint(ink.min));
                var upper = (Vector2)parent.InverseTransformPoint(icon.transform.TransformPoint(ink.max));
                var textBounds = caption.textBounds;
                if (textBounds.size.x <= 0 || textBounds.size.y <= 0)
                {
                    var size = caption.GetPreferredValues(caption.text);
                    Vector2 center = caption.rectTransform.rect.center;
                    if (caption.verticalAlignment == TMPro.VerticalAlignmentOptions.Baseline && caption.font)
                    {
                        var face = caption.font.faceInfo;
                        float scale = caption.fontSize / Mathf.Max(1, face.pointSize) * face.scale;
                        center.y = (face.ascentLine + face.descentLine) * scale * .5f;
                        size.y = (face.ascentLine - face.descentLine) * scale;
                    }
                    textBounds = new Bounds(center, size);
                }
                var textMin = (Vector2)parent.InverseTransformPoint(caption.transform.TransformPoint(textBounds.min));
                var textMax = (Vector2)parent.InverseTransformPoint(caption.transform.TransformPoint(textBounds.max));
                lower = Vector2.Min(lower, textMin) - blockPadding;
                upper = Vector2.Max(upper, textMax) + blockPadding;
                plate.anchorMin = plate.anchorMax = plate.pivot = new Vector2(.5f, .5f);
                plate.anchoredPosition = (lower + upper) * .5f - parent.rect.center;
                plate.sizeDelta = upper - lower;
            }
        }
        void PaintSurfaces()
        {
            if (surfaces == null) return;
            for (int i = 0; i < surfaces.Length; i++)
            {
                if (!surfaces[i]) continue;
                Color fill = i == 2 ? new Color32(242, 246, 248, 255) : new Color32(28, 44, 61, 255);
                if (pressed) fill = Color.Lerp(fill, Color.black, .08f);
                else if (hovered) fill = Color.Lerp(fill, Color.white, .07f);
                surfaces[i].Paint(fill, i == Variant ? SelectedColor(2) : Color.clear, 1.2f, 0, 0);
            }
        }
        public void OnPointerEnter(PointerEventData eventData) { hovered = true; PaintSurfaces(); }
        public void OnPointerExit(PointerEventData eventData) { hovered = pressed = false; PaintSurfaces(); }
        public void OnPointerDown(PointerEventData eventData) { if (eventData.button == PointerEventData.InputButton.Left) { pressed = true; PaintSurfaces(); } }
        public void OnPointerUp(PointerEventData eventData) { if (eventData.button == PointerEventData.InputButton.Left) { pressed = false; PaintSurfaces(); } }
    }
}
