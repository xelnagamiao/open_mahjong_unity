using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace OmUpdate {
    public sealed class ContentBoot : MonoBehaviour {
        [Serializable] sealed class Reply { public string status; public int baseVersion; public int releaseVersion; }
        BootSettings settings;
        ContentStore store;
        ContentIndex local;
        ContentBootView view;
        bool busy, codeLoaded, launchError;
        Action<string> progress;
        Action<float, float> progressBars;
        string directory;
        public static ContentBoot Active { get; private set; }

        async void Start() {
            Active = this; DontDestroyOnLoad(gameObject);
            Application.logMessageReceived += Log;
            try {
                settings = JsonUtility.FromJson<BootSettings>(await Text(Application.streamingAssetsPath + "/OmUpdate/boot.json"));
                store = new ContentStore(Path.Combine(Application.persistentDataPath, "android-content", settings.baseline));
                store.Recover();
                string next = Path.Combine(store.Root, "next");
                if (File.Exists(next)) {
                    string id = File.ReadAllText(next).Trim();
                    store.Activate(id);
                    File.Delete(next);
                }
                await OpenLocal();
                await Launch();
            } catch (Exception e) { Fail(e); }
        }
        void Log(string message, string trace, LogType type) {
            // Recoverable services (for example an offline game server) may
            // log Error while the game is usable. Roll back on startup faults.
            if (codeLoaded && (type == LogType.Exception || type == LogType.Assert)) launchError = true;
            if (codeLoaded && type == LogType.Error && message.StartsWith("Could not produce class with ID", StringComparison.Ordinal)) launchError = true;
        }
        void OnDestroy() { Application.logMessageReceived -= Log; if (Active == this) Active = null; }
        void Show(string text) { progress?.Invoke(text); }
        void Fail(Exception e) {
            busy = false;
            Debug.LogWarning("[AndroidContent] " + e);
            if (!view) { view = gameObject.AddComponent<ContentBootView>(); view.Bind(this); }
            view.Set("游戏启动失败，请重新打开游戏；如仍失败，请重新安装。", true, codeLoaded);
        }
        public void Website() => Application.OpenURL(settings?.website ?? "https://salasasa.cn/mobile-download");
        public void Exit() => Application.Quit();
        public async Task DownloadUpdate(Action<string> report, Action<float, float> reportProgress = null) {
            if (busy || !codeLoaded) throw new InvalidOperationException("游戏尚未准备好，请稍后重试。");
            progress = report;
            progressBars = reportProgress;
            try { await Check(); }
            finally { busy = false; progress = null; progressBars = null; }
        }
        async Task OpenLocal() {
            var embedded = JsonUtility.FromJson<ContentManifest>(await Text(Application.streamingAssetsPath + "/OmUpdate/content-update.json"));
            string id = store.Current;
            if (id != null) {
                try {
                    directory = store.DirectoryFor(id);
                    local = ReadIndex(directory);
                    if (local.releaseVersion < embedded.releaseVersion) throw new InvalidDataException("使用新安装包中的内容。");
                    // An APK can repair its bundled content without changing the
                    // game protocol version. Do not retain the old payload from
                    // an earlier APK; newer downloaded releases remain intact.
                    if (local.releaseVersion == embedded.releaseVersion && id != embedded.sha256)
                        throw new InvalidDataException("使用修复安装包中的内容。");
                    await Task.Run(() => ContentStore.VerifyIndex(directory, local, false));
                    Debug.Log("[AndroidContent] local quick check (no file hashing)");
                    return;
                } catch (Exception e) { Debug.LogWarning("本地内容无法使用，将恢复随包内容：" + e.Message); store.UseEmbedded(); }
            }
            embedded.Validate(settings.baseVersion, settings.baseline, settings.publicKey);
            string zip = Path.Combine(store.Root, "embedded.zip");
            RequireSpace(store.Root, embedded.sizeBytes + embedded.unpackedBytes);
            await CopyFile(Application.streamingAssetsPath + "/OmUpdate/" + embedded.file, zip);
            await Task.Run(() => store.Extract(zip, embedded));
            directory = store.DirectoryFor(embedded.sha256);
            local = ReadIndex(directory);
            await Task.Run(() => ContentStore.VerifyIndex(directory, local));
            File.Delete(zip);
            store.Activate(embedded.sha256);
            // The embedded package is our recovery source, not a downloaded candidate.
            store.Confirm();
        }
        ContentIndex ReadIndex(string folder) {
            var index = JsonUtility.FromJson<ContentIndex>(File.ReadAllText(ContentStore.SafePath(folder, "content-index.json")));
            if (index == null || index.baseVersion != settings.baseVersion || index.baseline != settings.baseline
                || index.releaseVersion < 1 || index.assemblies == null || index.assemblies.Length == 0
                || index.bundles == null || string.IsNullOrEmpty(index.scene)) throw new InvalidDataException("本地游戏内容与框架不匹配。");
            return index;
        }
        async Task Check() {
            busy = true; Show("正在检查游戏更新…");
            string response = await Text(settings.api + "/content?baseVersion=" + settings.baseVersion + "&releaseVersion=" + local.releaseVersion);
            var reply = JsonUtility.FromJson<Reply>(response);
            if (reply?.status == "apk_required") throw new InvalidOperationException("基础版本已变更，请下载安装包或去官网下载。");
            if (reply?.status == "client_ahead") throw new InvalidOperationException("客户端版本高于服务器，请等待服务器更新后重试。");
            if (reply?.status == "pending") throw new IOException("对应游戏更新尚未完整发布，请稍后重试。");
            if (reply?.status == "current") throw new InvalidOperationException("当前已是最新版本，无需下载。");
            if (reply?.status != "available") throw new InvalidDataException("更新服务返回了无效信息。");
            var manifest = JsonUtility.FromJson<ContentManifest>(response);
            manifest.Validate(settings.baseVersion, settings.baseline, settings.publicKey);
            string expected = "/api/client-updates/android/content/" + manifest.baseVersion + "/" + manifest.releaseVersion + "/" + manifest.sha256;
            if (manifest.downloadPath != expected) throw new InvalidDataException("更新下载地址无效。");
            string zip = Path.Combine(store.Root, manifest.sha256 + ".zip.part");
            RequireSpace(store.Root, manifest.sizeBytes + manifest.unpackedBytes - (File.Exists(zip) ? Math.Min(manifest.sizeBytes, new FileInfo(zip).Length) : 0));
            if (File.Exists(zip) && new FileInfo(zip).Length == manifest.sizeBytes
                && await Task.Run(() => ContentManifest.HashFile(zip)) != manifest.sha256) File.Delete(zip);
            await Download(new Uri(new Uri(settings.api), manifest.downloadPath).AbsoluteUri, zip, manifest.sizeBytes);
            string candidateDirectory = store.DirectoryFor(manifest.sha256);
            float installed = 0;
            var install = Task.Run(() => {
                store.Extract(zip, manifest, value => System.Threading.Volatile.Write(ref installed, .7f * value));
                var candidate = ReadIndex(candidateDirectory);
                if (candidate.releaseVersion != manifest.releaseVersion) throw new InvalidDataException("内容版本与发布清单不符。");
                ContentStore.VerifyIndex(candidateDirectory, candidate, true,
                    value => System.Threading.Volatile.Write(ref installed, .7f + .29f * value));
            });
            while (!install.IsCompleted) {
                float value = System.Threading.Volatile.Read(ref installed);
                progressBars?.Invoke(1, value);
                Show("下载完成，正在安装游戏内容 " + (value * 100).ToString("F1") + "%\n校验、解压并检查文件…");
                await Task.Delay(100);
            }
            await install;
            // Running assemblies cannot be replaced in this process. Activate on next launch.
            ContentStore.AtomicWrite(Path.Combine(store.Root, "next"), manifest.sha256);
            File.Delete(zip);
            progressBars?.Invoke(1, 1);
            Debug.Log("[AndroidContent] update staged release=" + manifest.releaseVersion);
        }
        async Task Launch() {
            busy = true; Show("正在启动游戏…"); codeLoaded = true;
            await ContentLoader.Load(directory, local);
            // Allow Awake/Start and the first frames to run. Exceptions leave the
            // pending intent in place; the next process restores the old package.
            // Scene activation has completed; yield for Start, without adding a
            // fixed sixty-frame delay on slow devices.
            for (int frame = 0; frame < 2; frame++) await Task.Yield();
            if (launchError || !SceneManager.GetActiveScene().isLoaded) throw new InvalidOperationException("新版游戏启动失败。");
            store.Confirm(); busy = false; if (view) view.Hide();
            Debug.Log("[AndroidContent] ready base=" + settings.baseVersion + " release=" + local.releaseVersion);
            try { await Task.Run(() => store.Prune()); } catch (IOException e) { Debug.LogWarning("内容缓存清理稍后重试：" + e.Message); }
        }
        static void RequireSpace(string directory, long required) {
            long available;
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var stat = new AndroidJavaObject("android.os.StatFs", directory)) available = stat.Call<long>("getAvailableBytes");
#else
            available = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))).AvailableFreeSpace;
#endif
            if (available < required + 64L * 1024 * 1024) throw new IOException("存储空间不足，请至少释放 " + ((required + 67108864 - available) / 1048576 + 1) + " MB 后重试。");
        }
        static async Task<string> Text(string url) {
            using (var request = UnityWebRequest.Get(url)) {
                request.timeout = 15; request.redirectLimit = 0;
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();
                if (request.result != UnityWebRequest.Result.Success || request.downloadedBytes > 32768) throw new IOException("无法获取更新信息，请检查网络后重试。");
                return request.downloadHandler.text;
            }
        }
        static async Task CopyFile(string url, string file) {
            using (var request = UnityWebRequest.Get(url)) {
                request.downloadHandler = new DownloadHandlerFile(file) { removeFileOnAbort = true };
                var operation = request.SendWebRequest(); while (!operation.isDone) await Task.Yield();
                if (request.result != UnityWebRequest.Result.Success) throw new IOException("无法读取安装包内的初始游戏内容。");
            }
        }
        async Task Download(string url, string file, long size) {
            var files = new DownloadFiles(file, url + "\n" + size, size);
            long offset = files.Length;
            if (offset == size) return;
            // Do not append a server's full 200 response to a partial file.
            string piece = files.Piece;
            using (var request = UnityWebRequest.Get(url)) {
                request.redirectLimit = 0;
                if (offset > 0) request.SetRequestHeader("Range", "bytes=" + offset + "-");
                request.downloadHandler = new DownloadHandlerFile(piece) { removeFileOnAbort = false };
                float last = Time.realtimeSinceStartup, began = last; ulong previous = 0;
                bool recorded = false;
                var operation = request.SendWebRequest();
                while (!operation.isDone) {
                    if (!recorded && request.responseCode != 0) recorded = files.RecordResponse(request.responseCode, request.GetResponseHeader("Content-Range"), offset);
                    if (request.downloadedBytes != previous) { previous = request.downloadedBytes; last = Time.realtimeSinceStartup; }
                    if (Time.realtimeSinceStartup - last > 45 || Time.realtimeSinceStartup - began > 1800
                        || request.downloadedBytes > (ulong)size) { request.Abort(); break; }
                    Show("正在下载游戏内容 " + Math.Min(100, (offset + (long)request.downloadedBytes) * 100d / size).ToString("F1") + "%\n" + ((offset + (long)request.downloadedBytes) / 1048576d).ToString("F1") + " / " + (size / 1048576d).ToString("F1") + " MB");
                    progressBars?.Invoke(Math.Min(1, (float)(offset + (long)request.downloadedBytes) / size), 0);
                    await Task.Yield();
                }
                if (!recorded) files.RecordResponse(request.responseCode, request.GetResponseHeader("Content-Range"), offset);
                files.Merge();
                if (request.result != UnityWebRequest.Result.Success || !File.Exists(file) || new FileInfo(file).Length != size)
                    throw new IOException("更新下载中断，请重试。已保存可续传的内容。");
            }
        }
    }
}
