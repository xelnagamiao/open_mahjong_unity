using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>Loads built-in faces from their packed atlas; source PNGs stay outside Resources.</summary>
public static class BuiltinTileSprites {
    public const string SourceRoot = "Assets/Art/Cards/Faces/";
    private static readonly Dictionary<string, SpriteAtlas> Atlases = new Dictionary<string, SpriteAtlas>();
    private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();

    public static Sprite Hand(string packId, int faceId) => Hand(packId, faceId.ToString());

    public static Sprite Hand(string packId, string faceName) {
        if (string.IsNullOrEmpty(faceName)) return null;
        return Load(TilePackIds.ResourcesPackRoot + "/" + packId + "/HandAtlas", faceName,
            SourceRoot + packId + "/hand/" + faceName + ".png");
    }

    public static Sprite OfficialTable(int faceId) {
        return Load(TilePackIds.OfficialAtlasResource, faceId.ToString(),
            SourceRoot + "official/table/" + faceId + ".png");
    }

    private static Sprite Load(string atlasPath, string faceName, string editorSourcePath) {
        string key = atlasPath + "/" + faceName;
        if (Sprites.TryGetValue(key, out Sprite cached) && cached != null) return cached;
        if (!Atlases.TryGetValue(atlasPath, out SpriteAtlas atlas) || atlas == null) {
            atlas = Resources.Load<SpriteAtlas>(atlasPath);
            if (atlas != null) Atlases[atlasPath] = atlas;
        }
        Sprite sprite = atlas != null ? atlas.GetSprite(faceName) : null;
#if UNITY_EDITOR
        // Atlas preview data can be absent before the first pack in a freshly imported project.
        // Editor authoring still works; this fallback and the source assets are not Player dependencies.
        if (sprite == null) return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(editorSourcePath);
#endif
        if (sprite != null) {
            sprite.name = faceName;
            Sprites[key] = sprite;
        }
        return sprite;
    }
}
