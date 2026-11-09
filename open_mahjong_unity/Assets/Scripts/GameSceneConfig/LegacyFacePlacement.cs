using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Reconstructs the pre-unification placement without resampling the source artwork.</summary>
public static class LegacyFacePlacement
{
    [Serializable] sealed class Picture { public int width, height; public int[] bounds; }
    [Serializable] sealed class Affine { public float scale, x, y; }
    [Serializable] sealed class Entry {
        public string pack; public int tileId; public Picture source, hand, table;
        public float oldHandScale, oldTableScale;
        public Affine exactTable;
    }
    [Serializable] sealed class Data { public int version; public Entry[] entries; }
    public struct Placement {
        public Vector2 offset, scale;
        public bool hasLegacy;
        public static Placement Identity => new Placement { scale = Vector2.one };
    }
    static Dictionary<string, Entry> cache;

    static bool TryEntry(int tileId, string pack, bool table, out Entry entry)
    {
        entry = null;
        if (tileId < 0 || ConfigManager.Instance == null || HongqueTileVisual.IsHongqueId(tileId)
            || DuocaiTileVisual.IsCurrent(tileId) || VietnameseTileVisual.IsCurrentRuleTile(tileId)
            || SingaporeTileVisual.IsAnimal(tileId) || MalaysiaTileVisual.Contains(tileId)) return false;
        pack = pack ?? ConfigManager.Instance.StandardTilePackId;
        // Official manzu now carry their complete placement in the visible,
        // pack-bound per-tile defaults. Never stack the old drawing correction.
        if (TilePackLayoutConfig.UsesVisibleManzuDefaults(pack, tileId)) return false;
        if (TilePackIds.IsCustomPack(pack) && !TileFaceResolver.HasCustomArtwork(tileId, table)) pack = TilePackIds.PackOfficial;
        if (!TilePackIds.IsBuiltinPack(pack)) return false;
        if (cache == null) {
            cache = new Dictionary<string, Entry>(StringComparer.Ordinal);
            var file = Resources.Load<TextAsset>("image/Cards/Faces/legacy-layout-bounds");
            if (file != null) {
                var data = JsonUtility.FromJson<Data>(file.text);
                if (data != null && data.version == 1 && data.entries != null)
                    foreach (var item in data.entries) cache[item.pack + ":" + item.tileId] = item;
            }
        }
        return cache.TryGetValue(pack + ":" + WashizuTile.Face(tileId), out entry);
    }

    public static Placement Hand(int tileId, Vector2 bodySize, string pack = null)
    {
        if (!TryEntry(tileId, pack, false, out var entry) || bodySize.x <= 0 || bodySize.y <= 0) return Placement.Identity;
        int face = WashizuTile.Face(tileId);
        if (entry.pack == TilePackIds.PackOfficial && ((face >= 11 && face <= 19) || face == 105)) {
            // An incomplete uploaded pack falls back to the official artwork.
            // Read that artwork's defaults rather than duplicating old transforms.
            var defaults = TilePackLayoutConfig.Default(TilePackIds.PackOfficial, false, face);
            var fit = TilePackLayoutConfig.ManzuHandCanvasFit(bodySize);
            return new Placement { offset = Vector2.Scale(new Vector2(defaults.x, defaults.y), fit),
                scale = Vector2.one * defaults.scale, hasLegacy = true };
        }
        return Calculate(entry.source, entry.hand, bodySize.x / bodySize.y, entry.oldHandScale, 1f);
    }

    public static Placement Table(int tileId, string pack = null)
    {
        if (!TryEntry(tileId, pack, true, out var entry)) return Placement.Identity;
        float currentScale = entry.pack == TilePackIds.PackOfficial ? TileTextureLayout.SnowTableImageScale : TileTextureLayout.TableImageScale;
        int face = WashizuTile.Face(tileId);
        if (entry.pack == TilePackIds.PackOfficial && ((face >= 11 && face <= 19) || face == 105)) {
            var defaults = TilePackLayoutConfig.Default(TilePackIds.PackOfficial, true, face);
            return new Placement { offset = new Vector2(defaults.x, defaults.y),
                scale = Vector2.one * (defaults.scale / currentScale), hasLegacy = true };
        }
        if (entry.exactTable != null && entry.exactTable.scale > 0 && Valid(entry.source) && Valid(entry.table)) {
            return FromAffine(entry, entry.table, entry.exactTable, TileTextureLayout.TableAspect, entry.oldTableScale, currentScale);
        }
        return Calculate(entry.source, entry.table, TileTextureLayout.TableAspect, entry.oldTableScale, currentScale);
    }

    static Placement FromAffine(Entry entry, Picture canvas, Affine affine, float aspect, float oldScale, float currentScale)
    {
        Vector2 fit = Fit(canvas, aspect) * oldScale;
        Vector2 center = new Vector2((affine.scale * entry.source.width * .5f + affine.x) / canvas.width - .5f,
            .5f - (affine.scale * entry.source.height * .5f + affine.y) / canvas.height);
        Vector2 oldSize = Vector2.Scale(new Vector2(affine.scale * entry.source.width / canvas.width,
            affine.scale * entry.source.height / canvas.height), fit);
        Vector2 newSize = Fit(entry.source, aspect) * currentScale;
        return new Placement { offset = Vector2.Scale(center, fit),
            scale = new Vector2(oldSize.x / newSize.x, oldSize.y / newSize.y), hasLegacy = true };
    }

    static Placement Calculate(Picture source, Picture old, float canvasAspect, float oldScale, float currentScale)
    {
        if (!Valid(source) || !Valid(old) || canvasAspect <= 0 || currentScale <= 0) return Placement.Identity;
        Vector2 oldFit = Fit(old, canvasAspect) * oldScale;
        Vector2 newFit = Fit(source, canvasAspect) * currentScale;
        Vector2 oldSize = Vector2.Scale(Size(old), oldFit), newSize = Vector2.Scale(Size(source), newFit);
        if (oldSize.x <= 0 || oldSize.y <= 0 || newSize.x <= 0 || newSize.y <= 0) return Placement.Identity;
        // Preserve the source artwork's proportions; never stretch individual tiles to their alpha bounds.
        Vector2 multiplier = Vector2.one * Mathf.Min(oldSize.x / newSize.x, oldSize.y / newSize.y);
        Vector2 oldCenter = Vector2.Scale(Center(old), oldFit), newCenter = Vector2.Scale(Center(source), newFit);
        return new Placement { offset = oldCenter - Vector2.Scale(newCenter, multiplier), scale = multiplier, hasLegacy = true };
    }
    static bool Valid(Picture p) => p != null && p.width > 0 && p.height > 0 && p.bounds != null && p.bounds.Length == 4;
    static Vector2 Fit(Picture p, float targetAspect) {
        float aspect = (float)p.width / p.height;
        return aspect > targetAspect ? new Vector2(1, targetAspect / aspect) : new Vector2(aspect / targetAspect, 1);
    }
    static Vector2 Size(Picture p) => new Vector2((float)(p.bounds[2] - p.bounds[0]) / p.width, (float)(p.bounds[3] - p.bounds[1]) / p.height);
    static Vector2 Center(Picture p) => new Vector2((p.bounds[0] + p.bounds[2]) / (2f * p.width) - .5f,
        .5f - (p.bounds[1] + p.bounds[3]) / (2f * p.height));
}
