#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Each successful APK build produces a matching sidecar. Publishing a new platform
// version does not imply that its APK has already reached the download server.
public sealed class AndroidUpdateBuild : IPostprocessBuildWithReport {
    public int callbackOrder => 1000;

    public void OnPostprocessBuild(BuildReport report) {
        // Unity has not finalized summary.result when it invokes this callback.
        if (report.summary.platform != BuildTarget.Android || report.summary.result == BuildResult.Failed ||
            report.summary.result == BuildResult.Cancelled) return;
        string apk = report.summary.outputPath;
        if (!File.Exists(apk) || !apk.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) return;
        if ((report.summary.options & BuildOptions.Development) == 0) VerifyApkSignatures(apk);
        WriteManifest(apk, PlayerSettings.bundleVersion, PlayerSettings.Android.bundleVersionCode,
            ConfigManager.releaseVersion, PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android));
    }

    internal static void VerifyApkSignatures(string apk) {
        var tools = new DirectoryInfo(Path.Combine(UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath, "build-tools"));
        var signer = tools.Exists ? tools.GetDirectories()
            .Where(d => Version.TryParse(d.Name, out _)).OrderByDescending(d => new Version(d.Name))
            .Select(d => Path.Combine(d.FullName, "lib", "apksigner.jar")).FirstOrDefault(File.Exists) : null;
        var java = Path.Combine(UnityEditor.Android.AndroidExternalToolsSettings.jdkRootPath, "bin",
            Application.platform == RuntimePlatform.WindowsEditor ? "java.exe" : "java");
        if (signer == null || !File.Exists(java))
            throw new BuildFailedException("Cannot verify the release APK: check Android SDK Build Tools and JDK in Unity External Tools.");
        var start = new System.Diagnostics.ProcessStartInfo {
            FileName = java,
            // Include API 23 in the signature check so apksigner actually exercises v1 even when minSdk >= 24.
            Arguments = "-jar \"" + signer + "\" verify --verbose --min-sdk-version 23 \"" + apk + "\"",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        using (var proc = System.Diagnostics.Process.Start(start)) {
            var stdout = proc.StandardOutput.ReadToEndAsync();
            var stderr = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(120000)) {
                proc.Kill();
                throw new BuildFailedException("Timed out verifying APK signatures: " + apk);
            }
            string report = stdout.GetAwaiter().GetResult();
            string errors = stderr.GetAwaiter().GetResult();
            if (proc.ExitCode != 0 || !Regex.IsMatch(report, @"(?m)^Verified using v1 scheme[^\r\n]*:\s*true\s*$")
                    || !Regex.IsMatch(report, @"(?m)^Verified using v2 scheme[^\r\n]*:\s*true\s*$"))
                throw new BuildFailedException("Release APK must verify with both JAR (v1) and v2 signatures: " + apk + "\n" + report + errors);
        }
    }

    public static void WriteManifest(string apk, string versionName, int versionCode, int releaseVersion, string packageName) {
        string digest;
        using (var stream = File.OpenRead(apk))
        using (var hash = SHA256.Create()) digest = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        var manifest = new AndroidUpdateManifest {
            schemaVersion = 1, versionName = versionName, versionCode = versionCode,
            releaseVersion = releaseVersion, packageName = packageName, file = Path.GetFileName(apk),
            sizeBytes = new FileInfo(apk).Length, sha256 = digest, notes = "",
            baseVersion = ConfigManager.base_version,
        };
        string destination = Path.Combine(Path.GetDirectoryName(apk), "android-update.json");
        string temporary = destination + ".tmp";
        File.WriteAllText(temporary, JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));
        if (File.Exists(destination)) File.Replace(temporary, destination, null);
        else File.Move(temporary, destination);
        Debug.Log("Android update manifest generated: " + destination);
    }
}

// Build Profiles can build Android while the Editor assembly still uses the
// previously active platform's defines, so this callback must not use UNITY_ANDROID.
public sealed class AndroidUpdateGradle : UnityEditor.Android.IPostGenerateGradleAndroidProject {
    private const string LauncherSigning = @"androidComponents.finalizeDsl { dsl ->
    def releaseSigner = dsl.buildTypes.getByName('release').signingConfig
    if (releaseSigner == null) throw new GradleException('No signing configuration on the release APK launcher.')
    releaseSigner.enableV1Signing = true
    releaseSigner.enableV2Signing = true
}";

    public int callbackOrder => 1000;
    public void OnPostGenerateGradleAndroidProject(string path) {
        string file = Path.Combine(path, "SalasasaUpdater.androidlib", "build.gradle");
        if (!File.Exists(file)) throw new BuildFailedException("Android updater library is missing from the Gradle project.");
        string source = File.ReadAllText(file);
        const string rules = "    defaultConfig { consumerProguardFiles 'proguard-rules.pro' }";
        if (!source.Contains("consumerProguardFiles")) {
            source += "\nandroid {\n" + rules + "\n}\n";
            File.WriteAllText(file, source, new UTF8Encoding(false));
        }
        // Unity passes the unityLibrary module; only its sibling launcher produces the application APK.
        string launcher = Path.GetFullPath(Path.Combine(path, "..", "launcher", "build.gradle"));
        if (!File.Exists(launcher))
            throw new BuildFailedException("Android release signing: Unity launcher/build.gradle missing at " + launcher);
        string gradle = File.ReadAllText(launcher);
        if (!gradle.Replace("\r\n", "\n").Contains(LauncherSigning.Replace("\r\n", "\n")))
            File.AppendAllText(launcher, "\n" + LauncherSigning + "\n", new UTF8Encoding(false));
        Debug.Log("Android updater consumer ProGuard rules configured.");
    }
}
#endif
