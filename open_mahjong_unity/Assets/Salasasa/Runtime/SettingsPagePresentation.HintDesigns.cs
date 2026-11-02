using UnityEngine;

namespace Salasasa.Home
{
    public sealed partial class SettingsPagePresentation
    {
        public SettingsShapeGraphic hintPlate, hintStripe, footerFill, footerRule;
        public int HintStyle { get; private set; }
        Color hintFill, hintBorder, hintStripeColor;
        float hintStroke;
        SettingsShapeGraphic.Shape hintShape;
        Vector2 hintSize;

        public void ApplyHintStyle(int index)
        {
            HintStyle = 0;
            if (!helpText || !hintPlate || !footerFill || !footerRule || !page) return;
            int skin = page.variant;
            Color accent = Palette(skin, "BA4930", "41679E", "B44D6B");
            Color light = Palette(skin, "FFF8EE", "FFFFFF", "FFFCF6");
            Color foreground = light;
            hintFill = accent; hintBorder = Color.clear; hintStroke = 0;
            hintStripeColor = Palette(skin, "EBC393", "9ECEED", "E8BED1");
            hintShape = skin == 0
                ? SettingsShapeGraphic.Shape.Slant : SettingsShapeGraphic.Shape.Square;
            footerFill.Paint(Color.clear, Color.clear, 0, SettingsShapeGraphic.Shape.Square);
            footerRule.Paint(Color.clear, Color.clear, 0, SettingsShapeGraphic.Shape.Square);
            foreground.a = 0;
            helpText.color = foreground;
            RestartHintBackdrop();
        }

        void MeasureHintSurface()
        {
            if (!helpText || !hintPlate || !footer) return;
            float available = Mathf.Max(1, helpText.rectTransform.rect.width);
            Vector2 preferred = helpText.GetPreferredValues(helpText.text, available, float.PositiveInfinity);
            hintSize = new Vector2(Mathf.Min(available, preferred.x) + 28,
                Mathf.Clamp(preferred.y + 12, 36, footer.rect.height));
        }

        void RefreshHintSurface(float opacity)
        {
            if (!helpText || !hintPlate || !footer) return;
            if (string.IsNullOrEmpty(helpText.text)) opacity = 0;
            Color fill = hintFill, border = hintBorder;
            fill.a *= opacity; border.a *= opacity;
            hintPlate.Paint(fill, border, hintStroke, hintShape, hintShape == SettingsShapeGraphic.Shape.Slant ? 6 : 0);
            var rect = hintPlate.rectTransform;
            rect.sizeDelta = hintSize;
            rect.anchoredPosition = new Vector2(helpText.rectTransform.anchoredPosition.x - 14,
                -(footer.rect.height - hintSize.y) * .5f);
            if (hintStripe)
            {
                Color stripe = hintStripeColor;
                stripe.a *= opacity;
                hintStripe.Paint(stripe, Color.clear, 0, SettingsShapeGraphic.Shape.Square);
                bool slanted = hintShape == SettingsShapeGraphic.Shape.Slant;
                hintStripe.rectTransform.sizeDelta = new Vector2(4, hintSize.y - (slanted ? 12 : 2));
                hintStripe.rectTransform.anchoredPosition = rect.anchoredPosition + new Vector2(slanted ? 7 : 1, slanted ? -6 : -1);
            }
        }

        static Color Palette(int skin, string warm, string summer, string night)
            => SettingsStyleCollection.Hex(skin == 0 ? warm : skin == 1 ? summer : night);
    }
}
