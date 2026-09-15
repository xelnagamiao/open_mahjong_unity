using System;
using UnityEditor;
using UnityEngine;

/// <summary>Compression policy for GPU-only table surfaces and normalized-UV Hongque 3D faces.</summary>
[InitializeOnLoad]
public static class TableSurfaceCompressionPolicy
{
    const string Board = "Assets/Resources/image/Board/";
    const string HongqueTable = "Assets/Resources/image/Cards/Faces/hongque/table/";
    static readonly string[] Roots = { Board.TrimEnd('/'), HongqueTable.TrimEnd('/') };
    static bool pending = true;

    static TableSurfaceCompressionPolicy()
    {
        EditorApplication.delayCall += ApplyWhenReady;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && pending)
                EditorApplication.delayCall += ApplyWhenReady;
        };
    }

    public static bool IsTarget(string path)
    {
        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return false;
        return path.StartsWith(Board + "TableCloth/", StringComparison.Ordinal)
            || path.StartsWith(Board + "TableSeams/", StringComparison.Ordinal)
            || path.StartsWith(Board + "Edge/", StringComparison.Ordinal)
            || path == Board + "TableLighting/OriginalLight.png"
            || path.StartsWith(HongqueTable, StringComparison.Ordinal);
    }

    public static bool Configure(TextureImporter importer)
    {
        bool changed = false;
        if (importer.isReadable) { importer.isReadable = false; changed = true; }
        if (importer.textureCompression != TextureImporterCompression.CompressedHQ)
        { importer.textureCompression = TextureImporterCompression.CompressedHQ; changed = true; }
        if (importer.compressionQuality != 100) { importer.compressionQuality = 100; changed = true; }
        // 3D faces use normalized UVs, not native-size UI sprites. Keep all source detail.
        // Do NOT apply this to hand: Sprite.Create + preserveAspect needs its original aspect.
        if (importer.assetPath.StartsWith(HongqueTable, StringComparison.Ordinal)
            && importer.npotScale != TextureImporterNPOTScale.ToLarger)
        { importer.npotScale = TextureImporterNPOTScale.ToLarger; changed = true; }

        changed |= ConfigurePlatform(importer, "DefaultTexturePlatform", TextureImporterFormat.Automatic, false);
        changed |= ConfigurePlatform(importer, "Standalone", TextureImporterFormat.BC7, true);
        changed |= ConfigurePlatform(importer, "Android", TextureImporterFormat.ASTC_4x4, true);
        // Remove old RGBA32 overrides without forcing desktop formats on browser/mobile builds.
        changed |= ConfigurePlatform(importer, "WebGL", TextureImporterFormat.Automatic, false);
        changed |= ConfigurePlatform(importer, "iPhone", TextureImporterFormat.Automatic, false);
        changed |= ConfigurePlatform(importer, "iOS", TextureImporterFormat.Automatic, false);
        return changed;
    }

    static bool ConfigurePlatform(TextureImporter importer, string name, TextureImporterFormat format, bool overridden)
    {
        var settings = importer.GetPlatformTextureSettings(name);
        if (settings.format == format && settings.overridden == overridden
            && settings.textureCompression == TextureImporterCompression.CompressedHQ
            && settings.compressionQuality == 100 && !settings.crunchedCompression) return false;
        settings.name = name;
        settings.format = format;
        settings.overridden = overridden;
        settings.textureCompression = TextureImporterCompression.CompressedHQ;
        settings.compressionQuality = 100;
        settings.crunchedCompression = false;
        importer.SetPlatformTextureSettings(settings);
        return true;
    }

    [MenuItem("Tools/Mahjong/Table Surfaces/Apply Texture Compression")]
    public static void Apply()
    {
        pending = true;
        ApplyWhenReady();
    }

    static void ApplyWhenReady()
    {
        if (!pending || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += ApplyWhenReady;
            return;
        }
        pending = false;
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", Roots))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!IsTarget(path)) continue;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || !Configure(importer)) continue;
            importer.SaveAndReimport();
            count++;
        }
        if (count > 0) Debug.Log($"Table texture compression: {count} importers updated (BC7 / ASTC 4x4).");
    }
}
