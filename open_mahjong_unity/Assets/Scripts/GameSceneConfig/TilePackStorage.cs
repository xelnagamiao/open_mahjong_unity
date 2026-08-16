using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 自定义标准牌面存储：桌面写 persistentDataPath，WebGL 走 IndexedDB（不经 PlayerPrefs / SendMessage 传文件体）。
/// </summary>
public static class TilePackStorage {
    public const string HandDirName = "TilePacks/standard";
    public const string TableDirName = "TilePacks/standard-table";

    public static string HandDirectory => Path.Combine(Application.persistentDataPath, HandDirName);
    public static string TableDirectory => Path.Combine(Application.persistentDataPath, TableDirName);

    public static void SaveImported(TilePackImporter.Result imported) {
        if (imported == null || !imported.Success) {
            return;
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL 已在选文件时把 zip 写入 IndexedDB；运行时只保留内存缓存。
        return;
#else
        WritePngMap(HandDirectory, imported.HandPngs);
        WritePngMap(TableDirectory, imported.TablePngs);
#endif
    }

    public static void LoadPngsFromDisk(Dictionary<int, byte[]> hand, Dictionary<int, byte[]> table) {
        ReadPngMap(HandDirectory, hand);
        ReadPngMap(TableDirectory, table);
    }

    public static void ClearDisk() {
#if UNITY_WEBGL && !UNITY_EDITOR
        return;
#else
        TryDeleteDirectory(HandDirectory);
        TryDeleteDirectory(TableDirectory);
#endif
    }

    public static void PickZip(Action<byte[]> onZip, Action<string> onError) {
        TilePackWebGlBridge.Ensure();
        TilePackWebGlBridge.Instance.BeginPick(onZip, onError);
    }

    public static void LoadZipFromIndexedDb(Action<byte[]> onZip, Action<string> onError) {
        TilePackWebGlBridge.Ensure();
        TilePackWebGlBridge.Instance.BeginLoad(onZip, onError);
    }

    public static void ClearIndexedDb(Action onDone) {
        TilePackWebGlBridge.Ensure();
        TilePackWebGlBridge.Instance.BeginClear(onDone);
    }

    public static void LoadStreamingBytes(string relativePath, Action<byte[]> onData, Action<string> onError) {
        if (string.IsNullOrEmpty(relativePath)) {
            onError?.Invoke("路径为空");
            return;
        }
#if (UNITY_WEBGL || UNITY_ANDROID) && !UNITY_EDITOR
        TilePackWebGlBridge.Ensure();
        TilePackWebGlBridge.Instance.BeginLoadStreaming(relativePath, onData, onError);
#else
        string path = Path.Combine(Application.streamingAssetsPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) {
            onError?.Invoke("找不到预装资源: " + relativePath);
            return;
        }
        try {
            onData?.Invoke(File.ReadAllBytes(path));
        }
        catch (Exception e) {
            onError?.Invoke(e.Message);
        }
#endif
    }

    private static void WritePngMap(string directory, Dictionary<int, byte[]> pngs) {
        TryDeleteDirectory(directory);
        if (pngs == null || pngs.Count == 0) {
            return;
        }
        Directory.CreateDirectory(directory);
        foreach (var pair in pngs) {
            File.WriteAllBytes(Path.Combine(directory, pair.Key + ".png"), pair.Value);
        }
    }

    private static void ReadPngMap(string directory, Dictionary<int, byte[]> target) {
        if (target == null || !Directory.Exists(directory)) {
            return;
        }
        string[] files = Directory.GetFiles(directory, "*.png");
        for (int i = 0; i < files.Length; i++) {
            string name = Path.GetFileNameWithoutExtension(files[i]);
            if (!int.TryParse(name, out int tileId) || !TilePackIds.IsStandardFaceId(tileId)) {
                continue;
            }
            try {
                target[tileId] = File.ReadAllBytes(files[i]);
            }
            catch (Exception e) {
                Debug.LogWarning("读取自定义牌面失败: " + files[i] + " " + e.Message);
            }
        }
    }

    private static void TryDeleteDirectory(string directory) {
        if (!Directory.Exists(directory)) {
            return;
        }
        try {
            Directory.Delete(directory, true);
        }
        catch (Exception e) {
            Debug.LogWarning("清除自定义牌面目录失败: " + directory + " " + e.Message);
        }
    }
}

/// <summary>WebGL IndexedDB 回调桥。桌面选文件也走同一入口，便于面板只调一处。</summary>
public sealed class TilePackWebGlBridge : MonoBehaviour {
    public static TilePackWebGlBridge Instance { get; private set; }

    private Action<byte[]> zipCallback;
    private Action<string> errorCallback;
    private Action clearCallback;
    private Action<byte[]> streamingCallback;
    private Action<string> streamingError;
    private int expectedLength;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void TilePackIdbPickZip(string gameObjectName, string methodName);

    [DllImport("__Internal")]
    private static extern void TilePackIdbLoadZip(string gameObjectName, string methodName);

    [DllImport("__Internal")]
    private static extern int TilePackIdbCopyZip(IntPtr dst, int maxLen);

    [DllImport("__Internal")]
    private static extern void TilePackIdbClear(string gameObjectName, string methodName);
#endif

    public static void Ensure() {
        if (Instance != null) {
            return;
        }
        GameObject go = new GameObject("TilePackWebGlBridge");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<TilePackWebGlBridge>();
    }

    public void BeginPick(Action<byte[]> onZip, Action<string> onError) {
        zipCallback = onZip;
        errorCallback = onError;
#if UNITY_WEBGL && !UNITY_EDITOR
        try {
            TilePackIdbPickZip(gameObject.name, "OnZipReady");
        }
        catch (Exception e) {
            errorCallback?.Invoke("无法打开文件选择: " + e.Message);
        }
#elif (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        errorCallback?.Invoke("移动端请使用电脑制作 zip 后拷入，或在 WebGL/桌面客户端上传");
#else
        var extensions = new[] { new SFB.ExtensionFilter("Tile Pack", "zip") };
        string[] paths = SFB.StandaloneFileBrowser.OpenFilePanel("选择牌面包 zip", "", extensions, false);
        if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) {
            return;
        }
        try {
            byte[] bytes = File.ReadAllBytes(paths[0]);
            zipCallback?.Invoke(bytes);
        }
        catch (Exception e) {
            errorCallback?.Invoke("读取 zip 失败: " + e.Message);
        }
#endif
    }

    public void BeginLoad(Action<byte[]> onZip, Action<string> onError) {
        zipCallback = onZip;
        errorCallback = onError;
#if UNITY_WEBGL && !UNITY_EDITOR
        try {
            TilePackIdbLoadZip(gameObject.name, "OnZipReady");
        }
        catch (Exception e) {
            errorCallback?.Invoke(e.Message);
        }
#else
        onError?.Invoke("empty");
#endif
    }

    public void BeginClear(Action onDone) {
        clearCallback = onDone;
#if UNITY_WEBGL && !UNITY_EDITOR
        try {
            TilePackIdbClear(gameObject.name, "OnZipCleared");
        }
        catch {
            onDone?.Invoke();
        }
#else
        onDone?.Invoke();
#endif
    }

    public void BeginLoadStreaming(string relativePath, Action<byte[]> onData, Action<string> onError) {
        streamingCallback = onData;
        streamingError = onError;
        StartCoroutine(LoadStreamingCo(relativePath));
    }

    private IEnumerator LoadStreamingCo(string relativePath) {
        string url = Application.streamingAssetsPath.Replace('\\', '/') + "/" + relativePath.Replace('\\', '/');
        using (UnityWebRequest request = UnityWebRequest.Get(url)) {
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) {
                streamingError?.Invoke(request.error);
                yield break;
            }
            streamingCallback?.Invoke(request.downloadHandler.data);
        }
    }

    // JS SendMessage 回调：ok|{len} / empty / cancel / error|{msg}
    public void OnZipReady(string message) {
        if (string.IsNullOrEmpty(message) || message == "cancel" || message == "empty") {
            if (message == "empty") {
                errorCallback?.Invoke("empty");
            }
            return;
        }
        if (message.StartsWith("error|", StringComparison.Ordinal)) {
            errorCallback?.Invoke(message.Substring(6));
            return;
        }
        if (!message.StartsWith("ok|", StringComparison.Ordinal)
            || !int.TryParse(message.Substring(3), out expectedLength)
            || expectedLength <= 0) {
            errorCallback?.Invoke("IndexedDB 回调无效");
            return;
        }
        byte[] bytes = CopyZipBytes(expectedLength);
        if (bytes == null || bytes.Length == 0) {
            errorCallback?.Invoke("从 IndexedDB 拷贝 zip 失败");
            return;
        }
        zipCallback?.Invoke(bytes);
    }

    public void OnZipCleared(string message) {
        clearCallback?.Invoke();
    }

    private static byte[] CopyZipBytes(int length) {
#if UNITY_WEBGL && !UNITY_EDITOR
        byte[] bytes = new byte[length];
        GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try {
            int copied = TilePackIdbCopyZip(handle.AddrOfPinnedObject(), length);
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
