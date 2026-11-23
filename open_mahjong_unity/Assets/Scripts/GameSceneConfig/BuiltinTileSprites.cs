using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>Loads built-in faces from their packed atlas; source PNGs stay outside Resources.</summary>
public static class BuiltinTileSprites {
    public const string SourceRoot = "Assets/Art/Cards/Faces/";
    private sealed class LoadedPack {
        public SpriteAtlas Atlas;
        public readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        public bool PendingRelease;
    }
    private static readonly Dictionary<string, LoadedPack> Packs = new Dictionary<string, LoadedPack>();

    /// <summary>2D 与 3D 共用压缩图集；完整透明源 PNG 保留在 Art 目录。</summary>
    public static string StandardAtlasName(string pack) => pack == TilePackIds.PackOfficial ? "SnowMapleFaces"
        : pack == TilePackIds.PackFluffy ? "FluffyFaces" : "HongKongFaces";

    // Archive entry requests only the selected pack. Keep the atlas and cloned sprites under the normal release path.
    internal static System.Collections.IEnumerable PrepareReplayAtlas(string packId) {
        if (!TilePackIds.IsBuiltinPack(packId) || Packs.ContainsKey(packId)) yield break;
        var request = Resources.LoadAsync<SpriteAtlas>(TilePackIds.ResourcesPackRoot + "/" + packId + "/" + StandardAtlasName(packId));
        yield return request;
        if (request.asset is SpriteAtlas atlas && !Packs.ContainsKey(packId))
            Packs.Add(packId, new LoadedPack { Atlas = atlas });
    }

    public static Sprite Shared(string packId, int faceId) {
        if (!TilePackIds.IsBuiltinPack(packId) || !TilePackIds.IsStandardFaceId(faceId)) return null;
        return Load(packId, StandardAtlasName(packId), faceId.ToString(),
            SourceRoot + packId + "/shared/" + faceId + ".png");
    }

    public static Sprite Hand(string packId, int faceId) => Hand(packId, faceId.ToString());

    public static Sprite Hand(string packId, string faceName) {
        if (string.IsNullOrEmpty(faceName)) return null;
        if (TilePackIds.IsBuiltinPack(packId) && int.TryParse(faceName, out int id))
            return Shared(packId, id) ?? Shared(TilePackIds.PackOfficial, id);
        return Load(packId, "HandAtlas", faceName,
            SourceRoot + packId + "/hand/" + faceName + ".png");
    }

    public static Sprite OfficialTable(int faceId) {
        return Shared(TilePackIds.PackOfficial, faceId);
    }

    private static Sprite Load(string packId, string atlasName, string faceName, string editorSourcePath) {
        if (!Packs.TryGetValue(packId, out LoadedPack pack)) {
            var atlas = Resources.Load<SpriteAtlas>(TilePackIds.ResourcesPackRoot + "/" + packId + "/" + atlasName);
            if (atlas != null) Packs[packId] = pack = new LoadedPack { Atlas = atlas };
        }
        // A fallback or another active view can still borrow a pack during rebinding.
        if (pack != null) {
            pack.PendingRelease = false;
            if (pack.Sprites.TryGetValue(faceName, out Sprite cached) && cached != null) return cached;
        }
        Sprite sprite = pack?.Atlas.GetSprite(faceName);
#if UNITY_EDITOR
        // Atlas preview data can be absent before the first pack in a freshly imported project.
        // Editor authoring still works; this fallback and the source assets are not Player dependencies.
        if (sprite == null) return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(editorSourcePath);
#endif
        if (sprite != null) {
            sprite.name = faceName;
            pack.Sprites[faceName] = sprite;
        }
        return sprite;
    }

    internal static void BeginStandardPackRefresh(string keep) {
        foreach (var entry in Packs)
            entry.Value.PendingRelease = TilePackIds.IsBuiltinPack(entry.Key) && entry.Key != keep;
    }

    internal static void ReleaseUnusedStandardPacks() {
        if (!Application.isPlaying) return;
        foreach (var id in new[] { TilePackIds.PackOfficial, TilePackIds.PackFluffy, TilePackIds.PackHkMahjong }) {
            if (!Packs.TryGetValue(id, out LoadedPack pack) || !pack.PendingRelease) continue;
            Packs.Remove(id);
            // These three atlases contain only standard faces. A platform may split one
            // over several pages, even if the hand only requested sprites from one page.
            if (pack.Sprites.Count < pack.Atlas.spriteCount) {
                foreach (int face in TilePackIds.StandardFaceIds) {
                    string name = face.ToString();
                    if (pack.Sprites.ContainsKey(name)) continue;
                    var missing = pack.Atlas.GetSprite(name);
                    if (missing != null) pack.Sprites.Add(name, missing);
                }
            }
            var pages = new HashSet<Texture2D>();
            foreach (var sprite in pack.Sprites.Values) {
                if (sprite == null) continue;
#if UNITY_EDITOR
                // The editor can return the source PNG or a transient pack preview.
                if (UnityEditor.AssetDatabase.GetAssetPath(sprite.texture) == UnityEditor.AssetDatabase.GetAssetPath(pack.Atlas))
#endif
                    pages.Add(sprite.texture);
                Object.Destroy(sprite);
            }
            Resources.UnloadAsset(pack.Atlas);
            foreach (var page in pages) if (page != null) Resources.UnloadAsset(page);
        }
    }

    // Rule views may share these sprites with an open replay. This is called only after
    // GameSceneTeardown has detached the replay objects.
    internal static void ReleaseHongqueHandCache() {
        if (!Packs.TryGetValue("hongque", out LoadedPack pack)) return;
        Packs.Remove("hongque");
        var pages = new HashSet<Texture2D>();
        foreach (var sprite in pack.Sprites.Values) {
            if (sprite == null) continue;
#if UNITY_EDITOR
            if (UnityEditor.AssetDatabase.GetAssetPath(sprite.texture) == UnityEditor.AssetDatabase.GetAssetPath(pack.Atlas))
#endif
                pages.Add(sprite.texture);
        }
        Resources.UnloadAsset(pack.Atlas);
        foreach (var page in pages) if (page != null) Resources.UnloadAsset(page);
    }

}
