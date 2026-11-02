using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>Editor-only, debounced font preparation. Play Mode never starts a scan.</summary>
[InitializeOnLoad]
public static class PlatformFontAutomation
{
    public const string Root = "Assets/Editor/FontAutomation";
    public const string RuntimeSources = "Assets/Resources/Localization/FontSources";
    const string PendingKey = "Salasasa.FontAutomation.Pending";
    const string ErrorKey = "Salasasa.FontAutomation.Error";
    static readonly string Project = Path.GetFullPath(".");
    public static readonly string Cache = Path.Combine(Project, "Library/PlatformFonts");
    static readonly string Output = Path.Combine(Cache, "Generated");
    static Process worker;
    static Task<string> stdout, stderr;
    static double due;
    public static bool Applying { get; private set; }

    static PlatformFontAutomation()
    {
        if (!File.Exists(Path.Combine(Cache, "inventory.json"))) SessionState.SetBool(PendingKey, true);
        due = EditorApplication.timeSinceStartup + 10;
        EditorApplication.update += Update;
        AssemblyReloadEvents.beforeAssemblyReload += StopWorker;
    }

    public static string FullSource(string weight) => weight == "Regular"
        ? "Assets/font/思源黑体/SourceHanSansSC-Regular.otf" : Root + "/Sources/SourceHanSansSC-" + weight + ".otf";

    public static string RuntimeSource(string weight) => weight == "Regular" ? FullSource(weight)
        : RuntimeSources + "/SalasasaSans-" + weight + ".otf";

    public static bool Relevant(string path)
    {
        path = path.Replace('\\', '/');
        if (path.StartsWith(Root + "/Generated/") || path.StartsWith(RuntimeSources + "/")) return false;
        if (path.StartsWith(Root + "/")) return !path.EndsWith(".meta");
        if (path.Contains("/Editor/") || path.Contains("/Plugins/") || path.Contains("/Tests/")) return false;
        if (path.Contains("/Localization/MixedFonts/") || path.Contains("/Localization/Materials/")) return false;
        return new[] { ".cs", ".unity", ".prefab", ".asset", ".json", ".csv", ".tsv", ".txt", ".otf", ".ttf", ".ttc" }
            .Contains(Path.GetExtension(path));
    }

    public static void Changed(string[] paths)
    {
        if (Applying || !paths.Any(Relevant)) return;
        SessionState.SetBool(PendingKey, true);
        SessionState.SetString(ErrorKey, "");
        due = EditorApplication.timeSinceStartup + 8;
    }

    static bool ExternalEditorWork()
    {
        var lockPath = Path.GetFullPath("../.om_workspace/mil-forks-20261002/unity-validation.lock");
        // Explicit preparation is owned by the caller; idle preparation waits for any shared work.
        return File.Exists(lockPath);
    }

    static void Update()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || Applying || ExternalEditorWork()) return;
        try
        {
            if (worker != null)
            {
                if (!worker.HasExited) return;
                FinishWorker();
                ApplyPrepared();
                return;
            }
            if (!SessionState.GetBool(PendingKey, false) || EditorApplication.timeSinceStartup < due ||
                SessionState.GetString(ErrorKey, "").Length != 0) return;
            SessionState.SetBool(PendingKey, false);
            StartWorker();
        }
        catch (Exception e)
        {
            StopWorker();
            SessionState.SetString(ErrorKey, e.Message);
            SessionState.SetBool(PendingKey, true);
            Debug.LogWarning("字体自动准备未完成（构建前会重试）：" + e.Message);
        }
    }

    static void StartWorker()
    {
        Directory.CreateDirectory(Output);
        PlatformFontCatalog.Export();
        string python = Environment.GetEnvironmentVariable("SALASASA_FONT_PYTHON");
        if (string.IsNullOrWhiteSpace(python)) python = "python";
        var start = new ProcessStartInfo(python,
            Quote(Path.Combine(Project, Root, "font_pipeline.py")) + " --project " + Quote(Project) +
            " --output " + Quote(Output) + " --cache " + Quote(Cache))
        { WorkingDirectory = Project, UseShellExecute = false, CreateNoWindow = true,
          RedirectStandardOutput = true, RedirectStandardError = true };
        start.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        worker = Process.Start(start);
        stdout = worker.StandardOutput.ReadToEndAsync();
        stderr = worker.StandardError.ReadToEndAsync();
    }

    static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    static void FinishWorker()
    {
        int code = worker.ExitCode;
        string output = stdout.GetAwaiter().GetResult(), error = stderr.GetAwaiter().GetResult();
        worker.Dispose(); worker = null;
        File.WriteAllText(Path.Combine(Cache, "worker.log"), output + error);
        if (code != 0) throw new BuildFailedException("字体字符收集失败：" + error +
            "\n需要 Python 3 和 fontTools；安装说明见 " + Root + "/README.md");
    }

    static void StopWorker()
    {
        if (worker == null) return;
        if (!worker.HasExited) { worker.Kill(); SessionState.SetBool(PendingKey, true); }
        worker.Dispose(); worker = null;
    }

    [MenuItem("Tools/Salasasa/Fonts/Update Generated Fonts")]
    public static void PrepareNow() => EnsureUpToDate();

    public static void EnsureUpToDate()
    {
        if (EditorApplication.isPlaying) throw new BuildFailedException("请退出 Play Mode 后准备字体。");
        if (worker != null)
        {
            if (!worker.WaitForExit(120000)) { StopWorker(); throw new BuildFailedException("字体准备超时。"); }
            FinishWorker();
        }
        // Rescan file stamps at build time, including changes made outside Unity.
        StartWorker();
        if (!worker.WaitForExit(120000)) { StopWorker(); throw new BuildFailedException("字体准备超时。"); }
        FinishWorker();
        ApplyPrepared();
        SessionState.SetBool(PendingKey, false);
        SessionState.SetString(ErrorKey, "");
    }

    static void ApplyPrepared()
    {
        string prepared = Path.Combine(Output, "manifest.json");
        string text = File.ReadAllText(prepared);
        var manifest = JsonUtility.FromJson<PlatformFontManifest>(text);
        string committed = Root + "/Generated/manifest.json";
        var previous=File.Exists(committed)?JsonUtility.FromJson<PlatformFontManifest>(File.ReadAllText(committed)):null;
        bool same = File.Exists(committed) && File.ReadAllText(committed) == text;
        if (same && PlatformFontAutomationAssets.SourcesMatch(manifest) &&
            File.Exists(Path.Combine(Cache, "applied.txt")) &&
            File.ReadAllText(Path.Combine(Cache, "applied.txt")) == manifest.glyphSignature) return;
        var watch = Stopwatch.StartNew();
        Applying = true;
        try
        {
            Directory.CreateDirectory(RuntimeSources);
            foreach (var entry in manifest.fonts)
            {
                string source = Path.Combine(Output, "SalasasaSans-" + entry.weight + ".otf");
                string destination = RuntimeSource(entry.weight);
                if (File.Exists(source) && (!File.Exists(destination) ||
                    PlatformFontAutomationAssets.Hash(destination) != entry.sha256))
                {
                    File.Copy(source, destination, true);
                    AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
                }
            }
            PlatformFontAutomationAssets.Apply(manifest,previous);
            Directory.CreateDirectory(Path.GetDirectoryName(committed));
            if (!same)
            {
                File.WriteAllText(committed, text);
                AssetDatabase.ImportAsset(committed);
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText(Path.Combine(Cache, "applied.txt"), manifest.glyphSignature);
            File.WriteAllText(Path.Combine(Cache, "apply-time-ms.txt"), watch.ElapsedMilliseconds.ToString());
        }
        finally { Applying = false; }
    }
}

[Serializable]
public sealed class PlatformFontManifest
{
    public int version;
    public uint[] regularCharacters;
    public PlatformFontSubset[] fonts;
    public PlatformExtraFont[] extraFonts;
    public string glyphSignature, regularSourceHash;
}

[Serializable]
public sealed class PlatformExtraFont
{
    public string path, source, sourceHash;
    public uint[] characters;
}

[Serializable]
public sealed class PlatformFontSubset
{
    public string weight, signature, sha256, sourceHash;
    public uint[] characters;
    public int bytes;
}

public sealed class PlatformFontImportWatcher : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        => PlatformFontAutomation.Changed(imported.Concat(deleted).Concat(moved).Concat(movedFrom).ToArray());
}

public sealed class PlatformFontAutoBuild : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;
    public void OnPreprocessBuild(BuildReport report)
    {
        PlatformFontAutomation.EnsureUpToDate();
        var failures=PlatformFontBuildValidation.ValidatePrefabs();
        if(failures.Length>0)throw new BuildFailedException("预制体固定文本缺字：\n"+string.Join("\n",failures));
    }
}
