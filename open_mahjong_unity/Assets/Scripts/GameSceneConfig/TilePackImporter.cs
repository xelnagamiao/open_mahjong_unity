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
    public const int TableWidth = 220;
    public const int TableHeight = 366;
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
                    ImportEntry(entry, result);
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

        if (result.HandPngs.Count == 0 || result.TablePngs.Count == 0) {
            result.Error = "压缩包必须同时包含「手牌牌面」(hand/) 和「3D牌面」(table/) 两个文件夹";
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

    private static void ImportEntry(ZipArchiveEntry entry, Result result) {
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
            result.Warnings.Add("牌面 PNG 必须放在 手牌牌面/ 或 3D牌面/（也可用 hand/、table/）: " + full);
            return;
        }
        if (isTable) {
            png = TileTableFaceBake.ProcessPng(png);
        }
        if (isHand && (width != 272 || height != 389) && !HasHandSizeWarning(result)) {
            result.Warnings.Add("手牌将原样叠加在牌面背景上，不必裁切；建议 272×389");
        }
        Dictionary<int, byte[]> target = isTable ? result.TablePngs : result.HandPngs;
        target[tileId] = png;
    }

    private static bool HasHandSizeWarning(Result result) {
        for (int i = 0; i < result.Warnings.Count; i++) {
            if (result.Warnings[i].StartsWith("手牌将原样", StringComparison.Ordinal)
                || result.Warnings[i].StartsWith("手牌建议", StringComparison.Ordinal)) {
                return true;
            }
        }
        return false;
    }

    public static bool IsTableAspect(int width, int height) {
        if (width <= 0 || height <= 0) {
            return false;
        }
        int expectedHeight = (int)System.Math.Round(width * (double)TableHeight / TableWidth);
        int expectedWidth = (int)System.Math.Round(height * (double)TableWidth / TableHeight);
        return System.Math.Abs(height - expectedHeight) <= 1 || System.Math.Abs(width - expectedWidth) <= 1;
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
        if (bytes == null || bytes.Length < 24) {
            return false;
        }
        width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        return width > 0 && height > 0;
    }
}
