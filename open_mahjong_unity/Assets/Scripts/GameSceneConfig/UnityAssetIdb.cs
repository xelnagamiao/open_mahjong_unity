using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>
/// WebGL 自定义大资源（牌背 / 桌布 / 边框）统一走 IndexedDB，与牌面包同一库。
/// 桌面不使用本类；选中项的 key 仍可写在 PlayerPrefs。
/// </summary>
public static class UnityAssetIdb {
    public const string KeyCardBack = "cardBack";
    public const string KeyHandBg = "handBg";
    public const string KeyHandBack = "handBack";
    public const string PrefixTablecloth = "tablecloth/";
    public const string PrefixTableEdge = "tableedge/";
    public const int MaxImageBytes = 8 * 1024 * 1024;

    private static readonly Dictionary<string, byte[]> Cache = new Dictionary<string, byte[]>();
    private static readonly List<Action> ReadyWaiters = new List<Action>();
    private static bool ready;
    private static bool loading;

    public static bool IsReady => ready;

    public static void EnsureReady(Action onReady) {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (onReady != null) {
            ReadyWaiters.Add(onReady);
        }
        if (ready) {
            FlushWaiters();
            return;
        }
        if (loading) {
            return;
        }
        loading = true;
        UnityAssetIdbBridge.Ensure();
        UnityAssetIdbBridge.Instance.BeginLoadAll(packed => {
            UnpackIntoCache(packed);
            FinishReady();
        }, _ => FinishReady());
#else
        ready = true;
        onReady?.Invoke();
#endif
    }

    public static byte[] GetCached(string key) {
        if (string.IsNullOrEmpty(key)) {
            return null;
        }
        return Cache.TryGetValue(key, out byte[] data) ? data : null;
    }

    public static List<string> KeysWithPrefix(string prefix) {
        var keys = new List<string>();
        if (string.IsNullOrEmpty(prefix)) {
            return keys;
        }
        foreach (var pair in Cache) {
            if (pair.Key.StartsWith(prefix, StringComparison.Ordinal) && pair.Value != null && pair.Value.Length > 0) {
                keys.Add(pair.Key);
            }
        }
        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    public static Texture2D ToTexture(byte[] bytes) {
        if (bytes == null || bytes.Length == 0) {
            return null;
        }
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
        Cache[key] = data;
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdbBridge.Ensure();
        UnityAssetIdbBridge.Instance.BeginPut(key, data, onDone, onError);
#else
        onDone?.Invoke();
#endif
    }

    public static void Delete(string key, Action onDone) {
        if (!string.IsNullOrEmpty(key)) {
            Cache.Remove(key);
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdbBridge.Ensure();
        UnityAssetIdbBridge.Instance.BeginDelete(key, onDone);
#else
        onDone?.Invoke();
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
                Cache[key] = bytes;
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
                Cache[dropKey] = bytes;
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

    private static void FinishReady() {
        ready = true;
        loading = false;
        FlushWaiters();
    }

    private static void FlushWaiters() {
        if (ReadyWaiters.Count == 0) {
            return;
        }
        Action[] list = ReadyWaiters.ToArray();
        ReadyWaiters.Clear();
        for (int i = 0; i < list.Length; i++) {
            try {
                list[i]?.Invoke();
            }
            catch (Exception e) {
                Debug.LogWarning("UnityAssetIdb 就绪回调失败: " + e.Message);
            }
        }
    }

    private static void UnpackIntoCache(byte[] packed) {
        Cache.Clear();
        if (packed == null || packed.Length < 6) {
            return;
        }
        if (packed[0] != 79 || packed[1] != 77 || packed[2] != 65 || packed[3] != 66) {
            Debug.LogWarning("UnityAssetIdb 打包格式无效");
            return;
        }
        int count = BitConverter.ToUInt16(packed, 4);
        int offset = 6;
        for (int i = 0; i < count; i++) {
            if (offset + 2 > packed.Length) {
                break;
            }
            int keyLen = BitConverter.ToUInt16(packed, offset);
            offset += 2;
            if (keyLen < 0 || offset + keyLen + 4 > packed.Length) {
                break;
            }
            string key = Encoding.UTF8.GetString(packed, offset, keyLen);
            offset += keyLen;
            int dataLen = (int)BitConverter.ToUInt32(packed, offset);
            offset += 4;
            if (dataLen < 0 || offset + dataLen > packed.Length) {
                break;
            }
            var data = new byte[dataLen];
            Buffer.BlockCopy(packed, offset, data, 0, dataLen);
            offset += dataLen;
            if (!string.IsNullOrEmpty(key) && dataLen > 0) {
                Cache[key] = data;
            }
        }
    }
}

/// <summary>WebGL IndexedDB 回调桥：文件体经 HEAPU8 拷贝，不走 SendMessage / PlayerPrefs。</summary>
public sealed class UnityAssetIdbBridge : MonoBehaviour {
    public static UnityAssetIdbBridge Instance { get; private set; }

    private Action<byte[]> loadAllCallback;
    private Action<string> loadAllError;
    private Action<string, byte[]> pickCallback;
    private Action<string> pickError;
    private Action<string, byte[]> dropCallback;
    private Action<string> dropError;
    private Action putDone;
    private Action<string> putError;
    private Action deleteDone;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void UnityAssetIdbPickAndPut(string keyOrPrefix, string accept, string gameObjectName, string methodName);

    [DllImport("__Internal")]
    private static extern void UnityAssetIdbPut(string key, IntPtr data, int length, string gameObjectName, string methodName);

    [DllImport("__Internal")]
    private static extern int UnityAssetIdbCopy(IntPtr dst, int maxLen);

    [DllImport("__Internal")]
    private static extern void UnityAssetIdbDelete(string key, string gameObjectName, string methodName);

    [DllImport("__Internal")]
    private static extern void UnityAssetIdbLoadAll(string gameObjectName, string methodName);

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

    public void BeginLoadAll(Action<byte[]> onPacked, Action<string> onError) {
        loadAllCallback = onPacked;
        loadAllError = onError;
#if UNITY_WEBGL && !UNITY_EDITOR
        try {
            UnityAssetIdbLoadAll(gameObject.name, "OnLoadAllReady");
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
        GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try {
            UnityAssetIdbPut(key, handle.AddrOfPinnedObject(), data.Length, gameObject.name, "OnPutReady");
        }
        catch (Exception e) {
            onError?.Invoke(e.Message);
        }
        finally {
            handle.Free();
        }
#else
        onDone?.Invoke();
#endif
    }

    public void BeginDelete(string key, Action onDone) {
        deleteDone = onDone;
#if UNITY_WEBGL && !UNITY_EDITOR
        try {
            UnityAssetIdbDelete(key, gameObject.name, "OnDeleteReady");
        }
        catch {
            onDone?.Invoke();
        }
#else
        onDone?.Invoke();
#endif
    }

    public void BeginPick(string keyOrPrefix, string accept, Action<string, byte[]> onDone, Action<string> onError) {
        pickCallback = onDone;
        pickError = onError;
#if UNITY_WEBGL && !UNITY_EDITOR
        try {
            UnityAssetIdbPickAndPut(keyOrPrefix, accept ?? "image/png,image/jpeg,image/jpg,image/webp", gameObject.name, "OnPickReady");
        }
        catch (Exception e) {
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

    public void OnLoadAllReady(string message) {
        if (message == "empty") {
            loadAllCallback?.Invoke(null);
            return;
        }
        if (!TryCopyOk(message, loadAllError, out _, out byte[] bytes)) {
            return;
        }
        loadAllCallback?.Invoke(bytes);
    }

    public void OnPutReady(string message) {
        if (message != null && message.StartsWith("error|", StringComparison.Ordinal)) {
            putError?.Invoke(message.Substring(6));
            return;
        }
        putDone?.Invoke();
    }

    public void OnDeleteReady(string message) {
        deleteDone?.Invoke();
    }

    public void OnPickReady(string message) {
        HandleKeyedBytes(message, pickCallback, pickError);
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
        key = split >= 0 ? rest.Substring(split + 1) : "";
        if (!int.TryParse(lenText, out int length) || length < 0) {
            onError?.Invoke("IndexedDB 回调无效");
            return false;
        }
        bytes = length == 0 ? Array.Empty<byte>() : CopyAssetBytes(length);
        if (length > 0 && (bytes == null || bytes.Length == 0)) {
            onError?.Invoke("从 IndexedDB 拷贝失败");
            return false;
        }
        return true;
    }

    private static byte[] CopyAssetBytes(int length) {
#if UNITY_WEBGL && !UNITY_EDITOR
        byte[] bytes = new byte[length];
        GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try {
            int copied = UnityAssetIdbCopy(handle.AddrOfPinnedObject(), length);
            if (copied != length) {
                if (copied <= 0) {
                    return null;
                }
                var trimmed = new byte[copied];
                Buffer.BlockCopy(bytes, 0, trimmed, 0, copied);
                return trimmed;
            }
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
