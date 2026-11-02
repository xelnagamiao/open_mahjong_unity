using UnityEngine;

namespace Salasasa.Home
{
    public sealed class LobbyIconBinding : MonoBehaviour
    {
        public const int OriginalStyle = 3;
        public LobbyIconLibrary library;
        public string iconId;
        public UnityEngine.UI.Image primary, accent;
        public UnityEngine.UI.Image shadow;
        public Color accentColor = new Color(.329f,.847f,1f,1f);
        public Sprite originalPrimary, originalShadow, originalAccent;
        public bool hasAuthoredGeometry;
        public Vector2 originalSize = new Vector2(72,64), originalOffset;
        public Vector2 originalArtworkOffset, variantArtworkOffset;
        public Vector2 variantSize = new Vector2(48,48), variantOffset = new Vector2(0,7);
        public int Style { get; private set; } = OriginalStyle;
        public Sprite SpriteFor(int style) => style == OriginalStyle ?
            (originalPrimary ? originalPrimary : library.Find(iconId).original) : library.Find(iconId).primary[Mathf.Clamp(style,0,2)];

        public void Apply(int style)
        {
            var entry = library.Find(iconId);
            Style = iconId == LobbyIconWorkbench.MailIconId ? OriginalStyle : Mathf.Clamp(entry.defaultStyle, 0, OriginalStyle);
            primary.sprite = SpriteFor(Style);
            if (shadow) shadow.sprite = Style == OriginalStyle && originalShadow ? originalShadow : primary.sprite;
            if (accent) {
                accent.sprite = Style == OriginalStyle ? originalAccent : entry.accent[Style];
                accent.color = accentColor;
                accent.enabled = (Style == 2 || Style == OriginalStyle) && accent.sprite != null;
            }
            var presentation = GetComponentInParent<HeaderButtonPresentation>();
            if (presentation) presentation.Apply();
        }
    }
}
