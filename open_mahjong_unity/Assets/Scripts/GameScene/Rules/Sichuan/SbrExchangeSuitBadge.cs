using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class SbrExchangeSuitBadge : MonoBehaviour {
    internal enum Style { Seal, EdgeTab, SideTag, FlatRing, FlatRail, FlatBand, FlatDiamond, PaperDiamond, SplitDiamond }
    internal static Style PreviewStyle { get; set; } = Style.FlatDiamond;
    internal int Suit { get; private set; }
    internal Style BadgeStyle { get; private set; }

    private static readonly Color32[] Inks = {
        default, new Color32(130, 22, 30, 255), new Color32(13, 48, 100, 255), new Color32(9, 64, 36, 255)
    };
    private static readonly string[] Captions = { "", "万", "筒", "条" };
    private static readonly Color32[] Glazes = {
        default, new Color32(216, 72, 59, 255), new Color32(52, 128, 204, 255), new Color32(32, 157, 100, 255)
    };
    private static readonly Color32 Ivory = new Color32(255, 247, 224, 255);
    private static readonly Color32 Charcoal = new Color32(30, 31, 36, 255);
    private static readonly Color32 Brass = new Color32(224, 168, 77, 255);
    private static readonly Sprite[,] Plates = new Sprite[(int)Style.SplitDiamond + 1, 3];
    private static Material outlinedGlyphMaterial;

    private static bool MountedOutside(Style style) =>
        style == Style.SideTag;

    internal static SbrExchangeSuitBadge Attach(Transform tile, int suit, bool extendRight = true) {
        if (!tile || suit < 1 || suit > 3) return null;
        var body = tile.GetComponent<MeshFilter>() ?? tile.GetComponentInChildren<MeshFilter>(true);
        if (!body || !body.sharedMesh) return null;
        var node = new GameObject(nameof(SbrExchangeSuitBadge));
        node.layer = tile.gameObject.layer;
        node.transform.SetParent(tile, false);
        var badge = node.AddComponent<SbrExchangeSuitBadge>();
        badge.Suit = suit;
        badge.BadgeStyle = PreviewStyle;
        badge.Build(body, extendRight);
        badge.SetRoll(0);
        return badge;
    }

    internal void SetRoll(float roll) {
        bool visible = roll >= .80f;
        if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
    }

    private void Build(MeshFilter body, bool extendRight) {
        Bounds bounds = body.sharedMesh.bounds;
        Matrix4x4 bodyToTile = transform.parent.worldToLocalMatrix * body.transform.localToWorldMatrix;
        Vector3 right = bodyToTile.MultiplyVector(Vector3.left);
        Vector3 up = bodyToTile.MultiplyVector(Vector3.up);
        float width = bounds.size.x * right.magnitude;
        float end = extendRight ? 1f : -1f;
        Vector3 back = new Vector3(bounds.center.x, bounds.center.y, bounds.max.z + bounds.size.z * .012f);
        if (BadgeStyle == Style.EdgeTab || BadgeStyle == Style.FlatRing || BadgeStyle == Style.FlatRail || BadgeStyle == Style.FlatBand)
            back.y = bounds.min.y + bounds.size.x * .10f;
        bool outside = MountedOutside(BadgeStyle);
        if (outside) {
            back.x -= end * bounds.size.x * 1.465f;
            back.y -= bounds.size.x * .435f;
        }
        transform.localPosition = bodyToTile.MultiplyPoint3x4(back);
        transform.localRotation = Quaternion.LookRotation(Vector3.Cross(right, up), up);
        if (outside) transform.localRotation *= Quaternion.Euler(-12f, 0, 0);
        transform.localScale = Vector3.one * width;

        var plateObject = new GameObject("Plate", typeof(SpriteRenderer));
        plateObject.layer = gameObject.layer;
        var plate = plateObject.GetComponent<SpriteRenderer>();
        plate.transform.SetParent(transform, false);
        if (outside) {
            plate.transform.localPosition = Vector3.right * (end * .435f);
            plate.flipX = !extendRight;
        }
        plate.sprite = PlateSprite(BadgeStyle, Suit);
        plate.shadowCastingMode = ShadowCastingMode.Off;
        plate.receiveShadows = false;

        var glyphObject = new GameObject("Glyph", typeof(TextMeshPro));
        glyphObject.layer = gameObject.layer;
        var glyph = glyphObject.GetComponent<TextMeshPro>();
        glyph.transform.SetParent(transform, false);
        glyph.font = TableHudTypography.Emphasis ?? PlatformTypography.ChineseRegular ?? TMP_Settings.defaultFontAsset;
        bool flat = (int)BadgeStyle >= (int)Style.FlatRing;
        bool darkGlyph = BadgeStyle == Style.FlatRing || BadgeStyle == Style.FlatRail || BadgeStyle == Style.PaperDiamond;
        glyph.fontSharedMaterial = flat && !darkGlyph ? OutlinedGlyph(glyph.font) : glyph.font.material;
        glyph.fontSize = 10f;
        glyph.alignment = TextAlignmentOptions.Center;
        glyph.textWrappingMode = TextWrappingModes.NoWrap;
        glyph.raycastTarget = false;
        glyph.rectTransform.sizeDelta = new Vector2(8, 8);
        glyph.color = flat ? (darkGlyph ? Inks[Suit] : Ivory)
            : BadgeStyle == Style.SideTag ? new Color32(255, 250, 227, 255) : Inks[Suit];
        glyph.text = Captions[Suit];
        glyph.ForceMeshUpdate();
        Bounds ink = glyph.textBounds;
        TMP_CharacterInfo character = glyph.textInfo.characterInfo[0];
        if (character.isVisible && character.textElement?.glyph != null) {
            var metrics = character.textElement.glyph.metrics;
            Vector3 size = new Vector3(metrics.width, metrics.height) * character.scale;
            Vector3 topLeft = new Vector3(character.origin + metrics.horizontalBearingX * character.scale,
                character.baseLine + metrics.horizontalBearingY * character.scale);
            ink = new Bounds(topLeft + new Vector3(size.x, -size.y) * .5f, size);
        }
        float glyphSize = flat ? .63f : BadgeStyle == Style.Seal ? .60f : .58f;
        float scale = glyphSize / Mathf.Max(.001f, Mathf.Max(ink.size.x, ink.size.y));
        glyph.transform.localScale = Vector3.one * scale;
        Vector3 glyphCenter = outside ? new Vector3(end * .49f, 0, -.014f)
            : new Vector3(0, 0, -.014f);
        glyph.transform.localPosition = glyphCenter - ink.center * scale;
        glyph.renderer.sortingOrder = plate.sortingOrder + 1;
        glyph.renderer.shadowCastingMode = ShadowCastingMode.Off;
        glyph.renderer.receiveShadows = false;
    }

    private static Material OutlinedGlyph(TMP_FontAsset font) {
        if (outlinedGlyphMaterial) return outlinedGlyphMaterial;
        outlinedGlyphMaterial = new Material(font.material) { name = "ExchangeBadge_InkOutline" };
        outlinedGlyphMaterial.EnableKeyword("OUTLINE_ON");
        outlinedGlyphMaterial.SetColor(ShaderUtilities.ID_OutlineColor, Charcoal);
        outlinedGlyphMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, .17f);
        return outlinedGlyphMaterial;
    }

    private static Sprite PlateSprite(Style style, int suit) {
        var sprite = Plates[(int)style, suit - 1];
        if (sprite) return sprite;
        const int size = 96;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = PlatePixel(style, suit, (x + .5f) / size - .5f, (y + .5f) / size - .5f);
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) {
            name = $"ExchangeBadge_{style}_{suit}", wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear, anisoLevel = 4
        };
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size, 0, SpriteMeshType.FullRect);
        sprite.name = texture.name;
        Plates[(int)style, suit - 1] = sprite;
        return sprite;
    }

    private static Color32 PlatePixel(Style style, int suit, float x, float y) {
        Color32 ink = Inks[suit];
        if ((int)style >= (int)Style.FlatRing) return FlatPlatePixel(style, Glazes[suit], ink, x, y);
        Color32 cream = new Color32(255, 243, 209, 255);
        Color32 border = new Color32(57, 47, 37, 255);
        float edge;
        Color32 color;
        if (style == Style.Seal) {
            edge = .445f - Mathf.Sqrt(x*x + y*y);
            color = edge < .014f ? border : edge < .047f ? ink : cream;
        } else if (style == Style.EdgeTab) {
            edge = CutCorner(x, y, .475f, .345f, .066f);
            color = edge < .014f ? border : edge < .032f || Mathf.Abs(x) > .40f ? ink : cream;
        } else {
            float stem = Mathf.Min(.062f - Mathf.Abs(y), Mathf.Min(x + .49f, -.19f - x));
            float flag = CutCorner(x - .055f, y, .385f, .35f, .095f);
            edge = Mathf.Max(stem, flag);
            color = edge < .018f ? border : flag < .046f ? cream : ink;
        }
        color.a = (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(edge * 96f + .5f));
        return color;
    }

    private static Color32 FlatPlatePixel(Style style, Color32 glaze, Color32 ink, float x, float y) {
        Color32 ivory = Ivory;
        Color32 border = Charcoal;
        float face;
        Color32 color;
        if (style == Style.FlatRing) {
            face = .462f - Mathf.Sqrt(x * x + y * y);
            if (face < .018f) color = border;
            else if (face < .078f) color = ink;
            else color = ivory;
        } else if (style == Style.FlatRail) {
            face = CutCorner(x, y, .468f, .335f, .055f);
            float inner = CutCorner(x, y, .395f, .255f, .045f);
            if (face < .016f) color = border;
            else if (inner > 0) color = ivory;
            else color = ink;
        } else if (style == Style.FlatBand) {
            face = CutCorner(x, y, .417f, .345f, .070f);
            float inset = CutCorner(x, y, .355f, .278f, .050f);
            if (face < .018f) color = border;
            else if (inset > 0) color = glaze;
            else color = ink;
        } else {
            face = (.575f - Mathf.Abs(x) - Mathf.Abs(y)) * .7071f;
            if (style == Style.FlatDiamond) {
                if (face < .020f) color = border;
                else color = ink;
            } else if (style == Style.PaperDiamond) {
                if (face < .020f) color = border;
                else if (face < .078f) color = ink;
                else color = ivory;
            } else {
                if (face < .020f) color = border;
                else if (y >= 0) color = ink;
                else color = glaze;
            }
        }
        color.a = (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(face * 92f + .52f));
        return color;
    }

    private static float CutCorner(float x, float y, float halfWidth, float halfHeight, float corner) {
        float dx = halfWidth - Mathf.Abs(x), dy = halfHeight - Mathf.Abs(y);
        return Mathf.Min(Mathf.Min(dx, dy), (dx + dy - corner) * .7071f);
    }
}
