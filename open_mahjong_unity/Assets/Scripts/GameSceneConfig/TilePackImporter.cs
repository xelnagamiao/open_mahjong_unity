using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

/// <summary>解析标准麻将自定义牌面 zip（可选 manifest.json）。</summary>
public static class TilePackImporter {
    public const int MaxImageEdge = 1024;
    public const int MaxPngBytes = 500 * 1024;
    public const int MaxUncompressedBytes = 20 * 1024 * 1024;
    public const string ExpectedFormat = "om-tilepack";
    public const string ExpectedFamily = "standard";

    public sealed class Result {
        public readonly Dictionary<int, byte[]> HandPngs = new Dictionary<int, byte[]>();
        public readonly Dictionary<int, byte[]> TablePngs = new Dictionary<int, byte[]>();
        public readonly List<string> Warnings = new List<string>();
        public string Error;
        public bool Success => string.IsNullOrEmpty(Error) && HandPngs.Count > 0 && TablePngs.Count > 0;
    }

    [Serializable]
    private class Manifest {
        public string format;
        public int version;
        public string family;
    }

    public static Result Import(byte[] zipBytes) {
        var result = new Result();
        if (zipBytes == null || zipBytes.Length == 0) {
            result.Error = "压缩包为空";
            return result;
        }
        if (zipBytes.Length > MaxUncompressedBytes) {
            result.Error = "压缩包过大（超过 20MB）";
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
                        result.Error = "解压后超过 20MB";
                        return result;
                    }
                }

                ZipArchiveEntry manifestEntry = FindManifest(archive);
                if (manifestEntry != null) {
                    string manifestError = ValidateManifest(ReadUtf8(manifestEntry));
                    if (!string.IsNullOrEmpty(manifestError)) {
                        result.Error = manifestError;
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

        if (result.HandPngs.Count == 0 || result.TablePngs.Count == 0) {
            result.Error = "压缩包必须同时包含 hand/ 和 table/ 两个文件夹（也可用「手牌牌面」「3D牌面」）";
        }
        else {
            int missing = 0;
            foreach (int id in TilePackIds.StandardFaceIds) {
                if (!result.HandPngs.ContainsKey(id) && !result.TablePngs.ContainsKey(id)) {
                    missing++;
                }
            }
            if (missing > 0) {
                result.Warnings.Add($"缺 {missing} 张，对局中将回退官方牌面");
            }
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
        if (fileName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("preview.png", StringComparison.OrdinalIgnoreCase)) {
            return;
        }
        if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) {
            if (!fileName.StartsWith(".")) {
                result.Warnings.Add("已忽略非 PNG: " + fileName);
            }
            return;
        }
        if (entry.Length > MaxPngBytes) {
            result.Warnings.Add("单张超过 500KB，已跳过: " + fileName);
            return;
        }

        string idPart = Path.GetFileNameWithoutExtension(fileName);
        if (!int.TryParse(idPart, out int tileId) || !TilePackIds.IsStandardFaceId(tileId)) {
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
            result.Warnings.Add("尺寸不合法（需 ≤1024）: " + fileName);
            return;
        }

        bool isTable = TilePackIds.IsTableFolder(full);
        bool isHand = TilePackIds.IsHandFolder(full);
        if (!isTable && !isHand) {
            result.Warnings.Add("牌面 PNG 必须放在 hand/ 或 table/（也可用 手牌牌面/、3D牌面/）: " + full);
            return;
        }
        if (!CanDecodePng(png, width, height, ref decodeProbe)) {
            result.Warnings.Add("PNG 数据损坏，已跳过: " + fileName);
            return;
        }
        // 保留原文件，避免重复导入时重采样、拉伸、裁切或把透明区域烘成底色。
        Dictionary<int, byte[]> target = isTable ? result.TablePngs : result.HandPngs;
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

    private static ZipArchiveEntry FindManifest(ZipArchive archive) {
        foreach (ZipArchiveEntry entry in archive.Entries) {
            string name = Path.GetFileName(entry.FullName);
            if (name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) {
                return entry;
            }
        }
        return null;
    }

    private static string ValidateManifest(string json) {
        if (string.IsNullOrWhiteSpace(json)) {
            return null;
        }
        Manifest manifest;
        try {
            manifest = JsonUtility.FromJson<Manifest>(json);
        }
        catch {
            return "manifest.json 无法解析";
        }
        if (manifest == null) {
            return null;
        }
        if (!string.IsNullOrEmpty(manifest.format)
            && !string.Equals(manifest.format, ExpectedFormat, StringComparison.OrdinalIgnoreCase)) {
            return "manifest.format 必须是 om-tilepack";
        }
        if (!string.IsNullOrEmpty(manifest.family)
            && !string.Equals(manifest.family, ExpectedFamily, StringComparison.OrdinalIgnoreCase)) {
            return "仅支持 family=standard 的牌面包（虹雀不可自定义）";
        }
        return null;
    }

    private static string ReadUtf8(ZipArchiveEntry entry) {
        using (Stream stream = entry.Open())
        using (var reader = new StreamReader(stream, Encoding.UTF8, true)) {
            return reader.ReadToEnd();
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
