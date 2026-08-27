using UnityEngine;

/// <summary>
/// 把 3D 牌面图宽度略拉宽、高度拉到接近官方牌顶高度后居中贴进 220×366。
/// </summary>
public static class TileTableFaceBake {
    public const int Width = 220;
    public const int Height = 366;
    public const float FitScaleX = 0.94f;
    public const float FitScaleY = 0.90f;
    public static readonly Color32 CanvasColor = new Color32(245, 246, 247, 255);

    public static byte[] ProcessPng(byte[] png) {
        if (png == null || png.Length == 0) {
            return png;
        }
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(source, png, false)) {
            Object.Destroy(source);
            return png;
        }
        Texture2D baked = Process(source);
        Object.Destroy(source);
        if (baked == null) {
            return png;
        }
        byte[] encoded = baked.EncodeToPNG();
        Object.Destroy(baked);
        return encoded != null && encoded.Length > 0 ? encoded : png;
    }

    public static Texture2D Process(Texture2D source) {
        if (source == null || source.width <= 0 || source.height <= 0) {
            return null;
        }

        int innerW = Mathf.Max(1, Mathf.Min(Width, Mathf.RoundToInt(Width * FitScaleX)));
        int innerH = Mathf.Max(1, Mathf.Min(Height, Mathf.RoundToInt(Height * FitScaleY)));
        int originX = (Width - innerW) / 2;
        int originY = (Height - innerH) / 2;

        var dest = new Texture2D(Width, Height, TextureFormat.RGBA32, false) {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "TableFaceBaked"
        };
        Color32[] pixels = new Color32[Width * Height];
        for (int i = 0; i < pixels.Length; i++) {
            pixels[i] = CanvasColor;
        }

        float invSrcW = 1f / source.width;
        float invSrcH = 1f / source.height;
        for (int y = 0; y < innerH; y++) {
            float v = (y + 0.5f) / innerH;
            float srcY = v * source.height;
            for (int x = 0; x < innerW; x++) {
                float u = (x + 0.5f) / innerW;
                float srcX = u * source.width;
                Color sampled = source.GetPixelBilinear(srcX * invSrcW, srcY * invSrcH);
                Color32 packed = sampled;
                if (sampled.a < 0.995f) {
                    packed = Color32.Lerp(CanvasColor, packed, sampled.a);
                    packed.a = 255;
                }
                pixels[(originY + y) * Width + originX + x] = packed;
            }
        }
        dest.SetPixels32(pixels);
        dest.Apply(false, false);
        return dest;
    }
}
