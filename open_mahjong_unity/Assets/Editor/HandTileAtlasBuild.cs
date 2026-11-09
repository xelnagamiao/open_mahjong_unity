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

/// <summary>Compresses shared standard faces and Hongque hands while preserving original sprite geometry.</summary>
public sealed class HandTileAtlasBuild : IPreprocessBuildWithReport
{
    private const string Root = "Assets/Resources/image/Cards/Faces/";
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        EnsurePack("official");
        EnsurePack("fluffy");
        EnsurePack("hkmahjong");
        EnsurePack("hongque");
    }

    public static string EnsurePack(string pack)
    {
        bool shared = TilePackIds.IsBuiltinPack(pack);
        if (EditorSettings.spritePackerMode != SpritePackerMode.SpriteAtlasV2)
            throw new BuildFailedException("Hand tile atlases require the project's existing Sprite Atlas V2 mode.");
        string folder = BuiltinTileSprites.SourceRoot + pack + (shared ? "/shared" : "/hand");
        string path = Root + pack + (shared ? "/SharedAtlas.spriteatlasv2" : "/HandAtlas.spriteatlasv2");
        int maxSize = shared ? 4096 : 2048;
        string[] sources = Directory.GetFiles(folder, "*.png").OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (shared) {
            // The 3D renderer maps the complete authored canvas. Tight Sprite geometry would
            // trim transparent margins even when atlas tight packing itself is disabled.
            foreach (string source in sources) {
                var sourceImporter = AssetImporter.GetAtPath(source.Replace('\\', '/')) as TextureImporter;
                if (sourceImporter == null) continue;
                var settings = new TextureImporterSettings();
                sourceImporter.ReadTextureSettings(settings);
                bool changed = settings.spriteMeshType != SpriteMeshType.FullRect;
                if (changed) {
                    settings.spriteMeshType = SpriteMeshType.FullRect;
                    sourceImporter.SetTextureSettings(settings);
                }
                // Archive the full PNG, but keep Windows runtime faces near the former
                // 400 x 532 table images. Atlas packing performs the final BC7 compression.
                var windowsSource = sourceImporter.GetPlatformTextureSettings("Standalone");
                if (!windowsSource.overridden || windowsSource.maxTextureSize != 512
                    || windowsSource.format != TextureImporterFormat.Automatic
                    || windowsSource.textureCompression != TextureImporterCompression.Uncompressed
                    || windowsSource.crunchedCompression) {
                    windowsSource.name = "Standalone";
                    windowsSource.overridden = true;
                    windowsSource.maxTextureSize = 512;
                    windowsSource.format = TextureImporterFormat.Automatic;
                    windowsSource.textureCompression = TextureImporterCompression.Uncompressed;
                    windowsSource.crunchedCompression = false;
                    sourceImporter.SetPlatformTextureSettings(windowsSource);
                    changed = true;
                }
                if (changed) sourceImporter.SaveAndReimport();
            }
        }
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
        bool atlasChanged = !importer.includeInBuild;
        if (atlasChanged) importer.includeInBuild = true;
        var packing = importer.packingSettings;
        if (packing.enableRotation || packing.enableTightPacking || packing.padding != 8
            || packing.enableAlphaDilation != (pack != "hongque")) {
            packing.enableRotation = false;
            packing.enableTightPacking = false;
            packing.padding = 8;
            packing.enableAlphaDilation = pack != "hongque";
            importer.packingSettings = packing;
            atlasChanged = true;
        }
        var texture = importer.textureSettings;
        if (!texture.generateMipMaps || texture.readable || !texture.sRGB
            || texture.filterMode != FilterMode.Bilinear || texture.anisoLevel != 16) {
            texture.generateMipMaps = true;
            texture.readable = false;
            texture.sRGB = true;
            texture.filterMode = FilterMode.Bilinear;
            texture.anisoLevel = 16;
            importer.textureSettings = texture;
            atlasChanged = true;
        }
        // Highest-quality block formats; the source PNGs remain intact, and Windows
        // uses its source import limit instead of shipping full authoring resolution.
        atlasChanged |= ConfigurePlatform(importer, "DefaultTexturePlatform", maxSize, TextureImporterFormat.Automatic);
        atlasChanged |= ConfigurePlatform(importer, "Standalone", shared ? 2048 : maxSize, TextureImporterFormat.BC7, true, true);
        foreach (string platform in new[] { "Android", "iPhone" })
            atlasChanged |= ConfigurePlatform(importer, platform, maxSize, TextureImporterFormat.ASTC_4x4, true);
        if (atlasChanged) importer.SaveAndReimport();
        return path;
    }

    private static bool ConfigurePlatform(SpriteAtlasImporter importer, string platform, int maxSize,
        TextureImporterFormat format, bool? overridden = null, bool useMaximumQuality = false)
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
