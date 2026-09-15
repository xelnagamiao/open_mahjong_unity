using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>每次导入保留一个独立牌组；PlayerPrefs 只保存目录和名称，文件体保存在本地或 IndexedDB。</summary>
public static class TilePackLibrary {
    const string CatalogKey = "CustomTilePackLibraryV1";
    [Serializable] public sealed class Entry {
        public string id;
        public string fileName;
        public string DisplayName => string.IsNullOrEmpty(fileName) ? "自定义" : Path.GetFileNameWithoutExtension(fileName);
    }
    [Serializable] sealed class Catalog { public List<Entry> entries = new List<Entry>(); }

    public static List<Entry> GetEntries() {
        Catalog catalog = null;
        try { catalog = JsonUtility.FromJson<Catalog>(PlayerPrefs.GetString(CatalogKey, "")); }
        catch (Exception e) { Debug.LogWarning("读取自定义牌组目录失败：" + e.Message); }
        var entries = catalog?.entries ?? new List<Entry>();
        entries.RemoveAll(e => e == null || !TilePackIds.IsCustomPack(e.id));
        // 旧版唯一自定义包保留原存储位置，在第一次新增前纳入目录。
        string legacyName = ConfigManager.Instance != null ? ConfigManager.Instance.CustomTilePackFileName : "";
        bool hasLegacy = !string.IsNullOrEmpty(legacyName)
            || (ConfigManager.Instance != null && ConfigManager.Instance.StandardTilePackId == TilePackIds.PackCustom);
#if !UNITY_WEBGL || UNITY_EDITOR
        hasLegacy |= Directory.Exists(TilePackStorage.HandDirectory)
            && Directory.GetFiles(TilePackStorage.HandDirectory,"*.png").Length > 0;
#endif
        if (hasLegacy && !entries.Exists(e => e.id == TilePackIds.PackCustom))
            entries.Insert(0, new Entry { id = TilePackIds.PackCustom, fileName = legacyName });
        return entries;
    }

    public static string DisplayName(string id) => GetEntries().Find(e => e.id == id)?.DisplayName ?? "自定义";

    public static void SaveNew(byte[] zip, string fileName, TilePackImporter.Result validated,
        Action<Entry> onSaved, Action<string> onError) {
        if (validated == null || !validated.Success || zip == null || zip.Length == 0
            || zip.Length > TilePackImporter.MaxUncompressedBytes) {
            onError?.Invoke("牌面包无效，未保存"); return;
        }
        var entry = new Entry { id = "custom-" + Guid.NewGuid().ToString("N"), fileName = Path.GetFileName(fileName ?? "自定义.zip") };
        Action complete = () => {
            var entries = GetEntries(); entries.Add(entry);
            PlayerPrefs.SetString(CatalogKey, JsonUtility.ToJson(new Catalog { entries = entries }));
            PlayerPrefs.Save(); onSaved?.Invoke(entry);
        };
#if UNITY_WEBGL && !UNITY_EDITOR
        TilePackLibraryRequest.Save(entry.id, zip, complete, onError);
#else
        try {
            string path = FilePath(entry.id);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, zip);
        } catch (Exception e) { onError?.Invoke("保存牌面包失败：" + e.Message); return; }
        complete();
#endif
    }

    public static void Load(string id, Action<TilePackImporter.Result> onLoaded, Action<string> onError) {
        if (!TilePackIds.IsCustomPack(id)) { onError?.Invoke("无效牌组"); return; }
        Action<byte[]> import = bytes => {
            var result = TilePackImporter.Import(bytes);
            if (result.Success) onLoaded?.Invoke(result); else onError?.Invoke(result.Error);
        };
        if (id == TilePackIds.PackCustom) {
#if UNITY_WEBGL && !UNITY_EDITOR
            TilePackLibraryRequest.Load(id, import, onError);
#else
            var legacy = new TilePackImporter.Result();
            TilePackStorage.LoadPngsFromDisk(legacy.HandPngs, legacy.TablePngs);
            if (legacy.Success) onLoaded?.Invoke(legacy); else onError?.Invoke("旧自定义牌组文件缺失");
#endif
            return;
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        TilePackLibraryRequest.Load(id, import, onError);
#else
        try { import(File.ReadAllBytes(FilePath(id))); }
        catch (Exception e) { onError?.Invoke("读取牌面包失败：" + e.Message); }
#endif
    }

    public static string FilePath(string id) {
        if (!TilePackIds.IsCustomPack(id) || id == TilePackIds.PackCustom) throw new ArgumentException("Invalid library ID");
        return Path.Combine(Application.persistentDataPath, "TilePacks", "library", id + ".zip");
    }
}

#if UNITY_WEBGL && !UNITY_EDITOR
// 独立请求对象避免快速切换牌组时覆盖另一个异步请求的回调。
public sealed class TilePackLibraryRequest : MonoBehaviour {
    [DllImport("__Internal")] static extern void TilePackIdbSaveLibraryZip(string key, byte[] bytes, int length, string go);
    [DllImport("__Internal")] static extern void TilePackIdbLoadLibraryZip(string key, string go);
    [DllImport("__Internal")] static extern int TilePackIdbCopyZip(IntPtr dst, int length);
    Action saved; Action<byte[]> loaded; Action<string> error;
    static TilePackLibraryRequest Create(Action<string> error) {
        var go = new GameObject("TilePackRequest_" + Guid.NewGuid().ToString("N"));
        DontDestroyOnLoad(go); var request = go.AddComponent<TilePackLibraryRequest>(); request.error = error; return request;
    }
    public static void Save(string id, byte[] bytes, Action saved, Action<string> error) {
        var r = Create(error); r.saved = saved;
        try { TilePackIdbSaveLibraryZip("tilepack/" + id, bytes, bytes.Length, r.name); }
        catch (Exception e) { r.OnResult("error|" + e.Message); }
    }
    public static void Load(string id, Action<byte[]> loaded, Action<string> error) {
        var r = Create(error); r.loaded = loaded;
        try { TilePackIdbLoadLibraryZip(id == TilePackIds.PackCustom ? "standardZip" : "tilepack/" + id, r.name); }
        catch (Exception e) { r.OnResult("error|" + e.Message); }
    }
    public void OnResult(string message) {
        try {
            if (message == "ok") { saved?.Invoke(); return; }
            if (!message.StartsWith("ok|", StringComparison.Ordinal)
                || !int.TryParse(message.Substring(3), out int length) || length <= 0
                || length > TilePackImporter.MaxUncompressedBytes) {
                error?.Invoke(message.StartsWith("error|") ? message.Substring(6) : "牌面文件缺失"); return;
            }
            var bytes = new byte[length]; var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try {
                if (TilePackIdbCopyZip(handle.AddrOfPinnedObject(), length) != length) { error?.Invoke("读取牌面包失败"); return; }
            } finally { handle.Free(); }
            loaded?.Invoke(bytes);
        } finally { Destroy(gameObject); }
    }
}
#endif
