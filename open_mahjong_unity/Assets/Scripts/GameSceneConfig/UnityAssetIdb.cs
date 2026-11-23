using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// WebGL 自定义大资源（牌背 / 桌布 / 边框）统一走 IndexedDB，与牌面包同一库。
/// 桌面不使用本类；选中项的 key 仍可写在 PlayerPrefs。
/// </summary>
public static class UnityAssetIdb {
    public const string KeyCardBack = "cardBack";
    public const string KeyHandBg = "handBg";
    public const string KeyHandBack = "handBack";
    public const string KeyTableBg = "tableBg";
    public const string PrefixTablecloth = "tablecloth/";
    public const string PrefixTableEdge = "tableedge/";
    public const int MaxImageBytes = 8 * 1024 * 1024;
    // File selection and drag/drop may be drafts: do not persist until named and confirmed.
    private const string ReadOnlyPickKey = "@read-only";

    private static readonly Dictionary<string, byte[]> Cache = new Dictionary<string, byte[]>();
    private static readonly Dictionary<string, byte[]> pendingWrites = new Dictionary<string, byte[]>();
    private static readonly LinkedList<string> recent = new LinkedList<string>();
    private static readonly HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> revisions = new Dictionary<string, int>();
    private static Dictionary<string, byte[]> activeReads;
    private static int cachedBytes;
    private static readonly List<Action> readyWaiters = new List<Action>();
    private static readonly List<Action<string>> readyErrors = new List<Action<string>>();
    private static bool loading;
    public static bool IsReady { get; private set; }

    public static void EnsureReady(Action onReady, Action<string> error = null) {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (IsReady) { onReady?.Invoke(); return; }
        if (onReady != null) readyWaiters.Add(onReady);
        if (error != null) readyErrors.Add(error);
        if (loading) return;
        loading = true;
        ListDirectory("", found => {
            foreach (string key in found) keys.Add(key);
            FinishDirectory(null);
        }, FinishDirectory);
#else
        IsReady = true;
        onReady?.Invoke();
#endif
    }

    private static void FinishDirectory(string error) {
        IsReady = error == null;
        loading = false;
        var callbacks = readyWaiters.ToArray(); readyWaiters.Clear();
        var errors = readyErrors.ToArray(); readyErrors.Clear();
        if (error != null) Debug.LogWarning(error);
        if (error == null) foreach (var callback in callbacks) {
            try { callback(); } catch (Exception e) { Debug.LogException(e); }
        } else foreach (var failed in errors) {
            try { failed(error); } catch (Exception e) { Debug.LogException(e); }
        }
    }

    public static bool Contains(string key) => key != null && (keys.Contains(key) || GetCached(key) != null);
    public static int Revision(string key) => key != null && revisions.TryGetValue(key, out var n) ? n : 0;
    public static List<string> KeysWithPrefix(string prefix) => keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList();

    public static byte[] GetCached(string key) {
        if (string.IsNullOrEmpty(key)) return null;
        if (pendingWrites.TryGetValue(key, out var pending)) return pending;
        if (activeReads != null && activeReads.TryGetValue(key, out var active)) return active;
        if (!Cache.TryGetValue(key, out var bytes)) return null;
        recent.Remove(key); recent.AddLast(key);
        return bytes;
    }

    private static void Remember(string key, byte[] data) {
        if (Cache.TryGetValue(key, out var old)) { cachedBytes -= old.Length; Cache.Remove(key); recent.Remove(key); }
        if (data == null) return;
        Cache[key] = data; cachedBytes += data.Length; recent.AddLast(key);
        while (cachedBytes > 16 * 1024 * 1024 || recent.Count > 64) {
            string oldest = recent.First.Value;
            cachedBytes -= Cache[oldest].Length; Cache.Remove(oldest); recent.RemoveFirst();
        }
    }

    // The preset writer already owns these bytes until its transaction completes.
    // Make drafts visible even if committed images are evicted while a save is pending.
    internal static void StagePendingWrite(string key, byte[] data) {
        if (data == null) pendingWrites.Remove(key);
        else pendingWrites[key] = data;
    }
    private static void Saved(string key, byte[] data) {
        if (data == null || (pendingWrites.TryGetValue(key, out var draft) && ReferenceEquals(draft, data))) pendingWrites.Remove(key);
        Remember(key, data);
        if (data != null) keys.Add(key); else keys.Remove(key);
        revisions[key] = Revision(key) + 1;
    }

    private static readonly Queue<Action<Action>> operations = new Queue<Action<Action>>();
    private static bool working;
    private static void Schedule(Action<Action> operation) {
        operations.Enqueue(operation);
        if (!working) StartNext();
    }
    private static void StartNext() {
        working = operations.Count > 0;
        if (!working) return;
        UnityAssetIdbBridge.Ensure();
        operations.Dequeue()(StartNext);
    }

    private static readonly Dictionary<(string, int, int), List<(Func<bool> wanted, Action<byte[], string> notify)>> reads =
        new Dictionary<(string, int, int), List<(Func<bool> wanted, Action<byte[], string> notify)>>();

    public static void Read(string key, Action<byte[]> done, Action<string> failed, int prefixBytes = 0, Func<bool> wanted = null) {
        var cached = GetCached(key);
        if (string.IsNullOrEmpty(key) || cached != null) { done(cached); return; }
#if UNITY_WEBGL && !UNITY_EDITOR
        var id = (key, prefixBytes, Revision(key));
        void Notify(byte[] bytes, string error) { if (error == null) done(bytes); else failed?.Invoke(error); }
        if (reads.TryGetValue(id, out var waiting)) { waiting.Add((wanted, Notify)); return; }
        waiting = new List<(Func<bool> wanted, Action<byte[], string> notify)> { (wanted, Notify) };
        reads.Add(id, waiting);
        Schedule(finish => {
            void Complete(byte[] bytes, string error) {
                reads.Remove(id);
                try {
                    foreach (var callback in waiting) {
                        try { callback.notify(bytes, error); } catch (Exception e) { Debug.LogException(e); }
                    }
                } finally { finish(); }
            }
            if (!waiting.Any(w => w.wanted == null || w.wanted())) { Complete(null, null); return; }
            UnityAssetIdbBridge.Instance.BeginRead(key, prefixBytes, bytes => Complete(bytes, null), reason => Complete(null, reason));
        });
#else
        done(null);
#endif
    }

    // Only the inputs to a single synchronous consumer (e.g. exporting the selected pack) are pinned.
    public static void WithAssets(IEnumerable<string> paths, Action done, Action<string> failed = null) {
#if UNITY_WEBGL && !UNITY_EDITOR
        var pending = new Queue<string>(paths.Where(k => !string.IsNullOrEmpty(k)).Distinct());
        var values = new Dictionary<string, byte[]>();
        void Next() {
            if (pending.Count > 0) {
                var key = pending.Dequeue();
                Read(key, bytes => { values[key] = bytes; Next(); }, failed ?? (reason => Debug.LogWarning(reason)));
                return;
            }
            var previous = activeReads;
            if (previous != null) foreach (var pair in previous) if (!values.ContainsKey(pair.Key)) values.Add(pair.Key, pair.Value);
            activeReads = values;
            try { done(); } finally { activeReads = previous; }
        }
        Next();
#else
        done();
#endif
    }

    internal static bool IsResolved(string key) => string.IsNullOrEmpty(key) ||
        (activeReads != null && activeReads.ContainsKey(key)) || pendingWrites.ContainsKey(key) || Cache.ContainsKey(key);

    const int DirectoryPageSize = 128;
    private static void ListDirectory(string prefix, Action<List<string>> done, Action<string> failed) {
        var found = new List<string>();
        void Page(string after) {
            Schedule(finish => UnityAssetIdbBridge.Instance.BeginDirectory(prefix, after, DirectoryPageSize, bytes => {
                try {
                    var page = JsonConvert.DeserializeObject<string[]>(Encoding.UTF8.GetString(bytes));
                    found.AddRange(page);
                    if (page.Length == DirectoryPageSize) Page(page[page.Length - 1]);
                    else done(found);
                } catch (Exception e) { failed(e.Message); }
                finally { finish(); }
            }, reason => { try { failed(reason); } finally { finish(); } }));
        }
        Page("");
    }

    // Directory consumers keep parsed metadata only. A Blob header is sliced before crossing into WASM.
    internal static void ReadDirectory(string prefix, int maxBytes, Action<string, byte[]> visit, Action done, Action<string> failed) {
        void Visit(List<string> directory) {
            var pending = new Queue<string>(directory);
            void Next() {
                if (pending.Count == 0) { done(); return; }
                string key = pending.Dequeue();
                Read(key, bytes => { visit(key, bytes); Next(); }, failed, maxBytes);
            }
            Next();
        }
        if (IsReady) Visit(KeysWithPrefix(prefix));
        else ListDirectory(prefix, found => { foreach (string key in found) keys.Add(key); Visit(found); }, failed);
    }

    public static Texture2D ToTexture(byte[] bytes) {
        if (bytes == null || bytes.Length == 0) {
            return null;
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        if (!LocalAssetImportLimits.ValidImage(bytes)) return null;
#endif
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (ImageConversion.LoadImage(texture, bytes)) {
            return texture;
        }
        UnityEngine.Object.Destroy(texture);
        return null;
    }

    public static Texture2D LoadTexture(string key) {
        return ToTexture(GetCached(key));
    }

    public static void Put(string key, byte[] data, Action onDone, Action<string> onError = null) {
        if (string.IsNullOrEmpty(key) || data == null || data.Length == 0) {
            onError?.Invoke("空数据");
            return;
        }
        if (data.Length > MaxImageBytes) {
            onError?.Invoke("图片超过 8MB，请压缩后再上传");
            return;
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        Schedule(finish => UnityAssetIdbBridge.Instance.BeginPut(key, data, () => {
            try { Saved(key, data); onDone?.Invoke(); } finally { finish(); }
        }, reason => { try { onError?.Invoke(reason); } finally { finish(); } }));
#else
        Saved(key, data); onDone?.Invoke();
#endif
    }

    public static void Delete(string key, Action onDone, Action<string> onError = null) {
        if (string.IsNullOrEmpty(key)) { onError?.Invoke("无效的资源键"); return; }
#if UNITY_WEBGL && !UNITY_EDITOR
        Schedule(finish => UnityAssetIdbBridge.Instance.BeginDelete(key, () => {
            try { Saved(key, null); onDone?.Invoke(); } finally { finish(); }
        }, reason => { try { onError?.Invoke(reason); } finally { finish(); } }));
#else
        Saved(key, null); onDone?.Invoke();
#endif
    }

    public static void PickBytes(string accept, Action<string, byte[]> onDone, Action<string> onError) {
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdbBridge.Ensure();
        UnityAssetIdbBridge.Instance.BeginPick(ReadOnlyPickKey, accept, onDone, onError);
#else
        onError?.Invoke("当前平台请使用本地文件选择");
#endif
    }

    public static void BindReadOnlyDrop(Action<string, byte[]> onDone, Action<string> onError) {
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdbBridge.Ensure();
        UnityAssetIdbBridge.Instance.BindDrop(ReadOnlyPickKey, onDone, onError);
#endif
    }

    public static void PickAndPut(string keyOrPrefix, string accept, Action<string, byte[]> onDone, Action<string> onError) {
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdbBridge.Ensure();
        UnityAssetIdbBridge.Instance.BeginPick(keyOrPrefix, accept, (key, bytes) => {
            if (bytes != null && bytes.Length > MaxImageBytes) {
                onError?.Invoke("图片超过 8MB，请压缩后再上传");
                return;
            }
            if (!string.IsNullOrEmpty(key) && bytes != null) {
                Saved(key, bytes);
            }
            onDone?.Invoke(key, bytes);
        }, onError);
#else
        onError?.Invoke("当前平台请使用本地文件选择");
#endif
    }

    public static void BindDrop(string key, Action<string, byte[]> onDone, Action<string> onError) {
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdbBridge.Ensure();
        UnityAssetIdbBridge.Instance.BindDrop(key, (dropKey, bytes) => {
            if (bytes != null && bytes.Length > MaxImageBytes) {
                onError?.Invoke("图片超过 8MB，请压缩后再上传");
                return;
            }
            if (!string.IsNullOrEmpty(dropKey) && bytes != null) {
                Saved(dropKey, bytes);
            }
            onDone?.Invoke(dropKey, bytes);
        }, onError);
#endif
    }

    public static void UnbindDrop() {
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdbBridge.Ensure();
        UnityAssetIdbBridge.Instance.UnbindDrop();
#endif
    }

}

/// <summary>WebGL IndexedDB 回调桥：文件体经 HEAPU8 拷贝，不走 SendMessage / PlayerPrefs。</summary>
public sealed class UnityAssetIdbBridge : MonoBehaviour {
    public static UnityAssetIdbBridge Instance { get; private set; }

    private Action<byte[]> readCallback;
    private Action<string> readError;
    private Action<string, byte[]> pickCallback;
    private Action<string> pickError;
    private Action<string, byte[]> dropCallback;
    private Action<string> dropError;
    private GCHandle pendingPut;
    private Action putDone;
    private Action<string> putError;
    private Action deleteDone;
    private Action<string> deleteError;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void UnityAssetIdbPickAndPut(string keyOrPrefix, string accept, string gameObjectName, string methodName);

    [DllImport("__Internal")]
    private static extern void UnityAssetIdbPut(string key, IntPtr data, int length, string gameObjectName, string methodName);

    [DllImport("__Internal")]
    private static extern int UnityAssetIdbCopy(int token, IntPtr dst, int maxLen);

    [DllImport("__Internal")]
    private static extern void UnityAssetIdbDelete(string key, string gameObjectName, string methodName);

    [DllImport("__Internal")]
    private static extern void UnityAssetIdbListKeys(string prefix, string after, int pageSize, string gameObjectName, string methodName);

    [DllImport("__Internal")]
    private static extern void UnityAssetIdbGet(string key, int limit, string gameObjectName, string methodName);

    [DllImport("__Internal")]
    private static extern void UnityAssetIdbBindDrop(string key, string gameObjectName, string methodName);

    [DllImport("__Internal")]
    private static extern void UnityAssetIdbUnbindDrop();
#endif

    public static void Ensure() {
        if (Instance != null) {
            return;
        }
        GameObject go = new GameObject("UnityAssetIdbBridge");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<UnityAssetIdbBridge>();
    }

    public void BeginDirectory(string prefix, string after, int pageSize, Action<byte[]> done, Action<string> failed) {
        readCallback = done;
        readError = failed;
#if UNITY_WEBGL && !UNITY_EDITOR
        try { UnityAssetIdbListKeys(prefix, after, pageSize, gameObject.name, nameof(OnReadReady)); }
        catch (Exception e) { failed(e.Message); }
#endif
    }

    public void BeginRead(string key, int limit, Action<byte[]> onBytes, Action<string> onError) {
        readCallback = onBytes;
        readError = onError;
#if UNITY_WEBGL && !UNITY_EDITOR
        try {
            UnityAssetIdbGet(key, limit, gameObject.name, nameof(OnReadReady));
        }
        catch (Exception e) {
            onError?.Invoke(e.Message);
        }
#else
        onError?.Invoke("empty");
#endif
    }

    public void BeginPut(string key, byte[] data, Action onDone, Action<string> onError) {
        putDone = onDone;
        putError = onError;
#if UNITY_WEBGL && !UNITY_EDITOR
        pendingPut = GCHandle.Alloc(data, GCHandleType.Pinned);
        try {
            UnityAssetIdbPut(key, pendingPut.AddrOfPinnedObject(), data.Length, gameObject.name, "OnPutReady");
        }
        catch (Exception e) { OnPutReady("error|" + e.Message); }
#else
        onDone?.Invoke();
#endif
    }

    public void BeginDelete(string key, Action onDone, Action<string> onError = null) {
        deleteDone = onDone;
        deleteError = onError;
#if UNITY_WEBGL && !UNITY_EDITOR
        try {
            UnityAssetIdbDelete(key, gameObject.name, "OnDeleteReady");
        }
        catch (Exception e) {
            onError?.Invoke(e.Message);
        }
#else
        onDone?.Invoke();
#endif
    }

    public void BeginPick(string keyOrPrefix, string accept, Action<string, byte[]> onDone, Action<string> onError) {
        if (pickCallback != null) { onError?.Invoke("请先完成上一次文件选择"); return; }
        pickCallback = onDone;
        pickError = onError;
#if UNITY_WEBGL && !UNITY_EDITOR
        try {
            UnityAssetIdbPickAndPut(keyOrPrefix, accept ?? "image/png,image/jpeg,image/jpg,image/webp", gameObject.name, "OnPickReady");
        }
        catch (Exception e) {
            pickCallback = null; pickError = null;
            onError?.Invoke("无法打开文件选择: " + e.Message);
        }
#endif
    }

    public void BindDrop(string key, Action<string, byte[]> onDone, Action<string> onError) {
        dropCallback = onDone;
        dropError = onError;
#if UNITY_WEBGL && !UNITY_EDITOR
        try {
            UnityAssetIdbBindDrop(key, gameObject.name, "OnDropReady");
        }
        catch (Exception e) {
            Debug.LogWarning("绑定拖拽失败: " + e.Message);
        }
#endif
    }

    public void UnbindDrop() {
#if UNITY_WEBGL && !UNITY_EDITOR
        try {
            UnityAssetIdbUnbindDrop();
        }
        catch {
            // ignore
        }
#endif
    }

    public void OnReadReady(string message) {
        var done = readCallback; var failed = readError;
        readCallback = null; readError = null;
        if (message == "empty") { done?.Invoke(null); return; }
        if (TryCopyOk(message, failed, out _, out var bytes)) done?.Invoke(bytes);
    }

    public void OnPutReady(string message) {
        if (pendingPut.IsAllocated) pendingPut.Free();
        var done = putDone; var failed = putError;
        putDone = null; putError = null;
        if (message != null && message.StartsWith("error|", StringComparison.Ordinal)) {
            failed?.Invoke(message.Substring(6));
            return;
        }
        done?.Invoke();
    }

    public void OnDeleteReady(string message) {
        var done = deleteDone; var error = deleteError;
        deleteDone = null; deleteError = null;
        if (message == "ok") done?.Invoke();
        else error?.Invoke(message != null && message.StartsWith("error|", StringComparison.Ordinal) ? message.Substring(6) : "IndexedDB 删除失败");
    }

    public void OnPickReady(string message) {
        var done = pickCallback; var failed = pickError;
        pickCallback = null; pickError = null;
        HandleKeyedBytes(message, done, failed);
    }

    public void OnDropReady(string message) {
        HandleKeyedBytes(message, dropCallback, dropError);
    }

    private static void HandleKeyedBytes(string message, Action<string, byte[]> onDone, Action<string> onError) {
        if (string.IsNullOrEmpty(message) || message == "cancel") {
            return;
        }
        if (!TryCopyOk(message, onError, out string key, out byte[] bytes)) {
            return;
        }
        onDone?.Invoke(key, bytes);
    }

    private static bool TryCopyOk(string message, Action<string> onError, out string key, out byte[] bytes) {
        key = null;
        bytes = null;
        if (string.IsNullOrEmpty(message) || message.StartsWith("error|", StringComparison.Ordinal)) {
            onError?.Invoke(message != null && message.Length > 6 ? message.Substring(6) : "IndexedDB 失败");
            return false;
        }
        if (!message.StartsWith("ok|", StringComparison.Ordinal)) {
            onError?.Invoke("IndexedDB 回调无效");
            return false;
        }
        string rest = message.Substring(3);
        int split = rest.IndexOf('|');
        string lenText = split >= 0 ? rest.Substring(0, split) : rest;
        int keyStart = split < 0 ? -1 : rest.IndexOf('|', split + 1);
        if (keyStart < 0 || !int.TryParse(rest.Substring(split + 1, keyStart - split - 1), out int token)) {
            onError?.Invoke("IndexedDB 传输标识无效"); return false;
        }
        key = rest.Substring(keyStart + 1);
        if (!int.TryParse(lenText, out int length) || length < 0) {
            onError?.Invoke("IndexedDB 回调无效");
            return false;
        }
        bytes = CopyAssetBytes(token, length);
        if (length > 0 && (bytes == null || bytes.Length == 0)) {
            onError?.Invoke("从 IndexedDB 拷贝失败");
            return false;
        }
        return true;
    }

    private static byte[] CopyAssetBytes(int token, int length) {
#if UNITY_WEBGL && !UNITY_EDITOR
        byte[] bytes = new byte[length];
        GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try {
            int copied = UnityAssetIdbCopy(token, handle.AddrOfPinnedObject(), length);
            if (copied != length) return null;
            return bytes;
        }
        finally {
            handle.Free();
        }
#else
        return null;
#endif
    }
}
