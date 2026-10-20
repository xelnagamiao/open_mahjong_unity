#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

public static class AndroidAppUpdateValidation {
    public static string RuntimeResult { get; private set; } = "not started";
    private const string PackageName = "cn.salasasa.mahjong";

    public static string Run(string directory = null) {
        directory = directory ?? Path.Combine(Application.temporaryCachePath, "android-update-validation");
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        var bytes = new byte[131073];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i % 251);
        string hash;
        using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        var manifest = new AndroidUpdateManifest {
            status = "available", schemaVersion = 1, releaseVersion = 31, versionCode = 31,
            versionName = "0.4.84.0", packageName = PackageName, sizeBytes = bytes.Length,
            sha256 = hash, downloadPath = "/api/client-updates/android/apk/" + hash,
        };
        var endpoint = new Uri("https://salasasa.cn/api/client-updates/android");
        bool Valid(int required = 31, long installed = 30, string package = PackageName) =>
            AndroidUpdateValidation.TryGetDownload(manifest, endpoint, required, installed, package, out _, out _);
        Check(Valid(), "matching newer APK accepted");
        Check(!Valid(0), "unknown required protocol rejected");
        Check(!Valid(30) && !Valid(32), "older and future protocol rejected");
        Check(!Valid(31, 31) && !Valid(31, 32), "same version and downgrade rejected");
        Check(!Valid(31, 30, "another.package"), "wrong package rejected");
        manifest.downloadPath = "https://another.example/file.apk";
        Check(!Valid(), "external download URL rejected");
        manifest.downloadPath = "/api/client-updates/android/apk/" + hash + "?redirect=1";
        Check(!Valid(), "unexpected download query rejected");
        manifest.downloadPath = "/api/client-updates/android/apk/" + hash;
        manifest.sizeBytes = 0; Check(!Valid(), "empty APK rejected");
        manifest.sizeBytes = AndroidUpdateValidation.MaximumApkBytes + 1; Check(!Valid(), "oversized APK rejected");
        manifest.sizeBytes = bytes.Length;
        manifest.sha256 = "broken"; Check(!Valid(), "invalid hash rejected");
        manifest.sha256 = hash;
        manifest.schemaVersion = 2; Check(!Valid(), "unknown schema rejected");
        manifest.schemaVersion = 1;
        manifest.status = "pending"; Check(!Valid(), "pending release cannot install");
        manifest.status = "available";
        var roundTrip = JsonUtility.FromJson<AndroidUpdateManifest>(JsonUtility.ToJson(manifest));
        Check(roundTrip.sizeBytes == bytes.Length && roundTrip.versionCode == 31, "JSON numeric fields preserved");
        string file = Path.Combine(directory, "validation.apk");
        try {
            File.WriteAllBytes(file, bytes);
            Check(AndroidUpdateValidation.VerifyFile(file, bytes.Length, hash, CancellationToken.None) == null, "valid multi-buffer file accepted");
            Check(AndroidUpdateValidation.VerifyFile(file, bytes.Length - 1, hash, CancellationToken.None) != null, "length mismatch rejected");
            Check(AndroidUpdateValidation.VerifyFile(file, bytes.Length, new string('0', 64), CancellationToken.None) != null, "checksum mismatch rejected");
            using (var cancellation = new CancellationTokenSource()) {
                cancellation.Cancel();
                Check(AndroidUpdateValidation.VerifyFile(file, bytes.Length, hash, cancellation.Token) != null, "verification cancellation supported");
            }
            bytes[100] ^= 1; File.WriteAllBytes(file, bytes);
            Check(AndroidUpdateValidation.VerifyFile(file, bytes.Length, hash, CancellationToken.None) != null, "same-size corruption rejected");
            File.Delete(file);
            Check(AndroidUpdateValidation.VerifyFile(file, bytes.Length, hash, CancellationToken.None) != null, "missing file fails safely");
        } finally { if (File.Exists(file)) File.Delete(file); }
        return JsonConvert.SerializeObject(new { passed = checks.Count, checks });
    }

    // Interactive acceptance uses the real update API/APK and simulates only the
    // installed version and native Android dialogs. This code is excluded from release players.
#if UNITY_EDITOR
    [Serializable]
    private sealed class PreviewSettings {
        public string origin;
        public string directory;
        public int requiredRelease;
        public long installedVersion;
        public long availableVersion;
        public string packageName;
    }
    private static PreviewSettings previewSettings;
    private const string PreviewSettingsKey = "AndroidAppUpdatePreview.SettingsFile";
    private const string PreviewRequestedKey = "AndroidAppUpdatePreview.Requested";

    // Remember configuration across Play Mode/domain reloads, but only opt in
    // for a Play session explicitly started by the preview menu or environment.
    // Only runtime objects are touched, so stopping Play restores the normal scene.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void PrepareInteractivePreview() {
        string settingsFile = Environment.GetEnvironmentVariable("OPEN_MAHJONG_ANDROID_UPDATE_PREVIEW");
        Environment.SetEnvironmentVariable("OPEN_MAHJONG_ANDROID_UPDATE_PREVIEW", null);
        if (string.IsNullOrEmpty(settingsFile) && UnityEditor.SessionState.GetBool(PreviewRequestedKey, false)) {
            settingsFile = UnityEditor.SessionState.GetString(PreviewSettingsKey, "");
        }
        UnityEditor.SessionState.EraseBool(PreviewRequestedKey);
        previewSettings = null;
        if (string.IsNullOrEmpty(settingsFile)) return;
        try {
            previewSettings = LoadPreviewSettings(settingsFile);
            UnityEditor.SessionState.SetString(PreviewSettingsKey, settingsFile);
        } catch (Exception error) {
            Debug.LogError("无法加载安卓更新预览配置：" + error.Message);
            return;
        }
        ConfigManager.Debug = true;
        ConfigManager.gameUrl = "ws://localhost:8081/game";
        ConfigManager.chatUrl = "ws://localhost:8083/chat";
        ConfigManager.webApiUrl = "http://localhost:3000";
        foreach (var network in UnityEngine.Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
            network.enabled = false;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartInteractivePreview() {
        if (previewSettings != null && NotificationManager.Instance != null) {
            NotificationManager.Instance.StartCoroutine(ShowInteractivePreview());
        }
    }

    private static IEnumerator ShowInteractivePreview() {
        yield return null;
        var settings = previewSettings;
        if (settings == null || NotificationManager.Instance == null) yield break;
        Preview(settings.origin, settings.directory, settings.requiredRelease,
            settings.installedVersion, settings.availableVersion, settings.packageName);
    }

    [UnityEditor.MenuItem("Tools/安卓应用内更新/重新开始本地预览")]
    private static void RestartInteractivePreview() {
        string settingsFile = UnityEditor.SessionState.GetString(PreviewSettingsKey, "");
        if (!File.Exists(settingsFile)) {
            settingsFile = UnityEditor.EditorUtility.OpenFilePanel("选择安卓更新本地预览配置", Path.GetDirectoryName(Application.dataPath), "json");
        }
        if (string.IsNullOrEmpty(settingsFile)) return;
        PreviewSettings settings;
        try {
            settings = LoadPreviewSettings(settingsFile);
        } catch (Exception error) {
            UnityEditor.EditorUtility.DisplayDialog("安卓更新预览", "预览配置无法读取：" + error.Message, "好的");
            UnityEditor.SessionState.EraseString(PreviewSettingsKey);
            return;
        }
        UnityEditor.SessionState.SetString(PreviewSettingsKey, settingsFile);
        if (!Application.isPlaying) {
            UnityEditor.SessionState.SetBool(PreviewRequestedKey, true);
            UnityEditor.EditorApplication.isPlaying = true;
            return;
        }
        if (NotificationManager.Instance == null) {
            UnityEditor.EditorUtility.DisplayDialog("安卓更新预览", "请先打开包含通知界面的 MainScene。", "好的");
            return;
        }
        previewSettings = settings;
        foreach (var message in UnityEngine.Object.FindObjectsByType<MessagePrefab>(FindObjectsSortMode.None)) {
            message.CloseMessage();
        }
        NotificationManager.Instance.StartCoroutine(ShowInteractivePreview());
    }

    [UnityEditor.MenuItem("Tools/安卓应用内更新/重新开始本地预览", true)]
    private static bool CanRestartInteractivePreview() =>
        !UnityEditor.EditorApplication.isCompiling && !UnityEditor.EditorApplication.isUpdating &&
        UnityEditor.EditorApplication.isPlaying == UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;

    private static PreviewSettings LoadPreviewSettings(string path) {
        var settings = JsonUtility.FromJson<PreviewSettings>(File.ReadAllText(path));
        if (settings == null || !Uri.TryCreate(settings.origin, UriKind.Absolute, out var origin) ||
            (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps) || !origin.IsLoopback ||
            string.IsNullOrWhiteSpace(settings.directory) || string.IsNullOrWhiteSpace(settings.packageName) ||
            settings.requiredRelease <= 0 || settings.installedVersion < 0 || settings.availableVersion <= settings.installedVersion) {
            throw new InvalidOperationException("需要本机 API 地址、下载目录、包名以及有效的测试版本号。");
        }
        return settings;
    }
#endif

    public static void Preview(string origin, string directory, int requiredRelease,
        long installedVersion, long availableVersion, string packageName) {
        if (!Application.isEditor || !Application.isPlaying || NotificationManager.Instance == null) {
            throw new InvalidOperationException("Editor Play Mode and NotificationManager required");
        }
        foreach (var existing in UnityEngine.Object.FindObjectsByType<AndroidAppUpdater>(FindObjectsSortMode.None)) {
            existing.SendMessage("Cancel");
        }
        var installer = new PreviewInstaller {
            InstalledVersionCode = installedVersion, ExpectedVersionCode = availableVersion,
            PackageName = packageName, DownloadDirectory = directory,
        };
        NotificationManager.Instance.ShowMessage("版本检查失败",
            "您的客户端版本与服务器版本不匹配，请更新客户端到最新版本后再试。",
            MessagePrefab.AndroidVersionActions(() => {
                installer.Updater = new GameObject("AndroidUpdateInteractivePreview").AddComponent<AndroidAppUpdater>();
                installer.Updater.Initialize(requiredRelease, origin, installer);
            }));
    }

    private sealed class PreviewInstaller : IAndroidUpdateInstaller {
        public long InstalledVersionCode { get; set; }
        public long ExpectedVersionCode;
        public string PackageName { get; set; }
        public string DownloadDirectory { get; set; }
        public bool CanInstall { get; private set; }
        public AndroidAppUpdater Updater;

        public void OpenPermissionSettings() {
            Updater.SendMessage("OnApplicationPause", true);
            NotificationManager.Instance.ShowModal("安装权限（编辑器模拟）",
                "真机此处会进入安卓系统设置。\n允许本应用安装更新？",
                new MessageAction("不允许", Resume),
                new MessageAction("允许", () => { CanInstall = true; Resume(); }));
        }

        private void Resume() {
            if (Updater == null) return;
            Updater.SendMessage("OnApplicationPause", false);
            // Switching between the Editor and another desktop window may have
            // already consumed the simulated focus return before this click.
            if (CanInstall && Updater.State == "permission") Updater.SendMessage("Install");
        }

        public void Install(long version) {
            if (!CanInstall || version != ExpectedVersionCode || !File.Exists(Path.Combine(DownloadDirectory, "update.apk"))) {
                throw new InvalidOperationException("Preview APK has not been downloaded and authorized");
            }
            NotificationManager.Instance.ShowModal("系统安装（编辑器模拟）",
                "真实 APK 已下载并校验。\n编辑器无法安装安卓应用，此处用于预览取消和完成安装的操作。",
                new MessageAction("取消"),
                new MessageAction("完成安装", () => {
                    if (Updater != null) Updater.SendMessage("Cancel");
                    NotificationManager.Instance.ShowModal("更新完成（编辑器模拟）",
                        "更新流程已完成。真机安装成功后会由系统更新应用。", new MessageAction("好的"));
                }));
        }
    }

    // Exercises real uGUI raycasts; equal-order nested canvases can draw and
    // receive clicks in a different order from their hierarchy siblings.
    public static void RunModalLayering() {
        if (!Application.isPlaying || NotificationManager.Instance == null) throw new InvalidOperationException("Play Mode and NotificationManager required");
        RuntimeResult = "running";
        NotificationManager.Instance.StartCoroutine(Guard(ModalLayering()));
    }

    private static bool ReceivesModalClicks(MessagePrefab panel) {
        foreach (var button in panel.GetComponentsInChildren<UnityEngine.UI.Button>()) {
            var rect = (RectTransform)button.transform;
            Canvas canvas = button.GetComponentInParent<Canvas>();
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
            var hits = new List<UnityEngine.EventSystems.RaycastResult>();
            var events = UnityEngine.EventSystems.EventSystem.current;
            events.RaycastAll(new UnityEngine.EventSystems.PointerEventData(events) { position = point }, hits);
            if (hits.Count == 0 || hits[0].gameObject.GetComponentInParent<MessagePrefab>() != panel) return false;
        }
        return true;
    }

    private static IEnumerator ModalLayering() {
        var checks = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        var manager = NotificationManager.Instance;
        var actions = new[] { new MessageAction("Back"), new MessageAction("Continue") };
        MessagePrefab first = null, second = null, third = null;
        try {
            first = manager.ShowModal("Modal layer 1", "Update dialog", actions);
            yield return new WaitForSecondsRealtime(.3f);
            Check(ReceivesModalClicks(first), "base modal receives clicks");
            second = manager.ShowModal("Modal layer 2", "Permission dialog", actions);
            yield return new WaitForSecondsRealtime(.3f);
            Check(ReceivesModalClicks(second) && !ReceivesModalClicks(first), "new permission modal blocks the underlying dialog");
            first.Show("Modal layer 1", "Updated underlying dialog", actions);
            yield return new WaitForSecondsRealtime(.3f);
            Check(ReceivesModalClicks(second), "refreshing the underlying dialog does not overtake the permission modal");
            second.CloseMessage();
            third = manager.ShowModal("Modal layer 3", "System install dialog", actions);
            yield return new WaitForSecondsRealtime(.3f);
            Check(ReceivesModalClicks(third) && !ReceivesModalClicks(first), "install modal stays above a fading permission modal");
            third.CloseMessage();
            yield return new WaitForSecondsRealtime(.3f);
            Check(ReceivesModalClicks(first), "closing nested modals restores underlying clicks");
            for (int i = 0; i < 3; i++) {
                second = manager.ShowModal("Modal layer 2", "Repeated permission dialog", actions);
                yield return new WaitForSecondsRealtime(.3f);
                Check(ReceivesModalClicks(second), "reopened modal receives clicks " + (i + 1));
                second.CloseMessage();
                yield return new WaitForSecondsRealtime(.3f);
                Check(ReceivesModalClicks(first), "underlying clicks restored " + (i + 1));
            }
            RuntimeResult = JsonConvert.SerializeObject(new { passed = checks.Count, checks, frames = Time.frameCount });
        } finally {
            if (third != null) third.CloseMessage();
            if (second != null) second.CloseMessage();
            if (first != null) first.CloseMessage();
        }
    }

    private sealed class FakeInstaller : IAndroidUpdateInstaller {
        public long InstalledVersionCode => 30;
        public string PackageName => AndroidAppUpdateValidation.PackageName;
        public string DownloadDirectory { get; set; }
        public bool CanInstall { get; set; }
        public int PermissionRequests;
        public int InstallRequests;
        public void OpenPermissionSettings() { PermissionRequests++; }
        public void Install(long version) {
            if (!CanInstall || version != 31 || !File.Exists(Path.Combine(DownloadDirectory, "update.apk"))) throw new Exception("Invalid install request");
            InstallRequests++;
        }
    }

    // Requires a local HTTP fixture, a running MainScene and its real notification prefab.
    // The installer is fake: these checks do not claim Android device installation.
    public static void RunRuntime(string origin, string directory) {
        if (!Application.isPlaying || NotificationManager.Instance == null) throw new InvalidOperationException("Play Mode and NotificationManager required");
        RuntimeResult = "running";
        NotificationManager.Instance.StartCoroutine(Guard(Runtime(origin, directory)));
    }

    private static IEnumerator Guard(IEnumerator flow) {
        var stack = new Stack<IEnumerator>(); stack.Push(flow);
        while (stack.Count > 0) {
            object next = null;
            bool moved;
            try { moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
            catch (Exception exception) { RuntimeResult = JsonConvert.SerializeObject(new { error = exception.ToString() }); yield break; }
            if (!moved) { stack.Pop(); continue; }
            if (next is IEnumerator nested) stack.Push(nested);
            else yield return next;
        }
    }

    private static IEnumerator SetMode(string origin, string mode) {
        using (var request = UnityWebRequest.Get(origin + "/test-control/" + mode)) {
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) throw new Exception("Fixture unavailable");
        }
    }

    private static MessagePrefab Panel(AndroidAppUpdater updater) => (MessagePrefab)typeof(AndroidAppUpdater)
        .GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(updater);

    private static UnityEngine.UI.Button Button(AndroidAppUpdater updater, string text) => Panel(updater)
        .GetComponentsInChildren<UnityEngine.UI.Button>().Single(b => b.GetComponentInChildren<TMP_Text>()?.text == text);

    private static IEnumerator WaitState(AndroidAppUpdater updater, string state) {
        float limit = Time.realtimeSinceStartup + 20;
        while (updater != null && updater.State != state && Time.realtimeSinceStartup < limit) yield return null;
        if (updater == null || updater.State != state) throw new Exception("Expected " + state + ", got " + updater?.State);
    }

    private static IEnumerator Runtime(string origin, string directory) {
        var checks = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        var installer = new FakeInstaller { DownloadDirectory = directory };
        AndroidAppUpdater updater = null;
        try {
            yield return SetMode(origin, "pending");
            updater = new GameObject("AndroidUpdateRuntimeValidation").AddComponent<AndroidAppUpdater>();
            updater.Initialize(31, origin, installer);
            yield return WaitState(updater, "error");
            Check(Button(updater, "关闭").interactable && Button(updater, "重试").interactable, "unpublished APK offers close and retry");
            yield return SetMode(origin, "good");
            Button(updater, "重试").onClick.Invoke();
            yield return WaitState(updater, "ready");
            Check(File.Exists(Path.Combine(directory, "update.apk")) && !File.Exists(Path.Combine(directory, "update.apk.part")), "retry downloads and verifies file");
            Button(updater, "安装").onClick.Invoke();
            Check(updater.State == "permission" && installer.InstallRequests == 0, "installation waits for permission");
            Button(updater, "去授权").onClick.Invoke();
            Check(installer.PermissionRequests == 1, "permission settings requested");
            updater.SendMessage("OnApplicationPause", true);
            updater.SendMessage("OnApplicationPause", false);
            Check(updater.State == "permission" && installer.InstallRequests == 0, "permission denial remains retryable");
            Button(updater, "去授权").onClick.Invoke();
            installer.CanInstall = true;
            updater.SendMessage("OnApplicationFocus", false);
            updater.SendMessage("OnApplicationFocus", true);
            updater.SendMessage("OnApplicationPause", false);
            Check(installer.InstallRequests == 1 && updater.State == "installing", "permission return starts one install");
            Button(updater, "重新安装").onClick.Invoke();
            Check(installer.InstallRequests == 2, "cancelled system install can be retried");
            Button(updater, "关闭").onClick.Invoke();
            yield return null; yield return null;
            Check(updater == null, "closing releases updater owner");

            yield return SetMode(origin, "corrupt");
            updater = new GameObject("AndroidUpdateRuntimeValidation").AddComponent<AndroidAppUpdater>();
            updater.Initialize(31, origin, installer);
            yield return WaitState(updater, "error");
            Check(!File.Exists(Path.Combine(directory, "update.apk.part")) && installer.InstallRequests == 2, "corrupted download cannot install");
            yield return SetMode(origin, "flaky");
            Button(updater, "重试").onClick.Invoke();
            yield return WaitState(updater, "error");
            Button(updater, "重试").onClick.Invoke();
            yield return WaitState(updater, "ready");
            Check(installer.InstallRequests == 2, "failed download retries without automatic install");
            Button(updater, "关闭").onClick.Invoke();
            yield return null; yield return null;

            yield return SetMode(origin, "slow");
            updater = new GameObject("AndroidUpdateRuntimeValidation").AddComponent<AndroidAppUpdater>();
            updater.Initialize(31, origin, installer);
            yield return WaitState(updater, "downloading");
            yield return new WaitForSecondsRealtime(.6f);
            Check(!Button(updater, "下载中…").interactable, "duplicate downloads disabled");
            Check(Panel(updater).GetComponentsInChildren<TMP_Text>().Any(text => text.text.Contains("进度：") && text.text.Contains(" MB")), "download percentage and bytes displayed");
            Button(updater, "关闭").onClick.Invoke();
            yield return null; yield return null;
            Check(updater == null && !File.Exists(Path.Combine(directory, "update.apk.part")), "cancel aborts request and removes partial file");
            RuntimeResult = JsonConvert.SerializeObject(new { passed = checks.Count, checks, frames = Time.frameCount });
        } finally {
            if (updater != null) { Panel(updater)?.CloseMessage(); UnityEngine.Object.Destroy(updater.gameObject); }
        }
    }
}
#endif
