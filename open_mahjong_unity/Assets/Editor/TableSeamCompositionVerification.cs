using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit Edit-mode GPU verification. No imports, Play, saves, or configuration writes.</summary>
[InitializeOnLoad]
public static class TableSeamCompositionVerification
{
    [Serializable] private class Catalog { public Entry[] cloth; }
    [Serializable] private class Entry { public string name, preview; }
    [Serializable] private class Check { public string name, detail; public bool passed; }
    [Serializable] private class Report
    {
        public string status, checkedAtUtc, unity, colorSpace, scope, error;
        public int passed, failed;
        public List<Check> checks = new List<Check>();
    }
    private static string Project => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    private static string Request => Path.Combine(Project, "Temp/TableSeamCompositionVerification.request");
    private static string Output => Path.GetFullPath(Path.Combine(Project, "../other/Tablecloth/SeamLibrary/validation/composition"));
    private const string MainScene = "Assets/Scenes/MainScene.unity";
    private static double nextPoll;
    private static bool running;
    private static Report report;
    static TableSeamCompositionVerification() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (running || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!File.Exists(Request) || File.ReadAllText(Request).Trim() != "VERIFY") return;
        File.Delete(Request);
        Run();
    }

    [MenuItem("Tools/Mahjong/Verify Independent Table Seam Composition")]
    public static void Run()
    {
        if (running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Run in Edit mode after imports and compilation finish.");
        running = true;
        Directory.CreateDirectory(Output);
        report = new Report
        {
            checkedAtUtc = DateTime.UtcNow.ToString("o"), unity = Application.unityVersion,
            colorSpace = QualitySettings.activeColorSpace.ToString(),
            scope = "Real GPU TableSeamComposer checks, production completion-path out-of-order injection, and table-only diagnostics using the serialized MainScene camera. " +
                "Preview scene only. No actual ResourceRequest races, gameplay, tile creation, selection callbacks, configuration writes, imports, saves, or Play mode."
        };
        var sceneState = SceneState();
        var activeHandle = SceneManager.GetActiveScene().handle;
        var selection = Selection.objects;
        string preferences = Preferences(), mainHash = Hash(Path.Combine(Project, MainScene));
        var activeTarget = RenderTexture.active;
        bool srgbWrite = GL.sRGBWrite;
        Scene preview = default;
        try
        {
            File.WriteAllText(Path.Combine(Output, "status.txt"), "RUNNING");
            var shader = Resources.Load<Shader>("Shaders/TableSeamComposite");
            Require(shader != null && shader.isSupported, "Production composition shader resolves and is supported");
            Require(!ShaderUtil.GetShaderMessages(shader).Any(m => m.severity.ToString() == "Error"), "Composition shader has no compiler errors");
            var bases = VerifyCatalog();
            var seams = VerifyOverlays();
            var cloth = Resources.Load<Texture2D>("image/Board/TableCloth/Tablecloth_blue");
            Require(cloth != null, "Original blue cloth resolves for numeric GPU comparison");
            VerifyTransparent(cloth);
            VerifySourceOver();
            VerifyComposition(cloth, seams);
            using (var composer = new TableSeamComposer())
                foreach (var source in bases)
                    Require(SamplingMatches(source, composer.Compose(source, seams[0]) as RenderTexture), source.name + ": composite matches native dimensions and sampling");
            preview = EditorSceneManager.OpenPreviewScene(MainScene);
            Require(preview.IsValid() && EditorSceneManager.IsPreviewScene(preview), "MainScene opened as isolated preview");
            VerifyCompletionPaths(preview, seams);
            CaptureTables(preview, seams);
        }
        catch (Exception error)
        {
            report.error = error.ToString();
            if (report.failed == 0) Add(false, "Verification exception", error.Message);
        }
        finally
        {
            try
            {
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                Add(RenderTexture.active == activeTarget && GL.sRGBWrite == srgbWrite, "Global render target and sRGB state preserved");
                RenderTexture.active = activeTarget; GL.sRGBWrite = srgbWrite;
                Add(SceneManager.GetActiveScene().handle == activeHandle && SceneState() == sceneState, "Open scenes, active scene, and dirty flags unchanged");
                Add(Selection.objects.SequenceEqual(selection), "Editor selection unchanged");
                Add(Preferences() == preferences, "Cloth, frame, and seam preferences unchanged");
                Add(Hash(Path.Combine(Project, MainScene)) == mainHash, "MainScene file SHA-256 unchanged");
            }
            catch (Exception error) { Add(false, "Preview cleanup", error.Message); report.error += "\n" + error; }
            report.status = report.failed == 0 ? "COMPLETE" : "FAILED";
            File.WriteAllText(Path.Combine(Output, "report.json"), JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(Output, "checks.txt"), report.checks.Select(c => (c.passed ? "PASS " : "FAIL ") + c.name + (c.detail == null ? "" : " | " + c.detail)), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(Output, "status.txt"), report.status + ": " + report.passed + " passed, " + report.failed + " failed.");
            running = false;
            Debug.Log("Table seam composition verification: " + report.status + ". " + Output);
        }
    }

    private static Texture2D[] VerifyCatalog()
    {
        var json = Resources.Load<TextAsset>("TableSurfacePreviews/catalog");
        Require(json != null, "Production preview catalog resolves");
        var catalog = JsonUtility.FromJson<Catalog>(json.text);
        Require(catalog?.cloth != null && catalog.cloth.Length == 13 && catalog.cloth.Select(c => c.name).Distinct().Count() == 13, "Catalog contains exactly 13 distinct base cloths");
        var result = new List<Texture2D>();
        foreach (var entry in catalog.cloth)
        {
            string path = FindTextureAssetPath("Assets/Resources/image/Board/TableCloth/" + entry.name);
            var source = Resources.Load<Texture2D>("image/Board/TableCloth/" + entry.name);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            var header = ImageHeader(Path.Combine(Project, path));
            Require(source != null && importer != null && AssetDatabase.GetAssetPath(source) == path && source.width == header[0] && source.height == header[1],
                entry.name + ": imported base matches source dimensions", header[0] + "x" + header[1]);
            Require(source.filterMode == importer.filterMode && source.anisoLevel == importer.anisoLevel &&
                (source.mipmapCount > 1) == importer.mipmapEnabled && importer.sRGBTexture, entry.name + ": runtime sampling matches importer");
            var thumb = Resources.Load<Texture2D>(entry.preview);
            Require(thumb != null && thumb.width == 256 && thumb.height == 256, entry.name + ": catalog 256px preview resolves");
            result.Add(source);
        }
        return result.ToArray();
    }

    private static Texture2D[] VerifyOverlays()
    {
        var result = new Texture2D[7];
        for (int i = 0; i < result.Length; i++)
        {
            string stem = "Seam_" + i.ToString("00");
            string path = "Assets/Resources/image/Board/TableSeams/" + stem + ".png";
            var header = PngHeader(Path.Combine(Project, path));
            var texture = Resources.Load<Texture2D>("image/Board/TableSeams/" + stem);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Require(header[0] == 1720 && header[1] == 1720 && header[2] == 8 && header[3] == 6 &&
                texture != null && texture.width == 1720 && texture.height == 1720 && AssetDatabase.GetAssetPath(texture) == path,
                stem + ": native 1720 RGBA8 PNG and Resources texture");
            Require(importer != null && importer.sRGBTexture && !importer.mipmapEnabled && texture.mipmapCount == 1 &&
                importer.filterMode == FilterMode.Bilinear && texture.filterMode == FilterMode.Bilinear && importer.textureCompression == TextureImporterCompression.Uncompressed,
                stem + ": uncompressed sRGB Bilinear overlay without mipmaps");
            var pixels = ReadTexture(texture);
            int clear = 0, partial = 0, marked = 0;
            foreach (var pixel in pixels) { if (pixel.a == 0) clear++; else { marked++; if (pixel.a < 255) partial++; } }
            Require(clear > 0 && marked > 0 && partial > 0, stem + ": actual imported alpha contains clear background and antialiased seam",
                "transparent=" + clear + "; nonzero=" + marked + "; partial=" + partial);
            result[i] = texture;
        }
        return result;
    }

    private static void VerifyTransparent(Texture2D cloth)
    {
        var clear = Solid(new Color32(241, 19, 193, 0));
        try
        {
            using (var composer = new TableSeamComposer())
            {
                Color32[] expected = ReadTexture(cloth);
                Color32[] actual = ReadTarget(composer.Compose(cloth, clear) as RenderTexture);
                int maximum = MaxDifference(expected, actual);
                Require(maximum <= 2, "Zero-alpha overlay preserves native base GPU appearance within 2/255",
                    "max RGBA delta=" + maximum + "; compared to same-size ordinary blit, allowing compressed-source quantization");
            }
        }
        finally { Object.DestroyImmediate(clear); }
    }

    private static void VerifySourceOver()
    {
        var baseColor = new Color32(37, 90, 141, 128);
        var seamColor = new Color32(195, 149, 55, 96);
        var cloth = Solid(baseColor); var seam = Solid(seamColor);
        var sentinel = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previousTarget = RenderTexture.active; bool previousSrgb = GL.sRGBWrite;
        try
        {
            sentinel.Create(); RenderTexture.active = sentinel;
            GL.sRGBWrite = QualitySettings.activeColorSpace != ColorSpace.Linear;
            bool sentinelSrgb = GL.sRGBWrite;
            using (var composer = new TableSeamComposer())
            {
                var output = composer.Compose(cloth, seam) as RenderTexture;
                Require(RenderTexture.active == sentinel && GL.sRGBWrite == sentinelSrgb, "Compose restores an unrelated render target and sRGB state");
                var expected = SourceOver(baseColor, seamColor); var actual = ReadTarget(output);
                int maximum = actual.Max(c => Difference(c, expected));
                Require(maximum <= 2, "GPU straight-alpha SourceOver matches sRGB-byte reference within 2/255",
                    "expected=" + expected + "; actual=" + actual[0] + "; max RGBA delta=" + maximum);
            }
        }
        finally
        {
            RenderTexture.active = previousTarget; GL.sRGBWrite = previousSrgb;
            sentinel.Release(); Object.DestroyImmediate(sentinel); Object.DestroyImmediate(cloth); Object.DestroyImmediate(seam);
        }
    }

    private static void VerifyComposition(Texture2D cloth, Texture2D[] seams)
    {
        var composer = new TableSeamComposer();
        try
        {
            Require(composer.Compose(cloth, null) == cloth && composer.Output == null && composer.CompositionCount == 0, "None returns exact original base reference and allocates no RT");
            for (int i = 0; i < seams.Length; i++)
            {
                int before = composer.CompositionCount;
                var output = composer.Compose(cloth, seams[i]) as RenderTexture;
                Require(SamplingMatches(cloth, output) && composer.CompositionCount == before + 1, "Seam " + i.ToString("00") + ": real GPU composition and style-change invalidation");
                Require(composer.Compose(cloth, seams[i]) == output && composer.CompositionCount == before + 1, "Seam " + i.ToString("00") + ": unchanged pair reuses RT without recomposition");
            }
            var released = composer.Output;
            Require(composer.Compose(cloth, null) == cloth && composer.Output == null && released == null, "Switching to None destroys owned RT and returns original base");
            composer.Compose(cloth, seams[0]);
            var ownedOutput = composer.Output; var ownedMaterial = Get(composer, "material") as Material;
            Require(ownedOutput != null && ownedMaterial != null, "Composer owns its RT and material before disposal");
            composer.Dispose();
            Require(composer.Output == null && ownedOutput == null && ownedMaterial == null, "Dispose destroys owned RT and material in Edit mode");
        }
        finally { composer.Dispose(); }
    }

    private static void VerifyCompletionPaths(Scene scene, Texture2D[] seams)
    {
        var desktop = All<Desktop>(scene).Single();
        Require((bool)Call(desktop, "EnsureMaterials"), "Preview Desktop creates owned materials without config callbacks");
        var surface = Get(desktop, "cloth");
        var sourceA = Resources.Load<Texture2D>("image/Board/TableCloth/Tablecloth_blue");
        var sourceB = Resources.Load<Texture2D>("image/Board/TableCloth/Tablecloth_green");
        var material = Get(surface, "Material") as Material;
        var composer = Get(desktop, "seamComposer") as TableSeamComposer;
        Set(surface, "SourceTexture", sourceA); Set(surface, "Version", 10);
        Set(desktop, "selectedSeamTexture", seams[0]); Set(desktop, "seamVersion", 20);
        Call(desktop, "ApplyClothOutput");
        var first = material.mainTexture; int count = composer.CompositionCount;
        Call(desktop, "CompleteBuiltinLoad", surface, 9, sourceB, sourceA);
        Require((Texture2D)Get(surface, "SourceTexture") == sourceA && material.mainTexture == first && composer.CompositionCount == count, "Production completion path rejects stale base version");
        Call(desktop, "CompleteSeamLoad", 1, 19, seams[1]);
        Require((Texture2D)Get(desktop, "selectedSeamTexture") == seams[0] && composer.CompositionCount == count, "Production completion path rejects stale seam version");
        Call(desktop, "CompleteSeamLoad", 1, 20, seams[1]);
        Call(desktop, "CompleteBuiltinLoad", surface, 10, sourceB, sourceA);
        Require((Texture2D)Get(surface, "SourceTexture") == sourceB && (Texture2D)Get(composer, "lastCloth") == sourceB && (Texture2D)Get(composer, "lastSeam") == seams[1],
            "Current seam completion followed by current base completion uses latest pair");
        Set(surface, "Version", 11); Set(desktop, "seamVersion", 21);
        Call(desktop, "CompleteBuiltinLoad", surface, 11, sourceA, sourceB);
        Call(desktop, "CompleteSeamLoad", 2, 21, seams[2]);
        Require((Texture2D)Get(composer, "lastCloth") == sourceA && (Texture2D)Get(composer, "lastSeam") == seams[2], "Current base completion followed by current seam completion uses latest pair");
        Set(desktop, "seamVersion", 22); Set(desktop, "selectedSeam", -1); Set(desktop, "selectedSeamTexture", null);
        Call(desktop, "ApplyClothOutput");
        Call(desktop, "CompleteSeamLoad", 2, 21, seams[2]);
        Require(material.mainTexture == sourceA && composer.Output == null && Get(desktop, "selectedSeamTexture") == null, "None remains exact base after stale seam completion");
        Add(true, "Completion tests inject actual production completion methods", "No Resources requests started and no PlayerPrefs reads/writes through ConfigManager");
    }

    private static void CaptureTables(Scene scene, Texture2D[] seams)
    {
        var desktop = All<Desktop>(scene).Single();
        MeshRenderer renderer; Texture2D cloth;
        using (var serialized = new SerializedObject(desktop))
        {
            renderer = serialized.FindProperty("meshRenderer").objectReferenceValue as MeshRenderer;
            cloth = serialized.FindProperty("defaultTableclothTexture").objectReferenceValue as Texture2D;
        }
        Require(renderer != null && cloth != null && renderer.sharedMaterials.Length >= 2 && renderer.GetComponent<MeshFilter>()?.sharedMesh != null,
            "MainScene Desktop resolves default cloth, original mesh, and cloth/frame material slots");
        var camera = All<Camera>(scene).Single(c => c.name == "Main Camera");
        foreach (var canvas in All<Canvas>(scene)) canvas.enabled = false;
        foreach (var other in All<Renderer>(scene)) other.enabled = other == renderer;
        foreach (var other in All<Camera>(scene)) other.enabled = false;
        for (Transform p = renderer.transform; p != null; p = p.parent) p.gameObject.SetActive(true);
        for (Transform p = camera.transform; p != null; p = p.parent) p.gameObject.SetActive(true);
        camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
        camera.aspect = 1920f / 1080;
        var originals = renderer.sharedMaterials; var materials = (Material[])originals.Clone();
        var ownedMaterial = new Material(originals[0]) { hideFlags = HideFlags.HideAndDontSave };
        materials[0] = ownedMaterial;
        var originalFrame = ((Material[])Get(desktop, "originalMaterials"))[1];
        materials[1] = originalFrame;
        renderer.sharedMaterials = materials;
        var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previousTarget = RenderTexture.active; bool previousSrgb = GL.sRGBWrite;
        try
        {
            target.Create();
            using (var composer = new TableSeamComposer())
                for (int style = -1; style < 7; style++)
                {
                    ownedMaterial.mainTexture = composer.Compose(cloth, style < 0 ? null : seams[style]);
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    string name = style < 0 ? "table-only_None_1920.png" : "table-only_Seam_" + style.ToString("00") + "_1920.png";
                    SaveTarget(target, Path.Combine(Output, name));
                    Require(renderer.sharedMaterials[1] == originalFrame, "Table-only " + (style < 0 ? "None" : style.ToString("00")) + ": main camera capture retains original frame material");
                }
            Add(true, "Eight table-only diagnostics use serialized MainScene camera, mesh, material, and lighting",
                "default cloth=" + cloth.name + "; camera position=" + camera.transform.position + "; rotation=" + camera.transform.eulerAngles +
                "; FOV=" + camera.fieldOfView + "; orthographic=" + camera.orthographic + "; no UI/gameplay/tiles");
        }
        finally
        {
            renderer.sharedMaterials = originals; RenderTexture.active = previousTarget; GL.sRGBWrite = previousSrgb;
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(ownedMaterial);
        }
    }

    private static bool SamplingMatches(Texture2D source, RenderTexture target) => target != null && target.IsCreated() &&
        target.width == source.width && target.height == source.height && target.useMipMap == (source.mipmapCount > 1) &&
        target.filterMode == source.filterMode && target.anisoLevel == source.anisoLevel && target.mipMapBias == source.mipMapBias &&
        target.wrapModeU == source.wrapModeU && target.wrapModeV == source.wrapModeV && target.wrapModeW == source.wrapModeW;
    private static Texture2D Solid(Color32 value)
    {
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        texture.SetPixels32(Enumerable.Repeat(value, 16).ToArray()); texture.Apply(false, false); return texture;
    }
    private static Color32 SourceOver(Color32 background, Color32 foreground)
    {
        float sa = foreground.a / 255f, ba = background.a / 255f, alpha = sa + ba * (1 - sa);
        return new Color32((byte)Mathf.RoundToInt((foreground.r * sa + background.r * ba * (1 - sa)) / alpha),
            (byte)Mathf.RoundToInt((foreground.g * sa + background.g * ba * (1 - sa)) / alpha),
            (byte)Mathf.RoundToInt((foreground.b * sa + background.b * ba * (1 - sa)) / alpha), (byte)Mathf.RoundToInt(alpha * 255));
    }
    private static Color32[] ReadTexture(Texture texture)
    {
        var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try { GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear; Graphics.Blit(texture, target); return ReadTarget(target); }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; RenderTexture.ReleaseTemporary(target); }
    }
    private static Texture2D CopyTarget(RenderTexture target)
    {
        var previous = RenderTexture.active;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, false);
        try { RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply(false, false); return texture; }
        catch { Object.DestroyImmediate(texture); throw; }
        finally { RenderTexture.active = previous; }
    }
    private static Color32[] ReadTarget(RenderTexture target) { var texture = CopyTarget(target); try { return texture.GetPixels32(); } finally { Object.DestroyImmediate(texture); } }
    private static void SaveTarget(RenderTexture target, string path) { var texture = CopyTarget(target); try { File.WriteAllBytes(path, texture.EncodeToPNG()); } finally { Object.DestroyImmediate(texture); } }
    private static int Difference(Color32 a, Color32 b) => Math.Max(Math.Max(Math.Abs(a.r - b.r), Math.Abs(a.g - b.g)), Math.Max(Math.Abs(a.b - b.b), Math.Abs(a.a - b.a)));
    private static int MaxDifference(Color32[] a, Color32[] b) { if (a.Length != b.Length) return 255; int maximum = 0; for (int i = 0; i < a.Length; i++) maximum = Math.Max(maximum, Difference(a[i], b[i])); return maximum; }
    private static string FindTextureAssetPath(string stem)
    {
        foreach (var extension in new[] { ".png", ".jpg", ".jpeg" })
        {
            var path = stem + extension;
            if (File.Exists(Path.Combine(Project, path))) return path;
        }
        throw new FileNotFoundException("Texture asset not found", stem);
    }
    private static int[] ImageHeader(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".png") return PngHeader(path);
        if (extension == ".jpg" || extension == ".jpeg") return JpegHeader(path);
        throw new InvalidDataException("Unsupported image: " + path);
    }
    private static int[] PngHeader(string path)
    {
        var bytes = new byte[26];
        using (var stream = File.OpenRead(path)) if (stream.Read(bytes, 0, bytes.Length) != bytes.Length) throw new InvalidDataException(path);
        if (bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71) throw new InvalidDataException("Not a PNG: " + path);
        return new[] { bytes[16] << 24 | bytes[17] << 16 | bytes[18] << 8 | bytes[19], bytes[20] << 24 | bytes[21] << 16 | bytes[22] << 8 | bytes[23], (int)bytes[24], (int)bytes[25] };
    }
    private static int[] JpegHeader(string path)
    {
        using (var stream = File.OpenRead(path))
        {
            if (stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8) throw new InvalidDataException("Not a JPEG: " + path);
            while (stream.Position < stream.Length)
            {
                int prefix;
                do { prefix = stream.ReadByte(); } while (prefix == 0xFF);
                if (prefix < 0) break;
                if (prefix == 0xD9 || prefix == 0xDA) break;
                if (prefix == 0xD8 || prefix == 0x01 || prefix >= 0xD0 && prefix <= 0xD7) continue;
                int length = (stream.ReadByte() << 8) | stream.ReadByte();
                if (length < 2 || stream.Position + length - 2 > stream.Length) throw new InvalidDataException("Invalid JPEG segment: " + path);
                bool isStartOfFrame = prefix >= 0xC0 && prefix <= 0xC3 || prefix >= 0xC5 && prefix <= 0xC7 || prefix >= 0xC9 && prefix <= 0xCB || prefix >= 0xCD && prefix <= 0xCF;
                if (isStartOfFrame)
                {
                    int precision = stream.ReadByte();
                    int height = (stream.ReadByte() << 8) | stream.ReadByte();
                    int width = (stream.ReadByte() << 8) | stream.ReadByte();
                    return new[] { width, height, precision, 0 };
                }
                stream.Position += length - 2;
            }
        }
        throw new InvalidDataException("JPEG dimensions not found: " + path);
    }
    private static object Get(object obj, string field) => obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(obj);
    private static void Set(object obj, string field, object value) => obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(obj, value);
    private static object Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(obj, args);
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
    private static string SceneState() => string.Join("|", Enumerable.Range(0, SceneManager.sceneCount).Select(i => { var s = SceneManager.GetSceneAt(i); return s.handle + ":" + s.path + ":" + s.isDirty; }));
    private static string Preferences() => string.Join("|", new[] { "SelectedTableClothPath", "SelectedTableClothIsCustom", "SelectedTableEdgePath", "SelectedTableEdgeIsCustom", "SelectedTableSeam" }
        .Select(k => k + ":" + PlayerPrefs.HasKey(k) + ":" + (k.EndsWith("Path") ? PlayerPrefs.GetString(k, "") : PlayerPrefs.GetInt(k, 0).ToString())));
    private static string Hash(string path) { using (var sha = SHA256.Create()) using (var file = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", ""); }
    private static void Add(bool passed, string name, string detail = null) { report.checks.Add(new Check { passed = passed, name = name, detail = detail }); if (passed) report.passed++; else report.failed++; }
    private static void Require(bool passed, string name, string detail = null) { Add(passed, name, detail); if (!passed) throw new InvalidOperationException(name + " " + detail); }
}
