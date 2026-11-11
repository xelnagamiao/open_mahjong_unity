using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

/// <summary>Checks imported files before allocating their contents or decoding a texture.</summary>
public static class LocalAssetImportLimits {
    public const int MaxImageBytes = 8 * 1024 * 1024;
    public const int MaxZipBytes = 128 * 1024 * 1024;
    public const int MaxImageEdge = 4096;
    public const int MaxSurfaceZipEntries = 256;

    // Check the directory count before ZipArchive constructs one object per
    // entry. Surface packages do not need ZIP64 or multi-volume archives.
    public static void ValidateSurfaceZip(byte[] bytes) {
        if (bytes == null || bytes.Length < 22 || bytes.Length > MaxZipBytes)
            throw new InvalidDataException("压缩包为空、损坏或超过 128MB");
        for (int i = bytes.Length - 22, end = Math.Max(0, bytes.Length - 65557); i >= end; i--) {
            if (bytes[i] != 0x50 || bytes[i + 1] != 0x4b || bytes[i + 2] != 5 || bytes[i + 3] != 6) continue;
            int commentLength = bytes[i + 20] | (bytes[i + 21] << 8);
            if (i + 22 + commentLength != bytes.Length) continue;
            int entries = bytes[i + 10] | (bytes[i + 11] << 8);
            int diskEntries = bytes[i + 8] | (bytes[i + 9] << 8);
            uint directorySize = (uint)(bytes[i + 12] | bytes[i + 13] << 8 | bytes[i + 14] << 16 | bytes[i + 15] << 24);
            uint directoryStart = (uint)(bytes[i + 16] | bytes[i + 17] << 8 | bytes[i + 18] << 16 | bytes[i + 19] << 24);
            if (bytes[i + 4] != 0 || bytes[i + 5] != 0 || bytes[i + 6] != 0 || bytes[i + 7] != 0
                || entries < 1 || entries > MaxSurfaceZipEntries || entries != diskEntries
                || directorySize > 1024 * 1024 || (long)directoryStart + directorySize != i)
                throw new InvalidDataException("压缩包目录无效或文件数量过多");
            return;
        }
        throw new InvalidDataException("压缩包不完整，请重新选择");
    }

    public static void ValidateSurfaceArchive(ZipArchive archive) {
        if (archive.Entries.Count < 1 || archive.Entries.Count > MaxSurfaceZipEntries)
            throw new InvalidDataException("压缩包文件数量过多");
        long total = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries) {
            string name = entry.FullName;
            if (string.IsNullOrEmpty(name) || name.Length > 512 || name.StartsWith("/")
                || name.Contains("\\") || name.Contains(":") || !paths.Add(name))
                throw new InvalidDataException("压缩包路径无效或文件重复");
            foreach (char character in name)
                if (char.IsControl(character)) throw new InvalidDataException("压缩包路径无效");
            foreach (string segment in name.TrimEnd('/').Split('/'))
                if (segment.Length == 0 || segment == "." || segment == "..")
                    throw new InvalidDataException("压缩包路径无效");
            if (entry.Length < 0 || entry.Length > MaxImageBytes)
                throw new InvalidDataException("压缩包内单个文件不能超过 8MB");
            total += entry.Length;
            if (total > MaxZipBytes) throw new InvalidDataException("解压后不能超过 128MB");
        }
    }

    public static byte[] ReadValidatedImage(Stream source, long expectedBytes) {
        if (expectedBytes < 4 || expectedBytes > MaxImageBytes)
            throw new InvalidDataException("图片为空或超过 8MB");
        var bytes = new byte[(int)expectedBytes];
        int offset = 0;
        while (offset < bytes.Length) {
            int read = source.Read(bytes, offset, bytes.Length - offset);
            if (read == 0) throw new InvalidDataException("图片解压不完整");
            offset += read;
        }
        if (source.ReadByte() != -1) throw new InvalidDataException("图片解压长度无效");
        using (var stream = new MemoryStream(bytes, false)) Validate(stream, false);
        return bytes;
    }

    public static byte[] ReadValidated(string path, bool allowZip) {
        using (var stream = File.OpenRead(path)) {
            Validate(stream, allowZip);
            stream.Position = 0;
            var bytes = new byte[(int)stream.Length];
            int offset = 0;
            while (offset < bytes.Length) {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0) throw new IOException("文件读取不完整，请重新选择");
                offset += read;
            }
            return bytes;
        }
    }

    public static void ValidateFile(string path, bool allowZip) {
        using (var stream = File.OpenRead(path)) Validate(stream, allowZip);
    }

    private static void Validate(Stream stream, bool allowZip) {
        if (stream.Length < 4) throw new InvalidDataException("文件为空或格式无效");
        int first = stream.ReadByte(), second = stream.ReadByte();
        int third = stream.ReadByte(), fourth = stream.ReadByte();
        if (allowZip && first == 0x50 && second == 0x4b && third == 3 && fourth == 4) {
            if (stream.Length > MaxZipBytes) throw new InvalidDataException("压缩包超过 128MB，请缩小后导入");
            return;
        }
        if (stream.Length > MaxImageBytes) throw new InvalidDataException("图片超过 8MB，请压缩后上传");
        stream.Position = 0;
        int width, height;
        bool valid = first == 0x89 && second == 0x50
            ? TryReadPngSize(stream, out width, out height)
            : TryReadJpegSize(stream, out width, out height);
        if (!valid) throw new InvalidDataException("无法读取图片，请选择 PNG 或 JPG 文件");
        if (width <= 0 || height <= 0 || width > MaxImageEdge || height > MaxImageEdge)
            throw new InvalidDataException("图片宽高不能超过 4096 像素");
    }

    private static bool TryReadPngSize(Stream stream, out int width, out int height) {
        width = height = 0;
        byte[] header = new byte[24];
        if (stream.Read(header, 0, header.Length) != header.Length) return false;
        byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        for (int i = 0; i < signature.Length; i++) if (header[i] != signature[i]) return false;
        if (ReadInt32(header, 8) != 13 || header[12] != 'I' || header[13] != 'H'
            || header[14] != 'D' || header[15] != 'R') return false;
        width = ReadInt32(header, 16); height = ReadInt32(header, 20);
        return true;
    }

    private static int ReadInt32(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

    private static bool TryReadJpegSize(Stream stream, out int width, out int height) {
        width = height = 0;
        if (stream.ReadByte() != 0xff || stream.ReadByte() != 0xd8) return false;
        while (stream.Position < stream.Length) {
            if (stream.ReadByte() != 0xff) return false;
            int marker;
            do { marker = stream.ReadByte(); } while (marker == 0xff);
            if (marker < 0 || marker == 0 || marker == 0xda || marker == 0xd9) return false;
            if (marker == 1 || (marker >= 0xd0 && marker <= 0xd8)) continue;
            int length = ReadUInt16(stream);
            if (length < 2 || stream.Position + length - 2 > stream.Length) return false;
            // Start-of-frame markers contain dimensions; DHT/JPG/DAC do not.
            if (marker >= 0xc0 && marker <= 0xcf && marker != 0xc4 && marker != 0xc8 && marker != 0xcc) {
                if (length < 8) return false;
                stream.ReadByte();
                height = ReadUInt16(stream); width = ReadUInt16(stream);
                return width > 0 && height > 0;
            }
            stream.Position += length - 2;
        }
        return false;
    }

    private static int ReadUInt16(Stream stream) {
        int high = stream.ReadByte(), low = stream.ReadByte();
        return high < 0 || low < 0 ? -1 : (high << 8) | low;
    }
}
