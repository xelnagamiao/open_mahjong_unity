using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public static partial class HandSurfaceLibrary
{
    static readonly HashSet<string> pendingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    static string NameKey(string name, bool back) => (back ? "back:" : "background:") + name.Trim();
    static bool ReserveName(string name, bool back) => pendingNames.Add(NameKey(name, back));
    static void ReleaseName(string name, bool back) => pendingNames.Remove(NameKey(name, back));
    static bool SameName(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    static bool NameExists(string name, bool back, string exceptId = null)
    {
        for (int i = 0; i < HandSurfaceStyles.Count; i++)
            if (SameName(name, HandSurfaceStyles.DisplayName(i))) return true;
        foreach (var entry in entries.Values)
            if (entry.back == back && entry.id != exceptId && SameName(entry.name, name)) return true;
        return false;
    }

    public static string ImportName(string fileName)
    {
        string stem;
        try { stem = Path.GetFileNameWithoutExtension(fileName ?? "").Trim(); } catch { stem = ""; }
        if (string.IsNullOrWhiteSpace(stem)) return "我的手牌";
        var text = new StringInfo(stem);
        return text.SubstringByTextElements(0, Math.Min(text.LengthInTextElements, NameLimit));
    }

    public static bool TryMatchingBack(string backgroundPath, bool customBackground,
        out string backPath, out bool customBack, out string reason)
    {
        backPath = null; customBack = false; reason = "未找到同名牌背，已保留当前牌背";
        if (!customBackground)
        {
            int index = HandSurfaceStyles.FindIndex(backgroundPath, false);
            if (index < 0) return false;
            backPath = HandSurfaceStyles.ResourcePath(index, true);
            return true;
        }
        var background = Find(backgroundPath);
        if (background == null || background.back) return false;
        int matches = 0;
        foreach (var entry in entries.Values)
        {
            if (!entry.back || !SameName(entry.name, background.name)) continue;
            backPath = entry.id; customBack = true; matches++;
        }
        if (matches == 1) return true;
        backPath = null;
        if (matches > 1) reason = "存在多个同名牌背，请先改名区分；已保留当前牌背";
        return false;
    }

    static void PublishChange()
    {
        CardBackManager.ApplyHandBackFollow();
        Changed?.Invoke();
    }

    // Validate every image before writing; publish a pair only after both writes succeed.
    public static void SaveImport(byte[] backgroundImage, byte[] backImage, string name,
        Action<Entry, Entry> done, Action<string> error)
    {
        if (!Ready) { EnsureReady(() => SaveImport(backgroundImage, backImage, name, done, error)); return; }
        if (!TryName(name, out name)) { error?.Invoke("请输入 1–24 个字的名称，不可换行"); return; }
        var records = new List<(Entry entry, byte[] image)>();
        string failure = null;
        foreach (bool back in new[] { false, true })
        {
            byte[] image = back ? backImage : backgroundImage;
            if (image == null) continue;
            if (NameExists(name, back)) { failure = "已存在同名" + (back ? "牌背" : "手牌背景") + "，请换一个名称"; break; }
            if (image.Length == 0 || image.Length > UnityAssetIdb.MaxImageBytes - 4096)
            { failure = "图片为空或超过 8MB，请压缩后上传"; break; }
            Texture2D texture = UnityAssetIdb.ToTexture(image);
            if (texture == null) { failure = "无法读取图片，请使用 PNG 或 JPG"; break; }
            int width = texture.width, height = texture.height;
            UnityEngine.Object.Destroy(texture);
            if (width > 4096 || height > 4096) { failure = "图片宽高不能超过 4096 像素"; break; }
            if (!ReserveName(name, back)) { failure = "同名图片正在保存，请稍候"; break; }
            records.Add((new Entry { id = Prefix + Guid.NewGuid().ToString("N"), name = name, back = back,
                width = width, height = height, createdUtc = DateTime.UtcNow.ToString("O") }, image));
        }
        void Release() { foreach (var record in records) ReleaseName(name, record.entry.back); }
        if (failure != null || records.Count == 0) { Release(); error?.Invoke(failure ?? "压缩包中没有手牌图片"); return; }
        int written = 0;
        void WriteNext()
        {
            if (written == records.Count)
            {
                Entry background = null, rear = null;
                foreach (var record in records)
                {
                    entries.Add(record.entry.id, record.entry);
                    if (record.entry.back) rear = Clone(record.entry); else background = Clone(record.entry);
                }
                Release(); PublishChange(); done?.Invoke(background, rear); return;
            }
            var next = records[written];
            Write(next.entry.id, Pack(next.entry, next.image), () => { written++; WriteNext(); }, reason =>
            {
                if (written == 0) { Release(); error?.Invoke(reason); return; }
                // There can be only one committed file to roll back (a two-image ZIP).
                DeleteStored(records[0].entry.id, () => { Release(); error?.Invoke(reason); }, rollbackError =>
                {
                    // Keep the surviving record visible/recoverable instead of hiding user data.
                    entries[records[0].entry.id] = records[0].entry;
                    Release(); PublishChange(); error?.Invoke(reason + "；已保留保存成功的图片：" + rollbackError);
                });
            });
        }
        WriteNext();
    }

    static void DeleteStored(string id, Action done, Action<string> error)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityAssetIdb.Delete(id, done, error);
#else
        try { File.Delete(FilePath(id)); } catch (Exception e) { error?.Invoke(e.Message); return; }
        done?.Invoke();
#endif
    }
}
