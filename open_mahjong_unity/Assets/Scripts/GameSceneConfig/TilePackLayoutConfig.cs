using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>Author defaults in the ZIP. Player overrides remain keyed by pack and 2D/3D scope.</summary>
public sealed class TilePackLayoutConfig
{
    public const int MaxBytes = 64 * 1024;
    readonly Dictionary<string, HandSurfaceLibrary.FaceLayout> sections = new Dictionary<string, HandSurfaceLibrary.FaceLayout>();
    static readonly Dictionary<string, TilePackLayoutConfig> Packs = new Dictionary<string, TilePackLayoutConfig>();
    static TilePackLayoutConfig manzuDefaults;
    static readonly Regex Section = new Regex(@"^\[(2d|3d)(?:\.([0-9]+))?\]$", RegexOptions.IgnoreCase);
    static readonly Regex Pair = new Regex(@"^(x|y|scale)\s*=\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+))\s*%?$", RegexOptions.IgnoreCase);

    public static TilePackLayoutConfig Parse(string text)
    {
        var config = new TilePackLayoutConfig();
        string section = null;
        var keys = new HashSet<string>();
        string[] lines = (text ?? "").TrimStart('\uFEFF').Split('\n');
        for (int index = 0; index < lines.Length; index++) {
            string line = lines[index].Split('#')[0].Trim();
            if (line.Length == 0) continue;
            var match = Section.Match(line);
            if (match.Success) {
                section = match.Groups[1].Value.ToLowerInvariant();
                if (match.Groups[2].Success) {
                    string code = match.Groups[2].Value;
                    if (!int.TryParse(code, out int id) || id.ToString(CultureInfo.InvariantCulture) != code || !TilePackIds.IsStandardFaceId(id))
                        throw Error(index, "无效的牌面代号");
                    section += "." + code;
                }
                if (config.sections.ContainsKey(section)) throw Error(index, "配置段重复");
                config.sections[section] = HandSurfaceLibrary.FaceLayout.Default;
                keys.Clear(); continue;
            }
            match = Pair.Match(line);
            if (section == null || !match.Success)
                throw Error(index, "使用 [2d]、[3d]、[2d.11] 或 [3d.11]，参数为 x、y、scale");
            string key = match.Groups[1].Value.ToLowerInvariant();
            if (!keys.Add(key)) throw Error(index, "参数重复");
            float low = key == "scale" ? 25 : -100, high = key == "scale" ? 200 : 100;
            if (!float.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float percent)
                || float.IsNaN(percent) || float.IsInfinity(percent) || percent < low || percent > high)
                throw Error(index, key + " 范围为 " + low + "%～" + high + "%");
            var value = config.sections[section];
            if (key == "x") value.x = percent / 100f;
            else if (key == "y") value.y = percent / 100f;
            else value.scale = percent / 100f;
            config.sections[section] = value;
        }
        return config;
    }
    static FormatException Error(int line, string detail) => new FormatException("牌面配置第 " + (line + 1) + " 行：" + detail);
    public static void Register(string pack, TilePackLayoutConfig config)
    {
        if (string.IsNullOrEmpty(pack)) return;
        Packs[pack] = config ?? new TilePackLayoutConfig();
        HandFaceAdjustmentSettings.Invalidate(pack);
        TableFaceLayoutSettings.Invalidate(pack);
        TileFaceOffsetSettings.Invalidate(pack);
    }
    public static HandSurfaceLibrary.FaceLayout Default(string pack, bool table, int tileId = -1)
    {
        string key = table ? "3d" : "2d";
        if (tileId >= 0) {
            int identity = FixedFacePack.IsFixed(pack) ? tileId : WashizuTile.Face(tileId);
            key += "." + identity.ToString(CultureInfo.InvariantCulture);
        }
        if (pack != null && Packs.TryGetValue(pack, out var config) && config.sections.TryGetValue(key, out var value))
            return value;
        if (UsesVisibleManzuDefaults(pack, tileId)) {
            if (manzuDefaults == null) {
                var file = Resources.Load<TextAsset>("image/Cards/Faces/official/manzu-f5288fc1");
                if (file == null) throw new InvalidOperationException("Missing f5288fc1 manzu defaults.");
                manzuDefaults = Parse(file.text);
            }
            return manzuDefaults.sections[key];
        }
        if (pack == FixedFacePack.Expanded && FixedFacePack.IsEditable(pack, tileId)) {
            FixedFacePack.Decode(pack, tileId, out var source, out int id);
            // The Duocai PNG set has its own visible per-tile defaults; other expanded
            // rules and the colored variants of standard faces keep their own layouts.
            if (source == CardFaceFamilyPreview.Source.Duocai && DuocaiTiles.Joker(id))
                return new HandSurfaceLibrary.FaceLayout { x = 0f, y = table ? 0f : -.037f, scale = table ? .896f : .855f };
        }
        return HandSurfaceLibrary.FaceLayout.Default;
    }

    public static bool UsesVisibleManzuDefaults(string pack, int tileId)
    {
        if ((pack ?? TableFaceLayoutSettings.SelectedPack) != TilePackIds.PackOfficial) return false;
        int face = WashizuTile.Face(tileId);
        return (face >= 11 && face <= 19) || face == 105;
    }

    // Single-tile 2D offsets for these defaults use the contained source canvas,
    // so their meaning stays fixed when the player changes the hand background.
    public static Vector2 ManzuHandCanvasFit(Vector2 bodySize)
    {
        if (bodySize.x <= 0 || bodySize.y <= 0) return Vector2.one;
        Vector2 uv = TileTextureLayout.ContainUvScale(272f / 389f, bodySize.x / bodySize.y);
        return new Vector2(1f / uv.x, 1f / uv.y);
    }
}
