using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>单牌设置的身份由牌面包、牌号、2D/3D 三者共同确定。</summary>
public static class TileFaceOffsetSettings
{
    [Serializable] sealed class Record {
        public int version = 1;
        public string pack;
        public int tileId;
        public bool table;
        public HandSurfaceLibrary.FaceLayout layout;
    }
    const string Prefix = "TileFaceAdjustmentV1.";
    const string VisibleDefaultsPrefix = "TileFaceAdjustmentV2.";
    static readonly Dictionary<string, HandSurfaceLibrary.FaceLayout> Values = new Dictionary<string, HandSurfaceLibrary.FaceLayout>();
    static int Identity(string pack, int tileId) => FixedFacePack.IsFixed(pack) ? tileId : WashizuTile.Face(tileId);
    static string LegacyKey(string pack, int tileId, bool table) => Prefix + pack + "." + Identity(pack, tileId) + (table ? ".3d" : ".2d");
    static string Key(string pack, int tileId, bool table) =>
        (TilePackLayoutConfig.UsesVisibleManzuDefaults(pack, tileId) ? VisibleDefaultsPrefix : Prefix)
        + pack + "." + Identity(pack, tileId) + (table ? ".3d" : ".2d");
    public static HandSurfaceLibrary.FaceLayout Normalize(HandSurfaceLibrary.FaceLayout v) {
        v.x = float.IsNaN(v.x) || float.IsInfinity(v.x) ? 0 : Mathf.Clamp(v.x, -1f, 1f);
        v.y = float.IsNaN(v.y) || float.IsInfinity(v.y) ? 0 : Mathf.Clamp(v.y, -1f, 1f);
        v.scale = float.IsNaN(v.scale) || float.IsInfinity(v.scale) || v.scale <= 0 ? 1 : Mathf.Clamp(v.scale, FaceScaleSlider.MinScale, FaceScaleSlider.MaxScale);
        return v;
    }
    public static void Invalidate(string pack) {
        string prefix = Prefix + pack + ".", visiblePrefix = VisibleDefaultsPrefix + pack + ".";
        foreach (string key in new List<string>(Values.Keys))
            if (key.StartsWith(prefix, StringComparison.Ordinal) || key.StartsWith(visiblePrefix, StringComparison.Ordinal)) Values.Remove(key);
    }
    public static HandSurfaceLibrary.FaceLayout Get(string pack, int tileId, bool table) {
        if (string.IsNullOrEmpty(pack) || !(FixedFacePack.IsFixed(pack) ? FixedFacePack.IsEditable(pack, tileId)
            : TilePackIds.IsStandardFaceId(WashizuTile.Face(tileId)))) return HandSurfaceLibrary.FaceLayout.Default;
        string key = Key(pack, tileId, table);
        if (Values.TryGetValue(key, out var value)) return value;
        value = TilePackLayoutConfig.Default(pack, table, Identity(pack, tileId));
        string json = PlayerPrefs.GetString(key, "");
        if (string.IsNullOrEmpty(json) && TilePackLayoutConfig.UsesVisibleManzuDefaults(pack, tileId))
            json = PlayerPrefs.GetString(LegacyKey(pack, tileId, table), "");
        if (!string.IsNullOrEmpty(json)) {
            try {
                var record = JsonUtility.FromJson<Record>(json);
                if (record != null && (record.version == 1 || record.version == 2) && record.pack == pack && record.tileId == Identity(pack, tileId) && record.table == table) {
                    value = Normalize(record.layout);
                    if (record.version == 1 && TilePackLayoutConfig.UsesVisibleManzuDefaults(pack, tileId)) {
                        var fit = Vector2.one;
                        if (!table) {
                            var body = TileFaceResolver.LoadHandBackground();
                            if (body != null) fit = TilePackLayoutConfig.ManzuHandCanvasFit(body.rect.size);
                        }
                        value = FoldManzuAdjustment(value, TilePackLayoutConfig.Default(pack, table, Identity(pack, tileId)), fit);
                        string backup = key + ".before-f5288";
                        if (!PlayerPrefs.HasKey(backup)) PlayerPrefs.SetString(backup, json);
                        Save(pack, tileId, table, value);
                    }
                }
            } catch (ArgumentException) { }
        }
        Values[key] = value;
        return value;
    }
    static HandSurfaceLibrary.FaceLayout FoldManzuAdjustment(HandSurfaceLibrary.FaceLayout adjustment,
        HandSurfaceLibrary.FaceLayout basis, Vector2 offsetFit) {
        return Normalize(new HandSurfaceLibrary.FaceLayout {
            x = adjustment.x / offsetFit.x + basis.x * adjustment.scale,
            y = adjustment.y / offsetFit.y + basis.y * adjustment.scale,
            scale = basis.scale * adjustment.scale
        });
    }
    public static HandSurfaceLibrary.FaceLayout Compose(string pack, int tileId, bool table, HandSurfaceLibrary.FaceLayout group,
        Vector2? offsetFit = null) {
        var tile = Get(pack, tileId, table);
        var fit = offsetFit ?? Vector2.one;
        return new HandSurfaceLibrary.FaceLayout {
            x = group.x + tile.x * group.scale * fit.x,
            y = group.y + tile.y * group.scale * fit.y,
            scale = group.scale * tile.scale
        };
    }
    public static void Preview(string pack, int tileId, bool table, HandSurfaceLibrary.FaceLayout value) {
        Values[Key(pack, tileId, table)] = Normalize(value);
        if (pack != TableFaceLayoutSettings.SelectedPack && !FixedFacePack.IsFixed(pack)) return;
        if (table) TileFaceResolver.NotifyTableLayoutChanged(); else TileFaceResolver.NotifyHandLayoutChanged();
    }
    public static void Save(string pack, int tileId, bool table, HandSurfaceLibrary.FaceLayout value) {
        value = Normalize(value);
        Values[Key(pack, tileId, table)] = value;
        PlayerPrefs.SetString(Key(pack, tileId, table), JsonUtility.ToJson(new Record {
            version = TilePackLayoutConfig.UsesVisibleManzuDefaults(pack, tileId) ? 2 : 1,
            pack = pack, tileId = Identity(pack, tileId), table = table, layout = value
        }));
        PlayerPrefs.Save();
    }
    public static void MigrateUncalibratedTable(string pack, HandSurfaceLibrary.FaceLayout old) {
        foreach (int id in TilePackIds.StandardFaceIds) {
            if (PlayerPrefs.HasKey(Key(pack, id, true)) || PlayerPrefs.HasKey(LegacyKey(pack, id, true))) continue;
            if (TilePackLayoutConfig.UsesVisibleManzuDefaults(pack, id)) {
                // The oldest group save was already absolute, before per-tile
                // baselines. Preserve it while folding in its former image area.
                Save(pack, id, true, new HandSurfaceLibrary.FaceLayout {
                    x = old.x, y = old.y, scale = old.scale * TileTextureLayout.SnowTableImageScale
                });
                continue;
            }
            var basis = LegacyFacePlacement.Table(id, pack);
            float scale = old.scale / basis.scale.x;
            Save(pack, id, true, new HandSurfaceLibrary.FaceLayout {
                x = old.x - basis.offset.x * scale, y = old.y - basis.offset.y * scale, scale = scale
            });
        }
    }
}
