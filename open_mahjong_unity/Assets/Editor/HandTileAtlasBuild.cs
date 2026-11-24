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

/// <summary>Checks imported standard faces and prepares the separate Hongque hand atlas.</summary>
public sealed class HandTileAtlasBuild : IPreprocessBuildWithReport
{
    private const string Root = "Assets/Resources/image/Cards/Faces/";
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        // Both bootstrap Players have already validated and packed their atlases.
        if (BundledGameBuild.BuildingBootstrap) return;
        ValidateSharedPacks();
        EnsurePack("hongque");
    }

    public static void ValidateSharedPacks()
    {
        foreach (string pack in BuiltinTileFaceImporter.Packs)
            EnsurePack(pack);
    }

    public static string EnsurePack(string pack)
    {
        bool shared = TilePackIds.IsBuiltinPack(pack);
        if (EditorSettings.spritePackerMode != SpritePackerMode.SpriteAtlasV2)
            throw new BuildFailedException("Hand tile atlases require the project's existing Sprite Atlas V2 mode.");
        string folder = shared ? BuiltinTileFaceImporter.Folder(pack) : BuiltinTileSprites.SourceRoot + pack + "/hand";
        string path = Root + pack + (shared ? "/" + BuiltinTileSprites.StandardAtlasName(pack) + ".spriteatlasv2" : "/HandAtlas.spriteatlasv2");
        int maxSize = pack == TilePackIds.PackOfficial ? 4096 : pack == TilePackIds.PackHkMahjong ? 1024 : 2048;
        if (!Directory.Exists(folder)) throw new BuildFailedException("Missing face directory: " + folder);
        string[] sources = Directory.GetFiles(folder, "*", shared ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (sources.Length == 0) throw new BuildFailedException("Empty face directory: " + folder);
        foreach (string source in sources)
        {
            string assetPath = source.Replace('\\', '/');
            if (shared) BuiltinTileFaceImporter.Validate(assetPath);
            if (AssetDatabase.LoadAssetAtPath<Sprite>(assetPath) == null)
                throw new BuildFailedException("Face image did not import as a Sprite: " + assetPath);
        }
        // Folder membership updates automatically when a pack gains new faces.
        if (!File.Exists(path))
        {
            if (shared) throw new BuildFailedException("Missing shared face atlas: " + path);
            var atlas = new SpriteAtlasAsset();
            atlas.Add(new UnityEngine.Object[] { AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder) });
            SpriteAtlasAsset.Save(atlas, path);
            AssetDatabase.ImportAsset(path);
        }
        var importer = AssetImporter.GetAtPath(path) as SpriteAtlasImporter;
        if (importer == null) throw new BuildFailedException("Cannot load hand atlas importer: " + path);
        var packing = importer.packingSettings;
        var texture = importer.textureSettings;
        // Only the standard atlases are shared with 3D. Hongque has separate table textures.
        FilterMode faceFilter = shared ? FilterMode.Trilinear : FilterMode.Bilinear;
        bool packingChanged = packing.enableRotation || packing.enableTightPacking != shared || packing.padding != 8
            || packing.enableAlphaDilation != (pack != "hongque");
        bool textureChanged = texture.generateMipMaps != shared || texture.readable || !texture.sRGB
            || texture.filterMode != faceFilter || texture.anisoLevel != 16;
        if (shared)
        {
            if (!importer.includeInBuild || packingChanged || textureChanged)
                throw new BuildFailedException("Shared atlas inclusion, packing or texture settings differ from the card policy: " + path);
            CheckAtlasPlatforms(importer, maxSize, true);
            return path;
        }

        bool atlasChanged = !importer.includeInBuild;
        if (atlasChanged) importer.includeInBuild = true;
        if (packingChanged) {
            packing.enableRotation = false;
            packing.enableTightPacking = false;
            packing.padding = 8;
            packing.enableAlphaDilation = pack != "hongque";
            importer.packingSettings = packing;
            atlasChanged = true;
        }
        if (textureChanged) {
            texture.generateMipMaps = shared;
            texture.readable = false;
            texture.sRGB = true;
            texture.filterMode = faceFilter;
            texture.anisoLevel = 16;
            importer.textureSettings = texture;
            atlasChanged = true;
        }
        atlasChanged |= CheckAtlasPlatforms(importer, maxSize, false);
        if (atlasChanged) importer.SaveAndReimport();
        return path;
    }

    private static bool CheckAtlasPlatforms(SpriteAtlasImporter importer, int maxSize, bool validateOnly)
    {
        bool changed = CheckPlatform(importer, "DefaultTexturePlatform", maxSize, TextureImporterFormat.Automatic, validateOnly);
        changed |= CheckPlatform(importer, "Standalone", Math.Min(2048, maxSize), TextureImporterFormat.BC7, validateOnly, true, true);
        foreach (string platform in new[] { "Android", "iPhone" })
            changed |= CheckPlatform(importer, platform, maxSize, TextureImporterFormat.ASTC_4x4, validateOnly, true);
        return changed;
    }

    private static bool CheckPlatform(SpriteAtlasImporter importer, string platform, int maxSize,
        TextureImporterFormat format, bool validateOnly, bool? overridden = null, bool useMaximumQuality = false)
    {
        var settings = importer.GetPlatformSettings(platform);
        var maximumQuality = typeof(TextureImporterPlatformSettings).GetProperty(
            "forceMaximumCompressionQuality_BC6H_BC7", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        bool qualityChanged = useMaximumQuality && maximumQuality != null
            && !Equals(maximumQuality.GetValue(settings), 1);
        // Unity normalizes aliases such as "iPhone" to "iOS" when reading them back.
        // GetPlatformSettings already selects the platform; compare its effective settings.
        if ((!overridden.HasValue || settings.overridden == overridden.Value)
            && settings.maxTextureSize == maxSize && settings.format == format
            && settings.textureCompression == TextureImporterCompression.CompressedHQ
            && settings.compressionQuality == 100 && !settings.crunchedCompression && !qualityChanged)
            return false;
        if (validateOnly)
            throw new BuildFailedException($"Shared atlas {platform} settings must use {format}, max {maxSize}, high quality: {importer.assetPath}");
        settings.name = platform;
        if (overridden.HasValue) settings.overridden = overridden.Value;
        settings.maxTextureSize = maxSize;
        settings.format = format;
        settings.textureCompression = TextureImporterCompression.CompressedHQ;
        settings.compressionQuality = 100;
        settings.crunchedCompression = false;
        if (qualityChanged) maximumQuality.SetValue(settings, 1);
        importer.SetPlatformSettings(settings);
        return true;
    }
}
