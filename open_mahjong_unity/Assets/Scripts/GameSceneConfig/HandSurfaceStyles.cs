using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Stable indices are used by saved face layouts. The catalog order is independent of those indices.</summary>
public static class HandSurfaceStyles
{
    public const int ClassicIndex = 0;
    public const int DefaultIndex = 1;
    const int FirstTint = 4;
    const int FirstReference = 8;

    static readonly string[] Names = { "经典", "靛蓝", "琥珀", "竹青", "靛蓝·雾白", "靛蓝·银灰", "靛蓝·亚麻", "靛蓝·岩灰" };
    static readonly string[] Ids = { "", "indigo", "amber", "jade", "indigo-mist", "indigo-silver", "indigo-linen", "indigo-stone" };

    public sealed class ReferenceStyle
    {
        public readonly int Index;
        public readonly string Id, Name;
        public readonly Color32 Face, Side, Back;
        public readonly Color32? TableFace, TableSide, TableBack;
        // Dimensions along the visible center column of the 2D texture (272 x 424).
        public readonly int BackPixels, SidePixels;
        public readonly float BackLayerFraction;

        internal ReferenceStyle(int index, string id, string name, int face, int side, int back,
            int backPixels, int sidePixels, float backLayerFraction, int? tableFace = null, int? tableSide = null, int? tableBack = null)
        {
            Index = index;
            Id = id;
            Name = name;
            Face = Rgb(face); Side = Rgb(side); Back = Rgb(back);
            BackPixels = backPixels; SidePixels = sidePixels;
            BackLayerFraction = backLayerFraction;
            TableFace = tableFace.HasValue ? Rgb(tableFace.Value) : (Color32?)null;
            TableSide = tableSide.HasValue ? Rgb(tableSide.Value) : (Color32?)null;
            TableBack = tableBack.HasValue ? Rgb(tableBack.Value) : (Color32?)null;
        }

        static Color32 Rgb(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
    }

    // Table inputs compensate the tile shader's neutral-white compression in MainScene.
    // The target lit glaze is #E0E0DF (amber), #EBEBEB (cyan), and #E0E1D3 (navy).
    public static readonly ReferenceStyle[] ReferenceStyles = {
        new ReferenceStyle(8, "warm-amber", "暖灰橙背", 0xDEDEDB, 0xACACB4, 0xCC7F23, 19, 27, .48f, 0xE7E7E6, 0xE7E7E6, 0xF39A2E),
        new ReferenceStyle(9, "cool-cyan", "冷灰蓝背", 0xEBEBEB, 0xBDBBC0, 0x54A4C2, 13, 35, .47f, 0xF5F5F5, 0xF5F5F5, 0x69B3E4),
        new ReferenceStyle(10, "linen-navy", "米灰靛背", 0xB5B5A9, 0xE6E7D7, 0x010225, 24, 60, .42f, 0xE2E3D5, 0xE2E3D5, 0x0A0A2C),
        new ReferenceStyle(11, "wide-amber", "橙背宽边", 0xDEDEDB, 0xACACB4, 0xCC7F23, 38, 27, .55f, 0xE7E7E6, 0xE7E7E6, 0xF39A2E),
        new ReferenceStyle(12, "wide-cyan", "蓝背宽边", 0xEBEBEB, 0xBDBBC0, 0x54A4C2, 46, 35, .58f, 0xF5F5F5, 0xF5F5F5, 0x69B3E4)
    };

    public static int Count => FirstReference + ReferenceStyles.Length;
    public static bool IsIndigoTint(int index) => index >= FirstTint && index < FirstReference;
    public static ReferenceStyle Reference(int index)
    {
        if (index < FirstReference) return null;
        foreach (var style in ReferenceStyles)
            if (style.Index == index) return style;
        return null;
    }
    public static string DisplayName(int index) => Reference(index)?.Name ?? Names[index];

    public static IEnumerable<int> DisplayOrder(bool back)
    {
        yield return DefaultIndex;
        // Indigo face tints all use the original indigo back.
        if (!back)
            for (int i = FirstTint; i < FirstReference; i++) yield return i;
        foreach (var style in ReferenceStyles) yield return style.Index;
        yield return 2;
        yield return 3;
        yield return ClassicIndex;
    }

    public static string ResourcePath(int index, bool back)
    {
        if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (index == ClassicIndex) return "";
        if (back && IsIndigoTint(index)) index = DefaultIndex;
        string id = Reference(index)?.Id ?? Ids[index];
        return "image/Cards/Surfaces/" + (back ? "backs" : "backgrounds") + "/hand-" + id;
    }

    public static int FindIndex(string path, bool back)
    {
        for (int i = 0; i < Count; i++)
            if (string.Equals(path ?? "", ResourcePath(i, back), StringComparison.Ordinal)) return i;
        return -1;
    }
}
