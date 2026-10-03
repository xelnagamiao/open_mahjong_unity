using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 虹雀牌的网络代码、整数牌 ID 与牌面资源之间的映射。
/// 资源统一在 Cards/Faces/hongque 下，分为 hand 与 table 两套。
/// </summary>
public static class HongqueTileVisual {
    public const int BaseId = 1000;
    public const int ColourCount = 14;
    public const int NumberCount = 9;

    private static readonly string[] ColourCodes = {
        "AX", "AY", "BX", "BY", "CX", "CY", "DX",
        "DY", "EX", "EY", "FX", "FY", "GX", "GY"
    };
    private static readonly Dictionary<int, Texture2D> TableTextureCache = new Dictionary<int, Texture2D>();
    private static readonly Dictionary<int, Sprite> SpriteCache = new Dictionary<int, Sprite>();
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

    /// <summary>手牌牌面资源路径（HQv3.1-hand）。</summary>
    public static string HandResourcePath(int tileId) {
        string code = ToCode(tileId);
        return code == null ? null : TilePackIds.HongqueHandRoot + "/" + code;
    }

    /// <summary>3D 卡牌牌面资源路径（HQv3.1-table）。</summary>
    public static string TableResourcePath(int tileId) {
        string code = ToCode(tileId);
        return code == null ? null : TilePackIds.HongqueTableRoot + "/" + code;
    }

    /// <summary>手牌牌面资源路径（默认手牌套）。</summary>
    public static string ResourcePath(int tileId) {
        return HandResourcePath(tileId);
    }

    /// <summary>加载 3D 卡牌牌面贴图（HQv3.1-table），供 3D 渲染使用。</summary>
    public static Texture2D LoadTableTexture(int tileId) {
        if (!IsHongqueId(tileId)) return null;
        if (TableTextureCache.TryGetValue(tileId, out Texture2D cached)) return cached;
        string path = TableResourcePath(tileId);
        Texture2D texture = path == null ? null : Resources.Load<Texture2D>(path);
        if (texture != null) TableTextureCache[tileId] = texture;
        return texture;
    }

    /// <summary>
    /// 虹雀有 126 张不同牌面。若在每次摸牌/出牌时才同步 Resources.Load，
    /// 首次出现的新牌面会在主线程产生明显卡点；开局一次性预热后，实战只查字典。
    /// </summary>
    public static void PreloadAllTextures() {
        if (texturesPreloaded) return;
        // Load the original Sprites so Unity can bind their atlas rects and mipmaps.
        // Creating a new Sprite from the packed Texture2D would show the entire atlas.
        Sprite[] sprites = Resources.LoadAll<Sprite>(TilePackIds.HongqueHandRoot);
        foreach (Sprite sprite in sprites) {
            if (sprite == null) continue;
            int tileId = FromCode(sprite.name);
            if (tileId != 0) CacheHandSprite(tileId, sprite);
        }
        texturesPreloaded = true;
    }

    public static Sprite LoadSprite(int tileId) {
        if (!IsHongqueId(tileId)) return null;
        if (SpriteCache.TryGetValue(tileId, out Sprite cached) && cached != null) return cached;
        Sprite sprite = Resources.Load<Sprite>(HandResourcePath(tileId));
        if (sprite != null) CacheHandSprite(tileId, sprite);
        return sprite;
    }

    private static void CacheHandSprite(int tileId, Sprite sprite) {
        // SpriteAtlas has no authored mip bias; retain the original hand import setting.
        sprite.texture.mipMapBias = -0.5f;
        SpriteCache[tileId] = sprite;
    }
}
