using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>虹雀牌的网络代码、整数牌 ID 与 HQv3.1 资源之间的唯一映射。</summary>
public static class HongqueTileVisual {
    public const int BaseId = 1000;
    public const int ColourCount = 14;
    public const int NumberCount = 9;

    private static readonly string[] ColourCodes = {
        "AX", "AY", "BX", "BY", "CX", "CY", "DX",
        "DY", "EX", "EY", "FX", "FY", "GX", "GY"
    };
    private static readonly Dictionary<int, Texture2D> TextureCache = new Dictionary<int, Texture2D>();
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

    public static string ResourcePath(int tileId) {
        string code = ToCode(tileId);
        return code == null ? null : $"image/HQv3.1/{code}";
    }

    public static Texture2D LoadTexture(int tileId) {
        if (!IsHongqueId(tileId)) return null;
        if (TextureCache.TryGetValue(tileId, out Texture2D cached)) return cached;
        string path = ResourcePath(tileId);
        Texture2D texture = path == null ? null : Resources.Load<Texture2D>(path);
        if (texture != null) TextureCache[tileId] = texture;
        return texture;
    }

    /// <summary>
    /// 虹雀有 126 张不同牌面。若在每次摸牌/出牌时才同步 Resources.Load，
    /// 首次出现的新牌面会在主线程产生明显卡点；开局一次性预热后，实战只查字典。
    /// </summary>
    public static void PreloadAllTextures() {
        if (texturesPreloaded) return;
        Texture2D[] textures = Resources.LoadAll<Texture2D>("image/HQv3.1");
        foreach (Texture2D texture in textures) {
            if (texture == null) continue;
            int tileId = FromCode(texture.name);
            if (tileId != 0) TextureCache[tileId] = texture;
        }
        texturesPreloaded = true;
    }

    public static Sprite LoadSprite(int tileId) {
        if (!IsHongqueId(tileId)) return null;
        if (SpriteCache.TryGetValue(tileId, out Sprite cached)) return cached;
        Texture2D texture = LoadTexture(tileId);
        if (texture == null) return null;
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f);
        sprite.name = ToCode(tileId);
        SpriteCache[tileId] = sprite;
        return sprite;
    }
}
