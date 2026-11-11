using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// Each revision is a complete snapshot of all resource overrides. Include any
// earlier overrides that must remain; omitted keys revert to bundled resources.
// Does not build a player or publish.
public static class IosResourceBuild
{
    const long MaximumBundleBytes = 256L * 1024 * 1024;
    static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".wav", ".mp3", ".ogg", ".json", ".txt" };

    [Serializable]
    sealed class AssetSelection { public string[] assets; }

    public static string PlayerVersion => IosContentManifest.NormalizeVersion(PlayerSettings.bundleVersion);

    public static string NormalizeResourceKey(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)
            || path.Contains("..") || path.Contains('\\') || path.Contains(':') || !Extensions.Contains(Path.GetExtension(path)))
            throw new BuildFailedException("Only explicit PNG/JPG/audio/JSON/TXT assets are allowed: " + path);
        int start = path.LastIndexOf("/Resources/", StringComparison.Ordinal);
        if (start < 0) throw new BuildFailedException("Resource content must have a Resources-relative key: " + path);
        string relative = path.Substring(start + "/Resources/".Length);
        string key = relative.Substring(0, relative.Length - Path.GetExtension(relative).Length).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 240 || key.StartsWith("/"))
            throw new BuildFailedException("Resource key must contain 1 to 240 characters: " + path);
        return key;
    }

    [MenuItem("Tools/Salasasa/iOS/Build Selected Resource Content")]
    public static void BuildSelected()
    {
        // A unique increasing revision, independent of player/protocol versions.
        int version = checked((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Build(Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath).ToArray(), version);
    }

    // CI callers supply {"assets":["Assets/.../Resources/...png"]}, not a folder.
    public static void BuildFromEnvironment()
    {
        string selectionPath = Environment.GetEnvironmentVariable("SALASASA_IOS_RESOURCE_SELECTION");
        if (string.IsNullOrWhiteSpace(selectionPath) || !File.Exists(selectionPath))
            throw new BuildFailedException("Set SALASASA_IOS_RESOURCE_SELECTION to the resource selection JSON file.");
        if (!int.TryParse(Environment.GetEnvironmentVariable("SALASASA_IOS_CONTENT_VERSION"),
            NumberStyles.None, CultureInfo.InvariantCulture, out int version))
            throw new BuildFailedException("Set SALASASA_IOS_CONTENT_VERSION to a positive integer.");
        Build(JsonUtility.FromJson<AssetSelection>(File.ReadAllText(selectionPath))?.assets, version);
    }

    public static string Build(string[] selectedPaths, int contentVersion)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new BuildFailedException("Exit Play Mode and wait for the current build before building iOS content.");
        string workspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        if (File.Exists(Path.Combine(workspace, ".om_workspace/mil-forks-20261002/unity-validation.lock")))
            throw new BuildFailedException("Shared Unity validation lock exists; coordinate before building content.");
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
            throw new BuildFailedException("Select the iOS build target before building iOS resources.");
        if (contentVersion < 1 || selectedPaths == null || selectedPaths.Length == 0)
            throw new BuildFailedException("Select explicit resource files and supply a positive content version.");

        string[] paths = selectedPaths.Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (paths.Length > 1024) throw new BuildFailedException("An iOS content release can contain at most 1024 resource keys.");
        var keys = new List<string>();
        foreach (string path in paths)
        {
            string key = NormalizeResourceKey(path);
            if (AssetDatabase.IsValidFolder(path)) throw new BuildFailedException("Select individual files, not folders: " + path);
            if (keys.Contains(key)) throw new BuildFailedException("Duplicate Resources key: " + key);
            Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type != typeof(Texture2D) && type != typeof(AudioClip) && type != typeof(TextAsset))
                throw new BuildFailedException("Unsupported resource asset type: " + path);
            // Bundles include dependencies. Prevent a selected file from pulling in executable or complex assets.
            foreach (string dependency in AssetDatabase.GetDependencies(path, true))
                if (dependency != path && !Extensions.Contains(Path.GetExtension(dependency)))
                    throw new BuildFailedException("Resource has an unsupported dependency: " + dependency);
            keys.Add(key);
        }

        string output = Path.Combine(workspace, ".om_workspace/ios-releases", contentVersion.ToString(CultureInfo.InvariantCulture));
        if (Directory.Exists(output)) throw new BuildFailedException("Content revision already exists; choose a new version: " + output);
        string staging = Path.Combine(output, "build");
        Directory.CreateDirectory(staging);
        // The current revision remains loaded until restart. A unique internal
        // bundle name permits validating a downloaded revision alongside it.
        string internalName = "resources-" + contentVersion.ToString(CultureInfo.InvariantCulture) + ".bundle";
        var map = new[] { new AssetBundleBuild {
            assetBundleName = internalName, assetNames = paths, addressableNames = keys.ToArray()
        } };
        var built = BuildPipeline.BuildAssetBundles(staging, map,
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, BuildTarget.iOS);
        if (!built) throw new BuildFailedException("iOS resource bundle build failed; inspect the build log.");
        string bundle = Path.Combine(staging, internalName);
        long size = new FileInfo(bundle).Length;
        if (size < 1 || size > MaximumBundleBytes)
            throw new BuildFailedException("iOS content bundle exceeds the 256 MiB limit; reduce the override snapshot.");
        string digest;
        using (var stream = File.OpenRead(bundle))
        using (var sha = SHA256.Create())
            digest = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        string file = "content-" + digest + ".bundle";
        File.Copy(bundle, Path.Combine(output, file));
        var manifest = new IosContentManifest {
            schemaVersion = 1, platform = "ios",
            bundleId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS),
            playerVersion = PlayerVersion, releaseVersion = ConfigManager.releaseVersion,
            contentVersion = contentVersion, file = file, sizeBytes = size, sha256 = digest,
            keys = keys.ToArray()
        };
        // Apply the consumer's compatibility rules before emitting a release.
        manifest.Validate(manifest.bundleId, PlayerVersion, ConfigManager.releaseVersion);
        string manifestPath = Path.Combine(output, "ios-content.json");
        string json = JsonUtility.ToJson(manifest, true);
        if (Encoding.UTF8.GetByteCount(json) > 32768)
            throw new BuildFailedException("iOS resource manifest exceeds the server's 32 KiB limit; reduce the selection.");
        File.WriteAllText(manifestPath, json, new UTF8Encoding(false));
        Debug.Log("iOS resource content prepared locally (not published): " + manifestPath);
        return manifestPath;
    }
}
