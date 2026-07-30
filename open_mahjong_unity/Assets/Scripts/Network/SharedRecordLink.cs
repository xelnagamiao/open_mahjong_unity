using System;
using System.Collections;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Opens a read-only 3D replay from a public share link without creating a login session.
/// Canonical link: https://salasasa.cn/unity-game/record/{gameId}
/// </summary>
public sealed class SharedRecordLink : MonoBehaviour {
    private const int MaxGameIdLength = 16;
    private const float SceneReadyTimeoutSeconds = 15f;
    private static readonly Regex GameIdPattern =
        new Regex("^[0-9A-Za-z]{1,16}$", RegexOptions.CultureInvariant);

    private static SharedRecordLink _instance;
    private bool _isLoading;
    private string _queuedGameId;

    /// <summary>
    /// True only while a replay opened through a public 3D share URL is active.
    /// This must not be inferred from UserId: WebGL can retain a previous login in local storage.
    /// </summary>
    public static bool IsPublicSharePlayback { get; private set; }

    public static string BuildShareUrl(string gameId) {
        return $"{ConfigManager.webUrl}/unity-game/record/{gameId}";
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap() {
        if (_instance != null) return;
        var host = new GameObject(nameof(SharedRecordLink));
        DontDestroyOnLoad(host);
        _instance = host.AddComponent<SharedRecordLink>();
    }

    private void Awake() {
        if (_instance != null && _instance != this) {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        Application.deepLinkActivated += OnDeepLinkActivated;
    }

    private IEnumerator Start() {
        yield return null;

        if (TryExtractGameId(Application.absoluteURL, out _)) {
            Open(Application.absoluteURL);
            yield break;
        }

        string[] args = Environment.GetCommandLineArgs();
        for (int i = 1; i < args.Length; i++) {
            if ((args[i] == "--record-url" || args[i] == "--record") && i + 1 < args.Length) {
                Open(args[i + 1]);
                yield break;
            }
            if (LooksLikeShareLink(args[i]) && TryExtractGameId(args[i], out _)) {
                Open(args[i]);
                yield break;
            }
        }
    }

    private void OnDestroy() {
        if (_instance == this) {
            Application.deepLinkActivated -= OnDeepLinkActivated;
            _instance = null;
        }
    }

    private void OnDeepLinkActivated(string url) {
        Open(url);
    }

    public static bool LooksLikeShareLink(string value) {
        if (string.IsNullOrWhiteSpace(value)) return false;
        string text = value.Trim();
        return text.IndexOf("://", StringComparison.Ordinal) >= 0
            || text.IndexOf("/unity-game/record/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool TryExtractGameId(string value, out string gameId) {
        gameId = null;
        if (string.IsNullOrWhiteSpace(value)) return false;

        string text = value.Trim().Trim('"', '\'');
        if (GameIdPattern.IsMatch(text)) {
            gameId = text;
            return true;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri uri)) return false;

        string candidate = null;
        if (uri.Scheme.Equals("salasasa", StringComparison.OrdinalIgnoreCase)) {
            if (uri.Host.Equals("record", StringComparison.OrdinalIgnoreCase)) {
                candidate = uri.AbsolutePath.Trim('/');
            }
        } else if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) {
            string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 2 < segments.Length; i++) {
                bool supportedPrefix =
                    segments[i].Equals("unity-game", StringComparison.OrdinalIgnoreCase)
                    && segments[i + 1].Equals("record", StringComparison.OrdinalIgnoreCase);
                if (supportedPrefix) {
                    candidate = Uri.UnescapeDataString(segments[i + 2]);
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(candidate)
            || candidate.Length > MaxGameIdLength
            || !GameIdPattern.IsMatch(candidate)) return false;

        gameId = candidate;
        return true;
    }

    public static bool Open(string value) {
        if (!TryExtractGameId(value, out string gameId)) return false;
        if (_instance == null) Bootstrap();
        _instance.Enqueue(gameId);
        return true;
    }

    private void Enqueue(string gameId) {
        _queuedGameId = gameId;
        if (!_isLoading) StartCoroutine(OpenQueuedRecord());
    }

    private IEnumerator OpenQueuedRecord() {
        _isLoading = true;
        while (!string.IsNullOrEmpty(_queuedGameId)) {
            string gameId = _queuedGameId;
            _queuedGameId = null;
            yield return FetchAndOpen(gameId);
        }
        _isLoading = false;
    }

    private IEnumerator FetchAndOpen(string gameId) {
        IsPublicSharePlayback = false;
        string endpoint = $"{ConfigManager.webUrl}/api/platform/unity-record/{UnityWebRequest.EscapeURL(gameId)}";

        using (UnityWebRequest request = UnityWebRequest.Get(endpoint)) {
            request.timeout = 20;
            request.SetRequestHeader("Accept", "application/json");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success) {
                ShowTip("无法打开牌谱", false, ReadServerError(request));
                yield break;
            }

            RecordDetail detail;
            try {
                JObject envelope = JObject.Parse(request.downloadHandler.text);
                if (envelope.Value<bool?>("success") != true || envelope["data"] == null) {
                    throw new InvalidOperationException(
                        envelope.Value<string>("message") ?? "牌谱接口返回了无效数据"
                    );
                }
                detail = envelope["data"].ToObject<RecordDetail>();
            } catch (Exception e) {
                Debug.LogError($"解析分享牌谱响应失败: {e}");
                ShowTip("无法打开牌谱", false, $"牌谱数据解析失败：{e.Message}");
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + SceneReadyTimeoutSeconds;
            while (WindowsManager.Instance == null && Time.realtimeSinceStartup < deadline) {
                yield return null;
            }

            if (WindowsManager.Instance == null) {
                Debug.LogError("打开分享牌谱失败：窗口管理器未就绪");
                yield break;
            }

            IsPublicSharePlayback = true;
            // GameRecordManager lives under the inactive game panel. Activate the
            // record scene first; waiting for it before switching creates a deadlock.
            WindowsManager.Instance.SwitchWindow("recordscene");

            deadline = Time.realtimeSinceStartup + SceneReadyTimeoutSeconds;
            while (GameRecordManager.Instance == null && Time.realtimeSinceStartup < deadline) {
                yield return null;
            }

            if (GameRecordManager.Instance == null) {
                Debug.LogError("打开分享牌谱失败：3D 牌谱场景未能初始化");
                IsPublicSharePlayback = false;
                WindowsManager.Instance.ExitGameTo("login");
                yield break;
            }

            RecordPanel.OpenRecord(detail);
        }
    }

    /// <summary>
    /// Ends the one-shot public share session when the viewer leaves the replay.
    /// </summary>
    public static void EndPublicSharePlayback() {
        IsPublicSharePlayback = false;
    }

    private static string ReadServerError(UnityWebRequest request) {
        try {
            string message = JObject.Parse(request.downloadHandler.text).Value<string>("message");
            if (!string.IsNullOrWhiteSpace(message)) return message;
        } catch {
            // Fall back to the transport-level status below.
        }
        return request.responseCode == 404
            ? "没有找到这份牌谱"
            : $"牌谱读取失败（HTTP {request.responseCode}）";
    }

    private static void ShowTip(string title, bool success, string message) {
        if (NotificationManager.Instance != null) {
            NotificationManager.Instance.ShowTip(title, success, message);
        } else {
            Debug.Log($"{title}: {message}");
        }
    }
}
