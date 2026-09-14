using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>Metadata + immutable, content-addressed image copies. Never overwrite upload slots.</summary>
public sealed class Card3DPresetStorage
{
    public const string DocumentKey = "card3dpresets/library.json";
    private const string ImagePrefix = "card3dpresets/images/";
    private readonly string directory;
    private readonly Dictionary<string, byte[]> pendingImages = new Dictionary<string, byte[]>();
    public Card3DPresetStorage(string directory) { this.directory = directory; }
    internal static bool IsSnapshotImage(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
#if UNITY_WEBGL && !UNITY_EDITOR
        return path.StartsWith(ImagePrefix, StringComparison.Ordinal);
#else
        try {
            string root = Path.GetFullPath(Path.Combine(Application.persistentDataPath,"Card3DPresets","images")) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        } catch { return false; }
#endif
    }
    public string Read()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        byte[] bytes = UnityAssetIdb.GetCached(DocumentKey);
        return bytes == null ? "" : Encoding.UTF8.GetString(bytes);
#else
        string path = Path.Combine(directory, "library.json");
        return File.Exists(path) ? File.ReadAllText(path) : "";
#endif
    }
    public string SnapshotImage(string path, bool custom)
    {
        if (!custom || string.IsNullOrEmpty(path)) return path ?? "";
#if UNITY_WEBGL && !UNITY_EDITOR
        byte[] bytes = UnityAssetIdb.GetCached(path);
#else
        byte[] bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
#endif
        if (bytes == null || bytes.Length == 0) throw new IOException("预设图片不存在，请重新选择图片后保存");
        string hash;
        using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
#if UNITY_WEBGL && !UNITY_EDITOR
        string target = ImagePrefix + hash;
        if (UnityAssetIdb.GetCached(target) == null) {
            pendingImages[target] = bytes;
            UnityAssetIdb.StagePendingWrite(target, bytes);
        }
#else
        string imageDirectory = Path.Combine(directory, "images"); Directory.CreateDirectory(imageDirectory);
        string target = Path.Combine(imageDirectory, hash + ".png");
        if (!File.Exists(target)) File.WriteAllBytes(target, bytes);
#endif
        return target;
    }
    public void Write(string json, Action done, Action<string> error)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var images = new List<KeyValuePair<string, byte[]>>(pendingImages);
        int index = 0;
        void Next() {
            if (index < images.Count) {
                var image = images[index++];
                UnityAssetIdb.Put(image.Key, image.Value, Next, error);
            } else UnityAssetIdb.Put(DocumentKey, Encoding.UTF8.GetBytes(json), () => {
                foreach (var image in images) pendingImages.Remove(image.Key);
                done?.Invoke();
            }, error);
        }
        Next();
#else
        try {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "library.json"), temp = path + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
            done?.Invoke();
        } catch (Exception e) { error?.Invoke(e.Message); }
#endif
    }
}

/// <summary>Only selected rotation images stay decoded; immutable snapshots can safely be shared.</summary>
public static class Card3DPresetTextureCache
{
    private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache()
    {
        foreach (var texture in textures.Values) if (texture != null) UnityEngine.Object.Destroy(texture);
        textures.Clear();
    }
    public static bool TryGet(string path, out Texture2D texture) => textures.TryGetValue(path ?? "", out texture) && texture != null;
    public static bool Owns(Texture2D texture) => texture != null && textures.ContainsValue(texture);
    public static void Warm(IEnumerable<Card3DAppearance> appearances)
    {
        foreach (var a in appearances) {
            if (a.backImageCustom) Load(a.backImage);
            if (a.backgroundImageCustom) Load(a.backgroundImage);
        }
    }
    private static void Load(string path)
    {
        if (string.IsNullOrEmpty(path) || TryGet(path,out _)) return;
        textures.Remove(path); // Discard stale native texture handles after an Editor play session.
        // Mutable upload slots must never become cached preset textures.
        if (!Card3DPresetStorage.IsSnapshotImage(path)) return;
#if UNITY_WEBGL && !UNITY_EDITOR
        var texture = UnityAssetIdb.LoadTexture(path);
#else
        var texture = CardBackManager.LoadTextureFromFile(path);
#endif
        if (texture != null) textures.Add(path, texture);
    }
    public static void ReleaseExcept(IEnumerable<Card3DAppearance> appearances)
    {
        var keep = new HashSet<string>();
        foreach (var a in appearances) { keep.Add(a.backImage ?? ""); keep.Add(a.backgroundImage ?? ""); }
        foreach (var path in new List<string>(textures.Keys)) {
            if (keep.Contains(path) || textures[path] == CardBackManager.CurrentTexture || textures[path] == CardBackManager.CurrentTableBackground) continue;
            UnityEngine.Object.Destroy(textures[path]); textures.Remove(path);
        }
    }
}
