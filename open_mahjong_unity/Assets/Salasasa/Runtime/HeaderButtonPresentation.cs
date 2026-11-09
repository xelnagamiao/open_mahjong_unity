using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Home
{
    [DisallowMultipleComponent]
    public sealed class HeaderButtonPresentation : MonoBehaviour
    {
        public FourSideHeaderButton button;
        public HeaderPresentationProfile profile;
        public TMP_Text caption, outline, shadow;
        public Graphic icon, iconShadow, iconAccent;
        public HeaderEntryPresentation Entry => profile ? profile.Find(button.route) : null;
        public float DesiredWidth
        {
            get
            {
                var entry = Entry;
                if (entry == null || button.compactIcon) return button.compactIcon ? 44 : 72;
                float width = entry.minimumWidth;
                if (entry.growForTranslation && caption)
                    width = Mathf.Max(width, MeasureCaption(caption, entry, profile) + 16);
                return Mathf.Clamp(width, entry.minimumWidth, Mathf.Max(entry.minimumWidth, entry.maximumWidth));
            }
        }

        public void Apply()
        {
            var entry = Entry;
            if (entry == null) return;
            float width = ((RectTransform)button.transform).rect.width;
            var binding=icon ? icon.GetComponent<LobbyIconBinding>() : null;
            bool variant=binding && binding.hasAuthoredGeometry && binding.Style!=LobbyIconBinding.OriginalStyle;
            if (button.artwork)
            {
                button.artwork.sizeDelta = new Vector2(width, button.compactIcon ? 44 : 64);
                button.artwork.anchoredPosition = entry.artworkOffset + (variant ? binding.variantArtworkOffset-binding.originalArtworkOffset : Vector2.zero);
            }
            ApplyText(caption, entry, Vector2.zero, profile.faceMaterial, width);
            ApplyText(outline, entry, Vector2.zero, profile.outlineMaterial, width);
            ApplyText(shadow, entry, entry.shadowOffset, profile.shadowMaterial, width);
            Vector2 size=entry.iconSize, offset=entry.iconOffset;
            if(variant)
            {
                size=Vector2.Scale(size,new Vector2(binding.variantSize.x/Mathf.Max(1,binding.originalSize.x),binding.variantSize.y/Mathf.Max(1,binding.originalSize.y)));
                offset+=binding.variantOffset-binding.originalOffset;
            }
            if (icon) SetRect(icon.rectTransform, size, offset);
            if (iconShadow) SetRect(iconShadow.rectTransform, size, offset + entry.iconShadowOffset);
            if (iconAccent) SetRect(iconAccent.rectTransform, size, offset);
            ApplyInk();
        }

        public static float MeasureCaption(TMP_Text text, HeaderEntryPresentation entry, HeaderPresentationProfile profile)
        {
            float size = text.fontSize;
            bool autoSize = text.enableAutoSizing;
            text.enableAutoSizing = false;
            text.fontSize = entry.fontSize;
            if (profile && profile.font) text.font = PlatformTypography.Resolve(profile.font, entry.Label(ConfigManager.CurrentLanguage));
            text.characterSpacing = entry.characterSpacing;
            float width = text.GetPreferredValues(entry.Label(ConfigManager.CurrentLanguage), Mathf.Infinity, Mathf.Infinity).x;
            text.fontSize = size;
            text.enableAutoSizing = autoSize;
            return width;
        }

        void ApplyText(TMP_Text text, HeaderEntryPresentation entry, Vector2 offset, Material material, float width)
            => ApplyCaption(text, entry, profile, offset, material, width);

        public static void ApplyCaption(TMP_Text text, HeaderEntryPresentation entry, HeaderPresentationProfile profile, Vector2 offset, Material material, float width)
        {
            if (!text) return;
            text.text = entry.Label(ConfigManager.CurrentLanguage);
            text.richText = false;
            if (profile.font) text.font = PlatformTypography.Resolve(profile.font, text.text);
            if (material) text.fontSharedMaterial = PlatformTypography.ResolveMaterial(material, text.font);
            text.fontStyle = FontStyles.Normal;
            text.fontSize = entry.fontSize;
            text.fontSizeMax = entry.fontSize;
            float minimum = PlatformText.HasLocaleCatalog(ConfigManager.CurrentLanguage) ? Mathf.Min(12, entry.minimumFontSize) : entry.minimumFontSize;
            text.fontSizeMin = Mathf.Min(minimum, entry.fontSize);
            text.enableAutoSizing = false;
            text.characterSpacing = entry.characterSpacing;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = entry.captionAlignment;
            text.raycastTarget = false;
            float captionWidth = entry.captionSize.x + Mathf.Max(0, width - entry.minimumWidth);
            SetRect(text.rectTransform, new Vector2(Mathf.Min(width, captionWidth), entry.captionSize.y), entry.captionOffset + offset);
            float available=text.rectTransform.rect.width;
            float required=text.GetPreferredValues(text.text).x;
            if(required>available && required>0)
                text.fontSize=Mathf.Clamp(entry.fontSize*available/required,minimum,entry.fontSize);
        }

        public void ApplyInk()
        {
            var entry = Entry;
            if (entry == null || entry.useHomePalette) return;
            if (caption) caption.color = button.IsSelected ? entry.selectedTextColor : entry.textColor;
            if (icon) icon.color = button.IsSelected ? entry.selectedIconColor : entry.iconColor;
        }

        static void SetRect(RectTransform rect, Vector2 size, Vector2 offset)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.sizeDelta = size;
            rect.anchoredPosition = offset;
            rect.localScale = Vector3.one;
        }
    }
}
