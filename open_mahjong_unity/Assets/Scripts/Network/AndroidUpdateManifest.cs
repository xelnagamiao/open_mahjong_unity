using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;

[Serializable]
public sealed class AndroidUpdateManifest {
    public int schemaVersion;
    public string status;
    public string message;
    public string versionName;
    public long versionCode;
    public int releaseVersion;
    public string packageName;
    public string file;
    public long sizeBytes;
    public string sha256;
    public string downloadPath;
    public string notes;
}

public static class AndroidUpdateValidation {
    public const long MaximumApkBytes = 2L * 1024 * 1024 * 1024;

    public static bool TryGetDownload(AndroidUpdateManifest manifest, Uri endpoint,
        int requiredRelease, long installedVersion, string packageName, out Uri download, out string error) {
        download = null;
        error = "更新信息无效，请稍后重试。";
        if (manifest == null || manifest.status != "available" || manifest.schemaVersion != 1) return false;
        if (requiredRelease <= 0 || manifest.releaseVersion != requiredRelease) {
            error = "与当前服务器匹配的新安装包尚未发布，请稍后重试。";
            return false;
        }
        if (manifest.versionCode <= installedVersion || manifest.versionCode > int.MaxValue) {
            error = "暂无可用的新安装包，请稍后重试。";
            return false;
        }
        if (manifest.packageName != packageName || manifest.sizeBytes < 1 || manifest.sizeBytes > MaximumApkBytes
            || string.IsNullOrEmpty(manifest.versionName) || manifest.versionName.Length > 64
            || manifest.sha256 == null || !Regex.IsMatch(manifest.sha256, "\\A[a-f0-9]{64}\\z")
            || manifest.downloadPath != "/api/client-updates/android/apk/" + manifest.sha256
            || !Uri.TryCreate(endpoint, manifest.downloadPath, out download)
            || download.Scheme != endpoint.Scheme || download.Authority != endpoint.Authority) return false;
        error = null;
        return true;
    }

    // Runs off the Unity thread; APKs are streamed through a fixed-size buffer.
    public static string VerifyFile(string filename, long expectedSize, string expectedHash, CancellationToken cancellation) {
        try {
            using (var stream = File.OpenRead(filename)) {
                if (stream.Length != expectedSize) return "安装包大小不符，可能未完整下载或下载错误，请重试。";
                using (var hash = SHA256.Create()) {
                    var buffer = new byte[65536];
                    int count;
                    while ((count = stream.Read(buffer, 0, buffer.Length)) > 0) {
                        if (cancellation.IsCancellationRequested) return "下载已取消。";
                        hash.TransformBlock(buffer, 0, count, buffer, 0);
                    }
                    hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    if (!string.Equals(BitConverter.ToString(hash.Hash).Replace("-", ""), expectedHash, StringComparison.OrdinalIgnoreCase)) {
                        return "安装包校验失败，文件可能已损坏，请重新下载。";
                    }
                }
            }
            return null;
        } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) {
            return "无法读取安装包，请检查存储空间后重试。";
        }
    }
}
