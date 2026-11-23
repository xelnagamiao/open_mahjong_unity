using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>Import the three standard card faces at the same resolution on every client.</summary>
public sealed class BuiltinTileFaceImporter : AssetPostprocessor
{
    internal const int MaxEdge = TilePackIds.MaxFaceTextureEdge;
    internal static readonly string[] Packs = { TilePackIds.PackOfficial, TilePackIds.PackFluffy, TilePackIds.PackHkMahjong };
    private static readonly string[] Platforms = { "Standalone", "WebGL", "Android", "iPhone" };

    internal static string Folder(string pack) => BuiltinTileSprites.SourceRoot + pack + "/shared";

    public override uint GetVersion() => 2;

    private void OnPreprocessTexture()
    {
        if (!assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return;
        foreach (string pack in Packs)
        {
            if (!assetPath.StartsWith(Folder(pack) + "/", StringComparison.Ordinal)) continue;
            Configure((TextureImporter)assetImporter);
            return;
        }
    }

    // A rectangle around all nontransparent pixels permits rectangular UV sampling in 3D.
    // Keep the logical sprite rect and pivot so Unity UI restores its transparent margins.
    private void OnPostprocessSprites(Texture2D texture, Sprite[] sprites)
    {
        if (sprites.Length != 1 || !Array.Exists(Packs, pack => assetPath.StartsWith(Folder(pack) + "/", StringComparison.Ordinal))) return;
        var sprite = sprites[0];
        var pixels = texture.GetPixels32();
        int w = texture.width, h = texture.height, left = w, right = 0, bottom = h, top = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (pixels[y * w + x].a != 0) {
                    left = Mathf.Min(left, x); right = Mathf.Max(right, x + 1);
                    bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y + 1);
                }
        // Unity exposes the full UV rect for an all-transparent sprite. Reserve that rect so UI cannot sample a neighbour.
        if (right == 0) { left = bottom = 0; right = w; top = h; }
        Vector2 Vertex(int x, int y) => new Vector2(x, y);
        sprite.OverrideGeometry(new[] { Vertex(left,bottom), Vertex(left,top), Vertex(right,top), Vertex(right,bottom) },
            new ushort[] { 0,1,2, 0,2,3 });
    }

    private static void Configure(TextureImporter importer)
    {
        // New PNGs must also produce one named Sprite with the full authored canvas for the 3D UVs.
        if (importer.textureType != TextureImporterType.Sprite)
            importer.textureType = TextureImporterType.Sprite;
        if (importer.spriteImportMode != SpriteImportMode.Single)
            importer.spriteImportMode = SpriteImportMode.Single;
        var sprite = new TextureImporterSettings();
        importer.ReadTextureSettings(sprite);
        if (sprite.spriteMeshType != SpriteMeshType.Tight || sprite.spriteExtrude != 0)
        {
            sprite.spriteMeshType = SpriteMeshType.Tight;
            sprite.spriteExtrude = 0;
            importer.SetTextureSettings(sprite);
        }

        // The atlas supplies the final GPU compression. Keep the source uncompressed while packing.
        var defaults = importer.GetDefaultPlatformTextureSettings();
        if (defaults.maxTextureSize != MaxEdge || defaults.textureCompression != TextureImporterCompression.Uncompressed
            || defaults.format != TextureImporterFormat.Automatic || defaults.crunchedCompression)
        {
            defaults.maxTextureSize = MaxEdge;
            defaults.textureCompression = TextureImporterCompression.Uncompressed;
            defaults.format = TextureImporterFormat.Automatic;
            defaults.crunchedCompression = false;
            importer.SetPlatformTextureSettings(defaults);
        }
        foreach (string platform in Platforms)
        {
            var settings = importer.GetPlatformTextureSettings(platform);
            if (!settings.overridden || settings.maxTextureSize == MaxEdge) continue;
            settings.maxTextureSize = MaxEdge;
            importer.SetPlatformTextureSettings(settings);
        }
    }

    internal static void Validate(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null || importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single)
            throw new BuildFailedException("Built-in face must import as a single Sprite: " + path);
        var sprite = new TextureImporterSettings();
        importer.ReadTextureSettings(sprite);
        if (sprite.spriteMeshType != SpriteMeshType.Tight || sprite.spriteExtrude != 0)
            throw new BuildFailedException("Built-in face must use the trimmed rectangular mesh: " + path);
        if (importer.GetDefaultPlatformTextureSettings().maxTextureSize != MaxEdge)
            throw new BuildFailedException($"Default face import size must be {MaxEdge}: {path}");
        foreach (string platform in Platforms)
        {
            var settings = importer.GetPlatformTextureSettings(platform);
            if (settings.overridden && settings.maxTextureSize != MaxEdge)
                throw new BuildFailedException($"{platform} face import size must be {MaxEdge}: {path}");
        }
    }
}
