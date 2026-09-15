using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Editor-only derivatives. Runtime selectors load the small catalog and previews,
/// never the original table textures. Source files and their importers are untouched.
/// </summary>
[InitializeOnLoad]
public static class TableSurfacePreviewBuilder
{
    public const string PreviewRoot = "Assets/Resources/TableSurfacePreviews";
    public const int PreviewSize = 256;
    internal const string FingerprintPrefix = "TableSurfacePreview:v1:256:";
    const string SourceRoot = "Assets/Resources/image/Board";
    const string ShaderPath = "Assets/Editor/TableSurfacePreviewResize.shader";
    static readonly string[] Categories = { "TableCloth", "Edge", "TableSeams" };
    static bool queued;
    static bool building;

    [Serializable]
    public sealed class Entry
    {
        public string name;
        public string preview;
        public string displayName;
    }

    [Serializable]
    public sealed class Catalog
    {
        public Entry[] cloth;
        public Entry[] edge;
        public Entry[] seams;
    }

    sealed class Source
    {
        public string path;
        public string category;
        public string stem;
        public string output;
    }

    static TableSurfacePreviewBuilder()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && queued) Schedule();
        };
        QueueUpdate();
    }

    [MenuItem("Tools/Mahjong/Table Surfaces/Rebuild 256px Previews")]
    public static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            QueueUpdate();
            Debug.Log("Table surface preview update queued until Edit mode.");
            return;
        }
        Build();
    }

    internal static bool IsSourcePath(string path)
    {
        path = path.Replace('\\', '/').TrimEnd('/');
        if (path.StartsWith("Assets/Resources/TableFrame/", StringComparison.Ordinal) ||
            path.StartsWith("Assets/TableFrame/SceneAssets/", StringComparison.Ordinal)) return true;
        if (path == SourceRoot) return true;
        return Categories.Any(category => path == SourceRoot + "/" + category ||
            path.StartsWith(SourceRoot + "/" + category + "/", StringComparison.Ordinal));
    }

    internal static bool IsPreviewPath(string path)
    {
        return Categories.Any(category => path.StartsWith(PreviewRoot + "/" + category + "/", StringComparison.Ordinal)) &&
            path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    }

    internal static void QueueUpdate()
    {
        if (building) return;
        queued = true;
        Schedule();
    }

    static void Schedule()
    {
        EditorApplication.delayCall -= RunQueued;
        EditorApplication.delayCall += RunQueued;
    }

    static void RunQueued()
    {
        if (!queued || building) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            Schedule();
            return;
        }
        try { Build(); }
        catch (Exception error) { Debug.LogException(error); }
    }

    static void Build()
    {
        if (building) return;
        building = true;
        queued = false;
        try
        {
            var sources = new List<Source>();
            foreach (string category in Categories)
            {
                if (category == "Edge") continue;
                string directory = SourceRoot + "/" + category;
                if (!Directory.Exists(directory)) continue;
                // Built-in table surfaces are PNG; JPEG additions are supported too.
                foreach (string file in Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly)
                    .Where(IsSupportedImage).OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.Ordinal))
                {
                    string stem = Path.GetFileNameWithoutExtension(file);
                    if (category == "TableCloth" && TableClothStyles.IsSolid(stem)) continue;
                    sources.Add(new Source { path = file.Replace('\\', '/'), category = category,
                        stem = stem, output = PreviewRoot + "/" + category + "/" + stem + ".png" });
                }
            }
            foreach (string name in TableClothStyles.SolidNames)
                sources.Add(new Source { category = "TableCloth", stem = name });
            foreach (string name in TableFrameStyles.OrderedNames)
                sources.Add(new Source { category = "Edge", stem = name, path = TableFrameStyles.IsSolid(name) ? null :
                    "Assets/Resources/" + TableFrameRenderer.BaseResourceDirectory + TableFrameStyles.SourceName(name) + ".png",
                    output = PreviewRoot + "/Edge/" + name + ".png" });
            if (sources.Where(source => source.output != null).GroupBy(source => source.output, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
                throw new InvalidOperationException("Table preview source names must be unique within each category.");

            EnsureFolder(PreviewRoot);
            foreach (string category in Categories) EnsureFolder(PreviewRoot + "/" + category);

            int encoded = 0;
            foreach (Source source in sources)
            {
                if (source.path == null && source.category != "Edge") continue;
                string fingerprint = source.category == "Edge" ? FingerprintPrefix + TableFramePreviewRenderer.Fingerprint(source.stem, source.path)
                    : FingerprintPrefix + HashFile(source.path);
                Vector4 tone = source.category == "Edge" ? TableFrameStyles.BaseTone(source.stem) : Vector4.one;
                if (source.category == "Edge" && source.stem == TableFrameStyles.Deep)
                    fingerprint += ":tone-v1:" + string.Join(",", new[] { tone.x, tone.y, tone.z, tone.w }
                        .Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
                var importer = AssetImporter.GetAtPath(source.output) as TextureImporter;
                // A source importer/meta-only change does not alter the image bytes.
                // The committed offline derivatives carry this same fingerprint.
                if (File.Exists(source.output) && importer != null && importer.userData == fingerprint) continue;
                byte[] png = source.category == "Edge" ? TableFramePreviewRenderer.Render(source.stem, source.path, PreviewSize)
                    : ResizeOnGpu(source.path, tone);
                if (!File.Exists(source.output) || !File.ReadAllBytes(source.output).SequenceEqual(png))
                    File.WriteAllBytes(source.output, png);
                AssetDatabase.ImportAsset(source.output, ImportAssetOptions.ForceUpdate);
                importer = AssetImporter.GetAtPath(source.output) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Missing preview importer: " + source.output);
                Configure(importer);
                importer.userData = fingerprint;
                importer.SaveAndReimport();
                encoded++;
            }

            var expected = new HashSet<string>(sources.Select(source => source.output), StringComparer.Ordinal);
            int removed = 0;
            foreach (string category in Categories)
            foreach (string file in Directory.GetFiles(PreviewRoot + "/" + category, "*.png", SearchOption.TopDirectoryOnly))
            {
                string path = file.Replace('\\', '/');
                if (expected.Contains(path)) continue;
                var importer = AssetImporter.GetAtPath(path);
                // Delete only derivatives owned by this builder, never unrelated files.
                if (importer != null && importer.userData.StartsWith(FingerprintPrefix, StringComparison.Ordinal))
                {
                    if (!AssetDatabase.DeleteAsset(path)) throw new IOException("Could not remove stale preview: " + path);
                    removed++;
                }
            }

            Func<string, Entry[]> entries = category => sources.Where(source => source.category == category)
                .OrderBy(source => category == "TableCloth" ? TableSurfaceNames.ClothSortOrder(source.stem) :
                    category == "Edge" ? TableFrameStyles.SortOrder(source.stem) : int.MaxValue)
                .ThenBy(source => source.stem, StringComparer.Ordinal)
                .Select(source => new Entry { name = source.stem,
                    preview = source.output == null ? "" : "TableSurfacePreviews/" + category + "/" + source.stem,
                    displayName = category == "TableCloth" ? TableSurfaceNames.ClothDisplayName(source.stem) :
                        category == "Edge" ? TableFrameStyles.DisplayName(source.stem) : source.stem }).ToArray();
            string json = JsonUtility.ToJson(new Catalog { cloth = entries("TableCloth"), edge = entries("Edge"), seams = entries("TableSeams") }, true) + "\n";
            string catalogPath = PreviewRoot + "/catalog.json";
            if (!File.Exists(catalogPath) || File.ReadAllText(catalogPath) != json)
            {
                File.WriteAllText(catalogPath, json, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(catalogPath, ImportAssetOptions.ForceUpdate);
            }
            if (encoded > 0 || removed > 0)
                Debug.Log($"Table surface previews: {sources.Count} entries, {encoded} encoded, {removed} stale derivatives removed.");
        }
        finally { building = false; }
    }

    static bool IsSupportedImage(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    static string HashFile(string path)
    {
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        // The files may have been installed while Unity was in the background.
        // CreateFolder in that state silently creates a second folder with " 1".
        if (Directory.Exists(path))
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return;
        }
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    internal static void Configure(TextureImporter importer)
    {
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.mipmapEnabled = false;
        importer.streamingMipmaps = false;
        importer.isReadable = false;
        importer.maxTextureSize = PreviewSize;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.anisoLevel = 0;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.crunchedCompression = false;
        var settings = importer.GetDefaultPlatformTextureSettings();
        settings.maxTextureSize = PreviewSize;
        settings.format = TextureImporterFormat.RGBA32;
        settings.textureCompression = TextureImporterCompression.Uncompressed;
        settings.crunchedCompression = false;
        settings.allowsAlphaSplitting = false;
        importer.SetPlatformTextureSettings(settings);
        foreach (string target in new[] { "Standalone", "Android", "iPhone", "WebGL" })
        {
            settings = importer.GetPlatformTextureSettings(target);
            settings.name = target;
            settings.overridden = true;
            settings.maxTextureSize = PreviewSize;
            settings.format = TextureImporterFormat.RGBA32;
            settings.textureCompression = TextureImporterCompression.Uncompressed;
            settings.crunchedCompression = false;
            settings.allowsAlphaSplitting = false;
            importer.SetPlatformTextureSettings(settings);
        }
    }

    static byte[] ResizeOnGpu(string sourcePath, Vector4 tone)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("Updating table previews requires an Editor graphics device; omit -nographics.");
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null || !shader.isSupported) throw new InvalidOperationException("Preview resize shader unavailable: " + ShaderPath);
        var temporary = new List<RenderTexture>();
        Texture2D source = null;
        Texture2D result = null;
        Material material = null;
        RenderTexture previousActive = RenderTexture.active;
        bool previousSrgb = GL.sRGBWrite;
        try
        {
            // Decode independent raw bytes as encoded-color data, leaving source importer
            // settings intact. No gamma transform is applied twice during PNG export.
            source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!ImageConversion.LoadImage(source, File.ReadAllBytes(sourcePath), false))
                throw new IOException("Could not decode table texture: " + sourcePath);
            source.filterMode = FilterMode.Bilinear;
            source.wrapMode = TextureWrapMode.Clamp;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetVector("_PreviewTone", tone);
            Func<int, int, RenderTexture> allocate = (width, height) =>
            {
                var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                rt.filterMode = FilterMode.Bilinear;
                rt.wrapMode = TextureWrapMode.Clamp;
                temporary.Add(rt);
                return rt;
            };
            GL.sRGBWrite = false;
            RenderTexture current = allocate(source.width, source.height);
            Graphics.Blit(source, current, material, 0); // Premultiply before filtering.
            while (current.width > PreviewSize || current.height > PreviewSize)
            {
                RenderTexture smaller = allocate(Math.Max(PreviewSize, current.width / 2), Math.Max(PreviewSize, current.height / 2));
                Graphics.Blit(current, smaller, material, 1);
                current = smaller;
            }
            RenderTexture output = allocate(PreviewSize, PreviewSize);
            Graphics.Blit(current, output, material, 2); // Restore straight RGBA.
            RenderTexture.active = output;
            result = new Texture2D(PreviewSize, PreviewSize, TextureFormat.RGBA32, false, true);
            result.ReadPixels(new Rect(0, 0, PreviewSize, PreviewSize), 0, 0, false);
            result.Apply(false, false);
            return ImageConversion.EncodeToPNG(result);
        }
        finally
        {
            RenderTexture.active = previousActive;
            GL.sRGBWrite = previousSrgb;
            foreach (RenderTexture rt in temporary) RenderTexture.ReleaseTemporary(rt);
            if (source != null) UnityEngine.Object.DestroyImmediate(source);
            if (result != null) UnityEngine.Object.DestroyImmediate(result);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
        }
    }
}

sealed class TableSurfacePreviewPostprocessor : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (TableSurfacePreviewBuilder.IsPreviewPath(assetPath))
            TableSurfacePreviewBuilder.Configure((TextureImporter)assetImporter);
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(TableSurfacePreviewBuilder.IsSourcePath))
            TableSurfacePreviewBuilder.QueueUpdate();
    }
}
