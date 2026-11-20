using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

public static class CardPackExporter
{
    public static void Export(Action<byte[], string> done, Action<string> error)
    {
        string pack = TableFaceLayoutSettings.SelectedPack;
        void Build(TilePackImporter.Result source) {
            try { done(Create(pack, source), SingleTileAdjustmentPanel.PackName(pack) + ".zip"); }
            catch (Exception e) { error?.Invoke("导出失败：" + e.Message); }
        }
        if (TilePackIds.IsCustomPack(pack)) TilePackLibrary.Load(pack, Build, error);
        else Build(null);
    }
    public static byte[] Create(string pack, TilePackImporter.Result source = null)
    {
        var config = ConfigManager.Instance;
        if (config == null || !HandSurfaceLibrary.Ready) throw new InvalidOperationException("卡牌设置尚未就绪");
        var pngs = new Dictionary<string, byte[]>();
        var text = new StringBuilder();
        var body = TileFaceResolver.LoadHandBackground().rect.size;
        TilePackLayoutConfig.WriteLayout(text, "2d", HandFaceAdjustmentSettings.Get(pack));
        TilePackLayoutConfig.WriteLayout(text, "3d", TableFaceLayoutSettings.Get(pack));
        foreach (int id in TilePackIds.StandardFaceIds) {
            byte[] png = null;
            if (source != null) source.Pngs.TryGetValue(id, out png);
            if (png == null) png = ReadBuiltin(TilePackIds.IsBuiltinPack(pack) ? pack : TilePackIds.PackOfficial, id + ".png");
            if (png == null) png = ReadBuiltin(TilePackIds.PackOfficial, id + ".png");
            if (png == null) continue;
            pngs[id + ".png"] = png;
            for (int view = 0; view < 2; view++) {
                bool table = view == 1;
                var tile = TileFaceOffsetSettings.Get(pack, id, table);
                var basis = table ? LegacyFacePlacement.Table(id, pack) : LegacyFacePlacement.Hand(id, body, pack);
                float aspect = table ? 0 : TilePackLayoutConfig.OffsetAspect(pack, id);
                float imageScale = basis.scale.x * (table ? TileFaceResolver.TableImageScaleFor(id, false)
                    : basis.hasLegacy ? 1f : TileFaceFit.HandArtworkScale(id));
                TilePackLayoutConfig.WriteLayout(text, (table ? "3d." : "2d.") + id, tile, aspect,
                    new HandSurfaceLibrary.FaceLayout { x = basis.offset.x, y = basis.offset.y, scale = imageScale });
            }
        }
        var hand = config.GetSelectedHandBackground();
        var back = config.GetSelectedHandBack();
        pngs["hand-bg.png"] = ReadHand(hand.path, hand.isCustom, false);
        pngs["hand-back.png"] = ReadHand(back.path, back.isCustom, true);
        var appearance = config.CaptureCard3DAppearance();
        bool hasBack = !string.IsNullOrEmpty(appearance.backImage), hasBackground = !string.IsNullOrEmpty(appearance.backgroundImage);
        if (hasBack) pngs["table-back.png"] = ReadSurface(appearance.backImage, appearance.backImageCustom);
        if (hasBackground) pngs["table-bg.png"] = ReadSurface(appearance.backgroundImage, appearance.backgroundImageCustom);
        TilePackLayoutConfig.WriteLayout(text, "hand", HandSurfaceLibrary.CurrentFaceLayout);
        CardPackAppearance.Capture(appearance, hasBack, hasBackground).Append(text);
        pngs["tiles.cfg"] = Encoding.UTF8.GetBytes(text.ToString());
        TilePackLayoutConfig.Parse(text.ToString());
        using (var output = new MemoryStream()) {
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true)) {
                long total = 0;
                foreach (var file in pngs) {
                    if (file.Value == null) throw new InvalidDataException("无法读取原图: " + file.Key);
                    int max = CardPackAppearance.IsSurface(file.Key) ? TilePackImporter.MaxSurfaceBytes : TilePackImporter.MaxPngBytes;
                    if (file.Key.EndsWith(".png", StringComparison.Ordinal) && file.Value.Length > max)
                        throw new InvalidDataException("图片超过大小限制: " + file.Key);
                    total += file.Value.Length;
                    if (total > TilePackImporter.MaxUncompressedBytes) throw new InvalidDataException("牌包超过 128MB");
                    using (var entry = zip.CreateEntry(file.Key, System.IO.Compression.CompressionLevel.Optimal).Open()) entry.Write(file.Value, 0, file.Value.Length);
                }
            }
            return output.ToArray();
        }
    }
    static byte[] ReadHand(string path, bool custom, bool back)
    {
        if (custom && HandSurfaceLibrary.IsId(path)) return Png(HandSurfaceLibrary.ExportImage(path));
        if (!custom && string.IsNullOrEmpty(path)) path = back ? TilePackIds.DefaultHandBackResource : TilePackIds.DefaultHandBgResource;
        return ReadSurface(path, custom);
    }
    static byte[] ReadSurface(string path, bool custom)
    {
        if (!custom) return ReadBuiltin("surfaces", path + ".png");
#if UNITY_WEBGL && !UNITY_EDITOR
        return Png(UnityAssetIdb.GetCached(path));
#else
        return Png(File.ReadAllBytes(path));
#endif
    }
    static byte[] Png(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 8) throw new InvalidDataException("图片无法读取");
        if (bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71) return bytes;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try {
            if (!ImageConversion.LoadImage(texture, bytes, false)) throw new InvalidDataException("图片无法解码");
            return texture.EncodeToPNG();
        } finally {
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture); else UnityEngine.Object.DestroyImmediate(texture);
        }
    }
    public static byte[] ReadBuiltin(string pack, string filename)
    {
        var asset = Resources.Load<TextAsset>("CardPackSources/" + pack);
        if (asset == null) return null;
        try {
            using (var input = new MemoryStream(asset.bytes, false))
            using (var zip = new ZipArchive(input, ZipArchiveMode.Read)) {
                var entry = zip.GetEntry(filename);
                if (entry == null) return null;
                using (var stream = entry.Open()) using (var output = new MemoryStream()) {
                    stream.CopyTo(output); return output.ToArray();
                }
            }
        } finally { Resources.UnloadAsset(asset); }
    }
}
