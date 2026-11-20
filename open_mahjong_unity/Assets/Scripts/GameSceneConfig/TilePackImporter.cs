using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

public static class TilePackImporter {
    public const int MaxImageEdge = 4096;
    public const int MaxPngBytes = 4 * 1024 * 1024;
    public const int MaxSurfaceBytes = 8 * 1024 * 1024;
    public const int MaxUncompressedBytes = 128 * 1024 * 1024;

    public sealed class Result {
        public readonly Dictionary<int, byte[]> Pngs = new Dictionary<int, byte[]>();
        public readonly Dictionary<string, byte[]> Surfaces = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Warnings = new List<string>();
        public string Error;
        public string ConfigText;
        public TilePackLayoutConfig LayoutConfig;
        public bool Success => string.IsNullOrEmpty(Error) && Pngs.Count > 0;
    }

    public static Result Import(byte[] zipBytes) {
        var result = new Result();
        if (zipBytes == null || zipBytes.Length == 0) {
            result.Error = "压缩包为空";
            return result;
        }
        if (zipBytes.Length > MaxUncompressedBytes) {
            result.Error = "压缩包过大（超过 128MB）";
            return result;
        }

        // 一次导入只复用一个解码探针，避免 WebGL 同帧保留整包临时 GPU 纹理。
        Texture2D decodeProbe = null;
        try {
            using (var zipStream = new MemoryStream(zipBytes, false))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, true)) {
                long uncompressed = 0;
                foreach (ZipArchiveEntry entry in archive.Entries) {
                    if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith("/")) {
                        continue;
                    }
                    uncompressed += entry.Length;
                    if (uncompressed > MaxUncompressedBytes) {
                        result.Error = "解压后超过 128MB";
                        return result;
                    }
                }

                foreach (ZipArchiveEntry entry in archive.Entries) {
                    ImportEntry(entry, result, ref decodeProbe);
                    if (!string.IsNullOrEmpty(result.Error)) {
                        return result;
                    }
                }
            }
        }
        catch (InvalidDataException) {
            result.Error = "不是有效的 zip 文件";
            return result;
        }
        catch (Exception e) {
            result.Error = "解压失败: " + e.Message;
            return result;
        }
        finally {
            if (decodeProbe != null) {
                if (Application.isPlaying) UnityEngine.Object.Destroy(decodeProbe);
                else UnityEngine.Object.DestroyImmediate(decodeProbe);
            }
        }

        if (result.Pngs.Count == 0) {
            result.Error = "压缩包内没有可识别的 PNG 牌面";
        }
        else {
            int missing = 0;
            foreach (int id in TilePackIds.StandardFaceIds) {
                if (!result.Pngs.ContainsKey(id)) {
                    missing++;
                }
            }
            if (missing > 0) {
                result.Warnings.Add($"缺 {missing} 张，对局中将回退官方牌面");
            }
        }
        if (result.ConfigText == null) result.Error = "牌面包缺少 tiles.cfg";
        if (result.LayoutConfig?.Appearance != null) {
            foreach (string name in result.LayoutConfig.Appearance.Images)
                if (!result.Surfaces.ContainsKey(name)) result.Error = "牌面包缺少背景图片: " + name;
        }
        return result;
    }

    private static void ImportEntry(ZipArchiveEntry entry, Result result, ref Texture2D decodeProbe) {
        if (entry == null || string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith("/")) {
            return;
        }
        string full = entry.FullName.Replace('\\', '/').TrimStart('/');
        if (full.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)) {
            return;
        }
        string fileName = Path.GetFileName(full);
        foreach (string part in full.Split('/')) {
            if (part.Equals("hand", StringComparison.OrdinalIgnoreCase) || part.Equals("table", StringComparison.OrdinalIgnoreCase)
                || part == "手牌牌面" || part.Equals("3d牌面", StringComparison.OrdinalIgnoreCase)) {
                result.Error = "牌面包需使用一套牌号 PNG 和 tiles.cfg";
                return;
            }
        }
        if (fileName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("preview.png", StringComparison.OrdinalIgnoreCase)) {
            return;
        }
        if (fileName.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase)) {
            if (!fileName.Equals("tiles.cfg", StringComparison.OrdinalIgnoreCase)) { result.Error = "牌面配置须命名为 tiles.cfg"; return; }
            if (result.ConfigText != null) { result.Error = "牌面包只能包含一个 .cfg 文件"; return; }
            if (entry.Length > TilePackLayoutConfig.MaxBytes) { result.Error = "牌面配置超过 64KB"; return; }
            try {
                using (var reader = new StreamReader(entry.Open(), new UTF8Encoding(false, true), false))
                    result.ConfigText = reader.ReadToEnd();
                result.LayoutConfig = TilePackLayoutConfig.Parse(result.ConfigText);
            } catch (Exception e) { result.Error = e.Message; }
            return;
        }
        if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) {
            if (!fileName.StartsWith(".")) {
                result.Warnings.Add("已忽略非 PNG: " + fileName);
            }
            return;
        }
        if (!CardPackAppearance.IsSurface(fileName) && entry.Length > MaxPngBytes) {
            result.Warnings.Add("单张超过 4MB，已跳过: " + fileName);
            return;
        }

        if (CardPackAppearance.IsSurface(fileName)) {
            if (entry.Length > MaxSurfaceBytes) { result.Error = "背景图片超过 8MB: " + fileName; return; }
            if (result.Surfaces.ContainsKey(fileName)) { result.Error = "背景图片重复: " + fileName; return; }
            using (var input = entry.Open()) using (var buffer = new MemoryStream()) {
                input.CopyTo(buffer);
                byte[] surface = buffer.ToArray();
                if (!TryReadPngSize(surface, out int sw, out int sh) || sw > MaxImageEdge || sh > MaxImageEdge
                    || !CanDecodePng(surface, sw, sh, ref decodeProbe)) { result.Error = "背景 PNG 无效: " + fileName; return; }
                result.Surfaces[fileName] = surface;
            }
            return;
        }
        string idPart = Path.GetFileNameWithoutExtension(fileName);
        if (!int.TryParse(idPart, out int tileId) || tileId.ToString(System.Globalization.CultureInfo.InvariantCulture) != idPart || !TilePackIds.IsStandardFaceId(tileId)) {
            result.Warnings.Add("无法识别的牌面文件名: " + fileName);
            return;
        }

        byte[] png;
        using (Stream stream = entry.Open())
        using (var memory = new MemoryStream()) {
            stream.CopyTo(memory);
            png = memory.ToArray();
        }
        if (!IsPng(png)) {
            result.Warnings.Add("不是 PNG: " + fileName);
            return;
        }
        if (!TryReadPngSize(png, out int width, out int height)
            || width <= 0 || height <= 0
            || width > MaxImageEdge || height > MaxImageEdge) {
            result.Warnings.Add("尺寸不合法（需 ≤4096）: " + fileName);
            return;
        }

        if (!CanDecodePng(png, width, height, ref decodeProbe)) {
            result.Warnings.Add("PNG 数据损坏，已跳过: " + fileName);
            return;
        }
        Dictionary<int, byte[]> target = result.Pngs;
        if (target.ContainsKey(tileId)) { result.Error = "牌面代号重复: " + full; return; }
        target[tileId] = png;
    }

    private static bool CanDecodePng(byte[] png, int width, int height, ref Texture2D decodeProbe) {
        if (decodeProbe == null) decodeProbe = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try {
            return ImageConversion.LoadImage(decodeProbe, png, false)
                && decodeProbe.width == width && decodeProbe.height == height;
        }
        catch {
            return false;
        }
    }

    private static bool IsPng(byte[] bytes) {
        return bytes != null && bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;
    }

    private static bool TryReadPngSize(byte[] bytes, out int width, out int height) {
        width = 0;
        height = 0;
        if (bytes == null || bytes.Length < 33
            || bytes[8] != 0 || bytes[9] != 0 || bytes[10] != 0 || bytes[11] != 13
            || bytes[12] != 'I' || bytes[13] != 'H' || bytes[14] != 'D' || bytes[15] != 'R') {
            return false;
        }
        width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        return width > 0 && height > 0;
    }
}
