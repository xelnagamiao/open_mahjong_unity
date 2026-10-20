using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public sealed class AndroidAppUpdater : MonoBehaviour {
    private static AndroidAppUpdater active;
    private IAndroidUpdateInstaller installer;
    private MessagePrefab panel;
    private UnityWebRequest request;
    private Coroutine operation;
    private CancellationTokenSource cancellation;
    private AndroidUpdateManifest manifest;
    private Uri endpoint;
    private int requiredRelease;
    private long installedVersion;
    private string partialPath;
    private string apkPath;
    private bool waitingForPermission;
    private bool leftForPermission;
    private bool stopping;

    public string State { get; private set; }

    public static void Open(int requiredReleaseVersion) {
        if (active != null) return;
        var updater = new GameObject("AndroidAppUpdater").AddComponent<AndroidAppUpdater>();
        active = updater;
        updater.Initialize(requiredReleaseVersion, ConfigManager.webApiUrl, new AndroidUpdateInstaller());
    }

    internal void Initialize(int requiredReleaseVersion, string apiBase, IAndroidUpdateInstaller installBridge) {
        installer = installBridge;
        requiredRelease = requiredReleaseVersion;
        panel = NotificationManager.Instance.ShowModal("安卓客户端更新", "正在检查更新…",
            new MessageAction("关闭", Cancel));
        if (panel == null) { Cancel(); return; }
        if (requiredReleaseVersion > 0 && requiredReleaseVersion < ConfigManager.releaseVersion) {
            ShowFailure("您的客户端版本高于服务器版本，请等待服务器更新完毕后再试。", false);
            return;
        }
        try {
            endpoint = new Uri(apiBase.TrimEnd('/') + "/api/client-updates/android");
            if (endpoint.Scheme != Uri.UriSchemeHttps && !(endpoint.Scheme == Uri.UriSchemeHttp && (Application.isEditor || ConfigManager.Debug))) {
                throw new InvalidOperationException("Update endpoint must use HTTPS");
            }
            installedVersion = installer.InstalledVersionCode;
            string directory = installer.DownloadDirectory;
            Directory.CreateDirectory(directory);
            partialPath = Path.Combine(directory, "update.apk.part");
            apkPath = Path.Combine(directory, "update.apk");
            Retry();
        } catch (Exception) {
            ShowFailure("无法初始化更新，请检查存储空间或稍后重试。", false);
        }
    }

    private void Show(string state, string content, string actionLabel, Action action) {
        State = state;
        panel.Show("安卓客户端更新", content, new[] {
            new MessageAction("关闭", Cancel),
            new MessageAction(actionLabel, action, false),
        });
        panel.SetActionInteractable(1, action != null);
    }

    private void ShowFailure(string content, bool retry = true) {
        State = "error";
        if (retry) Show("error", content, "重试", Retry);
        else panel.Show("安卓客户端更新", content, new[] { new MessageAction("关闭", Cancel) });
    }

    private void Retry() {
        if (operation != null || stopping) return;
        waitingForPermission = false;
        cancellation?.Dispose();
        cancellation = new CancellationTokenSource();
        Show("checking", "正在检查可用的安卓安装包…", "检查中…", null);
        operation = StartCoroutine(CheckAndDownload());
    }

    private IEnumerator CheckAndDownload() {
        // Ensure the caller stores the coroutine handle before an immediate failure.
        yield return null;
        request = UnityWebRequest.Get(endpoint + $"?requiredReleaseVersion={requiredRelease}&installedVersionCode={installedVersion}");
        request.timeout = 60;
        request.redirectLimit = 0;
        yield return request.SendWebRequest();
        string response = request.result == UnityWebRequest.Result.Success ? request.downloadHandler.text : null;
        request.Dispose();
        request = null;
        if (response == null || response.Length > 32768) {
            FailOperation("无法获取更新信息，请检查网络后重试。");
            yield break;
        }
        try { manifest = JsonUtility.FromJson<AndroidUpdateManifest>(response); }
        catch (ArgumentException) { manifest = null; }
        if (manifest != null && manifest.status == "pending") {
            FailOperation("与当前服务器匹配的新安装包尚未就绪，可能仍在上传，请稍后重试。");
            yield break;
        }
        if (!AndroidUpdateValidation.TryGetDownload(manifest, endpoint, requiredRelease, installedVersion,
                installer.PackageName, out Uri downloadUri, out string validationError)) {
            FailOperation(validationError);
            yield break;
        }

        string fileError = null;
        try {
            DeletePartial();
            request = UnityWebRequest.Get(downloadUri);
            request.downloadHandler = new DownloadHandlerFile(partialPath) { removeFileOnAbort = true };
            request.redirectLimit = 0;
        } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) {
            fileError = "无法保存安装包，请检查存储空间后重试。";
        }
        if (fileError != null) { FailOperation(fileError); yield break; }
        Show("downloading", DownloadText(0), "下载中…", null);
        var download = request.SendWebRequest();
        float lastProgress = Time.realtimeSinceStartup;
        float lastUi = lastProgress;
        float started = lastProgress;
        ulong lastBytes = 0;
        bool timedOut = false;
        while (!download.isDone) {
            float now = Time.realtimeSinceStartup;
            ulong bytes = request.downloadedBytes;
            if (bytes != lastBytes) { lastProgress = now; lastBytes = bytes; }
            if (now - lastProgress > 45 || now - started > 1800 || bytes > (ulong)manifest.sizeBytes) {
                timedOut = true;
                request.Abort();
                break;
            }
            if (now - lastUi >= .25f) { panel.UpdateContent(DownloadText(bytes)); lastUi = now; }
            yield return null;
        }
        bool succeeded = !timedOut && request.result == UnityWebRequest.Result.Success;
        request.Dispose();
        request = null;
        if (!succeeded) {
            DeletePartial();
            FailOperation("下载失败，可能网络中断或安装包正在更新，请重试。");
            yield break;
        }

        Show("verifying", "下载完成，正在校验安装包…", "校验中…", null);
        CancellationToken token = cancellation.Token;
        var verification = Task.Run(() => AndroidUpdateValidation.VerifyFile(partialPath, manifest.sizeBytes, manifest.sha256, token));
        while (!verification.IsCompleted) yield return null;
        string checksumError = verification.IsFaulted ? "安装包校验失败，请重新下载。" : verification.Result;
        if (checksumError != null) {
            DeletePartial();
            FailOperation(checksumError);
            yield break;
        }
        try {
            if (File.Exists(apkPath)) File.Delete(apkPath);
            File.Move(partialPath, apkPath);
        } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) {
            fileError = "无法保存安装包，请检查存储空间后重试。";
        }
        operation = null;
        if (fileError != null) { ShowFailure(fileError); yield break; }
        Show("ready", $"版本 {manifest.versionName} 已下载并通过校验。\n点击安装后，请按系统提示完成更新。", "安装", Install);
    }

    private string DownloadText(ulong bytes) {
        double percent = Math.Min(100, bytes * 100d / manifest.sizeBytes);
        return $"正在下载版本 {manifest.versionName}\n进度：{percent:F1}%\n{bytes / 1048576d:F1} MB / {manifest.sizeBytes / 1048576d:F1} MB\n关闭窗口将取消本次下载。";
    }

    private void FailOperation(string message) {
        operation = null;
        request?.Dispose();
        request = null;
        ShowFailure(message);
    }

    private void Install() {
        try {
            if (!installer.CanInstall) {
                Show("permission", "请在系统设置中允许本应用安装更新，完成后返回游戏。", "去授权", OpenPermission);
                return;
            }
            waitingForPermission = false;
            installer.Install(manifest.versionCode);
            Show("installing", "已打开系统安装界面。\n如果已取消安装，可以点击重新安装。", "重新安装", Install);
        } catch (Exception) {
            ShowFailure("无法打开安装程序或安装包与当前应用不匹配，请重新下载后重试。");
        }
    }

    private void OpenPermission() {
        try {
            waitingForPermission = true;
            leftForPermission = false;
            installer.OpenPermissionSettings();
        } catch (Exception) {
            waitingForPermission = false;
            ShowFailure("无法打开安装授权设置，请稍后重试。");
        }
    }

    private void OnApplicationPause(bool paused) => HandleFocus(!paused);
    private void OnApplicationFocus(bool focused) => HandleFocus(focused);
    private void HandleFocus(bool focused) {
        if (!waitingForPermission || stopping) return;
        if (!focused) { leftForPermission = true; return; }
        if (!leftForPermission) return;
        waitingForPermission = false;
        Install();
    }

    private void Update() {
        if (panel == null && !stopping) Cancel();
    }

    private void DeletePartial() {
        try { if (!string.IsNullOrEmpty(partialPath) && File.Exists(partialPath)) File.Delete(partialPath); }
        catch (IOException) { /* Cancellation can briefly overlap the hashing worker. */ }
        catch (UnauthorizedAccessException) { /* The next download reports the storage failure. */ }
    }

    private void Cancel() {
        if (stopping) return;
        stopping = true;
        cancellation?.Cancel();
        request?.Abort();
        if (panel != null) panel.CloseMessage();
        Destroy(gameObject);
    }

    private void OnDestroy() {
        stopping = true;
        cancellation?.Cancel();
        request?.Abort();
        request?.Dispose();
        cancellation?.Dispose();
        DeletePartial();
        if (active == this) active = null;
    }
}
