#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OmUpdate;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.U2D;

public static class WebTextureProfileBuild {
    public const string BootScene = "Assets/Scenes/OmWebTextureBootstrap.unity";
    const string MainScene = "Assets/Scenes/MainScene.unity";
    public static bool PackingScenes { get; private set; }
    public static bool BuildingBootstrap { get; private set; }

    // Build the scene and its resource dependencies together so Unity emits
    // cross-bundle references even for textures assigned in the Inspector.
    static AssetBundleBuild[] Describe(string[] scenes, out ResourceEntry[] index) {
        var resources = new List<ResourceEntry>();
        var groups = new Dictionary<string, List<string>> {
            ["textures"] = new List<string>(), ["images"] = new List<string>(), ["media"] = new List<string>(),
            ["content"] = new List<string>(), ["scenes"] = scenes.ToList()
        };
        var roots = new HashSet<string>(scenes, StringComparer.Ordinal);
        foreach (var path in AssetDatabase.GetAllAssetPaths().OrderBy(p => p, StringComparer.Ordinal)) {
            int at = path.LastIndexOf("/Resources/", StringComparison.Ordinal);
            if (at < 0 || !IsGameAsset(path) || DevelopmentExpressionBuild.ExcludedFromRelease(path)) continue;
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type == null || type == typeof(DefaultAsset) || type.Namespace?.StartsWith("UnityEditor", StringComparison.Ordinal) == true) continue;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            string group = BundleFor(type, importer);
            string key = path.Substring(at + "/Resources/".Length);
            resources.Add(new ResourceEntry { key = key.Substring(0, key.Length - Path.GetExtension(key).Length),
                asset = path, bundle = group, type = type.AssemblyQualifiedName, sprites = importer && importer.textureType == TextureImporterType.Sprite });
            groups[group].Add(path);
            roots.Add(path);
        }

        var dependencies = new HashSet<string>(AssetDatabase.GetDependencies(resources.Select(r => r.asset).ToArray(), true));
        foreach (string path in scenes) {
            var preview = EditorSceneManager.OpenPreviewScene(path);
            try {
                if (!ConfigManager.Debug && path == MainScene) DevelopmentContentSceneBuild.StripBuildCopy(preview);
                foreach (var asset in EditorUtility.CollectDependencies(preview.GetRootGameObjects())) {
                    string dependency = AssetDatabase.GetAssetPath(asset);
                    if (!string.IsNullOrEmpty(dependency)) dependencies.Add(dependency);
                }
            } finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        foreach (var path in dependencies.OrderBy(p => p, StringComparer.Ordinal)) {
            if (roots.Contains(path) || !IsGameAsset(path)) continue;
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type == null || type == typeof(DefaultAsset) || type.Namespace?.StartsWith("UnityEditor", StringComparison.Ordinal) == true) continue;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            // These authoring sprites are inputs to the atlas importer.
            if (importer && path.StartsWith(BuiltinTileSprites.SourceRoot, StringComparison.Ordinal)) continue;
            if (type != typeof(SceneAsset)) groups[BundleFor(type, importer)].Add(path);
        }
        index = resources.ToArray();
        return groups.Where(pair => pair.Value.Count != 0).Select(pair => new AssetBundleBuild {
            assetBundleName = pair.Key, assetNames = pair.Value.Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray()
        }).ToArray();
    }

    static string BundleFor(Type type, TextureImporter importer) {
        if (type == typeof(SpriteAtlas)) return "textures";
        if (importer) {
            var settings = importer.GetPlatformTextureSettings("WebGL");
            if (!settings.overridden) settings = importer.GetDefaultPlatformTextureSettings();
            return settings.format == TextureImporterFormat.RGBA32 || settings.format == TextureImporterFormat.RGB24
                || (settings.format == TextureImporterFormat.Automatic && settings.textureCompression == TextureImporterCompression.Uncompressed)
                ? "images" : "textures";
        }
        if (type == typeof(AudioClip) || type == typeof(Font) || type == typeof(TMPro.TMP_FontAsset) || type == typeof(TextAsset))
            return "media";
        return "content";
    }

    static bool IsGameAsset(string path) {
        if (!path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains("/Editor/")
            || AssetDatabase.IsValidFolder(path) || Path.GetFileName(path).StartsWith("README", StringComparison.OrdinalIgnoreCase)) return false;
        switch (Path.GetExtension(path).ToLowerInvariant()) {
            case ".cs": case ".dll": case ".asmdef": case ".asmref": return false;
            default: return true;
        }
    }

    static WebTextureManifest.Bundle[] Pack(string cache, AssetBundleBuild[] definitions, WebGLTextureSubtarget profile) {
        string directory = Path.Combine(cache, profile.ToString());
        Directory.CreateDirectory(directory);
        var manifest = BuildPipeline.BuildAssetBundles(new BuildAssetBundlesParameters {
            targetPlatform = BuildTarget.WebGL, subtarget = (int)profile, outputPath = directory,
            options = BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
            bundleDefinitions = definitions
        });
        if (!manifest) throw new BuildFailedException("Failed to build Web texture profile " + profile);
        return definitions.Select(def => {
            string name = def.assetBundleName;
            string path = Path.Combine(directory, name);
            if (!BuildPipeline.GetCRCForAssetBundle(path, out uint crc)) throw new BuildFailedException("Cannot read bundle CRC: " + path);
            return new WebTextureManifest.Bundle { name = name, crc = crc, bytes = checked((int)new FileInfo(path).Length),
                file = ContentManifest.HashFile(path) + ".bundle" };
        }).ToArray();
    }

    static void AssignPaths(WebTextureManifest content) {
        for (int i = 0; i < content.dxt.Length; i++) {
            var dxt = content.dxt[i];
            var astc = content.astc[i];
            if (dxt.name == "textures" && dxt.file == astc.file)
                throw new BuildFailedException("DXT and ASTC texture payloads are identical. Check the WebGL texture subtarget.");
            bool shared = dxt.file == astc.file;
            dxt.file = (shared ? "Shared/" : "DXT/") + dxt.name + "-" + dxt.file;
            astc.file = (shared ? "Shared/" : "ASTC/") + astc.name + "-" + astc.file;
        }
    }

    static void InstallFile(string source, string target) {
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        string temp = target + ".part";
        try {
            File.Copy(source, temp, true);
            if (File.Exists(target)) File.Replace(temp, target, null);
            else File.Move(temp, target);
        } finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    static void Publish(string output, string staging, string cache, WebTextureManifest content) {
        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void InstallProfile(string profile, WebTextureManifest.Bundle[] files) {
            foreach (var file in files) {
                string target = Path.Combine(output, "StreamingAssets", "WebGLContent", file.file);
                if (!live.Add(Path.GetFullPath(target))) continue;
                InstallFile(Path.Combine(cache, profile, file.name), target);
            }
        }
        InstallProfile("DXT", content.dxt);
        InstallProfile("ASTC", content.astc);
        foreach (string source in Directory.GetFiles(staging, "*", SearchOption.AllDirectories)) {
            string relative = source.Substring(staging.Length + 1);
            if (relative == "index.html") continue;
            string target = Path.Combine(output, relative);
            live.Add(Path.GetFullPath(target));
            InstallFile(source, target);
        }
        // The entry point becomes visible only after every file it can request.
        InstallFile(Path.Combine(staging, "index.html"), Path.Combine(output, "index.html"));
        void RemoveOldFiles(string directory, string pattern) {
            if (!Directory.Exists(directory)) return;
            foreach (string path in Directory.GetFiles(directory))
                if (!live.Contains(Path.GetFullPath(path)) && Regex.IsMatch(Path.GetFileName(path), pattern))
                    File.Delete(path);
        }
        foreach (string profile in new[] { "Shared", "DXT", "ASTC" })
            RemoveOldFiles(Path.Combine(output, "StreamingAssets", "WebGLContent", profile),
                @"^(?:textures|images|media|content|scenes)-[0-9a-f]{64}\.bundle$");
        RemoveOldFiles(Path.Combine(output, "Build"),
            @"^[0-9a-f]{32}\.(?:loader\.js|framework\.js|wasm|data)(?:\.gz|\.br|\.unityweb)?$");
    }

    public static void BuildWebGL() {
        string[] args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-omOutput");
        if (at < 0 || at + 1 >= args.Length || !Path.GetFullPath(".").Replace('\\', '/').Contains("/.om_workspace/"))
            throw new BuildFailedException("Use the WebGL target on an independent UnityBatchBuild project.");
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL) throw new BuildFailedException("WebGL must be active.");
        if (File.Exists(BootScene)) throw new BuildFailedException("Bootstrap staging scene already exists: " + BootScene);
        string output = Path.GetFullPath(args[at + 1]);
        string work = Directory.GetParent(Application.dataPath).Parent.FullName;
        string cache = Path.Combine(work, "web-texture-bundles");
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (!scenes.Contains(MainScene)) throw new BuildFailedException("MainScene must be enabled.");

        HandTileAtlasBuild.ValidateSharedPacks();
        HandTileAtlasBuild.EnsurePack("hongque");
        new PlatformFontAutoBuild().OnPreprocessBuild(null);
        new RuleSupportTableFontBuild().OnPreprocessBuild(null);
        new TMPro.TMP_PreBuildProcessor().OnPreprocessBuild(null);
        new PlatformFontBuildPreparation().OnPreprocessBuild(null);
        AssetDatabase.SaveAssets();

        var definitions = Describe(scenes, out var resources);
        var content = new WebTextureManifest { mainScene = MainScene, resources = resources };
        bool strip = PlayerSettings.stripUnusedMeshComponents;
        try {
            PackingScenes = true;
            PlayerSettings.stripUnusedMeshComponents = false;
            content.dxt = Pack(cache, definitions, WebGLTextureSubtarget.DXT);
            content.astc = Pack(cache, definitions, WebGLTextureSubtarget.ASTC);
        } finally {
            PackingScenes = false;
            PlayerSettings.stripUnusedMeshComponents = strip;
        }
        AssignPaths(content);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        new GameObject("WebGL content").AddComponent<WebTextureBootstrap>().content = content;
        string stagingRoot = Path.Combine(work, "web-player-staging-" + Guid.NewGuid().ToString("N"));
        string staging = Path.Combine(stagingRoot, "WebGL");
        try {
            if (!EditorSceneManager.SaveScene(scene, BootScene)) throw new BuildFailedException("Cannot save WebGL bootstrap scene.");
            BuildingBootstrap = true;
            using (new BundledGameBuild.ShellResourcesScope()) {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { BootScene }, locationPathName = staging, target = BuildTarget.WebGL,
                    assetBundleManifestPath = Path.Combine(cache, "DXT", "DXT.manifest"), options = BuildOptions.StrictMode,
                    extraScriptingDefines = new[] { "OM_WEB_TEXTURE_PROFILES" }
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException("WebGL player build failed: " + report.summary.result);
            }
            Publish(output, staging, cache, content);
        } finally {
            BuildingBootstrap = false;
            AssetDatabase.DeleteAsset(BootScene);
            if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, true);
        }
        Debug.Log("WEB_TEXTURE_PROFILES_SUCCEEDED " + output);
    }
}
#endif
