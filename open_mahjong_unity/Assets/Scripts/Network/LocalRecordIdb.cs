using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// WebGL 本地牌谱 IndexedDB 桥。桌面 / 安卓不使用。
/// </summary>
public sealed class LocalRecordIdbBridge : MonoBehaviour {
    public static LocalRecordIdbBridge Instance { get; private set; }

    readonly Queue<Action> _queue = new Queue<Action>();
    bool _busy;

    Action<byte[]> _bytesCallback;
    Action _doneCallback;
    Action<string> _errorCallback;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    static extern void LocalRecordIdbSnapshot(string key, string go, string method);
    [DllImport("__Internal")]
    static extern void LocalRecordIdbCommit(int token, IntPtr json, int len, string go, string method);

    [DllImport("__Internal")]
    static extern void LocalRecordIdbMigrationRead(int token, string key, string go, string method);
    [DllImport("__Internal")]
    static extern void LocalRecordIdbStage(int token, string key, IntPtr bytes, int length, string go, string method);
    [DllImport("__Internal")]
    static extern void LocalRecordIdbPendingKeys(string go, string method);

    [DllImport("__Internal")]
    static extern void LocalRecordIdbLoad(string key, string gameObjectName, string methodName);

    [DllImport("__Internal")]
    static extern int LocalRecordIdbCopy(IntPtr dst, int maxLen);

    [DllImport("__Internal")]
    static extern void LocalRecordIdbRelease(int token);
#endif

    public static void Ensure() {
        if (Instance != null) return;
        var go = new GameObject("LocalRecordIdbBridge");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<LocalRecordIdbBridge>();
    }

    public void BeginLoadIndex(Action<byte[]> onBytes, Action<string> onError) {
        Enqueue(() => StartLoad("__index__", onBytes, onError));
    }

    public void BeginLoad(string gameId, Action<byte[]> onBytes, Action<string> onError) {
        Enqueue(() => StartLoad(gameId, onBytes, onError));
    }

    internal void ReadMigration(int token, string key, Action<byte[]> done, Action<string> error) {
        Enqueue(() => {
            _bytesCallback = done; _errorCallback = error;
#if UNITY_WEBGL && !UNITY_EDITOR
            LocalRecordIdbMigrationRead(token, key, gameObject.name, nameof(OnLoadReady));
#else
            OnLoadReady("error|IndexedDB 不可用");
#endif
        });
    }

    internal void ListPending(Action<byte[]> done, Action<string> error) {
        Enqueue(() => {
            _bytesCallback = done; _errorCallback = error;
#if UNITY_WEBGL && !UNITY_EDITOR
            LocalRecordIdbPendingKeys(gameObject.name, nameof(OnLoadReady));
#else
            OnLoadReady("error|IndexedDB 不可用");
#endif
        });
    }

    internal void Stage(int token, string key, string value, Action done, Action<string> error) {
        Enqueue(() => {
            _doneCallback = done; _errorCallback = error;
#if UNITY_WEBGL && !UNITY_EDITOR
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { LocalRecordIdbStage(token, key, pin.AddrOfPinnedObject(), bytes.Length, gameObject.name, nameof(OnPutReady)); }
            finally { pin.Free(); }
#else
            OnPutReady("error|IndexedDB 不可用");
#endif
        });
    }

    int snapshotToken;
    internal void ReleaseSnapshot(int token) {
#if UNITY_WEBGL && !UNITY_EDITOR
        LocalRecordIdbRelease(token);
#endif
    }
    internal void BeginSnapshot(string key, Action<int,byte[]> done, Action<string> error) {
        Enqueue(() => {
            _bytesCallback = bytes => done(snapshotToken, bytes);
            _errorCallback = error;
#if UNITY_WEBGL && !UNITY_EDITOR
            LocalRecordIdbSnapshot(key ?? "", gameObject.name, nameof(OnSnapshotReady));
#else
            OnSnapshotReady("error|IndexedDB 不可用");
#endif
        });
    }
    public void OnSnapshotReady(string message) {
        var parts = (message ?? "").Split('|');
        if (parts.Length == 3 && int.TryParse(parts[2],out snapshotToken)) OnLoadReady(parts[0] + "|" + parts[1]);
        else OnLoadReady(message);
    }
    internal void BeginCommit(int token, DeviceRecordMigration.Plan plan, Action done, Action<string> error) {
        Enqueue(() => {
            _doneCallback = done; _errorCallback = error;
#if UNITY_WEBGL && !UNITY_EDITOR
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(
                new { writes = plan.Writes, deletes = plan.Deletes }));
            var pin = GCHandle.Alloc(bytes,GCHandleType.Pinned);
            try { LocalRecordIdbCommit(token,pin.AddrOfPinnedObject(),bytes.Length,gameObject.name,nameof(OnPutReady)); }
            catch(Exception e) { OnPutReady("error|" + e.Message); }
            finally { pin.Free(); }
#else
            OnPutReady("error|IndexedDB 不可用");
#endif
        });
    }

    void Enqueue(Action action) {
        _queue.Enqueue(action);
        Pump();
    }

    void Pump() {
        if (_busy || _queue.Count == 0) return;
        _busy = true;
        try { _queue.Dequeue()?.Invoke(); }
        catch (Exception e) { try { _errorCallback?.Invoke(e.Message); } finally { Finish(); } }
    }

    void Finish() {
        _busy = false;
        _bytesCallback = null;
        _doneCallback = null;
        _errorCallback = null;
        Pump();
    }

    void StartLoad(string key, Action<byte[]> onBytes, Action<string> onError) {
        _bytesCallback = onBytes;
        _errorCallback = onError;
#if UNITY_WEBGL && !UNITY_EDITOR
        try {
            LocalRecordIdbLoad(key, gameObject.name, "OnLoadReady");
        } catch (Exception e) {
            onError?.Invoke(e.Message);
            Finish();
        }
#else
        onError?.Invoke("empty");
        Finish();
#endif
    }

    public void OnLoadReady(string message) {
        try {
            if (message == "empty") {
                _bytesCallback?.Invoke(null);
                return;
            }
            if (string.IsNullOrEmpty(message) || message.StartsWith("error|", StringComparison.Ordinal)) {
                _errorCallback?.Invoke(message != null && message.Length > 6 ? message.Substring(6) : "IndexedDB 失败");
                return;
            }
            if (!message.StartsWith("ok|", StringComparison.Ordinal) ||
                !int.TryParse(message.Substring(3), out int length) ||
                length < 0) {
                _errorCallback?.Invoke("IndexedDB 回调无效");
                return;
            }
            var bytes = length == 0 ? Array.Empty<byte>() : CopyBytes(length);
            if (bytes == null) _errorCallback?.Invoke("牌谱数据复制不完整");
            else _bytesCallback?.Invoke(bytes);
        } finally {
            Finish();
        }
    }

    public void OnPutReady(string message) {
        try {
            if (message != null && message.StartsWith("error|", StringComparison.Ordinal)) {
                _errorCallback?.Invoke(message.Substring(6));
                return;
            }
            _doneCallback?.Invoke();
        } finally {
            Finish();
        }
    }

    static byte[] CopyBytes(int length) {
#if UNITY_WEBGL && !UNITY_EDITOR
        byte[] bytes = new byte[length];
        GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try {
            int copied = LocalRecordIdbCopy(handle.AddrOfPinnedObject(), length);
            if (copied != length) return null;
            return bytes;
        } finally {
            handle.Free();
        }
#else
        return null;
#endif
    }
}
