using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>3D 牌面的位置与大小按牌面包独立保存，不改变 2D 手牌设置。</summary>
public static class TableFaceLayoutSettings
{
    [Serializable] sealed class Record { public int version = 1; public HandSurfaceLibrary.FaceLayout layout; }
    // V2 is relative to the restored pre-unification placement; V1 values were relative to the uncalibrated artwork.
    const string Prefix = "TableFaceLayoutV2.";
    static readonly Dictionary<string, HandSurfaceLibrary.FaceLayout> Layouts = new Dictionary<string, HandSurfaceLibrary.FaceLayout>();

    public static string SelectedPack => ConfigManager.Instance != null
        ? ConfigManager.Instance.StandardTilePackId : TilePackIds.PackOfficial;
    public static HandSurfaceLibrary.FaceLayout Current => Get(SelectedPack);

    public static void Invalidate(string pack) => Layouts.Remove(pack);
    public static HandSurfaceLibrary.FaceLayout Get(string pack)
    {
        pack = string.IsNullOrEmpty(pack) ? TilePackIds.PackOfficial : pack;
        if (Layouts.TryGetValue(pack, out var value)) return value;
        value = TilePackLayoutConfig.Default(pack, true);
        string data = PlayerPrefs.GetString(Prefix + pack, "");
        if (!string.IsNullOrEmpty(data)) {
            try { var record = JsonUtility.FromJson<Record>(data); if (record != null && record.version == 1) value = record.layout.Normalized; }
            catch (ArgumentException) { }
        }
        else {
            string oldData = PlayerPrefs.GetString("TableFaceLayoutV1." + pack, "");
            if (!string.IsNullOrEmpty(oldData)) {
                try {
                    var previous = JsonUtility.FromJson<Record>(oldData);
                    if (previous != null && previous.version == 1) {
                        // Only built-in artwork has the restored historical baseline.
                        // Uploaded/fixed packs retain their visible group adjustment directly;
                        // never derive per-tile corrections from the official fallback artwork.
                        if (TilePackIds.IsBuiltinPack(pack))
                            TileFaceOffsetSettings.MigrateUncalibratedTable(pack, previous.layout.Normalized);
                        else value = previous.layout.Normalized;
                        PlayerPrefs.SetString(Prefix + pack, JsonUtility.ToJson(new Record { layout = value }));
                        PlayerPrefs.Save();
                    }
                } catch (ArgumentException) { }
            }
        }
        Layouts[pack] = value;
        return value;
    }

    public static HandSurfaceLibrary.FaceLayout ForTile(int tileId, string pack = null)
    {
        if (FixedFacePack.IsFixed(pack)) return TileFaceOffsetSettings.Compose(pack, tileId, true, Get(pack));
        if (FixedFacePack.TryResolve(tileId, out var fixedPack, out int key))
            return TileFaceOffsetSettings.Compose(fixedPack, key, true, Get(fixedPack));
        if (tileId < 0 || HongqueTileVisual.IsHongqueId(tileId) || DuocaiTileVisual.IsCurrent(tileId)
            || VietnameseTileVisual.IsCurrentRuleTile(tileId) || SingaporeTileVisual.IsAnimal(tileId)
            || MalaysiaTileVisual.Contains(tileId)) return HandSurfaceLibrary.FaceLayout.Default;
        return TilePackIds.IsStandardFaceId(WashizuTile.Face(tileId))
            ? TileFaceOffsetSettings.Compose(pack ?? SelectedPack, tileId, true, Get(pack ?? SelectedPack)) : HandSurfaceLibrary.FaceLayout.Default;
    }

    public static void Preview(string pack, HandSurfaceLibrary.FaceLayout value)
    {
        if (string.IsNullOrEmpty(pack)) return;
        Layouts[pack] = value.Normalized;
        if (pack == SelectedPack || FixedFacePack.IsFixed(pack)) TileFaceResolver.NotifyTableLayoutChanged();
    }

    public static void Save(string pack, HandSurfaceLibrary.FaceLayout value)
    {
        if (string.IsNullOrEmpty(pack)) return;
        value = value.Normalized;
        Layouts[pack] = value;
        PlayerPrefs.SetString(Prefix + pack, JsonUtility.ToJson(new Record { layout = value }));
        PlayerPrefs.Save();
    }
}
