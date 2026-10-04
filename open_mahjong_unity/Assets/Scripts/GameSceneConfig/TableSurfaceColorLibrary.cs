using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Small immutable color presets, separate from uploaded images and official defaults.</summary>
public static class TableSurfaceColorLibrary
{
    const string Prefix = "surface-color/";
    [Serializable] public sealed class Entry
    {
        public string id, name;
        public bool cloth;
        public long createdUtc;
        public Color rgb = Color.white;
        public float brightness;
        public Color DisplayColor => ConfigManager.ApplyColorBrightness(rgb, brightness);
    }
    static readonly List<Entry> entries = new List<Entry>();
    static bool ready;
    public static bool IsReady => ready;
    public static bool IsId(string id) => id != null && id.StartsWith(Prefix, StringComparison.Ordinal);
    static string DirectoryPath => Path.Combine(Application.persistentDataPath, "TableSurfaceColors");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { ready = false; entries.Clear(); }

    public static void EnsureReady(Action done)
    {
        if (ready) { done?.Invoke(); return; }
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.EnsureReady(() => {
            if (!ready) {
                foreach (string id in UnityAssetIdb.KeysWithPrefix(Prefix)) Read(id, UnityAssetIdb.GetCached(id));
                ready = true;
            }
            done?.Invoke();
        });
#else
        try {
            if (Directory.Exists(DirectoryPath))
                foreach (string file in Directory.GetFiles(DirectoryPath, "*.json"))
                    try { Read(null, File.ReadAllBytes(file)); }
                    catch (Exception error) { Debug.LogWarning("读取自定义颜色失败: " + error.Message); }
        } catch (Exception error) { Debug.LogWarning("读取自定义颜色目录失败: " + error.Message); }
        ready = true;
        done?.Invoke();
#endif
    }
    static void Read(string expectedId, byte[] bytes)
    {
        try {
            var entry = JsonUtility.FromJson<Entry>(Encoding.UTF8.GetString(bytes));
            if (entry == null || !ValidId(entry.id) || (expectedId != null && entry.id != expectedId)
                || entry.cloth != entry.id.StartsWith(Prefix + "cloth/", StringComparison.Ordinal)) return;
            entry.rgb = Normalize(entry.rgb); entry.brightness = FiniteClamp(entry.brightness, -1, 1);
            if (string.IsNullOrWhiteSpace(entry.name)) entry.name = entry.cloth ? "自定义桌布" : "自定义边框";
            if (entries.Find(e => e.id == entry.id) == null) entries.Add(entry);
        } catch (Exception error) { Debug.LogWarning("自定义颜色数据无效: " + error.Message); }
    }
    static float FiniteClamp(float value, float min, float max) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp(value,min,max);
    static Color Normalize(Color color) => new Color(FiniteClamp(color.r,0,1),FiniteClamp(color.g,0,1),FiniteClamp(color.b,0,1),1);
    static bool ValidId(string id)
    {
        if (!IsId(id)) return false;
        string[] parts = id.Split('/');
        return parts.Length == 3 && (parts[1] == "cloth" || parts[1] == "frame")
            && parts[2].Length == 32 && Guid.TryParseExact(parts[2], "N", out _);
    }
    static string FilePath(string id) => Path.Combine(DirectoryPath, id.Substring(id.LastIndexOf('/')+1)+".json");
    public static List<Entry> GetEntries(bool cloth)
    {
        var result=entries.FindAll(e=>e.cloth==cloth);
        result.Sort((a,b)=>a.createdUtc!=b.createdUtc ? a.createdUtc.CompareTo(b.createdUtc) : string.CompareOrdinal(a.id,b.id));
        return result;
    }
    public static string GetSuggestedName(bool cloth)
    {
        string prefix = cloth ? "自定义桌布 " : "自定义边框 ";
        int number = 1;
        while (entries.Exists(e => e.cloth == cloth && e.name == prefix + number)) number++;
        return prefix + number;
    }
    public static bool TryGet(string id, bool cloth, out Entry entry)
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        if (!ready) EnsureReady(null);
#endif
        entry = entries.Find(e => e.id == id && e.cloth == cloth);
        return entry != null;
    }
    public static void Create(bool cloth, Color rgb, float brightness, Action<Entry> done, Action<string> failed)
    {
        Create(cloth, rgb, brightness, null, done, failed);
    }
    public static void Create(bool cloth, Color rgb, float brightness, string name, Action<Entry> done, Action<string> failed)
    {
        EnsureReady(() => {
            string entryName = string.IsNullOrWhiteSpace(name) ? GetSuggestedName(cloth) : name.Trim();
            entryName = entryName.Replace("\r", " ").Replace("\n", " ");
            if (entryName.Length > 24) entryName = entryName.Substring(0, 24).Trim();
            if (entryName.Length == 0) { failed?.Invoke("请输入预设名称"); return; }
            var entry = new Entry { id = Prefix + (cloth ? "cloth/" : "frame/") + Guid.NewGuid().ToString("N"),
                name = entryName, cloth = cloth, createdUtc=DateTime.UtcNow.Ticks, rgb = Normalize(rgb), brightness = FiniteClamp(brightness,-1,1) };
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(entry));
#if UNITY_WEBGL && !UNITY_EDITOR
            UnityAssetIdb.Put(entry.id, bytes, () => { entries.Add(entry); done?.Invoke(entry); }, error => {
                UnityAssetIdb.StagePendingWrite(entry.id, null); failed?.Invoke(error);
            });
#else
            try { Directory.CreateDirectory(DirectoryPath); File.WriteAllBytes(FilePath(entry.id), bytes); }
            catch (Exception error) { failed?.Invoke(error.Message); return; }
            entries.Add(entry); done?.Invoke(entry);
#endif
        });
    }
    public static void Delete(string id, Action done, Action<string> failed)
    {
        if (!ValidId(id)) { failed?.Invoke("无效的颜色预设"); return; }
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.Delete(id, () => { entries.RemoveAll(e => e.id == id); done?.Invoke(); });
#else
        try { if (File.Exists(FilePath(id))) File.Delete(FilePath(id)); }
        catch (Exception error) { failed?.Invoke(error.Message); return; }
        entries.RemoveAll(e => e.id == id); done?.Invoke();
#endif
    }
}
