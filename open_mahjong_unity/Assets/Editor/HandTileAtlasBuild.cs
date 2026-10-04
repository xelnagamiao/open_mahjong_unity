// [UNITY-SKILL:SPRITEATLAS]
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.U2D;
using UnityEngine;

/// <summary>Keeps each built-in hand pack independent while preserving original sprite geometry.</summary>
public sealed class HandTileAtlasBuild : IPreprocessBuildWithReport
{
    private const string Root = "Assets/Resources/image/Cards/Faces/";
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        foreach (string pack in new[] { "official", "fluffy", "hkmahjong", "hongque" }) EnsurePack(pack);
    }

    public static string EnsurePack(string pack)
    {
        if (EditorSettings.spritePackerMode != SpritePackerMode.SpriteAtlasV2)
            throw new BuildFailedException("Hand tile atlases require the project's existing Sprite Atlas V2 mode.");
        string folder = Root + pack + "/hand";
        string path = Root + pack + "/HandAtlas.spriteatlasv2";
        string[] sources = Directory.GetFiles(folder, "*.png").OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Sprite[] sprites = sources.Select(p => AssetDatabase.LoadAssetAtPath<Sprite>(p.Replace('\\', '/'))).ToArray();
        if (sprites.Length == 0 || sprites.Any(s => s == null))
            throw new BuildFailedException("Hand pack contains an image without a Sprite: " + pack);
        // Folder membership updates automatically when a pack gains new faces.
        if (!File.Exists(path))
        {
            var atlas = new SpriteAtlasAsset();
            atlas.Add(new UnityEngine.Object[] { AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder) });
            SpriteAtlasAsset.Save(atlas, path);
            AssetDatabase.ImportAsset(path);
        }
        var importer = AssetImporter.GetAtPath(path) as SpriteAtlasImporter;
        if (importer == null) throw new BuildFailedException("Cannot load hand atlas importer: " + path);
        importer.includeInBuild = true;
        var packing = importer.packingSettings;
        packing.enableRotation = false;
        packing.enableTightPacking = false;
        packing.padding = pack == "hongque" ? 8 : 4;
        packing.enableAlphaDilation = pack != "hongque";
        importer.packingSettings = packing;
        var texture = importer.textureSettings;
        texture.generateMipMaps = pack == "hongque";
        texture.readable = false;
        texture.sRGB = true;
        texture.filterMode = FilterMode.Bilinear;
        texture.anisoLevel = pack == "hongque" ? 16 : 1;
        importer.textureSettings = texture;
        // Full-resolution, highest-quality block formats; never use Crunch or downscale.
        var defaults = importer.GetPlatformSettings("DefaultTexturePlatform");
        defaults.name = "DefaultTexturePlatform";
        defaults.maxTextureSize = 2048;
        defaults.format = TextureImporterFormat.Automatic;
        defaults.textureCompression = TextureImporterCompression.CompressedHQ;
        defaults.compressionQuality = 100;
        defaults.crunchedCompression = false;
        importer.SetPlatformSettings(defaults);
        var desktop = importer.GetPlatformSettings("Standalone");
        desktop.name = "Standalone";
        desktop.overridden = true;
        desktop.maxTextureSize = 2048;
        desktop.format = TextureImporterFormat.BC7;
        desktop.textureCompression = TextureImporterCompression.CompressedHQ;
        desktop.compressionQuality = 100;
        desktop.crunchedCompression = false;
        var maximumQuality = typeof(TextureImporterPlatformSettings).GetProperty(
            "forceMaximumCompressionQuality_BC6H_BC7", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        maximumQuality?.SetValue(desktop, 1);
        importer.SetPlatformSettings(desktop);
        foreach (string platform in new[] { "Android", "iPhone" })
        {
            var mobile = importer.GetPlatformSettings(platform);
            mobile.name = platform;
            mobile.overridden = true;
            mobile.maxTextureSize = 2048;
            mobile.format = TextureImporterFormat.ASTC_4x4;
            mobile.textureCompression = TextureImporterCompression.CompressedHQ;
            mobile.compressionQuality = 100;
            mobile.crunchedCompression = false;
            importer.SetPlatformSettings(mobile);
        }
        importer.SaveAndReimport();
        return path;
    }
}
