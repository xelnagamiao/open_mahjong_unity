using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>2D 手牌底图图库。名称与图片放在同一个记录中，存储成功后才公布选项。</summary>
public static partial class HandSurfaceLibrary
{
    public const string Prefix = "hand-surface/v1/";
    public const int NameLimit = 24;
    [Serializable] public sealed class Entry
    {
        public int version = 1;
        public string id, name, createdUtc;
        public bool back;
        public int width, height;
    }
    static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
    static readonly HashSet<string> busy = new HashSet<string>(StringComparer.Ordinal);
    static readonly List<Action> waiters = new List<Action>();
    static bool loading;
    public static bool Ready { get; private set; }
    public static event Action Changed;
    static string DirectoryPath => Path.Combine(Application.persistentDataPath, "HandSurfaces", "v1");

    public static bool IsId(string id) => id != null && id.StartsWith(Prefix, StringComparison.Ordinal)
        && Guid.TryParseExact(id.Substring(Prefix.Length), "N", out _);
    static string FilePath(string id)
    {
        if (!IsId(id)) throw new ArgumentException("Invalid hand surface ID");
        return Path.Combine(DirectoryPath, id.Substring(Prefix.Length) + ".hsi");
    }
    public static bool TryName(string input, out string name)
    {
        name = (input ?? "").Trim();
        if (name.Length == 0 || new StringInfo(name).LengthInTextElements > NameLimit) return false;
        foreach (char c in name) if (char.IsControl(c)) return false;
        return true;
    }
    public static string SuggestedName(string file, bool back, bool pair = false)
    {
        string stem;
        try { stem = Path.GetFileNameWithoutExtension(file ?? ""); } catch { stem = ""; }
        if (!TryName(stem, out stem) || stem == "hand-bg" || stem == "hand-back") stem = back ? "我的牌背" : "我的手牌背景";
        string candidate = stem;
        int number = 2;
        while (NameExists(candidate, back) || (pair && NameExists(candidate, !back)))
        {
            string suffix = " " + number++;
            var elements = new StringInfo(stem);
            candidate = elements.SubstringByTextElements(0, Math.Min(elements.LengthInTextElements, NameLimit - suffix.Length)) + suffix;
        }
        return candidate;
    }
    public static List<Entry> GetEntries(bool back)
    {
        var result = new List<Entry>();
        foreach (var e in entries.Values) if (e.back == back) result.Add(Clone(e));
        result.Sort((a, b) => { int order = string.CompareOrdinal(a.createdUtc, b.createdUtc); return order != 0 ? order : string.CompareOrdinal(a.id, b.id); });
        return result;
    }
    public static Entry Find(string id) => id != null && entries.TryGetValue(id, out var e) ? Clone(e) : null;
    static Entry Clone(Entry e) => JsonUtility.FromJson<Entry>(JsonUtility.ToJson(e));

    public static void EnsureReady(Action done = null)
    {
        if (Ready) { done?.Invoke(); return; }
        if (done != null) waiters.Add(done);
        if (loading) return;
        loading = true;
        entries.Clear();
#if UNITY_WEBGL && !UNITY_EDITOR
        void Failed(string reason) { loading = false; waiters.Clear(); Debug.LogWarning(reason); }
#endif
        void Complete() {
            Ready = true; loading = false;
            MigrateLegacy(false, () => MigrateLegacy(true, () =>
            {
                var callbacks = waiters.ToArray(); waiters.Clear();
                foreach (var callback in callbacks) callback?.Invoke();
                PublishChange();
            }));
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.ReadDirectory(Prefix, 8 + 4096 + 1, ReadEntryHeader,
            () => UnityAssetIdb.ReadDirectory(LayoutPrefix, 4097, ReadLayout, Complete, Failed), Failed);
#else
        try
        {
            if (Directory.Exists(DirectoryPath))
                foreach (string file in Directory.GetFiles(DirectoryPath, "*.hsi", SearchOption.TopDirectoryOnly))
                    ReadEntry(Prefix + Path.GetFileNameWithoutExtension(file));
        }
        catch (Exception e) { Debug.LogWarning("读取手牌图库失败：" + e.Message); }
        ReadFaceLayouts();
        Complete();
#endif
    }
    static void ReadEntry(string id)
    {
        try { if (IsId(id) && Unpack(Read(id), out var entry, out _) && entry.id == id) entries[id] = entry; }
        catch (Exception e) { Debug.LogWarning("跳过损坏的手牌图库记录：" + e.Message); }
    }
    static void ReadEntryHeader(string id, byte[] data)
    {
        try { if (IsId(id) && TryReadHeader(data, out var entry, out _) && entry.id == id) entries[id] = entry; }
        catch (Exception e) { Debug.LogWarning("跳过损坏的手牌图库记录：" + e.Message); }
    }
    static byte[] Read(string id)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return UnityAssetIdb.GetCached(id);
#else
        string path = FilePath(id);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > UnityAssetIdb.MaxImageBytes) return null;
        return File.ReadAllBytes(path);
#endif
    }
    public static Texture2D LoadTexture(string id, bool back)
    {
        if (!IsId(id)) return null;
        try
        {
            if (!Unpack(Read(id), out var entry, out var image) || entry.id != id || entry.back != back) return null;
            return UnityAssetIdb.ToTexture(image);
        }
        catch (Exception e) { Debug.LogWarning("读取手牌图片失败：" + e.Message); return null; }
    }
    public static byte[] ExportImage(string id)
    {
        if (!IsId(id) || !Unpack(Read(id), out var entry, out var image) || entry.id != id)
            throw new IOException("图片记录无法读取");
        return image;
    }
    public static void SaveNew(byte[] image, string name, bool back, Action<Entry> done, Action<string> error)
    {
        SaveImport(back ? null : image, back ? image : null, name,
            (background, rear) => done?.Invoke(back ? rear : background), error);
    }
    public static void Rename(string id, string name, Action done, Action<string> error)
    {
        if (!TryName(name, out name)) { error?.Invoke("请输入 1–24 个字的名称，不可换行"); return; }
        if (!entries.TryGetValue(id ?? "", out var current)) { error?.Invoke("该自定义图片不存在"); return; }
        if (NameExists(name, current.back, id)) { error?.Invoke("已存在同名" + (current.back ? "牌背" : "手牌背景") + "，请换一个名称"); return; }
        if (!ReserveName(name, current.back)) { error?.Invoke("同名图片正在保存，请稍候"); return; }
        if (!busy.Add(id)) { ReleaseName(name, current.back); error?.Invoke("正在保存，请稍候"); return; }
        void Fail(string reason) { busy.Remove(id); ReleaseName(name, current.back); error?.Invoke(reason); }
        UnityAssetIdb.WithAssets(new[] { id }, () => {
            byte[] image;
            try { if (!Unpack(Read(id), out _, out image)) throw new IOException("图片记录损坏"); }
            catch (Exception e) { busy.Remove(id); ReleaseName(name, current.back); error?.Invoke(e.Message); return; }
            var renamed = Clone(current); renamed.name = name;
            Write(id, Pack(renamed, image), () => { entries[id] = renamed; busy.Remove(id); ReleaseName(name, current.back); PublishChange(); done?.Invoke(); },
                Fail);
        }, Fail);
    }
    public static void Delete(string id, Action done, Action<string> error)
    {
        if (!IsId(id) || !entries.TryGetValue(id, out var entry)) { error?.Invoke("只能删除图库中的自定义图片"); return; }
        if (!busy.Add(id)) { error?.Invoke("正在保存，请稍候"); return; }
        void Complete()
        {
            entries.Remove(id); busy.Remove(id);
            if (!entry.back) RemoveFaceLayout(id);
            if (ConfigManager.Instance != null)
            {
                var selected = entry.back ? ConfigManager.Instance.GetSelectedHandBack() : ConfigManager.Instance.GetSelectedHandBackground();
                if (selected.isCustom && selected.path == id) CardBackManager.SelectBuiltinHandSurface(HandSurfaceStyles.DefaultIndex, entry.back, true);
            }
            PublishChange(); done?.Invoke();
        }
        void Fail(string reason) { busy.Remove(id); error?.Invoke(reason); }
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.Delete(id, Complete, Fail);
#else
        try { File.Delete(FilePath(id)); } catch (Exception e) { Fail("删除失败：" + e.Message); return; }
        Complete();
#endif
    }
    static void Write(string id, byte[] data, Action done, Action<string> error)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.Put(id, data, done, reason => error?.Invoke("保存失败：" + reason));
#else
        string path = FilePath(id), temporary = path + ".pending";
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllBytes(temporary, data);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        catch (Exception e) { error?.Invoke("保存失败：" + e.Message); return; }
        done?.Invoke();
#endif
    }
    static byte[] Pack(Entry entry, byte[] image)
    {
        byte[] json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(entry));
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        { writer.Write(0x31495348); writer.Write(json.Length); writer.Write(json); writer.Write(image); return stream.ToArray(); }
    }
    static bool Unpack(byte[] data, out Entry entry, out byte[] image)
    {
        image = null;
        if (!TryReadHeader(data, out entry, out int length)) return false;
        image = new byte[data.Length - 8 - length];
        Buffer.BlockCopy(data, 8 + length, image, 0, image.Length);
        return true;
    }
    static bool TryReadHeader(byte[] data, out Entry entry, out int length)
    {
        entry = null; length = 0;
        if (data == null || data.Length < 12 || data.Length > UnityAssetIdb.MaxImageBytes || BitConverter.ToInt32(data, 0) != 0x31495348) return false;
        length = BitConverter.ToInt32(data, 4);
        if (length < 2 || length > 4096 || length >= data.Length - 8) return false;
        entry = JsonUtility.FromJson<Entry>(Encoding.UTF8.GetString(data, 8, length));
        if (entry == null || entry.version != 1 || !IsId(entry.id) || !TryName(entry.name, out _)
            || entry.width <= 0 || entry.height <= 0 || entry.width > 4096 || entry.height > 4096) return false;
        return true;
    }
    static void MigrateLegacy(bool back, Action done)
    {
        string marker = "HandSurfaceMigrationV1." + (back ? "back" : "background");
        if (PlayerPrefs.GetInt(marker, 0) != 0) { done(); return; }
        string legacy = back ? CardBackManager.HandBackFilePath : CardBackManager.HandBgFilePath;
#if UNITY_WEBGL && !UNITY_EDITOR
        legacy = back ? UnityAssetIdb.KeyHandBack : UnityAssetIdb.KeyHandBg;
        UnityAssetIdb.Read(legacy, Migrate, reason => { Debug.LogWarning(reason); done(); });
#else
        byte[] bytes = null;
        try
        {
            if (File.Exists(legacy) && new FileInfo(legacy).Length < UnityAssetIdb.MaxImageBytes) bytes = File.ReadAllBytes(legacy);
        }
        catch (Exception e) { Debug.LogWarning("保留旧手牌图片，迁移暂未完成：" + e.Message); done(); return; }
        Migrate(bytes);
#endif
        void Migrate(byte[] image) {
            // 空缓存也可能是 IndexedDB 暂时不可用，不能把“未读取到”记成“已迁移”。
            if (image == null || image.Length == 0) { done(); return; }
            SaveNew(image, back ? "原有上传牌背" : "原有上传背景", back, entry =>
            {
                PlayerPrefs.SetInt(marker, 1); PlayerPrefs.Save();
                if (ConfigManager.Instance != null)
                {
                    var selected = back ? ConfigManager.Instance.GetSelectedHandBack() : ConfigManager.Instance.GetSelectedHandBackground();
                    if (selected.isCustom && selected.path == legacy) CardBackManager.SelectUploadedHandSurface(entry.id, back, true);
                }
                done();
            }, reason => { Debug.LogWarning("旧手牌图片仍保留：" + reason); done(); });
        }
    }
}
