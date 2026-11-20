using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;
using UnityEngine;

/// <summary>Author defaults in the ZIP. Player overrides remain keyed by pack and 2D/3D scope.</summary>
public sealed class TilePackLayoutConfig
{
    public const int MaxBytes = 64 * 1024;
    readonly Dictionary<string, HandSurfaceLibrary.FaceLayout> sections = new Dictionary<string, HandSurfaceLibrary.FaceLayout>();
    static readonly Dictionary<string, TilePackLayoutConfig> Packs = new Dictionary<string, TilePackLayoutConfig>();
    readonly Dictionary<string, float> offsetAspects = new Dictionary<string, float>();
    readonly Dictionary<string, HandSurfaceLibrary.FaceLayout> imageLayouts = new Dictionary<string, HandSurfaceLibrary.FaceLayout>();
    public CardPackAppearance Appearance { get; private set; }
    public HandSurfaceLibrary.FaceLayout HandLayout { get; private set; } = HandSurfaceLibrary.FaceLayout.Default;
    static readonly Regex Section = new Regex(@"^\[(2d|3d|hand|appearance)(?:\.([0-9]+))?\]$", RegexOptions.IgnoreCase);
    static readonly Regex Pair = new Regex(@"^(x|y|scale|offset_aspect|image_x|image_y|image_scale)\s*=\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+))\s*%?$", RegexOptions.IgnoreCase);

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
                    if (section == "hand" || section == "appearance") throw Error(index, "无效的配置段");
                    string code = match.Groups[2].Value;
                    if (!int.TryParse(code, out int id) || id.ToString(CultureInfo.InvariantCulture) != code || !TilePackIds.IsStandardFaceId(id))
                        throw Error(index, "无效的牌面代号");
                    section += "." + code;
                }
                if (config.sections.ContainsKey(section)) throw Error(index, "配置段重复");
                config.sections[section] = HandSurfaceLibrary.FaceLayout.Default;
                if (section == "appearance") config.Appearance = new CardPackAppearance();
                keys.Clear(); continue;
            }
            if (section == "appearance") {
                int equals = line.IndexOf('=');
                if (equals < 1) throw Error(index, "外观参数格式错误");
                string field = line.Substring(0, equals).Trim().ToLowerInvariant();
                if (!keys.Add(field)) throw Error(index, "参数重复");
                config.Appearance.Set(field, line.Substring(equals + 1).Trim());
                continue;
            }
            match = Pair.Match(line);
            if (section == null || !match.Success)
                throw Error(index, "使用 [2d]、[3d]、[2d.11] 或 [3d.11]，参数为 x、y、scale");
            string key = match.Groups[1].Value.ToLowerInvariant();
            if (!keys.Add(key)) throw Error(index, "参数重复");
            bool image = key.StartsWith("image_", StringComparison.Ordinal);
            if (image && !section.Contains(".")) throw Error(index, "图片基准仅用于单牌配置");
            float low = key == "offset_aspect" || key == "image_scale" ? .01f : key == "scale" ? 25 : -100;
            float high = key == "image_scale" ? 1000f : key == "offset_aspect" ? 100f : key == "scale" ? 200 : 100;
            if (!float.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float percent)
                || float.IsNaN(percent) || float.IsInfinity(percent) || percent < low || percent > high)
                throw Error(index, key + " 范围为 " + low + "%～" + high + "%");
            if (key == "offset_aspect") {
                if (!section.StartsWith("2d", StringComparison.Ordinal)) throw Error(index, "offset_aspect 仅用于 2D");
                config.offsetAspects[section] = percent;
                continue;
            }
            if (image) {
                if (!config.imageLayouts.TryGetValue(section, out var basis)) basis = HandSurfaceLibrary.FaceLayout.Default;
                if (key == "image_x") basis.x = percent / 100f;
                else if (key == "image_y") basis.y = percent / 100f;
                else basis.scale = percent / 100f;
                config.imageLayouts[section] = basis;
                continue;
            }
            var value = config.sections[section];
            if (key == "x") value.x = percent / 100f;
            else if (key == "y") value.y = percent / 100f;
            else value.scale = percent / 100f;
            config.sections[section] = value;
            if (section == "hand") config.HandLayout = value;
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
        if (TryConfig(pack, out var config) && config.sections.TryGetValue(key, out var value))
            return value;
        if (pack == FixedFacePack.Expanded && FixedFacePack.IsEditable(pack, tileId)) {
            FixedFacePack.Decode(pack, tileId, out var source, out int id);
            // The Duocai PNG set has its own visible per-tile defaults; other expanded
            // rules and the colored variants of standard faces keep their own layouts.
            if (source == CardFaceFamilyPreview.Source.Duocai && DuocaiTiles.Joker(id))
                return new HandSurfaceLibrary.FaceLayout { x = 0f, y = table ? 0f : -.037f, scale = table ? .896f : .855f };
        }
        return HandSurfaceLibrary.FaceLayout.Default;
    }

    static bool TryConfig(string pack, out TilePackLayoutConfig config)
    {
        pack = pack ?? TableFaceLayoutSettings.SelectedPack;
        if (Packs.TryGetValue(pack, out config)) return true;
        if (!TilePackIds.IsBuiltinPack(pack)) return false;
        var file = Resources.Load<TextAsset>(TilePackIds.ResourcesPackRoot + "/" + pack + "/tiles");
        config = file == null ? new TilePackLayoutConfig() : Parse(file.text);
        Packs[pack] = config;
        return true;
    }
    public static bool HasTileDefaults(string pack, int tileId, bool table = false) =>
        TryConfig(pack, out var config) && config.sections.ContainsKey((table ? "3d." : "2d.") + WashizuTile.Face(tileId));
    public static bool TryImageLayout(string pack, int tileId, bool table, out HandSurfaceLibrary.FaceLayout layout)
    {
        layout = HandSurfaceLibrary.FaceLayout.Default;
        return TryConfig(pack, out var config) && config.imageLayouts.TryGetValue((table ? "3d." : "2d.") + WashizuTile.Face(tileId), out layout);
    }
    public static float OffsetAspect(string pack, int tileId)
    {
        if (!TryConfig(pack, out var config)) return 0;
        return config.offsetAspects.TryGetValue("2d." + WashizuTile.Face(tileId), out float aspect) ? aspect
            : config.offsetAspects.TryGetValue("2d", out aspect) ? aspect : 0;
    }
    public static Vector2 HandCanvasFit(string pack, int tileId, Vector2 bodySize)
    {
        float aspect = OffsetAspect(pack, tileId);
        if (aspect <= 0 || bodySize.x <= 0 || bodySize.y <= 0) return Vector2.one;
        Vector2 uv = TileTextureLayout.ContainUvScale(aspect, bodySize.x / bodySize.y);
        return new Vector2(1f / uv.x, 1f / uv.y);
    }
    public static void WriteLayout(StringBuilder text, string section, HandSurfaceLibrary.FaceLayout value, float aspect = 0,
        HandSurfaceLibrary.FaceLayout? image = null)
    {
        text.Append('[').Append(section).Append("]\n");
        text.Append("x=").Append((value.x * 100).ToString("0.#########", CultureInfo.InvariantCulture)).Append('\n');
        text.Append("y=").Append((value.y * 100).ToString("0.#########", CultureInfo.InvariantCulture)).Append('\n');
        text.Append("scale=").Append((value.scale * 100).ToString("0.#########", CultureInfo.InvariantCulture)).Append('\n');
        if (aspect > 0) text.Append("offset_aspect=").Append(aspect.ToString("0.#########", CultureInfo.InvariantCulture)).Append('\n');
        if (image.HasValue) {
            var basis = image.Value;
            text.Append("image_x=").Append((basis.x * 100).ToString("0.#########", CultureInfo.InvariantCulture)).Append('\n');
            text.Append("image_y=").Append((basis.y * 100).ToString("0.#########", CultureInfo.InvariantCulture)).Append('\n');
            text.Append("image_scale=").Append((basis.scale * 100).ToString("0.#########", CultureInfo.InvariantCulture)).Append('\n');
        }
        text.Append('\n');
    }
}
