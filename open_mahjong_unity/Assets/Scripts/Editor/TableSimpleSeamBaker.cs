#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Bakes flat, antialiased line masks in the existing table atlas UVs.</summary>
public static class TableSimpleSeamBaker
{
    private const int Size = 2048;
    private const float HalfExtent = 400f;
    private const float Radius = 28f;
    private const float Width = 2.4f;
    private const string Folder = "Assets/Resources/image/Board/TableSeams/";

    [MenuItem("Tools/Mahjong/Table/Rebuild Simple Seams")]
    public static void Bake()
    {
        for (int style = 7; style <= 10; style++)
        {
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                // Reflection symmetry keeps all four seats identical. RGB is
                // retained under transparent pixels to avoid dark filter fringes.
                float px = Mathf.Abs(x + .5f - Size * .5f);
                float py = Mathf.Abs(y + .5f - Size * .5f);
                float distance = RoundedDistance(px, py, HalfExtent, Radius);
                float alpha = Stroke(distance, Width);
                if (style == 8)
                {
                    float along = QuarterDistance(px, py);
                    float quarter = 2f * (HalfExtent - Radius) + Mathf.PI * Radius * .5f;
                    float period = quarter / 45f;
                    float phase = Mathf.Repeat(along + period * .5f, period) - period * .5f;
                    alpha *= Mathf.Clamp01(period * .32f + .5f - Mathf.Abs(phase));
                }
                else if (style == 9)
                {
                    float inner = RoundedDistance(px, py, HalfExtent - 7f, Radius - 7f);
                    alpha = Mathf.Max(Stroke(distance, 1.8f), Stroke(inner, 1.8f));
                }
                else if (style == 10)
                {
                    // Leave the long sides open; each corner retains an arc and
                    // short straight legs at precisely the same outer boundary.
                    alpha *= Mathf.Clamp01(Mathf.Min(px, py) - (HalfExtent - 108f) + .5f);
                }
                pixels[y * Size + x] = new Color32(224, 237, 241, (byte)Mathf.RoundToInt(alpha * 225f));
            }
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply();
                string path = Folder + "Seam_" + style.ToString("00") + ".png";
                File.WriteAllBytes(path, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.isReadable = false;
                importer.maxTextureSize = Size;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            finally { Object.DestroyImmediate(texture); }
        }
    }

    private static float RoundedDistance(float x, float y, float half, float radius)
    {
        float qx = x - (half - radius), qy = y - (half - radius);
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }

    private static float Stroke(float distance, float width) =>
        Mathf.Clamp01(width * .5f + .5f - Mathf.Abs(distance));

    // Arc length from the upper midpoint to the right midpoint. Mirroring this
    // quarter makes dash spacing continuous at the corners and side midpoints.
    private static float QuarterDistance(float x, float y)
    {
        float straight = HalfExtent - Radius;
        if (x <= straight) return x;
        if (y <= straight) return straight + Mathf.PI * Radius * .5f + straight - y;
        return straight + Radius * Mathf.Atan2(x - straight, y - straight);
    }
}
#endif
