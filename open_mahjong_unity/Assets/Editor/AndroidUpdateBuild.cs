#if UNITY_EDITOR
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
        WriteManifest(apk, PlayerSettings.bundleVersion, PlayerSettings.Android.bundleVersionCode,
            ConfigManager.releaseVersion, PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android));
    }

    public static void WriteManifest(string apk, string versionName, int versionCode, int releaseVersion, string packageName) {
        string digest;
        using (var stream = File.OpenRead(apk))
        using (var hash = SHA256.Create()) digest = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        var manifest = new AndroidUpdateManifest {
            schemaVersion = 1, versionName = versionName, versionCode = versionCode,
            releaseVersion = releaseVersion, packageName = packageName, file = Path.GetFileName(apk),
            sizeBytes = new FileInfo(apk).Length, sha256 = digest, notes = "",
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
        Debug.Log("Android updater consumer ProGuard rules configured.");
    }
}
#endif
