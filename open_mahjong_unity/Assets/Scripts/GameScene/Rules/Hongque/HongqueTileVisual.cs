using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 虹雀牌的网络代码、整数牌 ID 与牌面资源之间的映射。
/// 资源统一在 Cards/Faces/hongque 下，分为 hand 与 table 两套。
/// </summary>
public static class HongqueTileVisual {
    public const int BaseId = 1000;
    public const int ColourCount = 15;
    public const int NumberCount = 9;
    // The original 3D mapping crops the table texture vertically around its center.
    public const float TableVerticalScale = 1.1f;

    private static readonly string[] ColourCodes = {
        "AX", "AY", "BX", "BY", "CX", "CY", "DX",
        "DY", "EX", "EY", "FX", "FY", "GX", "GY", "HX"
    };
    private static readonly Dictionary<int, Texture2D> TableTextureCache = new Dictionary<int, Texture2D>();
    private static readonly Dictionary<int, Sprite> SpriteCache = new Dictionary<int, Sprite>();
    private static readonly HashSet<int> GeneratedHandIds = new HashSet<int>();
    private static bool texturesPreloaded;

    public static bool IsHongqueId(int tileId) {
        int value = tileId - BaseId;
        int colour = value / 10;
        int number = value % 10;
        return colour >= 0 && colour < ColourCount && number >= 1 && number <= NumberCount;
    }

    public static int FromCode(string code) {
        if (string.IsNullOrEmpty(code) || code.Length != 3) return 0;
        int colour = Array.IndexOf(ColourCodes, code.Substring(0, 2).ToUpperInvariant());
        if (colour < 0 || code[2] < '1' || code[2] > '9') return 0;
        return BaseId + colour * 10 + (code[2] - '0');
    }

    public static string ToCode(int tileId) {
        if (!IsHongqueId(tileId)) return null;
        int value = tileId - BaseId;
        return ColourCodes[value / 10] + (value % 10);
    }

    /// <summary>3D 卡牌牌面资源路径（HQv3.1-table）。</summary>
    public static string TableResourcePath(int tileId) {
        string code = ToCode(tileId);
        return code == null ? null : TilePackIds.HongqueTableRoot + "/" + code;
    }

    /// <summary>加载 3D 卡牌牌面贴图（HQv3.1-table），供 3D 渲染使用。</summary>
    public static Texture2D LoadTableTexture(int tileId) {
        if (!IsHongqueId(tileId)) return null;
        if (TableTextureCache.TryGetValue(tileId, out Texture2D cached)) return cached;
        string path = TableResourcePath(tileId);
        Texture2D texture = path == null ? null : Resources.Load<Texture2D>(path);
        if (texture != null) {
            texture.filterMode = FilterMode.Trilinear;
            texture.mipMapBias = 0f;
            TableTextureCache[tileId] = texture;
        }
        return texture;
    }

    /// <summary>
    /// 虹雀有 126 张不同牌面。若在每次摸牌/出牌时才同步 Resources.Load，
    /// 首次出现的新牌面会在主线程产生明显卡点；开局一次性预热后，实战只查字典。
    /// </summary>
    public static void PreloadAllTextures() => ReplayFrameLoader.Finish(PrepareHandSprites());

    internal static System.Collections.IEnumerable PrepareHandSprites() {
        if (texturesPreloaded) yield break;
        foreach (int tileId in TilePackIds.HongqueFaceIds) {
            LoadSprite(tileId);
            yield return null;
        }
        texturesPreloaded = true;
    }

    public static Sprite LoadSprite(int tileId) => LoadSprite(tileId, true);

    // A settings preview may share a live game's sprite, but must not pin the entire gallery in its cache.
    internal static Sprite LoadSprite(int tileId, bool cacheForGame) {
        if (!IsHongqueId(tileId)) return null;
        if (SpriteCache.TryGetValue(tileId, out Sprite cached) && cached != null) return cached;
        Sprite sprite = tileId >= 1141 ? CreateColourlessHand(tileId) : null;
        if (sprite == null) sprite = BuiltinTileSprites.Hand("hongque", ToCode(tileId));
        if (sprite != null && cacheForGame) {
            SpriteCache[tileId] = sprite;
            if (tileId >= 1141 && sprite.texture != null) GeneratedHandIds.Add(tileId);
        }
        return sprite;
    }

    private static Sprite CreateColourlessHand(int tileId) {
        var source = Resources.Load<Texture2D>(TableResourcePath(tileId));
        var reference = BuiltinTileSprites.Hand("hongque", "AX1");
        var shader = Resources.Load<Shader>("Hongque/ColourlessHand");
        if (source == null || reference == null || shader == null) return null;
        int width = (int)reference.rect.width, height = (int)reference.rect.height;
        var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        material.SetVector("_CanvasSize", new Vector4(width, height, 0, 0));
        var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        try {
            Graphics.Blit(source, target, material);
            RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) {
                name = ToCode(tileId) + "Hand", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f),
                reference.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        } finally {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.Destroy(material);
        }
    }

    /// <summary>释放已结束虹雀牌谱独占的缓存。挂起牌谱不会调用此方法。</summary>
    internal static void ReleaseGameCache() {
        foreach (int tileId in GeneratedHandIds) {
            if (SpriteCache.TryGetValue(tileId, out Sprite sprite) && sprite != null) {
                Texture2D texture = sprite.texture;
                UnityEngine.Object.Destroy(sprite);
                if (texture != null) UnityEngine.Object.Destroy(texture);
            }
            SpriteCache.Remove(tileId);
        }
        GeneratedHandIds.Clear();
        SpriteCache.Clear();
        TableTextureCache.Clear();
        texturesPreloaded = false;
        BuiltinTileSprites.ReleaseHongqueHandCache();
    }

}
