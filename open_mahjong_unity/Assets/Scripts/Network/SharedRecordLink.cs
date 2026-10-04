using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Opens a read-only 3D replay from a public share link without creating a login session.
/// Canonical link: https://salasasa.cn/game-unity?recordId={gameId}
/// </summary>
public sealed class SharedRecordLink : MonoBehaviour {
    public const string LocalConvertedGameId = "localconverted";

    private const float SceneReadyTimeoutSeconds = 15f;
    private static readonly Regex GameIdPattern =
        new Regex("^[0-9A-Za-z]{1,16}$", RegexOptions.CultureInvariant);
    private static readonly Regex ShareGameIdInText =
        new Regex(@"(?:(?:^|/)2d/record/|[?&]recordId=|salasasa://record/)([0-9A-Za-z]{1,16})(?=[/?#&]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static SharedRecordLink _instance;
    private static int? _pendingRound;
    private static int? _pendingNode;
    private bool _isLoading;
    private string _queuedGameId;
    private bool _localConvertedReady;
    private byte[] _localConvertedBytes;
    private string _localConvertedError;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    static extern void LocalConvertedRecordLoad(string gameObjectName, string methodName);

    [DllImport("__Internal")]
    static extern int LocalConvertedRecordCopy(IntPtr dst, int maxLen);
#endif

    /// <summary>
    /// True only while a replay opened through a public 3D share URL is active.
    /// This must not be inferred from UserId: WebGL can retain a previous login in local storage.
    /// </summary>
    public static bool IsPublicSharePlayback { get; private set; }

    public static string BuildShareUrl(string gameId) {
        return $"{ConfigManager.webUrl}/game-unity?recordId={Uri.EscapeDataString(gameId)}";
    }

    /// <summary>round 为从 1 开始的小局索引，node 为当前已执行的动作数。</summary>
    public static string BuildShareUrl(string gameId, int round, int node) {
        return $"{BuildShareUrl(gameId)}&round={Math.Max(1, round)}&node={Math.Max(0, node)}";
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
            || text.IndexOf("recordId=", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("2d/record/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool TryExtractGameId(string value, out string gameId) {
        gameId = null;
        if (string.IsNullOrWhiteSpace(value)) return false;

        string text = value.Trim().Trim('"', '\'');
        if (GameIdPattern.IsMatch(text)) {
            gameId = text;
            return true;
        }

        Match match = ShareGameIdInText.Match(text);
        if (!match.Success) return false;
        gameId = match.Groups[1].Value;
        return true;
    }

    public static void CapturePosition(string value) {
        ParsePositionQuery(value ?? "", out _pendingRound, out _pendingNode);
    }

    public static void ClearPendingJump() {
        _pendingRound = null;
        _pendingNode = null;
    }

    public static void ApplyPendingJumpIfAny() {
        if (!_pendingRound.HasValue && !_pendingNode.HasValue) return;
        int round = _pendingRound ?? 1;
        int node = _pendingNode ?? 0;
        ClearPendingJump();
        GameRecordManager.Instance?.JumpToSharePosition(round, node);
    }

    private static void ParsePositionQuery(string text, out int? round, out int? node) {
        round = null;
        node = null;
        int queryAt = text.IndexOf('?');
        if (queryAt < 0) return;
        string query = text.Substring(queryAt + 1);
        int hashAt = query.IndexOf('#');
        if (hashAt >= 0) query = query.Substring(0, hashAt);
        foreach (string pair in query.Split('&')) {
            int separator = pair.IndexOf('=');
            string name = separator >= 0 ? pair.Substring(0, separator) : pair;
            string raw = separator >= 0 ? pair.Substring(separator + 1) : "";
            name = Uri.UnescapeDataString(name);
            raw = Uri.UnescapeDataString(raw.Replace("+", " "));
            if (!int.TryParse(raw, out int parsed)) continue;
            if (name.Equals("round", StringComparison.OrdinalIgnoreCase) && parsed >= 1) round = parsed;
            else if (name.Equals("node", StringComparison.OrdinalIgnoreCase) && parsed >= 0) node = parsed;
        }
    }

    public static bool Open(string value) {
        if (!TryExtractGameId(value, out string gameId)) return false;
        CapturePosition(value);
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
        if (IsLocalConvertedGameId(gameId)) {
            yield return OpenLocalConvertedRecord();
            yield break;
        }

        string apiRoot = string.IsNullOrEmpty(ConfigManager.webApiUrl)
            ? ""
            : ConfigManager.webApiUrl.TrimEnd('/');
        string endpoint = $"{apiRoot}/api/platform/unity-record/{UnityWebRequest.EscapeURL(gameId)}";

        using (UnityWebRequest request = UnityWebRequest.Get(endpoint)) {
            request.timeout = 20;
            request.SetRequestHeader("Accept", "application/json");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success) {
                ClearPendingJump();
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
                ClearPendingJump();
                Debug.LogError($"解析分享牌谱响应失败: {e}");
                ShowTip("无法打开牌谱", false, $"牌谱数据解析失败：{e.Message}");
                yield break;
            }

            yield return PlayPublicRecord(detail);
        }
    }

    public static bool IsLocalConvertedGameId(string gameId) {
        return string.Equals(gameId, LocalConvertedGameId, StringComparison.Ordinal);
    }

    private IEnumerator OpenLocalConvertedRecord() {
#if UNITY_WEBGL && !UNITY_EDITOR
        _localConvertedReady = false;
        _localConvertedBytes = null;
        _localConvertedError = null;
        try {
            LocalConvertedRecordLoad(gameObject.name, nameof(OnLocalConvertedReady));
        } catch (Exception e) {
            ClearPendingJump();
            ShowTip("无法打开牌谱", false, e.Message);
            yield break;
        }

        float deadline = Time.realtimeSinceStartup + SceneReadyTimeoutSeconds;
        while (!_localConvertedReady && Time.realtimeSinceStartup < deadline) {
            yield return null;
        }

        if (!_localConvertedReady) {
            ClearPendingJump();
            ShowTip("无法打开牌谱", false, "读取本地转换牌谱超时");
            yield break;
        }

        if (!string.IsNullOrEmpty(_localConvertedError)
            || _localConvertedBytes == null
            || _localConvertedBytes.Length == 0) {
            ClearPendingJump();
            ShowTip(
                "无法打开牌谱",
                false,
                string.IsNullOrEmpty(_localConvertedError)
                    ? "本地牌谱已失效，请返回转换工具重新转换"
                    : _localConvertedError
            );
            yield break;
        }

        RecordDetail detail;
        try {
            string json = Encoding.UTF8.GetString(_localConvertedBytes);
            detail = JObject.Parse(json).ToObject<RecordDetail>();
            if (detail == null || detail.record == null) {
                throw new InvalidOperationException("本地牌谱数据无效");
            }
        } catch (Exception e) {
            ClearPendingJump();
            Debug.LogError($"解析本地转换牌谱失败: {e}");
            ShowTip("无法打开牌谱", false, $"牌谱数据解析失败：{e.Message}");
            yield break;
        }

        yield return PlayPublicRecord(detail, localPlayback: true);
#else
        ClearPendingJump();
        ShowTip("无法打开牌谱", false, "请在网页 3D 中打开转换牌谱");
        yield break;
#endif
    }

    public void OnLocalConvertedReady(string message) {
        try {
            if (message == "empty") {
                _localConvertedBytes = null;
                _localConvertedError = null;
                return;
            }
            if (string.IsNullOrEmpty(message) || message.StartsWith("error|", StringComparison.Ordinal)) {
                _localConvertedBytes = null;
                _localConvertedError = message != null && message.Length > 6
                    ? message.Substring(6)
                    : "无法读取本地转换牌谱";
                return;
            }
            if (!message.StartsWith("ok|", StringComparison.Ordinal)
                || !int.TryParse(message.Substring(3), out int length)
                || length < 0) {
                _localConvertedBytes = null;
                _localConvertedError = "本地转换牌谱回调无效";
                return;
            }
            _localConvertedError = null;
            _localConvertedBytes = length == 0 ? Array.Empty<byte>() : CopyLocalConvertedBytes(length);
        } finally {
            _localConvertedReady = true;
        }
    }

    private IEnumerator PlayPublicRecord(RecordDetail detail, bool localPlayback = false) {
        float deadline = Time.realtimeSinceStartup + SceneReadyTimeoutSeconds;
        while (WindowsManager.Instance == null && Time.realtimeSinceStartup < deadline) {
            yield return null;
        }

        if (WindowsManager.Instance == null) {
            ClearPendingJump();
            Debug.LogError("打开分享牌谱失败：窗口管理器未就绪");
            yield break;
        }

        if (GameSessionGuard.BlockIfExclusiveSession("阅览牌谱")) {
            ClearPendingJump();
            yield break;
        }
        IsPublicSharePlayback = true;
        // 与列表/本地牌谱共用显式场景初始化和失败返回，避免等待未激活组件的单例。
        if (!RecordPanel.OpenRecord(detail, localPlayback)) {
            IsPublicSharePlayback = false;
        }
    }

    static byte[] CopyLocalConvertedBytes(int length) {
#if UNITY_WEBGL && !UNITY_EDITOR
        byte[] bytes = new byte[length];
        GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try {
            int copied = LocalConvertedRecordCopy(handle.AddrOfPinnedObject(), length);
            if (copied != length) {
                if (copied <= 0) return null;
                var trimmed = new byte[copied];
                Buffer.BlockCopy(bytes, 0, trimmed, 0, copied);
                return trimmed;
            }
            return bytes;
        } finally {
            handle.Free();
        }
#else
        return null;
#endif
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
