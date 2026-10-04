using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public static partial class HandSurfaceLibrary
{
    // 百分比相对于牌体尺寸，独立于屏幕分辨率；只对前景花纹生效。
    [Serializable] public struct FaceLayout
    {
        public float x, y, scale;
        public static FaceLayout Default => new FaceLayout { scale = 1f };
        public FaceLayout Normalized => new FaceLayout {
            x = Finite(x, -.5f, .5f, 0), y = Finite(y, -.5f, .5f, 0),
            scale = Finite(scale, .25f, 2f, 1) };
        static float Finite(float v, float min, float max, float fallback) =>
            float.IsNaN(v) || float.IsInfinity(v) || (min > 0 && v <= 0) ? fallback : Mathf.Clamp(v, min, max);
    }
    const string LayoutPrefix = "hand-layout/v1/";
    [Serializable] sealed class LayoutRecord { public int version = 1; public string key; public FaceLayout layout; }
    static readonly Dictionary<string, FaceLayout> faceLayouts = new Dictionary<string, FaceLayout>(StringComparer.Ordinal);
    static string previewLayoutKey;
    static FaceLayout previewLayout;
    static string LayoutDirectory => Path.Combine(DirectoryPath, "Layouts");

    static string FaceLayoutKey(string backgroundPath)
    {
        int builtin = HandSurfaceStyles.FindIndex(backgroundPath, false);
        if (builtin >= 0) return LayoutPrefix + "builtin-" + builtin;
        return IsId(backgroundPath) ? LayoutPrefix + "upload-" + backgroundPath.Substring(Prefix.Length) : null;
    }
    static bool ValidLayoutKey(string key)
    {
        if (key == null || !key.StartsWith(LayoutPrefix, StringComparison.Ordinal)) return false;
        string suffix = key.Substring(LayoutPrefix.Length);
        return (suffix.StartsWith("builtin-", StringComparison.Ordinal) && int.TryParse(suffix.Substring(8), out int i) && i >= 0 && i < HandSurfaceStyles.Count)
            || (suffix.StartsWith("upload-", StringComparison.Ordinal) && Guid.TryParseExact(suffix.Substring(7), "N", out _));
    }
    static string LayoutFile(string key)
    {
        if (!ValidLayoutKey(key)) throw new ArgumentException("Invalid face layout key");
        return Path.Combine(LayoutDirectory, key.Substring(LayoutPrefix.Length) + ".json");
    }
    public static bool CanEditFaceLayout(string backgroundPath)
    {
        if (!Ready) return false;
        if (HandSurfaceStyles.FindIndex(backgroundPath, false) >= 0) return true;
        return backgroundPath != null && entries.TryGetValue(backgroundPath, out var entry) && !entry.back;
    }
    public static FaceLayout GetFaceLayout(string backgroundPath)
    {
        string key = FaceLayoutKey(backgroundPath);
        if (key != null && key == previewLayoutKey) return previewLayout;
        return key != null && faceLayouts.TryGetValue(key, out var layout) ? layout : DefaultFaceLayout(backgroundPath);
    }
    public static FaceLayout DefaultFaceLayout(string backgroundPath)
    {
        // 272×424 新版牌体：正面中心约 y=233，原花纹基准中心约 y=207。
        // 经典正面高约 344px，新版正面高约 376px，花纹等比放大至 109%。
        // 三套内置花纹共用此基准；经典及上传图保留原始基准。
        int builtin = HandSurfaceStyles.FindIndex(backgroundPath, false);
        return builtin > 0 ? new FaceLayout { y = -.02f, scale = 1.09f } : FaceLayout.Default;
    }
    public static FaceLayout CurrentFaceLayout => ConfigManager.Instance == null ? FaceLayout.Default
        : GetFaceLayout(ConfigManager.Instance.GetSelectedHandBackground().path);

    public static void RestoreDefaultFaceLayout(string backgroundPath)
    {
        if (!Ready) { EnsureReady(() => RestoreDefaultFaceLayout(backgroundPath)); return; }
        RemoveFaceLayout(backgroundPath);
        TileFaceResolver.NotifyHandLayoutChanged();
    }

    public static void PreviewFaceLayout(string backgroundPath, FaceLayout layout)
    {
        if (!CanEditFaceLayout(backgroundPath)) return;
        previewLayoutKey = FaceLayoutKey(backgroundPath); previewLayout = layout.Normalized;
        TileFaceResolver.NotifyHandLayoutChanged();
    }
    public static void CancelFaceLayoutPreview(string backgroundPath)
    {
        if (previewLayoutKey == null || previewLayoutKey != FaceLayoutKey(backgroundPath)) return;
        previewLayoutKey = null;
        TileFaceResolver.NotifyHandLayoutChanged();
    }
    public static void SaveFaceLayout(string backgroundPath, FaceLayout layout, Action done, Action<string> error)
    {
        if (!Ready) { EnsureReady(() => SaveFaceLayout(backgroundPath, layout, done, error)); return; }
        if (!CanEditFaceLayout(backgroundPath)) { error?.Invoke("背景不存在，无法保存牌面位置"); return; }
        string key = FaceLayoutKey(backgroundPath);
        if (!busy.Add(key)) { error?.Invoke("正在保存，请稍候"); return; }
        layout = layout.Normalized;
        byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new LayoutRecord { key = key, layout = layout }));
        void Complete() {
            faceLayouts[key] = layout; busy.Remove(key);
            if (previewLayoutKey == key) previewLayoutKey = null;
            TileFaceResolver.NotifyHandLayoutChanged(); done?.Invoke();
        }
        void Fail(string reason) { busy.Remove(key); error?.Invoke("保存牌面位置失败：" + reason); }
#if UNITY_WEBGL && !UNITY_EDITOR
        byte[] previous = UnityAssetIdb.GetCached(key);
        UnityAssetIdb.Put(key, data, Complete, reason => { UnityAssetIdb.StagePendingWrite(key, previous); Fail(reason); });
#else
        string file = LayoutFile(key), temporary = file + ".pending";
        try {
            Directory.CreateDirectory(LayoutDirectory); File.WriteAllBytes(temporary, data);
            if (File.Exists(file)) File.Replace(temporary, file, null); else File.Move(temporary, file);
        } catch (Exception e) { Fail(e.Message); return; }
        Complete();
#endif
    }
    static void ReadFaceLayouts()
    {
        faceLayouts.Clear(); previewLayoutKey = null;
        void ReadLayout(string key, byte[] data) {
            if (data == null || data.Length > 4096) return;
            try {
                var record = JsonUtility.FromJson<LayoutRecord>(Encoding.UTF8.GetString(data));
                if (record != null && record.version == 1 && ValidLayoutKey(key) && record.key == key)
                    faceLayouts[key] = record.layout.Normalized;
            } catch (Exception e) { Debug.LogWarning("保留但跳过损坏的牌面位置设置：" + e.Message); }
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        foreach (string key in UnityAssetIdb.KeysWithPrefix(LayoutPrefix)) ReadLayout(key, UnityAssetIdb.GetCached(key));
#else
        try {
            if (Directory.Exists(LayoutDirectory)) foreach (string file in Directory.GetFiles(LayoutDirectory, "*.json"))
                if (new FileInfo(file).Length <= 4096) ReadLayout(LayoutPrefix + Path.GetFileNameWithoutExtension(file), File.ReadAllBytes(file));
        } catch (Exception e) { Debug.LogWarning("读取牌面位置设置失败：" + e.Message); }
#endif
    }
    static void RemoveFaceLayout(string backgroundPath)
    {
        string key = FaceLayoutKey(backgroundPath);
        if (key == null) return;
        faceLayouts.Remove(key);
        if (previewLayoutKey == key) previewLayoutKey = null;
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.Delete(key, null, reason => Debug.LogWarning("背景已删除，清理位置设置失败：" + reason));
#else
        try { string file = LayoutFile(key); if (File.Exists(file)) File.Delete(file); }
        catch (Exception e) { Debug.LogWarning("背景已删除，清理位置设置失败：" + e.Message); }
#endif
    }
}
