using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OmUpdate {
    [Serializable] public sealed class ContentManifest {
        public int schemaVersion = 1;
        public string status;
        public int baseVersion;
        public int releaseVersion;
        public string baseline;
        public string file;
        public long sizeBytes;
        public long unpackedBytes;
        public string sha256;
        public string signature;
        public string downloadPath;
        public string[] assemblies;
        public string[] metadata;
        public string[] bundles;
        public string scene;

        public string SignedText() => string.Join("\n", schemaVersion, baseVersion, releaseVersion,
            baseline, file, sizeBytes, unpackedBytes, sha256);

        public void Validate(int installedBase, string installedBaseline, string publicKey) {
            if (schemaVersion != 1 || baseVersion != installedBase || baseline != installedBaseline
                || releaseVersion < 1 || sizeBytes < 1 || sizeBytes > 2147483648L
                || unpackedBytes < 1 || unpackedBytes > 4294967296L
                || !IsHash(sha256) || file != "content-" + sha256 + ".zip")
                throw new InvalidDataException("内容包与已安装框架不兼容，或清单无效。");
            using (var rsa = new RSACryptoServiceProvider()) {
                rsa.FromXmlString(publicKey);
                if (!rsa.VerifyData(Encoding.UTF8.GetBytes(SignedText()), "SHA256", Convert.FromBase64String(signature)))
                    throw new InvalidDataException("内容包签名无效。");
            }
        }

        public static bool IsHash(string value) => value != null && Regex.IsMatch(value, "\\A[a-f0-9]{64}\\z");
        public static string HashFile(string file, Action<long, long> progress = null) {
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(file)) {
                if (progress == null) return Hex(hash.ComputeHash(stream));
                var buffer = new byte[131072];
                long read = 0;
                int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) != 0) {
                    hash.TransformBlock(buffer, 0, count, buffer, 0);
                    read += count;
                    progress(read, stream.Length);
                }
                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                progress(stream.Length, stream.Length);
                return Hex(hash.Hash);
            }
        }
        public static string Hex(byte[] value) => BitConverter.ToString(value).Replace("-", "").ToLowerInvariant();
    }

    [Serializable] public sealed class ContentIndex {
        public int baseVersion;
        public int releaseVersion;
        public string baseline;
        public string[] assemblies;
        public string[] metadata;
        public string[] bundles;
        public string scene;
        public ResourceEntry[] resources;
        public ContentFile[] files;
        public ContentInitializer[] initializers;
    }
    [Serializable] public sealed class ContentInitializer { public string assembly; public string type; public string method; public int phase; }
    [Serializable] public sealed class ContentFile { public string path; public long size; public string sha256; }
    [Serializable] public sealed class ResourceEntry { public string key; public string asset; public string bundle; public string type; public bool sprites; }
    [Serializable] public sealed class BootSettings {
        public int baseVersion;
        public int releaseVersion;
        public string baseline;
        public string api;
        public string website;
        public string publicKey;
    }
}
