using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>玩家对 2D 牌面的调整，以所选背景的基准点和旧版排版为起点。</summary>
public static class HandFaceAdjustmentSettings
{
    [Serializable] sealed class Record { public int version = 1; public HandSurfaceLibrary.FaceLayout layout; }
    const string Prefix = "HandFaceAdjustmentV1.";
    static readonly Dictionary<string, HandSurfaceLibrary.FaceLayout> Layouts = new Dictionary<string, HandSurfaceLibrary.FaceLayout>();
    public static string SelectedPack => TableFaceLayoutSettings.SelectedPack;
    public static void Invalidate(string pack) => Layouts.Remove(pack);
    public static HandSurfaceLibrary.FaceLayout Get(string pack) {
        pack = string.IsNullOrEmpty(pack) ? TilePackIds.PackOfficial : pack;
        if (Layouts.TryGetValue(pack, out var value)) return value;
        value = TilePackLayoutConfig.Default(pack, false);
        string json = PlayerPrefs.GetString(Prefix + pack, "");
        if (!string.IsNullOrEmpty(json)) {
            try { var record = JsonUtility.FromJson<Record>(json); if (record != null && record.version == 1) value = record.layout.Normalized; }
            catch (ArgumentException) { }
        }
        Layouts[pack] = value; return value;
    }
    public static HandSurfaceLibrary.FaceLayout ForTile(int tileId, string pack = null, Vector2? offsetFit = null) {
        if (FixedFacePack.IsFixed(pack)) return TileFaceOffsetSettings.Compose(pack, tileId, false, Get(pack));
        if (pack == null && FixedFacePack.TryResolve(tileId, out var fixedPack, out int key))
            return TileFaceOffsetSettings.Compose(fixedPack, key, false, Get(fixedPack));
        if (tileId < 0 || HongqueTileVisual.IsHongqueId(tileId) || DuocaiTileVisual.IsCurrent(tileId)
            || VietnameseTileVisual.IsCurrentRuleTile(tileId) || SingaporeTileVisual.IsAnimal(tileId)
            || MalaysiaTileVisual.Contains(tileId) || !TilePackIds.IsStandardFaceId(WashizuTile.Face(tileId)))
            return HandSurfaceLibrary.FaceLayout.Default;
        pack = pack ?? SelectedPack;
        return TileFaceOffsetSettings.Compose(pack, tileId, false, Get(pack), offsetFit);
    }
    public static void Preview(string pack, HandSurfaceLibrary.FaceLayout value) {
        if (string.IsNullOrEmpty(pack)) return;
        Layouts[pack] = value.Normalized;
        if (pack == SelectedPack || FixedFacePack.IsFixed(pack)) TileFaceResolver.NotifyHandLayoutChanged();
    }
    public static void Save(string pack, HandSurfaceLibrary.FaceLayout value) {
        if (string.IsNullOrEmpty(pack)) return;
        value = value.Normalized; Layouts[pack] = value;
        PlayerPrefs.SetString(Prefix + pack, JsonUtility.ToJson(new Record { layout = value }));
        PlayerPrefs.Save();
    }
}
