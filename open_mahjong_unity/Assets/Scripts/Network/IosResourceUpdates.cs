using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

// iOS content releases contain data/media only. Protocol and executable updates
// always require a new signed player, never Android's assembly-loading updater.
[Serializable]
public sealed class IosContentManifest {
    public int schemaVersion, releaseVersion, contentVersion;
    public string platform, bundleId, playerVersion, file, sha256, downloadPath, status;
    public long sizeBytes;
    public string[] keys;

    public const long MaximumBytes = 256L * 1024 * 1024;
    public static string NormalizeVersion(string value) {
        var parts = (value ?? "").Split('.');
        return parts.Length >= 3 ? string.Join(".", parts.Take(3)) : value;
    }
    public void Validate(string identifier, string version, int release) {
        if (schemaVersion != 1 || platform != "ios" || bundleId != identifier
            || playerVersion != NormalizeVersion(version) || releaseVersion != release
            || contentVersion < 1 || sizeBytes < 1 || sizeBytes > MaximumBytes
            || !OmUpdate.ContentManifest.IsHash(sha256) || file != "content-" + sha256 + ".bundle"
            || keys == null || keys.Length == 0 || keys.Length > 1024
            || keys.Any(k => string.IsNullOrWhiteSpace(k) || k.Length > 240 || k != k.ToLowerInvariant()
                || k.Contains("..") || k.Contains("\\") || k.StartsWith("/") || k.Contains(":") || k.Any(char.IsControl))
            || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new InvalidDataException("iOS 资源更新与当前客户端不兼容。");
    }
}

public static class IosResourceCache {
    static bool initialized;
    static AssetBundle bundle;
    static HashSet<string> keys;
    public static int ActiveVersion { get; private set; }
    public static string Root => Path.Combine(Application.persistentDataPath, "ios-resources",
        IosContentManifest.NormalizeVersion(Application.version) + "-" + ConfigManager.releaseVersion);

    // Activated once per process. Downloaded updates never unload assets used by
    // a live match; the next launch reads the atomic manifest pointer.
    static void EnsureLoaded() {
        if (initialized) return;
        initialized = true;
#if UNITY_IOS && !UNITY_EDITOR
        try {
            string pointer = Path.Combine(Root, "current.json");
            if (!File.Exists(pointer) || new FileInfo(pointer).Length > 65536) return;
            var manifest = JsonUtility.FromJson<IosContentManifest>(File.ReadAllText(pointer));
            manifest.Validate(Application.identifier, Application.version, ConfigManager.releaseVersion);
            string path = Path.Combine(Root, manifest.file);
            if (!File.Exists(path) || new FileInfo(path).Length != manifest.sizeBytes
                || OmUpdate.ContentManifest.HashFile(path) != manifest.sha256) return;
            bundle = AssetBundle.LoadFromFile(path);
            if (!bundle) return;
            keys = new HashSet<string>(manifest.keys, StringComparer.Ordinal);
            ActiveVersion = manifest.contentVersion;
            // Old bundles are no longer needed after a successful activation.
            foreach (var old in Directory.GetFiles(Root, "content-*.bundle"))
                if (old != path) try { File.Delete(old); } catch (IOException) { }
        } catch (Exception e) { Debug.LogWarning("[iOS resources] 使用随包资源：" + e.Message); }
#endif
    }
    public static T Load<T>(string path) where T : UnityEngine.Object {
        EnsureLoaded();
        string key = path?.ToLowerInvariant();
        if (bundle && key != null && keys.Contains(key)) {
            try { var asset = bundle.LoadAsset<T>(key); if (asset) return asset; }
            catch (Exception e) { Debug.LogWarning("[iOS resources] 资源回退：" + e.Message); }
        }
        return Resources.Load<T>(path);
    }
    public static T[] LoadAll<T>(string path) where T : UnityEngine.Object {
        EnsureLoaded();
        string key = path?.ToLowerInvariant();
        if (bundle && key != null && keys.Contains(key)) {
            try {
                var values = bundle.LoadAssetWithSubAssets<T>(key);
                if (values.Length != 0) return values;
            } catch (Exception e) { Debug.LogWarning("[iOS resources] 子资源回退：" + e.Message); }
        }
        return Resources.LoadAll<T>(path);
    }
    public static int InstalledVersion { get { EnsureLoaded(); return ActiveVersion; } }
}

public sealed class IosResourceUpdates : MonoBehaviour {
    // Stop oversized metadata before it becomes a large in-memory response.
    sealed class LimitedJson : DownloadHandlerScript {
        readonly MemoryStream bytes = new MemoryStream();
        protected override bool ReceiveData(byte[] data, int length) {
            if (data == null || length < 0 || bytes.Length + length > 65536) return false;
            bytes.Write(data, 0, length); return true;
        }
        public string Text => System.Text.Encoding.UTF8.GetString(bytes.ToArray());
        protected override byte[] GetData() => bytes.ToArray();
    }
    [Serializable] sealed class VersionReply { public string status, platform, bundleId; public int schemaVersion, releaseVersion; }
    // Use the same game release number as Android. Repacking a compatible
    // release (or incrementing an Apple submission build number) must not nag players.
    public static bool ShouldOfferPlayerUpdate(int offeredRelease, int installedRelease) =>
        installedRelease > 0 && offeredRelease > installedRelease;
    static IosResourceUpdates instance;
    UnityWebRequest activeRequest;
    bool busy;
    int notifiedRelease;
    float lastCheck = -600;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() {
#if UNITY_IOS && !UNITY_EDITOR
        if (instance) return;
        instance = new GameObject("IosResourceUpdates").AddComponent<IosResourceUpdates>();
        DontDestroyOnLoad(instance.gameObject);
#endif
    }
    IEnumerator Start() { yield return new WaitForSecondsRealtime(3); Check(); }
    void OnApplicationPause(bool paused) { if (!paused) Check(); }
    void Check() {
        if (busy || Time.realtimeSinceStartup - lastCheck < 300) return;
        lastCheck = Time.realtimeSinceStartup;
        StartCoroutine(FetchSafely());
    }
    public static void OpenDownloadPage() => Application.OpenURL(ConfigManager.iosDownloadUrl);
    IEnumerator FetchSafely() {
        var fetch = Fetch();
        try {
            while (true) {
                bool next;
                try { next = fetch.MoveNext(); }
                catch (Exception e) { Debug.LogWarning("[iOS resources] 暂时无法更新，将继续使用现有资源：" + e.Message); break; }
                if (!next) break;
                yield return fetch.Current;
            }
        } finally { (fetch as IDisposable)?.Dispose(); }
    }
    IEnumerator Fetch() {
        busy = true;
        try {
            string endpoint = ConfigManager.webApiUrl.TrimEnd('/') + "/api/client-updates/ios";
            using (var version = UnityWebRequest.Get(endpoint + "/version")) {
                var json = new LimitedJson(); version.downloadHandler = json;
                activeRequest = version; version.timeout = 20; version.redirectLimit = 0;
                yield return version.SendWebRequest();
                if (version.result == UnityWebRequest.Result.Success && version.downloadedBytes <= 32768) {
                    VersionReply info = null;
                    try { info = JsonUtility.FromJson<VersionReply>(json.Text); } catch (ArgumentException) { }
                    if (info?.status == "available" && info.schemaVersion == 1 && info.platform == "ios"
                        && info.bundleId == Application.identifier
                        && ShouldOfferPlayerUpdate(info.releaseVersion, ConfigManager.releaseVersion)
                        && notifiedRelease != info.releaseVersion) {
                        notifiedRelease = info.releaseVersion;
                        NotificationManager.Instance?.ShowMessage("iOS 版本更新", "已有新客户端版本，请前往 iOS 下载页按当前安装渠道升级。",
                            new MessageAction("稍后"), new MessageAction("前往更新", OpenDownloadPage));
                    }
                }
                activeRequest = null;
            }
            string url = endpoint + "/content?playerVersion=" + UnityWebRequest.EscapeURL(IosContentManifest.NormalizeVersion(Application.version))
                + "&releaseVersion=" + ConfigManager.releaseVersion + "&contentVersion=" + IosResourceCache.InstalledVersion;
            IosContentManifest manifest = null;
            using (var request = UnityWebRequest.Get(url)) {
                var json = new LimitedJson(); request.downloadHandler = json;
                activeRequest = request; request.timeout = 20; request.redirectLimit = 0;
                yield return request.SendWebRequest();
                activeRequest = null;
                if (request.result != UnityWebRequest.Result.Success || request.downloadedBytes > 65536) yield break;
                try {
                    manifest = JsonUtility.FromJson<IosContentManifest>(json.Text);
                    if (manifest?.status != "available") yield break;
                    manifest.Validate(Application.identifier, Application.version, ConfigManager.releaseVersion);
                    if (manifest.downloadPath != "/api/client-updates/ios/content/" + manifest.sha256)
                        throw new InvalidDataException("资源下载地址无效。");
                } catch (Exception e) { Debug.LogWarning("[iOS resources] " + e.Message); manifest = null; }
            }
            if (manifest == null || manifest.contentVersion <= IosResourceCache.InstalledVersion) yield break;
            string root = IosResourceCache.Root;
            string path = Path.Combine(root, manifest.file), partial = path + ".part";
            bool cached = false;
            IosContentManifest stagedManifest = null;
            try {
                Directory.CreateDirectory(root);
                string pointer = Path.Combine(root, "current.json");
                if (File.Exists(pointer) && new FileInfo(pointer).Length <= 65536) {
                    var staged = JsonUtility.FromJson<IosContentManifest>(File.ReadAllText(pointer));
                    if (staged != null) {
                        staged.Validate(Application.identifier, Application.version, ConfigManager.releaseVersion);
                        string stagedFile = Path.Combine(root, staged.file);
                        if (staged.contentVersion >= manifest.contentVersion
                            && File.Exists(stagedFile) && new FileInfo(stagedFile).Length == staged.sizeBytes)
                            stagedManifest = staged;
                    }
                }
            } catch (Exception e) { Debug.LogWarning("[iOS resources] 将重新下载以修复本地更新信息：" + e.Message); }
            if (stagedManifest != null) {
                var verifyStaged = Task.Run(() => OmUpdate.ContentManifest.HashFile(Path.Combine(root, stagedManifest.file)) == stagedManifest.sha256);
                while (!verifyStaged.IsCompleted) yield return null;
                cached = !verifyStaged.IsFaulted && verifyStaged.Result;
            }
            if (cached) yield break;
            using (var download = UnityWebRequest.Get(new Uri(new Uri(endpoint), manifest.downloadPath))) {
                activeRequest = download;
                download.downloadHandler = new DownloadHandlerFile(partial) { removeFileOnAbort = true };
                download.timeout = 180; download.redirectLimit = 0;
                var operation = download.SendWebRequest();
                while (!operation.isDone) {
                    if (download.downloadedBytes > (ulong)manifest.sizeBytes) { download.Abort(); break; }
                    yield return null;
                }
                activeRequest = null;
                if (download.result != UnityWebRequest.Result.Success) { TryDelete(partial); yield break; }
            }
            var check = Task.Run(() => new FileInfo(partial).Length == manifest.sizeBytes
                && OmUpdate.ContentManifest.HashFile(partial) == manifest.sha256);
            while (!check.IsCompleted) yield return null;
            if (check.IsFaulted || !check.Result) { TryDelete(partial); yield break; }
            // Validate it as an iOS bundle before changing the next-launch pointer.
            var probe = AssetBundle.LoadFromFileAsync(partial);
            yield return probe;
            if (!probe.assetBundle) { TryDelete(partial); yield break; }
            bool complete = manifest.keys.All(probe.assetBundle.Contains);
            probe.assetBundle.Unload(true);
            if (!complete) { TryDelete(partial); yield break; }
            try {
                if (File.Exists(path)) File.Delete(path);
                File.Move(partial, path);
                string pointer = Path.Combine(root, "current.json"), next = pointer + ".tmp";
                File.WriteAllText(next, JsonUtility.ToJson(manifest));
                if (File.Exists(pointer)) File.Replace(next, pointer, null);
                else File.Move(next, pointer);
                NotificationManager.Instance?.ShowTip("ios-content-ready", false, "资源更新已下载，将在下次启动时生效。");
            } catch (Exception e) { Debug.LogWarning("[iOS resources] 更新未激活：" + e.Message); TryDelete(partial); }
        } finally { activeRequest = null; busy = false; }
    }
    static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    void OnDestroy() { activeRequest?.Abort(); if (instance == this) instance = null; }
}
