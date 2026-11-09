using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>Independent 3D surface images, retaining original bytes and existing preset paths.</summary>
public static class CardDesignImageLibrary
{
    const string CatalogKey = "CardDesignImageLibraryV1";
    const string Prefix = "card-design-image/v1/";
    [Serializable] public sealed class Entry { public string id, path, name; public bool back; }
    [Serializable] sealed class Catalog { public List<Entry> entries = new List<Entry>(); }
    public static event Action Changed;

    static Catalog Read()
    {
        try { return JsonUtility.FromJson<Catalog>(PlayerPrefs.GetString(CatalogKey, "")) ?? new Catalog(); }
        catch (ArgumentException) { return new Catalog(); }
    }
    static void Write(Catalog catalog)
    {
        PlayerPrefs.SetString(CatalogKey, JsonUtility.ToJson(catalog)); PlayerPrefs.Save();
        Changed?.Invoke();
    }
    public static string CurrentPath(bool back)
    {
        if (ConfigManager.Instance == null) return "";
        var selected = back ? ConfigManager.Instance.GetSelectedCardBackImage() : ConfigManager.Instance.GetSelectedTableBackground();
        return selected.isCustom ? selected.path ?? "" : "";
    }
    public static bool IsSolid(bool back) => back ? string.IsNullOrEmpty(CurrentPath(true))
        : ConfigManager.Instance == null || string.IsNullOrEmpty(CurrentPath(false)) || ConfigManager.Instance.TableFaceUseSolidColor || !ConfigManager.Instance.UseTableFaceBackground;

    public static List<Entry> GetEntries(bool back)
    {
        var catalog = Read();
        string current = CurrentPath(back);
        if (!string.IsNullOrEmpty(current) && !catalog.entries.Exists(e => e.back == back && e.path == current) && CanRead(current))
        {
            catalog.entries.Add(new Entry { id = Guid.NewGuid().ToString("N"), path = current, back = back, name = SuggestedName(back ? "原有牌背" : "原有背景", back) });
            Write(catalog);
        }
        return catalog.entries.FindAll(e => e.back == back);
    }
    public static string SuggestedName(string filename, bool back)
    {
        string stem;
        try { stem = Path.GetFileNameWithoutExtension(filename ?? ""); } catch (ArgumentException) { stem = ""; }
        if (!HandSurfaceLibrary.TryName(stem, out stem)) stem = back ? "我的牌背" : "我的3D背景";
        var catalog = Read(); string candidate = stem; int number = 2;
        while (catalog.entries.Exists(e => e.back == back && e.name == candidate))
        {
            string suffix = " " + number++;
            var elements = new StringInfo(stem);
            candidate = elements.SubstringByTextElements(0, Math.Min(elements.LengthInTextElements, 24 - suffix.Length)) + suffix;
        }
        return candidate;
    }
    public static bool CanRead(string path)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return UnityAssetIdb.GetCached(path) != null;
#else
        return !string.IsNullOrEmpty(path) && File.Exists(path);
#endif
    }
    public static Texture2D LoadTexture(string path)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return UnityAssetIdb.LoadTexture(path);
#else
        return CardBackManager.LoadTextureFromFile(path);
#endif
    }
    public static void SaveNew(byte[] bytes, string name, bool back, Action<Entry> saved, Action<string> error)
    {
        if (bytes == null || bytes.Length == 0 || bytes.Length > UnityAssetIdb.MaxImageBytes) { error?.Invoke("图片大小需在 8MB 以内"); return; }
        if (!HandSurfaceLibrary.TryName(name, out name)) { error?.Invoke("请输入 1–24 个字的名称"); return; }
        if (Read().entries.Exists(e => e.back == back && e.name == name)) { error?.Invoke("已存在同名图片，请换一个名称"); return; }
        string id = Guid.NewGuid().ToString("N");
#if UNITY_WEBGL && !UNITY_EDITOR
        string path = Prefix + id;
#else
        string path = Path.Combine(Application.persistentDataPath, "CardDesignImages", id + ".png");
#endif
        var entry = new Entry { id = id, path = path, name = name, back = back };
        void Complete()
        {
            try { var catalog = Read(); catalog.entries.Add(entry); Write(catalog); saved?.Invoke(entry); }
            catch (Exception e) { error?.Invoke("保存图片目录失败：" + e.Message); }
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.Put(path, bytes, Complete, error);
#else
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, bytes); }
        catch (Exception e) { error?.Invoke("保存图片失败：" + e.Message); return; }
        Complete();
#endif
    }
    public static void Rename(string id, string name, Action done, Action<string> error)
    {
        var catalog = Read(); var entry = catalog.entries.Find(e => e.id == id);
        if (entry == null || !HandSurfaceLibrary.TryName(name, out name)) { error?.Invoke("名称无效或图片已不存在"); return; }
        if (catalog.entries.Exists(e => e.id != id && e.back == entry.back && e.name == name)) { error?.Invoke("已存在同名图片"); return; }
        entry.name = name;
        try { Write(catalog); done?.Invoke(); } catch (Exception e) { error?.Invoke(e.Message); }
    }
    public static void Remove(string id)
    {
        var catalog = Read(); catalog.entries.RemoveAll(e => e.id == id);
        // Presets may still refer to these immutable files; removing a choice does not invalidate them.
        Write(catalog);
    }
    public static void Select(Entry entry)
    {
        if (entry == null || ConfigManager.Instance == null) return;
        if (!CanRead(entry.path)) { SceneConfigUi.ShowTip("图片文件无法读取，已保留当前设置"); return; }
        if (!CardBackManager.SelectSurfaceImage(entry.path, entry.back)) { SceneConfigUi.ShowTip("图片文件无法读取，已保留当前设置"); return; }
        Changed?.Invoke();
    }
    public static void SelectSolid(bool back, bool clearSelection = false)
    {
        if (ConfigManager.Instance == null) return;
        if (back || clearSelection) CardBackManager.SelectSurfaceImage("", back);
        else CardBackManager.SetTableFaceSolidColorEnabled(true);
        Changed?.Invoke();
    }
}
