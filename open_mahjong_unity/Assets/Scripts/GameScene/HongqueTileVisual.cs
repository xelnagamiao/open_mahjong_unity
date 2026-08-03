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
    private static readonly Dictionary<int, Sprite> SpriteCache = new Dictionary<int, Sprite>();

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
        string path = ResourcePath(tileId);
        return path == null ? null : Resources.Load<Texture2D>(path);
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
