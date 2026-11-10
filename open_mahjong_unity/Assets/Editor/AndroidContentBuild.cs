#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using OmUpdate;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;

// Runs in the independent Android build copy only. No SDK is installed into
// the source project, and normal Windows/WebGL build profiles stay untouched.
public static class AndroidContentBuild {
    // AssetBundle scene processors receive a null BuildReport. Keep their
    // release behavior identical to BuildPlayer without affecting Play Mode.
    public static bool BuildingContentBundles { get; private set; }
    public static bool BuildingBootstrapPlayer { get; private set; }
    static string Arg(string name) {
        var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
    static string Work => Path.GetFullPath(Arg("-omWork") ?? throw new BuildFailedException("Missing -omWork"));
    static string Output => Path.GetFullPath(Arg("-omOutput") ?? throw new BuildFailedException("Missing -omOutput"));
    static bool Validation => Environment.GetCommandLineArgs().Contains("-omValidation");
    static string BundleCache => Path.Combine(Work, Validation ? "validation-bundle-cache" : "bundle-cache", ConfigManager.base_version.ToString(), "bundles");
    static Type Sdk(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null)
        ?? throw new BuildFailedException("HybridCLR SDK is not installed in the independent build copy: " + name);
    static object Invoke(string type, string method, params object[] args) {
        var methods = Sdk(type).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == method && m.GetParameters().Length == args.Length);
        foreach (var m in methods) {
            if (m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(v => v)) return m.Invoke(null, args);
        }
        throw new MissingMethodException(type, method);
    }
    static object Settings {
        get { var t = Sdk("HybridCLR.Settings.HybridCLRSettings"); return t.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null); }
    }
    static void Configure() {
        string project = Path.GetFullPath(".").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!project.Replace('\\', '/').Contains("/.om_workspace/")) throw new BuildFailedException("Android content builds require an isolated .om_workspace project.");
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) throw new BuildFailedException("Android must be the active build target.");
        if (Validation) {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "cn.salasasa.local.contentupdate");
            PlayerSettings.companyName = "OMU Local Validation";
            PlayerSettings.productName = "Android Content Update Validation";
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.X86_64;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        }
        var settings = Settings;
        settings.GetType().GetField("enable").SetValue(settings, true);
        var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Player)
            .Where(a => a.sourceFiles.Any(f => f.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal)
                    || f.Replace('\\', '/').StartsWith(Application.dataPath.Replace('\\', '/') + "/", StringComparison.OrdinalIgnoreCase))
                && !a.name.StartsWith("OmUpdate.", StringComparison.Ordinal))
            .Select(a => a.name).Distinct().OrderBy(s => s).ToArray();
        if (!assemblies.Contains("Assembly-CSharp")) throw new BuildFailedException("Game assemblies were not discovered; refusing an empty hot-update package.");
        Debug.Log("[AndroidContent] hot-update assemblies: " + string.Join(", ", assemblies));
        settings.GetType().GetField("hotUpdateAssemblies").SetValue(settings, assemblies);
        Sdk("HybridCLR.Settings.HybridCLRSettings").GetMethod("Save").Invoke(null, null);
        if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP)
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        if (PlayerSettings.GetApiCompatibilityLevel(NamedBuildTarget.Android) != ApiCompatibilityLevel.NET_Unity_4_8)
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android, ApiCompatibilityLevel.NET_Unity_4_8);
        // Game scenes/components arrive from content bundles. The fixed APK
        // must retain native engine types that its bootstrap scene never uses.
        PlayerSettings.stripEngineCode = false;
        var installerType = Sdk("HybridCLR.Installer.InstallerController");
        var installer = Activator.CreateInstance(installerType);
        if (!(bool)installerType.GetMethod("HasInstalledHybridCLR").Invoke(installer, null)
            || !Equals(installerType.GetProperty("InstalledLibil2cppVersion").GetValue(installer), installerType.GetProperty("PackageVersion").GetValue(installer))) {
            // Respect the repository's read-only README rule even inside ignored
            // build copies: archive preparation and copies explicitly exclude them.
            var util = Sdk("HybridCLR.SettingsUtil");
            string local = (string)util.GetProperty("LocalUnityDataDir").GetValue(null);
            string il2cpp = (string)util.GetProperty("LocalIl2CppDir").GetValue(null);
            if (Directory.Exists(local)) {
                string archive = Path.GetFullPath(Path.Combine(Work, "native-archives", Guid.NewGuid().ToString("N")));
                if (!Path.GetFullPath(local).StartsWith(Work + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || !archive.StartsWith(Work + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new BuildFailedException("Unexpected native cache location.");
                Directory.CreateDirectory(Path.GetDirectoryName(archive)); Directory.Move(local, archive);
            }
            CopyTree(Path.Combine(EditorApplication.applicationContentsPath, "MonoBleedingEdge"), Path.Combine(local, "MonoBleedingEdge"));
            CopyTree(Path.Combine(EditorApplication.applicationContentsPath, "il2cpp"), il2cpp, "libil2cpp");
            CopyTree(Path.Combine(Work, "NativeRuntimeSource", "libil2cpp"), Path.Combine(il2cpp, "libil2cpp"));
            installerType.GetMethod("WriteLocalVersion", BindingFlags.Public | BindingFlags.Instance).Invoke(installer, null);
            if (!(bool)installerType.GetMethod("HasInstalledHybridCLR").Invoke(installer, null)) throw new BuildFailedException("HybridCLR native runtime installation did not complete.");
        }
    }
    static void CopyTree(string source, string destination, string skipRootDirectory = null) {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source)) {
            if (Path.GetFileName(file).StartsWith("README", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        }
        foreach (var dir in Directory.GetDirectories(source))
            if (Path.GetFileName(dir) != skipRootDirectory) CopyTree(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
    [Serializable] sealed class Baseline {
        public int baseVersion; public string fingerprint; public string bridgeHash;
        public string[] metadata; public string publicKey;
    }
    static string HashText(string text) { using (var sha = SHA256.Create()) return ContentManifest.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(text))); }
    static string NativeFingerprint() {
        var files = Directory.GetFiles("Assets/AndroidUpdate", "*", SearchOption.AllDirectories)
            .Concat(Directory.Exists("Assets/Plugins/Android") ? Directory.GetFiles("Assets/Plugins/Android", "*", SearchOption.AllDirectories) : Array.Empty<string>())
            .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                && !f.Replace('\\', '/').EndsWith("/AndroidBootstrap.unity", StringComparison.Ordinal)
                && !Path.GetFileName(f).StartsWith("README", StringComparison.OrdinalIgnoreCase))
            .Concat(new[] { "Packages/packages-lock.json" }).OrderBy(f => f, StringComparer.Ordinal);
        return HashText(Application.unityVersion + "\n" + PlayerSettings.Android.targetArchitectures + "\n"
            + PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) + "\n"
            + PlayerSettings.Android.minSdkVersion + ":" + PlayerSettings.Android.targetSdkVersion + ":"
            + PlayerSettings.stripEngineCode + ":" + PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Android) + "\n"
            + string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).Select(a => a.ToString())) + "\n"
            + string.Join("\n", files.Select(f => f.Replace('\\', '/') + ":" + ContentManifest.HashFile(f))));
    }
    static void FrameworkPreservation(string baseRoot, bool create) {
        foreach (string field in new[] { "outputLinkFile", "outputAOTGenericReferenceFile" }) {
            string relative = (string)Settings.GetType().GetField(field).GetValue(Settings);
            string live = ContentStore.SafePath(Application.dataPath, relative.Replace('\\', '/'));
            if (Path.GetFileName(live).StartsWith("README", StringComparison.OrdinalIgnoreCase)) throw new BuildFailedException("Invalid generated framework file.");
            string archive = Path.Combine(baseRoot, "preservation", field);
            string from = create ? live : archive, to = create ? archive : live;
            if (!File.Exists(from)) throw new BuildFailedException("Framework preservation file is missing: " + from);
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(from, to, true);
            if (File.Exists(from + ".meta")) File.Copy(from + ".meta", to + ".meta", true);
        }
        if (!create) AssetDatabase.Refresh();
    }
    static string BridgeHash() {
        string[] files = Directory.GetFiles("Library/HybridCLR", "*", SearchOption.AllDirectories)
            .Where(f => f.Replace('\\', '/').Contains("/hybridclr/generated/") && (f.EndsWith(".cpp") || f.EndsWith(".h") || f.EndsWith(".inc")))
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToArray();
        if (files.Length == 0) throw new BuildFailedException("HybridCLR method bridges were not generated.");
        return HashText(string.Join("\n", files.Select(f => Path.GetFileName(f) + ":" + ContentManifest.HashFile(f))));
    }
    public static void BuildApk() => Run(true);
    public static void BuildContent() => Run(false);
    const string BootScene = "Assets/AndroidUpdate/AndroidBootstrap.unity";
    static void PrepareBootstrapScene() {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("AndroidContentBoot").AddComponent<ContentBoot>();
        EditorSceneManager.SaveScene(scene, BootScene);
        // The AOT export needs the framework and generated linker references;
        // game scenes are built into the content bundles, outside the APK.
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScene, true) };
    }
    static void Run(bool apk) {
        try {
            Configure();
            string baseRoot = Path.Combine(Work, Validation ? "validation-baselines" : "baselines", ConfigManager.base_version.ToString());
            Directory.CreateDirectory(baseRoot);
            string baselineFile = Path.Combine(baseRoot, "baseline.json");
            Baseline previous = File.Exists(baselineFile) ? JsonUtility.FromJson<Baseline>(File.ReadAllText(baselineFile)) : null;
            if (!apk && previous == null) throw new BuildFailedException("No published framework baseline. Build Android APK first.");
            string native = NativeFingerprint();
            if (previous != null && previous.fingerprint != native) throw new BuildFailedException("Android framework changed. Increment base_version and build a new APK.");
            // A completed AOT export can survive a later packaging failure.
            // This checkpoint never authorizes a content-only publication.
            string checkpointFile = Path.Combine(baseRoot, "aot-ready.json");
            Baseline reusable = previous ?? (File.Exists(checkpointFile) ? JsonUtility.FromJson<Baseline>(File.ReadAllText(checkpointFile)) : null);
            if (reusable != null && reusable.fingerprint != native) reusable = null;
            bool freshFramework = reusable == null;
            if (apk) PrepareBootstrapScene();
            if (freshFramework) Invoke("HybridCLR.Commands.PrebuildCommand", "GenerateAll");
            else Invoke("HybridCLR.Commands.CompileDllCommand", "CompileDll", BuildTarget.Android, false);
            // Both content-only and subsequent APK builds reuse the framework's
            // AOT baseline, avoiding another clean player export for the same base.
            if (!freshFramework) {
                string archived = Path.Combine(baseRoot, "metadata");
                string expected = "Library/HybridCLR/AssembliesPostIl2CppStrip/Android";
                Directory.CreateDirectory(expected);
                foreach (var file in Directory.GetFiles(archived, "*.dll")) File.Copy(file, Path.Combine(expected, Path.GetFileName(file)), true);
                Invoke("HybridCLR.Commands.Il2CppDefGeneratorCommand", "GenerateIl2CppDef");
                Invoke("HybridCLR.Commands.MethodBridgeGeneratorCommand", "GenerateMethodBridgeAndReversePInvokeWrapper", BuildTarget.Android);
            }
            string bridge = BridgeHash();
            if (previous != null && bridge != previous.bridgeHash) throw new BuildFailedException("Native method bridges changed. Increment base_version and build an APK before publishing this content.");
            // A fresh source mirror removes SDK-generated assets. Keep the same
            // linker preservation when rebuilding an APK for an existing base.
            if (freshFramework || apk) FrameworkPreservation(baseRoot, freshFramework);
            string metadataSource = freshFramework ? "Library/HybridCLR/AssembliesPostIl2CppStrip/Android" : Path.Combine(baseRoot, "metadata");
            if (!Directory.Exists(metadataSource)) throw new BuildFailedException("AOT metadata is missing: " + metadataSource);
            var metadata = Directory.GetFiles(metadataSource, "*.dll").Select(Path.GetFileName).OrderBy(n => n).ToArray();
            if (metadata.Length == 0) throw new BuildFailedException("No AOT metadata was generated.");
            if (freshFramework) {
                Directory.CreateDirectory(Path.Combine(baseRoot, "metadata"));
                foreach (string name in metadata) File.Copy(Path.Combine(metadataSource, name), Path.Combine(baseRoot, "metadata", name), true);
            }
            string keyFile = Path.Combine(baseRoot, "signing-private.xml");
            if (!File.Exists(keyFile) && reusable != null) throw new BuildFailedException("Signing key is missing. Restore the original private key for this base_version.");
            using (var rsa = new RSACryptoServiceProvider(3072)) {
                rsa.PersistKeyInCsp = false;
                if (File.Exists(keyFile)) rsa.FromXmlString(File.ReadAllText(keyFile));
                else File.WriteAllText(keyFile, rsa.ToXmlString(true));
                string publicKey = rsa.ToXmlString(false);
                if (reusable != null && reusable.publicKey != publicKey) throw new BuildFailedException("Framework signing key does not match.");
                var baseline = new Baseline { baseVersion = ConfigManager.base_version, fingerprint = native, bridgeHash = bridge, metadata = metadata, publicKey = publicKey };
                if (previous == null) ContentStore.AtomicWrite(checkpointFile, JsonUtility.ToJson(baseline, true));
                string staging = Path.Combine(Work, "staging", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                ContentIndex index = Pack(staging, metadataSource, metadata, native);
                // Reject new AOT API references absent from the archived framework.
                CheckAotReferences(staging, baseRoot, freshFramework);
                string publication = Path.Combine(Output, "hotupdate", ConfigManager.base_version.ToString(), ConfigManager.releaseVersion.ToString());
                Directory.CreateDirectory(publication);
                string zip = Path.Combine(staging, "../content-" + Guid.NewGuid().ToString("N") + ".zip");
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) {
                    foreach (string file in Directory.GetFiles(staging, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)) {
                        var entry = archive.CreateEntry(file.Substring(staging.Length + 1).Replace('\\', '/'), System.IO.Compression.CompressionLevel.Optimal);
                        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                        using (var source = File.OpenRead(file)) using (var output = entry.Open()) source.CopyTo(output);
                    }
                }
                string hash = ContentManifest.HashFile(zip);
                string name = "content-" + hash + ".zip";
                var manifest = new ContentManifest { baseVersion = ConfigManager.base_version, releaseVersion = ConfigManager.releaseVersion,
                    baseline = native, file = name, sizeBytes = new FileInfo(zip).Length,
                    unpackedBytes = Directory.GetFiles(staging, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length), sha256 = hash };
                manifest.signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(manifest.SignedText()), "SHA256"));
                string manifestFile = Path.Combine(publication, "content-update.json");
                if (File.Exists(manifestFile)) {
                    var old = JsonUtility.FromJson<ContentManifest>(File.ReadAllText(manifestFile));
                    if (old.sha256 != hash) throw new BuildFailedException("This base/release version already has different content. Increment releaseVersion; published versions are immutable.");
                }
                File.Copy(zip, Path.Combine(publication, name), true);
                ContentStore.AtomicWrite(manifestFile, JsonUtility.ToJson(manifest, true));
                if (apk) {
                    string embedded = "Assets/StreamingAssets/OmUpdate";
                    Directory.CreateDirectory(embedded);
                    foreach (string old in Directory.GetFiles(embedded, "content-*.zip").Where(p => Path.GetFileName(p) != name)) {
                        string archive = Path.Combine(Work, "native-asset-staging-archives", Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(archive);
                        File.Move(old, Path.Combine(archive, Path.GetFileName(old)));
                        if (File.Exists(old + ".meta")) File.Move(old + ".meta", Path.Combine(archive, Path.GetFileName(old) + ".meta"));
                    }
                    File.Copy(Path.Combine(publication, name), Path.Combine(embedded, name), true);
                    File.Copy(manifestFile, Path.Combine(embedded, "content-update.json"), true);
                    File.WriteAllText(Path.Combine(embedded, "boot.json"), JsonUtility.ToJson(new BootSettings {
                        baseVersion = ConfigManager.base_version, releaseVersion = ConfigManager.releaseVersion, baseline = native,
                        publicKey = publicKey, api = (Validation ? "http://127.0.0.1:58680" : ConfigManager.webApiUrl.TrimEnd('/')) + "/api/client-updates/android", website = ConfigManager.mobileDownloadUrl,
                    }, true));
                    AssetDatabase.Refresh();
                    string apkCodeFile = Path.Combine(Work, Validation ? "validation-apk-version-code.txt" : "apk-version-code.txt");
                    int lastCode = File.Exists(apkCodeFile) ? int.Parse(File.ReadAllText(apkCodeFile)) : 0;
                    int nextCode = checked(Math.Max(lastCode, PlayerSettings.Android.bundleVersionCode) + 1);
                    PlayerSettings.Android.bundleVersionCode = nextCode;
                    ResetAndroidAssetStaging();
                    BuildReport report;
                    BuildingBootstrapPlayer = true;
                    try {
                        using (new BootstrapRenderPipelineScope())
                        using (new ShellResourcesScope()) report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { BootScene }, target = BuildTarget.Android,
                            assetBundleManifestPath = Path.Combine(BundleCache, "bundles.manifest"),
                            locationPathName = Path.Combine(Output, "open_mahjong_unity.apk"), options = BuildOptions.None });
                    } finally { BuildingBootstrapPlayer = false; }
                    if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Android APK build failed.");
                    ValidateApkLayout(Path.Combine(Output, "open_mahjong_unity.apk"), manifest);
                    ContentStore.AtomicWrite(apkCodeFile, nextCode.ToString());
                    ContentStore.AtomicWrite(baselineFile, JsonUtility.ToJson(baseline, true));
                }
                File.WriteAllText(Path.Combine(Output, "android-content-build-result.json"), JsonUtility.ToJson(new BuildResultInfo {
                    success = true, apk = apk, baseVersion = manifest.baseVersion, releaseVersion = manifest.releaseVersion,
                    manifest = manifestFile, sha256 = hash, sizeBytes = manifest.sizeBytes,
                }, true));
                Directory.Delete(staging, true); File.Delete(zip);
                Debug.Log("ANDROID_CONTENT_BUILD_SUCCEEDED " + manifestFile);
            }
        } catch (Exception e) { Debug.LogException(e); throw; }
    }
    [Serializable] sealed class BuildResultInfo { public bool success, apk; public int baseVersion, releaseVersion; public string manifest, sha256; public long sizeBytes; }
    // Global URP assets deserialize before ContentBoot can load the hot game
    // assembly. Keep their game-defined renderer features out of the shell.
    sealed class BootstrapRenderPipelineScope : IDisposable {
        readonly UnityEngine.Rendering.RenderPipelineAsset pipeline;
        readonly UnityEngine.Rendering.RenderPipelineAsset[] qualityPipelines;
        readonly int quality;
        UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset bootstrapPipeline;
        UnityEngine.Rendering.Universal.UniversalRendererData bootstrapRenderer;
        string temporaryFolder;
        bool restored;
        public BootstrapRenderPipelineScope() {
            pipeline = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            quality = QualitySettings.GetQualityLevel();
            qualityPipelines = Enumerable.Range(0, QualitySettings.names.Length)
                .Select(QualitySettings.GetRenderPipelineAssetAt).ToArray();
            try {
                if (!Path.GetFullPath(".").Replace('\\', '/').Contains("/.om_workspace/"))
                    throw new BuildFailedException("Bootstrap render pipeline isolation requires an independent build copy.");
                var original = pipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
                if (original == null) throw new BuildFailedException("The game URP asset is unavailable for bootstrap isolation.");
                string folderName = "OmBootstrapRenderPipeline_" + Guid.NewGuid().ToString("N");
                if (string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets", folderName)))
                    throw new BuildFailedException("Cannot create temporary bootstrap render pipeline folder.");
                temporaryFolder = "Assets/" + folderName;
                // Preserve URP/global-settings compatibility, but never clone
                // the game renderer or its hot-update feature sub-assets.
                bootstrapRenderer = ScriptableObject.CreateInstance<UnityEngine.Rendering.Universal.UniversalRendererData>();
                bootstrapRenderer.name = "Android Bootstrap Renderer";
                if (bootstrapRenderer.rendererFeatures.Count != 0)
                    throw new BuildFailedException("The fresh bootstrap renderer unexpectedly contains renderer features.");
                AssetDatabase.CreateAsset(bootstrapRenderer, temporaryFolder + "/Renderer.asset");
                bootstrapPipeline = UnityEngine.Object.Instantiate(original);
                bootstrapPipeline.name = "Android Bootstrap Pipeline";
                using (var serialized = new SerializedObject(bootstrapPipeline)) {
                    var renderers = serialized.FindProperty("m_RendererDataList");
                    var defaultRenderer = serialized.FindProperty("m_DefaultRendererIndex");
                    if (renderers == null || defaultRenderer == null)
                        throw new BuildFailedException("Unsupported URP renderer data layout.");
                    renderers.arraySize = 1;
                    renderers.GetArrayElementAtIndex(0).objectReferenceValue = bootstrapRenderer;
                    defaultRenderer.intValue = 0;
                    var legacyRenderer = serialized.FindProperty("m_RendererData");
                    if (legacyRenderer != null) legacyRenderer.objectReferenceValue = null;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                AssetDatabase.CreateAsset(bootstrapPipeline, temporaryFolder + "/Pipeline.asset");
                AssetDatabase.SaveAssets();
                UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = bootstrapPipeline;
                for (int i = 0; i < qualityPipelines.Length; ++i) {
                    QualitySettings.SetQualityLevel(i, false);
                    QualitySettings.renderPipeline = bootstrapPipeline;
                }
                QualitySettings.SetQualityLevel(quality, false);
                // AssetBundle shader processing can leave this singleton
                // caching the original game pipeline across the Player build.
                UnityEditor.Rendering.CoreBuildData.instance.Dispose();
            } catch { Dispose(); throw; }
        }
        public void Dispose() {
            if (restored) return;
            try {
                UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = pipeline;
                for (int i = 0; i < qualityPipelines.Length; ++i) {
                    QualitySettings.SetQualityLevel(i, false);
                    QualitySettings.renderPipeline = qualityPipelines[i];
                }
                restored = true;
            } finally { QualitySettings.SetQualityLevel(quality, false); }
            UnityEditor.Rendering.CoreBuildData.instance.Dispose();
            if (temporaryFolder != null) {
                if (!AssetDatabase.DeleteAsset(temporaryFolder))
                    throw new BuildFailedException("Cannot remove temporary bootstrap render pipeline folder: " + temporaryFolder);
                temporaryFolder = null;
            }
            if (bootstrapPipeline != null && !AssetDatabase.Contains(bootstrapPipeline)) UnityEngine.Object.DestroyImmediate(bootstrapPipeline);
            if (bootstrapRenderer != null && !AssetDatabase.Contains(bootstrapRenderer)) UnityEngine.Object.DestroyImmediate(bootstrapRenderer);
            AssetDatabase.SaveAssets();
        }
    }
    [Serializable] sealed class ScriptingAssemblyRegistry { public string[] names; public int[] types; }
    static void ValidateApkLayout(string apk, ContentManifest expected) {
        var currentPlayerFiles = new HashSet<string>(Directory.GetFiles("Library/PlayerDataCache/Android/Data", "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName), StringComparer.Ordinal);
        using (var archive = ZipFile.OpenRead(apk)) {
            // HybridCLR patches this registry after Unity exports Player data.
            // Replaying asset copies can overwrite that patch while leaving a
            // valid APK whose hot-update scene scripts cannot deserialize.
            var registries = archive.Entries.Where(e => e.FullName == "assets/bin/Data/ScriptingAssemblies.json").ToArray();
            if (registries.Length != 1) throw new BuildFailedException("APK scripting assembly registry is missing or duplicated.");
            ScriptingAssemblyRegistry registry;
            try {
                using (var reader = new StreamReader(registries[0].Open()))
                    registry = JsonUtility.FromJson<ScriptingAssemblyRegistry>(reader.ReadToEnd());
            } catch (Exception error) { throw new BuildFailedException("APK scripting assembly registry is invalid: " + error.Message); }
            if (registry == null || registry.names == null || registry.types == null || registry.names.Length != registry.types.Length
                || registry.names.Any(string.IsNullOrEmpty) || registry.names.Distinct(StringComparer.Ordinal).Count() != registry.names.Length)
                throw new BuildFailedException("APK scripting assembly registry has invalid names/types pairs.");
            foreach (string assembly in (string[])Settings.GetType().GetField("hotUpdateAssemblies").GetValue(Settings)) {
                string name = assembly + ".dll";
                int at = Array.IndexOf(registry.names, name);
                if (at < 0 || registry.types[at] != 16)
                    throw new BuildFailedException("APK scripting assembly registry is missing the HybridCLR user assembly registration (type 16): " + name);
            }
            var payloads = archive.Entries.Where(e => e.FullName.StartsWith("assets/OmUpdate/content-", StringComparison.Ordinal) && e.FullName.EndsWith(".zip", StringComparison.Ordinal)).ToArray();
            if (payloads.Length != 1 || payloads[0].FullName != "assets/OmUpdate/" + expected.file || payloads[0].Length != expected.sizeBytes)
                throw new BuildFailedException("APK contains missing or stale embedded content. Refusing to publish Android update metadata.");
            using (var sha = SHA256.Create()) using (var stream = payloads[0].Open())
                if (ContentManifest.Hex(sha.ComputeHash(stream)) != expected.sha256) throw new BuildFailedException("APK embedded content hash mismatch.");
            foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("assets/bin/Data/", StringComparison.Ordinal))) {
                string name = Path.GetFileName(entry.FullName);
                int split = name.LastIndexOf(".split", StringComparison.Ordinal);
                if (split >= 0 && int.TryParse(name.Substring(split + 6), out _)) name = name.Substring(0, split);
                string guid = name.EndsWith(".resource", StringComparison.Ordinal) ? name.Substring(0, name.Length - 9) : name;
                if (guid.Length == 32 && guid.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')) && !currentPlayerFiles.Contains(name))
                    throw new BuildFailedException("APK contains stale Player data from an earlier build: " + entry.FullName);
            }
        }
    }
    static void ResetAndroidAssetStaging() {
        // Incremental Gradle export can retain removed GUID files and previous
        // StreamingAssets ZIPs. Keep native compiler caches; reset only assets.
        string project = Path.GetFullPath(".").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string assets = Path.GetFullPath("Library/Bee/Android/Prj/IL2CPP/Gradle/unityLibrary/src/main/assets");
        string archive = Path.GetFullPath(Path.Combine(Work, "native-asset-staging-archives", Guid.NewGuid().ToString("N"), "assets"));
        if (!project.Replace('\\', '/').Contains("/.om_workspace/")
            || !assets.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !archive.StartsWith(Work.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new BuildFailedException("Unexpected Android asset staging path.");
        if (!Directory.Exists(assets)) return;
        if (Directory.GetFiles(assets, "*", SearchOption.AllDirectories).Any(p => Path.GetFileName(p).StartsWith("README", StringComparison.OrdinalIgnoreCase)))
            throw new BuildFailedException("Protected README found in generated Android staging.");
        Directory.CreateDirectory(Path.GetDirectoryName(archive));
        Directory.Move(assets, archive);
    }
    // Resources are already present in the embedded complete content ZIP. Unity
    // otherwise includes them a second time even when only BootScene is built.
    // Move only resource assets in the isolated copy, retaining GUIDs so that
    // explicitly referenced framework assets still resolve. Scripts, assemblies
    // and protected README files stay at their original paths throughout.
    sealed class ShellResourcesScope : IDisposable {
        readonly List<KeyValuePair<string, string>> moved = new List<KeyValuePair<string, string>>();
        readonly List<string> includedAtlases = new List<string>();
        const string Packed = "Assets/OmShellPackedResources";
        const string TemporarySettings = "Assets/Resources/TMP Settings.asset";
        bool temporary, packedCreated;
        public ShellResourcesScope() {
            if (!Path.GetFullPath(".").Replace('\\', '/').Contains("/.om_workspace/"))
                throw new BuildFailedException("Resource isolation requires an independent build copy.");
            try {
                if (AssetDatabase.IsValidFolder(Packed)) throw new BuildFailedException("An interrupted shell build must be resynchronized before retrying.");
                // Atlas Include In Build is independent of Resources. The
                // content bundles already contain these packed textures.
                foreach (string path in AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)
                    && !p.Contains("/Editor/") && (p.EndsWith(".spriteatlas") || p.EndsWith(".spriteatlasv2")))) {
                    var importer = AssetImporter.GetAtPath(path) as UnityEditor.U2D.SpriteAtlasImporter;
                    var atlas = importer == null ? AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>(path) : null;
                    bool included = importer != null ? importer.includeInBuild : atlas != null && UnityEditor.U2D.SpriteAtlasExtensions.IsIncludeInBuild(atlas);
                    if (included) { includedAtlases.Add(path); SetAtlasIncluded(path, false); }
                }
                var assets = AssetDatabase.GetAllAssetPaths()
                    .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && p.Contains("/Resources/") && !p.Contains("/Editor/")
                        && !AssetDatabase.IsValidFolder(p) && !Path.GetFileName(p).StartsWith("README", StringComparison.OrdinalIgnoreCase)
                        && !new[] { ".cs", ".dll", ".asmdef", ".asmref" }.Contains(Path.GetExtension(p).ToLowerInvariant())).ToArray();
                AssetDatabase.CreateFolder("Assets", "OmShellPackedResources"); packedCreated = true;
                // Register all destination folders before batching asset moves.
                // CreateFolder inside StartAssetEditing can leave child folders
                // unavailable to MoveAsset until their imports have completed.
                foreach (string asset in assets) {
                    string destination = Packed + asset.Substring("Assets".Length).Replace("/Resources/", "/PackedResources/");
                    EnsureFolder(Path.GetDirectoryName(destination).Replace('\\', '/'));
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.StartAssetEditing();
                try {
                    foreach (string asset in assets) {
                        string destination = Packed + asset.Substring("Assets".Length).Replace("/Resources/", "/PackedResources/");
                        Move(asset, destination);
                        moved.Add(new KeyValuePair<string, string>(asset, destination));
                    }
                // TMP's AOT package can request its settings before game content
                // has loaded. Do not pull the full game's default fonts into the
                // fixed framework just to satisfy this early lookup.
                    if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                    AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<TMPro.TMP_Settings>(), TemporarySettings); temporary = true;
                } finally { AssetDatabase.StopAssetEditing(); }
                AssetDatabase.SaveAssets();
            } catch { Dispose(); throw; }
        }
        static void Move(string from, string to) {
            string error = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(error)) throw new BuildFailedException("Cannot isolate shell Resources: " + error);
        }
        static void EnsureFolder(string folder) {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
        static void SetAtlasIncluded(string path, bool included) {
            if (AssetImporter.GetAtPath(path) is UnityEditor.U2D.SpriteAtlasImporter importer) {
                importer.includeInBuild = included; importer.SaveAndReimport();
            } else {
                var atlas = AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>(path);
                if (atlas == null) throw new BuildFailedException("Cannot restore sprite atlas build flag: " + path);
                UnityEditor.U2D.SpriteAtlasExtensions.SetIncludeInBuild(atlas, included);
                EditorUtility.SetDirty(atlas);
            }
        }
        public void Dispose() {
            AssetDatabase.StartAssetEditing();
            try {
                if (temporary) { AssetDatabase.DeleteAsset(TemporarySettings); temporary = false; }
                for (int i = moved.Count - 1; i >= 0; --i) { Move(moved[i].Value, moved[i].Key); moved.RemoveAt(i); }
                if (packedCreated) { AssetDatabase.DeleteAsset(Packed); packedCreated = false; }
            } finally { AssetDatabase.StopAssetEditing(); }
            foreach (string path in includedAtlases) SetAtlasIncluded(path, true);
            includedAtlases.Clear();
            AssetDatabase.SaveAssets();
        }
    }
    static ContentIndex Pack(string staging, string metadataSource, string[] metadata, string baseline) {
        var builds = new List<AssetBundleBuild>();
        var resources = new List<ResourceEntry>();
        foreach (string path in AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && !AssetDatabase.IsValidFolder(p)).OrderBy(p => p, StringComparer.Ordinal)) {
            if (DevelopmentExpressionBuild.ExcludedFromRelease(path)) continue;
            int at = path.LastIndexOf("/Resources/", StringComparison.Ordinal);
            if (at < 0 || path.Contains("/Editor/") || path.EndsWith(".cs") || path.EndsWith(".asmdef") || path.EndsWith(".asmref")) continue;
            string key = path.Substring(at + 11); key = key.Substring(0, key.Length - Path.GetExtension(key).Length);
            var assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (assetType == null || assetType.Namespace?.StartsWith("UnityEditor", StringComparison.Ordinal) == true) continue;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            resources.Add(new ResourceEntry { key = key, asset = path, bundle = "bundles/resources",
                type = assetType.AssemblyQualifiedName, sprites = importer != null && importer.textureType == TextureImporterType.Sprite });
        }
        var gamePipeline = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
        string gamePipelinePath = AssetDatabase.GetAssetPath(gamePipeline);
        if (gamePipeline == null || string.IsNullOrEmpty(gamePipelinePath))
            throw new BuildFailedException("The game render pipeline is unavailable for Android content.");
        const string pipelineKey = "OmUpdate/GameRenderPipeline";
        if (resources.Any(r => r.key.Equals(pipelineKey, StringComparison.OrdinalIgnoreCase)))
            throw new BuildFailedException("Reserved Android content resource key is already in use: " + pipelineKey);
        resources.Add(new ResourceEntry { key = pipelineKey, asset = gamePipelinePath,
            bundle = "bundles/resources", type = gamePipeline.GetType().AssemblyQualifiedName });
        builds.Add(new AssetBundleBuild { assetBundleName = "resources", assetNames = resources.Select(r => r.asset).Distinct(StringComparer.Ordinal).ToArray() });
        builds.Add(new AssetBundleBuild { assetBundleName = "scenes", assetNames = new[] { "Assets/Scenes/MainScene.unity" } });
        // Explicit resource roots already belong to resources. Assign implicit
        // dependencies used by both bundles once, instead of embedding a copy
        // into each bundle (fonts, materials and shader dependencies included).
        var explicitAssets = new HashSet<string>(builds.SelectMany(b => b.assetNames), StringComparer.Ordinal);
        var resourceDependencies = new HashSet<string>(AssetDatabase.GetDependencies(builds[0].assetNames, true), StringComparer.Ordinal);
        var shared = AssetDatabase.GetDependencies(builds[1].assetNames, true)
            .Where(p => resourceDependencies.Contains(p) && !explicitAssets.Contains(p) && p.StartsWith("Assets/", StringComparison.Ordinal)
                && !AssetDatabase.IsValidFolder(p) && !p.Contains("/Editor/")
                && !new[] { ".cs", ".dll", ".asmdef", ".asmref" }.Contains(Path.GetExtension(p).ToLowerInvariant()))
            .Where(p => { var type = AssetDatabase.GetMainAssetTypeAtPath(p);
                return type != null && type != typeof(DefaultAsset) && type.Namespace?.StartsWith("UnityEditor", StringComparison.Ordinal) != true; })
            .Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (shared.Length > 0) builds.Insert(0, new AssetBundleBuild { assetBundleName = "shared", assetNames = shared });
        Debug.Log("[AndroidContent] Shared implicit dependencies (" + shared.Length + "): " + string.Join(", ", shared));
        // Match the ordinary Player build's TMP preprocessor: discard only the
        // dynamic glyph caches whose own clear-on-build flag is enabled.
        new TMPro.TMP_PreBuildProcessor().OnPreprocessBuild(null);
        AssetDatabase.SaveAssets();
        // Keep Unity's own dependency/type-tree cache between runs. Each export
        // still contains both complete bundles, never a hand-maintained delta.
        string bundleCache = BundleCache;
        Directory.CreateDirectory(bundleCache);
        BuildingContentBundles = true;
        // The FBX's imported material does not use COLOR, but the runtime tile
        // material does (red=face, green=back, blue=edge). Bundle-local shader
        // analysis cannot see that material switch and strips this channel.
        bool stripMeshChannels = PlayerSettings.stripUnusedMeshComponents;
        try {
            PlayerSettings.stripUnusedMeshComponents = false;
            if (BuildPipeline.BuildAssetBundles(bundleCache, builds.ToArray(), BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.Android) == null)
                throw new BuildFailedException("Android content AssetBundle build failed.");
        } finally {
            PlayerSettings.stripUnusedMeshComponents = stripMeshChannels;
            BuildingContentBundles = false;
        }
        string bundleDir = Path.Combine(staging, "bundles"); Directory.CreateDirectory(bundleDir);
        foreach (var build in builds) File.Copy(Path.Combine(bundleCache, build.assetBundleName), Path.Combine(bundleDir, build.assetBundleName), true);
        Directory.CreateDirectory(Path.Combine(staging, "code")); Directory.CreateDirectory(Path.Combine(staging, "metadata"));
        var names = (string[])Settings.GetType().GetField("hotUpdateAssemblies").GetValue(Settings);
        string dllRoot = "Library/HybridCLR/HotUpdateDlls/Android";
        var ordered = SortAssemblies(names, dllRoot);
        foreach (var name in ordered) File.Copy(Path.Combine(dllRoot, name), Path.Combine(staging, "code", name), true);
        foreach (var name in metadata) File.Copy(Path.Combine(metadataSource, name), Path.Combine(staging, "metadata", name), true);
        var index = new ContentIndex { baseVersion = ConfigManager.base_version, releaseVersion = ConfigManager.releaseVersion, baseline = baseline,
            scene = "Assets/Scenes/MainScene.unity", assemblies = ordered, metadata = metadata,
            bundles = builds.Select(b => "bundles/" + b.assetBundleName).ToArray(), resources = resources.ToArray(),
            initializers = CollectInitializers(ordered, dllRoot) };
        index.files = Directory.GetFiles(staging, "*", SearchOption.AllDirectories).Select(f => new ContentFile {
            path = f.Substring(staging.Length + 1).Replace('\\', '/'), size = new FileInfo(f).Length, sha256 = ContentManifest.HashFile(f),
        }).OrderBy(f => f.path, StringComparer.Ordinal).ToArray();
        File.WriteAllText(Path.Combine(staging, "content-index.json"), JsonUtility.ToJson(index, true));
        return index;
    }
    static ContentInitializer[] CollectInitializers(string[] assemblies, string root) {
        var result = new List<ContentInitializer>();
        using (var resolver = new Mono.Cecil.DefaultAssemblyResolver()) {
        resolver.AddSearchDirectory(root);
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(RuntimeInitializeOnLoadMethodAttribute).Assembly.Location));
        foreach (string assembly in assemblies) {
            using (var module = Mono.Cecil.AssemblyDefinition.ReadAssembly(Path.Combine(root, assembly), new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver })) {
                foreach (var type in module.MainModule.GetTypes()) foreach (var method in type.Methods) {
                    var attribute = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == typeof(RuntimeInitializeOnLoadMethodAttribute).FullName);
                    if (attribute == null) continue;
                    int phase = attribute.ConstructorArguments.Count == 0 ? (int)RuntimeInitializeLoadType.AfterSceneLoad
                        : Convert.ToInt32(attribute.ConstructorArguments[0].Value);
                    if (!method.IsStatic || method.HasParameters || method.HasGenericParameters || type.HasGenericParameters
                        || !Enum.IsDefined(typeof(RuntimeInitializeLoadType), phase))
                        throw new BuildFailedException("Invalid runtime initializer: " + method.FullName);
                    result.Add(new ContentInitializer { assembly = Path.GetFileNameWithoutExtension(assembly),
                        type = type.FullName.Replace('/', '+'), method = method.Name, phase = phase });
                }
            }
        }
        }
        return result.OrderBy(i => i.type == "OmAndroidContentFonts" ? 0 : 1).ToArray();
    }
    static string[] SortAssemblies(string[] names, string root) {
        var result = new List<string>(); var visiting = new HashSet<string>(); var visited = new HashSet<string>();
        Action<string> visit = null;
        visit = name => {
            if (visited.Contains(name)) return;
            if (!visiting.Add(name)) throw new BuildFailedException("Circular hot-update assembly dependency: " + name);
            // Reflection-only inspection avoids loading the game into the Editor's current domain.
            using (var module = Mono.Cecil.AssemblyDefinition.ReadAssembly(Path.Combine(root, name + ".dll")))
                foreach (var dependency in module.MainModule.AssemblyReferences.Where(r => names.Contains(r.Name)).OrderBy(r => r.Name)) visit(dependency.Name);
            visiting.Remove(name); visited.Add(name); result.Add(name + ".dll");
        };
        foreach (string name in names) visit(name);
        return result.ToArray();
    }
    static void CheckAotReferences(string staging, string baselineRoot, bool create) {
        var signatures = new SortedSet<string>(StringComparer.Ordinal);
        var hotNames = Directory.GetFiles(Path.Combine(staging, "code"), "*.dll").Select(Path.GetFileNameWithoutExtension).ToHashSet();
        using (var resolver = new Mono.Cecil.DefaultAssemblyResolver()) {
        resolver.AddSearchDirectory(Path.Combine(staging, "metadata"));
        resolver.AddSearchDirectory(Path.Combine(staging, "code"));
        foreach (var dll in Directory.GetFiles(Path.Combine(staging, "code"), "*.dll")) {
            using (var module = Mono.Cecil.AssemblyDefinition.ReadAssembly(dll, new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver })) {
                foreach (var member in module.MainModule.GetMemberReferences()) {
                    // CLR array Get/Set/Address/.ctor are runtime pseudo-methods;
                    // they have no MethodDefinition in a stripped AOT assembly.
                    if (member.DeclaringType is Mono.Cecil.ArrayType) continue;
                    var scope = member.DeclaringType.Scope as Mono.Cecil.AssemblyNameReference;
                    if (scope == null || hotNames.Contains(scope.Name)) continue;
                    Mono.Cecil.MemberReference resolved;
                    try {
                        resolved = member is Mono.Cecil.MethodReference method ? (Mono.Cecil.MemberReference)method.Resolve()
                            : member is Mono.Cecil.FieldReference field ? field.Resolve() : null;
                    } catch (Exception e) { throw new BuildFailedException("AOT dependency is unavailable in the archived framework: " + member.FullName + "\n" + e.Message); }
                    if (resolved == null) throw new BuildFailedException("AOT dependency was stripped from the framework. Increment base_version and rebuild APK: " + member.FullName);
                    // Compare resolved generic definitions, so List<NewRule>.Add
                    // does not falsely count as a new native API for every rule.
                    signatures.Add(scope.Name + "::" + resolved.FullName);
                }
            }
        }
        }
        string filename = Path.Combine(baselineRoot, "aot-references.txt");
        if (create) File.WriteAllLines(filename, signatures);
    }
}
#endif
